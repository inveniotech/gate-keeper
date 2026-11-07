// SPDX-License-Identifier: Apache-2.0
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';

/** Validates immutable, ordered repository and licence pins before invoking Git. */
export function validateManifest(manifest) {
  if (manifest?.schemaVersion !== 1 || !Array.isArray(manifest.repositories) || manifest.repositories.length === 0) {
    throw new Error('Supply a version 1 validation manifest containing repository pins.');
  }
  let previous = '';
  for (const row of manifest.repositories) {
    if (!/^[A-Za-z0-9][A-Za-z0-9-]*\/[A-Za-z0-9][A-Za-z0-9._-]*$/.test(row.repository ?? '')
      || row.repository <= previous || !/^[a-f0-9]{40}$/.test(row.revision ?? '')
      || !['MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC'].includes(row.licence)
      || !/^[A-Za-z0-9_-][A-Za-z0-9._-]*$/.test(row.licencePath ?? '')
      || !/^[a-f0-9]{64}$/.test(row.licenceSha256 ?? '')
      || !/^\d{4}-\d{2}-\d{2}$/.test(row.pinnedOn ?? '')
      || !Number.isFinite(Date.parse(`${row.pinnedOn}T00:00:00Z`))
      || new Date(`${row.pinnedOn}T00:00:00Z`).toISOString().slice(0, 10) !== row.pinnedOn
      || !['dotnet', 'jvm', 'node'].includes(row.stack)
      || !['client-library', 'data-access', 'framework', 'nigerian-company'].includes(row.role)
      || !['pending', 'complete'].includes(row.triage)) {
      throw new Error('A validation pin is invalid, duplicated or out of order. Review its immutable commit, licence and metadata.');
    }
    previous = row.repository;
  }
}

/** Enforces the existing corpus size and eligible-file limits without rounding. */
export function checkInspection(inspection) {
  if (!Number.isSafeInteger(inspection.eligibleFiles) || inspection.eligibleFiles < 200 || inspection.eligibleFiles > 20000
    || !Number.isSafeInteger(inspection.totalBytes) || inspection.totalBytes < 0 || inspection.totalBytes > 268435456) {
    throw new Error('The checkout is outside the 200–20,000 eligible-file or 256 MiB corpus bounds. Choose another pin.');
  }
}

function main() {
  const [manifestPath, inspectorPath, outputPath] = process.argv.slice(2);
  if (process.argv.length !== 5) throw new Error('Use: node ci/validation-corpus/validation.mjs <manifest> <inspector DLL> <new report file>.');
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  validateManifest(manifest);
  const inspector = resolve(inspectorPath);
  const work = mkdtempSync(join(tmpdir(), 'gate-keeper-validation-'));
  const reports = [];
  const environment = { ...process.env, GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: process.platform === 'win32' ? 'NUL' : '/dev/null',
    GIT_TERMINAL_PROMPT: '0', GIT_LFS_SKIP_SMUDGE: '1' };
  const execute = (command, args, cwd, binary = false) => {
    const result = spawnSync(command, args, { cwd, env: environment, encoding: binary ? null : 'utf8', timeout: 120000, maxBuffer: 1024 * 1024 });
    if (result.error || result.status !== 0) throw new Error('Validation checkout or inspection failed. Reproduce the pinned input locally; no remote diagnostics are retained.');
    return binary ? result.stdout : result.stdout.trim();
  };
  try {
    for (const row of manifest.repositories) {
      console.log(`Verifying ${row.repository} at ${row.revision}.`);
      const root = join(work, row.repository.replace('/', '--'));
      mkdirSync(root);
      // Empty templates and isolated Git configuration prevent checkout hooks or global filters from running.
      execute('git', ['init', '--quiet', '--template=', root], work);
      execute('git', ['remote', 'add', 'origin', `https://github.com/${row.repository}.git`], root);
      execute('git', ['-c', 'protocol.file.allow=never', '-c', 'protocol.ext.allow=never', 'fetch', '--quiet', '--depth=1', '--no-tags', 'origin', row.revision], root);
      execute('git', ['-c', 'core.hooksPath=', 'checkout', '--quiet', '--detach', 'FETCH_HEAD'], root);
      if (execute('git', ['rev-parse', 'HEAD'], root) !== row.revision) throw new Error('The checkout does not match its pinned commit.');
      // Hash the committed bytes, independent of checkout line-ending conversion or licence symlinks.
      const licenceBytes = execute('git', ['show', `${row.revision}:${row.licencePath}`], root, true);
      const licenceHash = createHash('sha256').update(licenceBytes).digest('hex');
      if (licenceHash !== row.licenceSha256) throw new Error('The licence bytes differ from the reviewed pin. Review the repository licence before measuring.');
      console.log('Verified committed licence bytes.');
      const inspection = JSON.parse(execute('dotnet', [inspector, root], work));
      checkInspection(inspection);
      console.log(`Verified ${inspection.eligibleFiles} eligible files and ${inspection.totalBytes} checkout bytes.`);
      reports.push({ repository: row.repository, revision: row.revision, licence: row.licence, licenceSha256: licenceHash,
        ...inspection, triage: row.triage });
    }
    writeFileSync(outputPath, JSON.stringify({ schemaVersion: 1, detectionMeasured: false, repositories: reports }, null, 2) + '\n', { flag: 'wx' });
    console.log(`Verified ${reports.length} pinned checkouts. Detection and owner triage remain unmeasured.`);
  } finally {
    if (dirname(work) !== resolve(tmpdir())) throw new Error('The cleanup path is outside the temporary directory.');
    rmSync(work, { recursive: true, force: true });
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  try { main(); }
  catch { console.error('Validation corpus preparation failed. Check the manifest, pinned licence, checkout bounds and inspector locally.'); process.exitCode = 2; }
}
