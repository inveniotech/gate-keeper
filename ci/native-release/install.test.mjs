// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash, createPrivateKey, createPublicKey, sign } from 'node:crypto';
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { delimiter, join, dirname, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { installRelease } from './install.mjs';

// This RFC 8032 seed is public development-only material, never a production key.
const developmentKey = createPrivateKey({ key: Buffer.from('302e020100300506032b6570042204209d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60', 'hex'), format: 'der', type: 'pkcs8' });
const publicKey = createPublicKey(developmentKey).export({ format: 'pem', type: 'spki' });
const archiveName = process.platform === 'win32' ? 'gatekeeper-win-x64.zip' : `gatekeeper-${process.platform === 'darwin' ? 'osx' : 'linux'}-x64.tar.gz`;
const executableName = process.platform === 'win32' ? 'gatekeeper.exe' : 'gatekeeper';

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'gate-keeper-install-test-'));
  t.after(async () => {
    assert.equal(dirname(root), resolve(tmpdir()));
    await rm(root, { recursive: true, force: true });
  });
  const source = join(root, 'source');
  await mkdir(source);
  await writeFile(join(source, executableName), 'synthetic executable bytes\n');
  const archive = join(root, archiveName);
  const pack = spawnSync('tar', ['-a', '-cf', archive, '-C', source, executableName], { encoding: 'utf8' });
  assert.equal(pack.status, 0, pack.stderr);
  const bytes = await readFile(archive);
  const manifest = Buffer.from(`${createHash('sha256').update(bytes).digest('hex')}  ${archiveName}\n`);
  const assets = new Map([[archiveName, bytes], ['SHA256SUMS', manifest], ['SHA256SUMS.sig', sign(null, manifest, developmentKey)]]);
  const downloads = [];
  const options = { version: '0.2.0-alpha.3', publicKey, destination: join(root, 'installed'), platform: process.platform, architecture: 'x64',
    fetchAsset: async name => { downloads.push(name); if (!assets.has(name)) throw new Error('Missing signature or asset'); return assets.get(name); } };
  return { root, options, assets, downloads };
}

test('installer verifies the manifest and archive before installing the exact executable', async t => {
  const { options, downloads } = await fixture(t);
  const installed = await installRelease(options);
  assert.equal(await readFile(installed, 'utf8'), 'synthetic executable bytes\n');
  assert.deepEqual(downloads, ['SHA256SUMS', 'SHA256SUMS.sig', archiveName]);
});

for (const [name, mutate, message] of [
  ['missing signature', assets => assets.delete('SHA256SUMS.sig'), /Missing signature/],
  ['tampered signature', assets => assets.set('SHA256SUMS.sig', Buffer.alloc(64)), /signature/],
  ['tampered manifest', assets => assets.set('SHA256SUMS', Buffer.concat([assets.get('SHA256SUMS'), Buffer.from('tampered')])), /signature/],
  ['tampered archive', assets => assets.set(archiveName, Buffer.from('tampered')), /checksum/],
]) {
  test(`installer rejects ${name} without installing an executable`, async t => {
    const { options, assets } = await fixture(t);
    mutate(assets);
    await assert.rejects(installRelease(options), message);
    await assert.rejects(readdir(options.destination), { code: 'ENOENT' });
  });
}

test('installer rejects a different trusted public key before downloading the archive', async t => {
  const { options, downloads } = await fixture(t);
  const other = createPublicKey({ key: Buffer.from('302a300506032b65700321003d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c', 'hex'), format: 'der', type: 'spki' });
  await assert.rejects(installRelease({ ...options, publicKey: other }), /signature/);
  assert.deepEqual(downloads, ['SHA256SUMS', 'SHA256SUMS.sig']);
});

test('installer refuses to overwrite an existing executable', async t => {
  const { options, downloads } = await fixture(t);
  await mkdir(options.destination);
  await writeFile(join(options.destination, executableName), 'retain');
  await assert.rejects(installRelease(options), /already exists/);
  assert.equal(await readFile(join(options.destination, executableName), 'utf8'), 'retain');
  assert.deepEqual(downloads, []);
});

test('installer rejects unpinned versions before any download', async t => {
  const { options, downloads } = await fixture(t);
  for (const version of ['latest', '../0.2.0', '0.2.0\n', '0.2.0-alpha.01']) {
    await assert.rejects(installRelease({ ...options, version }), /version/);
  }
  assert.deepEqual(downloads, []);
});

