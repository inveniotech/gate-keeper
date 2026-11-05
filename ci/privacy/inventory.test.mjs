// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

test('the synthetic privacy inventory is regenerated from the documented corpus script', async () => {
  const generator = new URL('../../tests/corpus/New-CorpusValues.mjs', import.meta.url);
  const result = spawnSync(process.execPath, [fileURLToPath(generator), '--values-json'], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const committed = await readFile(new URL('../../tests/corpus/synthetic-values.json', import.meta.url), 'utf8');
  assert.equal(result.stdout, committed);
  assert.ok(JSON.parse(committed).length > 30);
});
