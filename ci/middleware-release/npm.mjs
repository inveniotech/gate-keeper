// SPDX-License-Identifier: Apache-2.0
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateNpmPackage } from './validate.mjs';
const root = fileURLToPath(new URL('../../', import.meta.url));
const source = join(root, 'middleware/node');
const destination = join(root, 'artifacts/middleware/npm');
mkdirSync(destination, { recursive: true });
function run(command, args, cwd) {
  const windowsNpm = process.platform === 'win32' && command === 'npm';
  const result = spawnSync(windowsNpm ? process.execPath : command,
    windowsNpm ? [join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js'), ...args] : args,
    { cwd, encoding: 'utf8' });
  if (result.status !== 0) throw new Error(`${command} failed: ${result.stderr}\n${result.stdout}`);
  return result.stdout;
}
const packed = JSON.parse(run('npm', ['pack', '--json', '--pack-destination', destination], source))[0];
validateNpmPackage(packed, readFileSync(join(root, 'middleware/version.txt'), 'utf8').trim());
const consumer = mkdtempSync(join(tmpdir(), 'gatekeeper-package-'));
try {
  writeFileSync(join(consumer, 'package.json'), JSON.stringify({ private: true, type: 'module' }));
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund', '--offline', join(destination, packed.filename)], consumer);
  const probe = `import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
for (const suffix of ['', '/express', '/nest', '/pino', '/winston']) {
  const esm = await import('@inveniotech/gate-keeper' + suffix);
  const cjs = require('@inveniotech/gate-keeper' + suffix);
  assert.deepEqual(Object.keys(esm).sort(), Object.keys(cjs).sort());
  if (!suffix) assert.throws(() => esm.loadInstallation({ embedded: [], keys: { pack: {} } }), esm.PackLoadError);
}`;
  writeFileSync(join(consumer, 'probe.mjs'), probe);
  run(process.execPath, ['probe.mjs'], consumer);
  assert.ok(packed.filename.endsWith('.tgz'));
  console.log('Installed archive passes independent ESM and CommonJS consumer checks. No publication occurred.');
} finally { rmSync(consumer, { recursive: true, force: true }); }
