// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { verifyRuns } from './scan.mjs';

const evidence = JSON.parse(readFileSync(new URL('../../docs/specs/examples/results/minimal.gk.json', import.meta.url)));
function run(changes = {}) {
  const doc = { ...evidence, evaluatedOn: '2026-11-05', packs: [{ packId: 'local.synthetic', version: 1 }], ...changes };
  return { status: doc.exitCode, stdout: `${JSON.stringify(doc)}\n`, stderr: '' };
}

test('dogfood accepts byte-identical clean evidence with a loaded pack', () => {
  const result = run({ exitCode: 0 });
  assert.equal(verifyRuns(result, result, '2026-11-05').exitCode, 0);
});
test('dogfood preserves the violations exit instead of treating it as a tool error', () => {
  const result = run({ exitCode: 1 });
  assert.equal(verifyRuns(result, result, '2026-11-05').exitCode, 1);
});
test('dogfood rejects tool failures without exposing diagnostics or partial source', () => {
  const result = { status: 2, stdout: 'synthetic confidential output', stderr: 'synthetic confidential error' };
  assert.throws(() => verifyRuns(result, result, '2026-11-05'), error => {
    assert.doesNotMatch(error.message, /confidential/);
    return /could not complete/.test(error.message);
  });
});
test('dogfood rejects inconsistent output, diagnostics and process status', () => {
  const result = run();
  for (const changed of [{ ...result, stdout: `${result.stdout}\n` }, { ...result, stderr: 'changed' }, { ...result, status: 1 }]) {
    assert.throws(() => verifyRuns(result, changed, '2026-11-05'));
  }
});
test('dogfood rejects malformed, empty-pack, wrong-date and inconsistent evidence', () => {
  const invalid = [
    { ...run(), stdout: 'not JSON' }, run({ packs: [] }), run({ evaluatedOn: '2026-11-04' }),
    run({ schemaVersion: 2 }), run({ historyScanned: true }), run({ findings: null }),
    { ...run(), status: 1 }, { ...run(), error: new Error('synthetic spawn failure') },
  ];
  for (const result of invalid) assert.throws(() => verifyRuns(result, result, '2026-11-05'));
});
