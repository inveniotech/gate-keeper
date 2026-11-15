// SPDX-License-Identifier: Apache-2.0
import { createHash } from 'node:crypto';
import { lstat, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const rids = ['linux-arm64', 'linux-musl-arm64', 'linux-musl-x64', 'linux-x64', 'osx-arm64', 'osx-x64', 'win-arm64', 'win-x64'];

/** Returns the release version for a numbered alpha tag matching the engine stamp; rejects other tags. */
export function validateAlphaTag(tag, engineVersion) {
  const match = /^cli-v((?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))-alpha\.([1-9]\d*)$/.exec(tag);
  if (!match || match[0] !== tag || match[1] !== engineVersion) {
    throw new Error(`Invalid alpha tag. Use cli-v${engineVersion}-alpha.N with a positive number and the checked-out engine version.`);
  }
  return `${match[1]}-alpha.${match[2]}`;
}

async function readBounded(path, maximum) {
  const info = await lstat(path);
  if (!info.isFile() || info.size === 0 || info.size > maximum) {
    throw new Error(`Release input ${path} is empty, oversized or not a regular file. Rebuild the native artefacts.`);
  }
  return readFile(path);
}

/** Validates all native assets before writing a new release directory; never overwrites an existing directory. */
export async function prepareAlphaRelease({ input, output, tag, engineVersion, commit, repository }) {
  const version = validateAlphaTag(tag, engineVersion);
  if (!/^[a-f0-9]{40}$/.test(commit) || repository !== 'inveniotech/gate-keeper') {
    throw new Error('Supply the full source commit and the inveniotech/gate-keeper repository before preparing a release.');
  }
  const allowed = new Set([...rids.map(rid => `native-${rid}`), 'native-release-review', 'native-benchmarks']);
  for (const name of await readdir(input)) {
    if (!allowed.has(name)) throw new Error(`Unexpected release input ${name}. Download only this run's native artefacts.`);
  }
  const assets = [];
  for (const rid of rids) {
    const folder = join(input, `native-${rid}`);
    const stem = `gatekeeper-${rid}`;
    const archive = `${stem}.${rid.startsWith('win-') ? 'zip' : 'tar.gz'}`;
    const expected = [archive, `${stem}.json`, `${stem}.sha256`].sort();
    if (JSON.stringify((await readdir(folder)).sort()) !== JSON.stringify(expected)) {
      throw new Error(`Unexpected or missing files for ${rid}. Rebuild the complete native artefact.`);
    }
    const record = JSON.parse(await readBounded(join(folder, `${stem}.json`), 65536));
    if (record.product !== 'gatekeeper' || record.productVersion !== engineVersion || record.runtimeIdentifier !== rid
      || record.archive !== archive || !Number.isSafeInteger(record.binaryBytes) || record.binaryBytes < 1 || record.binaryBytes > 10485760) {
      throw new Error(`Invalid release metadata for ${rid}. Rebuild from the tagged source within the native size budget.`);
    }
    const bytes = await readBounded(join(folder, archive), 12 * 1024 * 1024);
    const digest = createHash('sha256').update(bytes).digest('hex');
    if (record.sha256 !== digest) throw new Error(`Release checksum mismatch for ${rid}. Rebuild the native artefact.`);
    const checksum = `${digest}  ${archive}\n`;
    if ((await readBounded(join(folder, `${stem}.sha256`), 1024)).toString('utf8') !== checksum) {
      throw new Error(`Release checksum sidecar mismatch for ${rid}. Rebuild the native artefact.`);
    }
    assets.push({ archive, bytes, checksum });
  }
  const measurements = (await readBounded(join(input, 'native-release-review', 'release-notes.md'), 1024 * 1024)).toString('utf8');
  const notes = [
    `# Gate-Keeper ${version}`, '',
    'This alpha is published for evaluation and code-signing review. It is not a production release.', '',
    `Source: https://github.com/${repository}/commit/${commit}`, '',
    `The release version is ${version}; the binaries and evidence retain engine version ${engineVersion}.`, '',
    'Windows binaries are unsigned. Windows SmartScreen may warn and Smart App Control may block execution.',
    'macOS binaries are unsigned and are not notarised. The operating system may warn or block execution.',
    'SHA256SUMS has no detached Ed25519 signature. Checksums detect changed bytes; they do not authenticate the publisher.',
    'GitHub build attestations accompany the archives. Verify a downloaded archive with:', '',
    '```sh', `gh attestation verify gatekeeper-<runtime>.<zip-or-tar.gz> --repo ${repository}`, '```', '',
    'The CLI does not yet bundle a rule pack. Supply a configured local rule pack to evaluate scanning.', '',
    '## Native build measurements', '', measurements.trimEnd(), '',
  ].join('\n');
  await mkdir(output);
  for (const asset of assets) await writeFile(join(output, asset.archive), asset.bytes);
  await writeFile(join(output, 'SHA256SUMS'), assets.map(asset => asset.checksum).join(''));
  await writeFile(join(output, 'release-notes.md'), notes);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const engineSource = await readFile('src/Engine/EngineProduct.cs', 'utf8');
  const engineVersion = /EngineVersion = "([^"]+)"/.exec(engineSource)?.[1];
  const tag = process.env.RELEASE_TAG ?? '';
  validateAlphaTag(tag, engineVersion);
  if (process.argv[2] !== '--validate-tag') {
    await prepareAlphaRelease({ input: 'artifacts', output: 'release', tag, engineVersion,
      commit: process.env.GITHUB_SHA ?? '', repository: process.env.GITHUB_REPOSITORY ?? '' });
  }
}
