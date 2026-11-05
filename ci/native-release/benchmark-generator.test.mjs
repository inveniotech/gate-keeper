// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readdir, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const generator = new URL('../../tests/corpus/New-CorpusValues.mjs', import.meta.url);

test('benchmark generator produces the same 500 eligible files and metadata twice', async () => {
  const root = await mkdtemp(join(tmpdir(), 'gate-keeper-benchmark-test-'));
  try {
    const digests = [];
    for (const name of ['first', 'second']) {
      const destination = join(root, name);
      const run = spawnSync(process.execPath, [fileURLToPath(generator), '--benchmark', destination, '--files', '500'], { encoding: 'utf8' });
      assert.equal(run.status, 0, run.stderr);
      const files = (await readdir(destination, { recursive: true })).filter(p => p.endsWith('.txt')).sort();
      assert.equal(files.length, 500);
      const hash = createHash('sha256');
      for (const path of files) hash.update(await readFile(join(destination, path)));
      digests.push(hash.digest('hex'));
      const manifest = JSON.parse(await readFile(destination + '.manifest.json', 'utf8'));
      assert.equal(manifest.fileCount, 500);
      assert.equal(manifest.seed, 20261104);
      assert.equal(manifest.historyScanned, false);
      assert.ok(manifest.totalBytes <= 10 * 1024 * 1024);
    }
    assert.equal(digests[0], digests[1]);
    const repeat = spawnSync(process.execPath, [fileURLToPath(generator), '--benchmark', join(root, 'first'), '--files', '500']);
    assert.notEqual(repeat.status, 0);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
