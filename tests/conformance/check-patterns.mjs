// P3-00: compare JavaScript regex candidate spans with C#-generated expectations.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const decode = (value) => Buffer.from(value, 'base64').toString('utf8');
let checked = 0;
for (const line of readFileSync(new URL('./patterns.tsv', import.meta.url), 'utf8').trimEnd().split('\n')) {
  if (line.startsWith('#')) continue;
  const [id, pattern, text, expected = ''] = line.split('\t');
  const expression = new RegExp(decode(pattern), 'gd');
  const actual = [...decode(text).matchAll(expression)].map((match) => {
    const span = match.indices.groups?.emit ?? match.indices[0];
    return `${match.index}:${match[0].length}:${span[0]}:${span[1] - span[0]}`;
  }).join(',');
  assert.equal(actual, expected, `Regex portability differs for ${id}`);
  checked++;
}
assert.ok(checked > 0, 'No portability cases were measured');
console.log(`Node regex portability passed: ${checked} cases.`);
