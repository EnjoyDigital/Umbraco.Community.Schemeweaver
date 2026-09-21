// Minimal TypeSafe System One client for the eval harness (no SDK dependency —
// TypeSafe ships Python and JavaScript SDKs, but adding a runtime dep to the eval
// harness buys nothing over one fetch call, and the C# package will have to speak
// raw HTTP anyway since there is no .NET SDK).
//
// The API key is read from .env.local at the repo root (TYPESAFE_API_KEY), or from
// the environment if set. Never hardcode the secret.

import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const ENDPOINT = 'https://api.typesafe.ai/v1/systemone';

/** Model alias. jev-latest is the stable release the SDKs default to. */
export const MODEL = process.env.TYPESAFE_MODEL || 'jev-latest';

/**
 * Read TYPESAFE_API_KEY from the environment, else from .env.local at the repo root.
 * Parsed with a targeted regex rather than a dotenv dep; values may be quoted.
 */
export function getApiKey() {
  if (process.env.TYPESAFE_API_KEY) return process.env.TYPESAFE_API_KEY;

  const envPath = join(process.cwd(), '.env.local');
  if (existsSync(envPath)) {
    const raw = readFileSync(envPath, 'utf8').replace(/^﻿/, '');
    const m = raw.match(/^\s*TYPESAFE_API_KEY\s*=\s*(.*)$/m);
    if (m) {
      const v = m[1].trim().replace(/^["']|["']$/g, '');
      if (v) return v;
    }
  }

  throw new Error(
    'No TypeSafe API key. Set TYPESAFE_API_KEY, or add TYPESAFE_API_KEY=... to .env.local at the repo root.',
  );
}

/** Token usage accumulated across every call in this process (for cost reporting). */
export const usage = { inputTokens: 0, outputTokens: 0, requests: 0 };

/** Jev 1.13 input pricing: $42 per billion input tokens. Output is free. */
const USD_PER_INPUT_TOKEN = 42 / 1_000_000_000;

export function usdSpent() {
  return usage.inputTokens * USD_PER_INPUT_TOKEN;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * POST one System One request: a shared `state` plus a map of independent `questions`
 * that are evaluated in parallel and cannot see each other's answers.
 *
 * Retries 429 (rate limit) and 529 (overloaded) with exponential backoff, per the
 * API docs. Other non-2xx statuses throw immediately — a 422 means the request shape
 * is wrong and retrying it just burns time.
 */
export async function systemOne({ state, questions, model = MODEL, attempt = 0 }) {
  const res = await fetch(ENDPOINT, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${getApiKey()}`,
      'content-type': 'application/json',
    },
    body: JSON.stringify({ state, questions, model }),
  });

  if ((res.status === 429 || res.status === 529) && attempt < 5) {
    const wait = Math.min(2 ** attempt * 500, 8000) + Math.random() * 250;
    await sleep(wait);
    return systemOne({ state, questions, model, attempt: attempt + 1 });
  }

  if (!res.ok) {
    const body = await res.text();
    throw new Error(`TypeSafe ${res.status}: ${body.slice(0, 400)}`);
  }

  const data = await res.json();
  usage.requests += 1;
  usage.inputTokens += data.usage?.input_tokens ?? 0;
  usage.outputTokens += data.usage?.output_tokens ?? 0;
  return data.answers || {};
}

/**
 * Split a question map into chunks and run them as separate requests, merging the
 * answers. Questions in one request share the state, and Jev caps a request at 64k
 * tokens (32k for state plus the longest question), so a wide content type against a
 * 130-property schema type has to be chunked. Chunks run sequentially: the eval is
 * not latency-bound and sequential keeps well inside the rate limits.
 */
export async function askChunked(state, questions, chunkSize = 20) {
  const ids = Object.keys(questions);
  const answers = {};
  for (let i = 0; i < ids.length; i += chunkSize) {
    const slice = ids.slice(i, i + chunkSize);
    if (slice.length === 0) continue;
    const sub = Object.fromEntries(slice.map((id) => [id, questions[id]]));
    Object.assign(answers, await systemOne({ state, questions: sub }));
  }
  return answers;
}

/** Question constructors mirroring the SDK helpers. */
export const choice = (instructions, criteria) => ({ type: 'choice', instructions, criteria });

/**
 * A Noul's criteria, when given, is an OBJECT with `true` and `false` keys describing
 * what each answer means — not a single prose string (the API rejects that with a 422).
 */
export const noul = (instructions, criteria) =>
  criteria ? { type: 'noul', instructions, criteria } : { type: 'noul', instructions };

export const score = (instructions, criteria) => ({ type: 'score', instructions, criteria });
