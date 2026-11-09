// SPDX-License-Identifier: Apache-2.0
// Observational benchmark, deliberately separate from deterministic test gates.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { performance } from 'node:perf_hooks';
import { RuntimeMatcher } from '../../middleware/node/runtime.ts';
const { rules } = JSON.parse(readFileSync(new URL('./vectors.json', import.meta.url), 'utf8'));
const runtime = new RuntimeMatcher(rules);
for (let warm = 0; warm < 3; warm++) runtime.redact('synthetic near miss');
for (const size of [1_000, 10_000, 65_536]) {
 for (const [scenario, seed] of [['email-near-miss', 'a'], ['email-missing-domain', 'a@.'], ['keyword-digits', 'bvn 22222222222222222222 '], ['reserved-masks', '😀[REDACTED:previous.rule] ']]) {
  const input = [...seed.repeat(Math.ceil(size / [...seed].length))].slice(0, size).join('');
  const start = performance.now(); const result = runtime.redact(input);
  const elapsed = performance.now() - start; assert.equal(result, input);
  console.log(JSON.stringify({ stack: 'node', scenario, scalars: size, milliseconds: +elapsed.toFixed(2) }));
 }
}
