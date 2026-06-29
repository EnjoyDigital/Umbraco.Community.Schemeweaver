// Inspector: for given content type aliases, show gold vs AI vs heuristic mappings and
// a per-schema-property diff (matched / mismatched / missed / extra). Feeds the
// devil's-advocate + expert prompt-refinement step.
//
// Usage: node eval/inspect.mjs eventPage vehiclePage [...]   (defaults to all in latest report)

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { loadAllGold } from './gold.mjs';
import { normaliseSuggestion } from './score.mjs';

const report = JSON.parse(readFileSync(join(process.cwd(), 'eval/reports/latest.json'), 'utf8'));
const gold = loadAllGold();
const aliases = process.argv.slice(2).length ? process.argv.slice(2) : report.perType.map((p) => p.alias);

const fmt = (m) =>
  `${m.schemaProp} <- ${m.sourceType}:${m.contentProp ?? '∅'}${m.nestedType ? '/' + m.nestedType : ''}`;

for (const alias of aliases) {
  const row = report.perType.find((p) => p.alias === alias);
  const g = gold.get(alias.toLowerCase());
  if (!row || !g) {
    console.log(`\n### ${alias}: not in report/gold`);
    continue;
  }
  const goldT = g.mappings
    .filter((m) => m.schemaProp && (m.contentProp || m.sourceType !== 'property'))
    .map((m) => ({
      schemaProp: m.schemaProp.toLowerCase(),
      contentProp: (m.contentProp ?? '').toLowerCase() || null,
      sourceType: m.sourceType.toLowerCase(),
      nestedType: (m.nestedType ?? '').toLowerCase() || null,
      rich: m.isSelfContainedRich,
    }));
  const ai = (row.aiRaw || []).map(normaliseSuggestion);

  console.log(`\n### ${alias} -> ${g.schemaType}   [${row.tag}]  AI strictF1=${row.ai.strictF1} heu=${row.heuristic.strictF1}  rich AI ${row.ai.rich.hit}/${row.ai.rich.goldCount} heu ${row.heuristic.rich.hit}/${row.heuristic.rich.goldCount}`);

  for (const gt of goldT) {
    const aiM = ai.find((a) => a.schemaProp === gt.schemaProp);
    let verdict;
    if (!aiM) verdict = 'MISSED (AI did not map)';
    else if (
      aiM.contentProp === gt.contentProp &&
      aiM.sourceType === gt.sourceType &&
      (!gt.nestedType || aiM.nestedType === gt.nestedType)
    )
      verdict = 'ok';
    else verdict = `MISMATCH ai=[${fmt(aiM)}]`;
    if (verdict !== 'ok') console.log(`   ${gt.rich ? '★' : ' '} gold ${fmt(gt)}   => ${verdict}`);
  }
  const goldProps = new Set(goldT.map((g) => g.schemaProp));
  const extra = ai.filter((a) => a.schemaProp && !goldProps.has(a.schemaProp));
  if (extra.length) console.log(`   + AI extra (not in gold): ${extra.map(fmt).join(' | ')}`);
}
