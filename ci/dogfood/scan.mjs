// SPDX-License-Identifier: Apache-2.0
import { spawnSync } from 'node:child_process';
import { mkdirSync, realpathSync, writeFileSync } from 'node:fs';
import { basename, dirname, isAbsolute, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Verifies repeatability and scan completion; tool diagnostics are never copied into errors. */
export function verifyRuns(first, second, evaluatedOn) {
  for (const result of [first, second]) {
    if (result.error || ![0, 1].includes(result.status)) {
      throw new Error('The dogfood scan could not complete. Check the scanner, configured rule pack and repository access locally.');
    }
  }
  if (first.status !== second.status || first.stdout !== second.stdout || first.stderr !== second.stderr) {
    throw new Error('Repeated dogfood scans differ. Reproduce with the same tree, configuration and evaluation date.');
  }
  let evidence;
  try { evidence = JSON.parse(first.stdout); }
  catch { throw new Error('The scanner did not return evidence JSON. Use a CLI version supporting scan --json.'); }
  if (evidence?.schemaVersion !== 1 || evidence.evaluatedOn !== evaluatedOn
    || evidence.engine?.product !== 'gatekeeper' || evidence.exitCode !== first.status
    || evidence.historyScanned !== false || !Array.isArray(evidence.findings)
    || !Array.isArray(evidence.packs) || evidence.packs.length === 0) {
    throw new Error('The evidence does not describe the requested work-tree scan with a loaded pack. Check the configuration and scanner version.');
  }
  return evidence;
}

function main() {
  const [binary, repository, evaluatedOn, destination] = process.argv.slice(2);
  if (process.argv.length !== 6 || !/^\d{4}-\d{2}-\d{2}$/.test(evaluatedOn ?? '')
    || !Number.isFinite(Date.parse(`${evaluatedOn}T00:00:00Z`))
    || new Date(`${evaluatedOn}T00:00:00Z`).toISOString().slice(0, 10) !== evaluatedOn) {
    throw new Error('Use: node ci/dogfood/scan.mjs <CLI executable or DLL> <repository> <YYYY-MM-DD> <new output directory>.');
  }
  const root = realpathSync(repository);
  const output = resolve(realpathSync(dirname(resolve(destination))), basename(destination));
  const location = relative(root, output);
  if (!location || (!isAbsolute(location) && location !== '..' && !location.startsWith('../') && !location.startsWith('..\\'))) {
    throw new Error('Place dogfood output outside the scanned repository so results cannot change the next scan.');
  }
  // Refuse existing output so a failed scan cannot leave stale evidence presented as current.
  mkdirSync(output);
  const executable = realpathSync(binary);
  const args = ['scan', '--json', '--evaluated-on', evaluatedOn];
  const command = executable.endsWith('.dll') ? 'dotnet' : executable;
  if (executable.endsWith('.dll')) args.unshift(executable);
  const invoke = () => spawnSync(command, args, {
    cwd: root, encoding: 'utf8', timeout: 30_000, maxBuffer: 16 * 1024 * 1024,
    env: { ...process.env, NO_COLOR: '1' },
  });
  const first = invoke();
  const evidence = verifyRuns(first, invoke(), evaluatedOn);
  writeFileSync(resolve(output, 'evidence.json'), first.stdout);
  // The evidence contract omits matched values. Diagnostics can contain free text, so do not retain them.
  console.log(`Dogfood scan: ${evidence.findings.length} findings; ${evidence.packs.length} loaded packs; exit ${evidence.exitCode}.`);
  process.exitCode = evidence.exitCode;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  try { main(); }
  catch (error) {
    // Filesystem and process errors can contain local paths. Only our fixed messages are safe to print.
    console.error(error.code ? 'Dogfood setup failed. Check the input paths and use a new output directory.' : error.message);
    process.exitCode = 2;
  }
}