test('installer rejects duplicate checksum entries even with a valid signature', async t => {
  const { options, assets } = await fixture(t);
  const manifest = Buffer.concat([assets.get('SHA256SUMS'), assets.get('SHA256SUMS')]);
  assets.set('SHA256SUMS', manifest);
  assets.set('SHA256SUMS.sig', sign(null, manifest, developmentKey));
  await assert.rejects(installRelease(options), /duplicate/i);
});

test('installer rejects unexpected archive contents even with valid signatures and checksums', async t => {
  const { options, assets, root } = await fixture(t);
  await writeFile(join(root, 'source', 'unexpected.txt'), 'unexpected');
  const archive = join(root, process.platform === 'win32' ? 'extra.zip' : 'extra.tar.gz');
  const pack = spawnSync('tar', ['-a', '-cf', archive, '-C', join(root, 'source'), executableName, 'unexpected.txt'], { encoding: 'utf8' });
  assert.equal(pack.status, 0, pack.stderr);
  const bytes = await readFile(archive);
  const manifest = Buffer.from(`${createHash('sha256').update(bytes).digest('hex')}  ${archiveName}\n`);
  assets.set(archiveName, bytes);
  assets.set('SHA256SUMS', manifest);
  assets.set('SHA256SUMS.sig', sign(null, manifest, developmentKey));
  await assert.rejects(installRelease(options), /unexpected/i);
  await assert.rejects(readdir(options.destination), { code: 'ENOENT' });
});

test('native installer wrapper installs an offline signed development package', async t => {
  const { options, assets, root } = await fixture(t);
  const source = join(root, 'signed-release');
  await mkdir(source);
  for (const [name, bytes] of assets) await writeFile(join(source, name), bytes);
  const key = join(root, 'development-public.pem');
  await writeFile(key, publicKey);
  const windows = process.platform === 'win32';
  const wrapper = fileURLToPath(new URL(windows ? '../../install.ps1' : '../../install.sh', import.meta.url));
  const args = windows
    ? ['-NoProfile', '-File', wrapper, '-Version', options.version, '-PublicKey', key, '-Destination', options.destination, '-SourceDirectory', source]
    : [wrapper, '--version', options.version, '--public-key', key, '--destination', options.destination, '--source-directory', source];
  const run = spawnSync(windows ? 'pwsh' : 'sh', args, { encoding: 'utf8', env: { ...process.env, PATH: `${dirname(process.execPath)}${delimiter}${process.env.PATH}` } });
  assert.equal(run.status, 0, run.stderr);
  assert.equal(await readFile(join(options.destination, executableName), 'utf8'), 'synthetic executable bytes\n');
});

test('installer rejects a malformed signed manifest before fetching the archive', async t => {
  const { options, assets, downloads } = await fixture(t);
  const manifest = Buffer.from(`${'a'.repeat(64)}  ../../outside.zip\n`);
  assets.set('SHA256SUMS', manifest);
  assets.set('SHA256SUMS.sig', sign(null, manifest, developmentKey));
  await assert.rejects(installRelease(options), /invalid archive/);
  assert.deepEqual(downloads, ['SHA256SUMS', 'SHA256SUMS.sig']);
});

test('installer rejects an oversized manifest before checking its signature', async t => {
  const { options, assets, downloads } = await fixture(t);
  assets.set('SHA256SUMS', Buffer.alloc(65537));
  await assert.rejects(installRelease(options), /oversized/);
  assert.deepEqual(downloads, ['SHA256SUMS']);
});

test('installer bounds uncompressed executable bytes before writing the destination', async t => {
  const { options, assets, root } = await fixture(t);
  await writeFile(join(root, 'source', executableName), Buffer.alloc(10485761));
  const archive = join(root, archiveName);
  const pack = spawnSync('tar', ['-a', '-cf', archive, '-C', join(root, 'source'), executableName], { encoding: 'utf8' });
  assert.equal(pack.status, 0, pack.stderr);
  const bytes = await readFile(archive);
  const manifest = Buffer.from(`${createHash('sha256').update(bytes).digest('hex')}  ${archiveName}\n`);
  assets.set(archiveName, bytes);
  assets.set('SHA256SUMS', manifest);
  assets.set('SHA256SUMS.sig', sign(null, manifest, developmentKey));
  await assert.rejects(installRelease(options), /could not be extracted|size budget/);
  await assert.rejects(readdir(options.destination), { code: 'ENOENT' });
});
