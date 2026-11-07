// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

test('the inspector uses product eligibility and counts checkout bytes without emitting source', () => {
  const root = mkdtempSync(join(tmpdir(), 'gate-keeper-inspector-'));
  try {
    for (let index = 0; index < 200; index++) writeFileSync(join(root, `file-${index}.txt`), 'synthetic text\n');
    writeFileSync(join(root, 'package-lock.json'), '{"synthetic":true}\n');
    writeFileSync(join(root, 'binary.bin'), Buffer.from([0, 1, 2]));
    mkdirSync(join(root, 'node_modules'));
    writeFileSync(join(root, 'node_modules/excluded.txt'), 'synthetic ignored text\n');
    const inspector = fileURLToPath(new URL('./Inspect/bin/Release/net10.0/Inspect.dll', import.meta.url));
    const result = spawnSync('dotnet', [inspector, root], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
    assert.deepEqual(JSON.parse(result.stdout), { eligibleFiles: 200, totalBytes: 3045 });
    assert.doesNotMatch(result.stdout + result.stderr, /synthetic|excluded.txt/);
  } finally {
    assert.equal(dirname(root), resolve(tmpdir()));
    rmSync(root, { recursive: true, force: true });
  }
});
