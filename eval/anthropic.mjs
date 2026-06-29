// Minimal Anthropic Messages API client for the eval harness (no SDK dependency).
//
// The API key is read from the TestHost's dotnet user-secrets (the same key the
// Umbraco.AI satellite uses in-product), or from ANTHROPIC_API_KEY if set. This keeps
// the standalone prompt-tuning loop representative of the in-product model while never
// hardcoding the secret.

import { readFileSync } from 'node:fs';

const SECRETS = 'C:/Users/oliver_enjoy-digital/AppData/Roaming/Microsoft/UserSecrets/95d126b6-eee0-4838-8ec9-afa38a12a91f/secrets.json';

export function getApiKey() {
  if (process.env.ANTHROPIC_API_KEY) return process.env.ANTHROPIC_API_KEY;
  const s = JSON.parse(readFileSync(SECRETS, 'utf8').replace(/^﻿/, ''));
  const k = s['Umbraco:AI:Secrets:AnthropicApiKey'];
  if (!k) throw new Error('No Anthropic API key in env or user-secrets');
  return k;
}

export const MODEL = process.env.EVAL_MODEL || 'claude-sonnet-4-6';

/** Call the Messages API with a system + user prompt; returns the text content. */
export async function complete({ system, user, maxTokens = 4096, model = MODEL }) {
  const res = await fetch('https://api.anthropic.com/v1/messages', {
    method: 'POST',
    headers: {
      'x-api-key': getApiKey(),
      'anthropic-version': '2023-06-01',
      'content-type': 'application/json',
    },
    body: JSON.stringify({
      model,
      max_tokens: maxTokens,
      system,
      messages: [{ role: 'user', content: user }],
    }),
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`Anthropic ${res.status}: ${body.slice(0, 400)}`);
  }
  const data = await res.json();
  return (data.content || []).map((b) => b.text || '').join('');
}

/** Extract the first JSON array from a model response (tolerates prose / code fences). */
export function extractJsonArray(text) {
  let t = text.trim();
  if (t.startsWith('```')) {
    t = t.replace(/^```[a-z]*\n?/i, '').replace(/```\s*$/, '').trim();
  }
  const start = t.indexOf('[');
  const end = t.lastIndexOf(']');
  if (start >= 0 && end > start) return JSON.parse(t.slice(start, end + 1));
  throw new Error(`no JSON array in response: ${text.slice(0, 200)}`);
}
