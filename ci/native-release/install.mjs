// SPDX-License-Identifier: Apache-2.0
import { createHash, createPublicKey, verify } from 'node:crypto';
import { constants } from 'node:fs';
import { chmod, copyFile, lstat, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const repository = 'https://github.com/inveniotech/gate-keeper/releases/download';
const versionPattern = /^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-(?:alpha|beta|rc)\.(?:0|[1-9]\d*))?$/;
const archivePattern = /^gatekeeper-(?:win|linux|linux-musl|osx)-(?:x64|arm64)\.(?:zip|tar\.gz)$/;

async function downloadAsset(version, name, maximum) {
  const response = await fetch(`${repository}/cli-v${version}/${name}`, { signal: AbortSignal.timeout(60000) });
  if (!response.ok) throw new Error(`Could not download ${name} for ${version} (HTTP ${response.status}). Use a release with a signed checksum manifest.`);
  if (new URL(response.url).protocol !== 'https:') throw new Error('The release download redirected outside HTTPS. Nothing was installed.');
  const chunks = [];
  let length = 0;
  for await (const chunk of response.body) {
    length += chunk.length;
    if (length > maximum) {
      throw new Error(`Release asset ${name} exceeds its size limit. Do not install this release.`);
    }
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}

function bounded(bytes, maximum, name) {
  if (!Buffer.isBuffer(bytes) || bytes.length === 0 || bytes.length > maximum) throw new Error(`Release asset ${name} is empty or oversized. Do not install this release.`);
  return bytes;
}

function checksumFor(manifest, archive) {
  const text = manifest.toString('utf8');
  if (!Buffer.from(text).equals(manifest) || !text.endsWith('\n') || text.includes('\r')) throw new Error('The signed checksum manifest is not UTF-8 with LF line endings and a final newline.');
  const entries = new Map();
  for (const line of text.slice(0, -1).split('\n')) {
    const match = /^([a-f0-9]{64})  (.+)$/.exec(line);
    if (!match || !archivePattern.test(match[2])) throw new Error('The signed checksum manifest contains an invalid archive entry.');
    if (entries.has(match[2])) throw new Error('The signed checksum manifest contains a duplicate archive entry.');
    entries.set(match[2], match[1]);
  }
  const checksum = entries.get(archive);
  if (!checksum) throw new Error(`The signed checksum manifest does not contain ${archive}. Choose a supported release.`);
  return checksum;
}

/** Installs a pinned release only after exact-byte Ed25519 and SHA-256 verification; refuses existing executables. */
export async function installRelease({ version, publicKey, destination, platform = process.platform, architecture = process.arch, musl = false, sourceDirectory, fetchAsset }) {
  if (typeof version !== 'string' || !versionPattern.test(version) || version.includes('\n')) throw new Error('Supply an explicit version such as 0.2.0-alpha.3; latest and version ranges are not allowed.');
  const os = { win32: 'win', linux: musl ? 'linux-musl' : 'linux', darwin: 'osx' }[platform];
  if (!os || !['x64', 'arm64'].includes(architecture) || (musl && platform !== 'linux')) throw new Error('This operating system or architecture has no supported release archive.');
  if (!destination) throw new Error('Supply an installation destination directory.');
  const binaryName = platform === 'win32' ? 'gatekeeper.exe' : 'gatekeeper';
  const target = join(resolve(destination), binaryName);
  try {
    await lstat(target);
    throw new Error(`The destination executable already exists. Choose a new destination or explicitly remove the previous installation first.`);
  } catch (error) { if (error.code !== 'ENOENT') throw error; }
  if (publicKey?.type !== 'public' && !/^-----BEGIN PUBLIC KEY-----\r?\n/.test(publicKey?.toString() ?? '')) throw new Error('Supply a public-only PEM verification key. Private keys must never be supplied to this installer.');
  const key = publicKey?.type === 'public' ? publicKey : createPublicKey(publicKey);
  if (key.asymmetricKeyType !== 'ed25519') throw new Error('Supply an independently trusted Ed25519 public key in PEM SubjectPublicKeyInfo format.');
  const archive = `gatekeeper-${os}-${architecture}.${platform === 'win32' ? 'zip' : 'tar.gz'}`;
  const load = fetchAsset ?? (sourceDirectory
    ? async (name, maximum) => {
      const path = join(resolve(sourceDirectory), name);
      const info = await lstat(path);
      if (!info.isFile() || info.size > maximum) throw new Error(`Local release asset ${name} is not a regular file within the size limit.`);
      return readFile(path);
    }
    : async (name, maximum) => downloadAsset(version, name, maximum));
  const manifest = bounded(await load('SHA256SUMS', 65536), 65536, 'SHA256SUMS');
  const signature = bounded(await load('SHA256SUMS.sig', 64), 64, 'SHA256SUMS.sig');
  if (signature.length !== 64 || !verify(null, manifest, key, signature)) throw new Error('The checksum manifest signature is invalid. Nothing was installed; check the independently trusted public key and release.');
  const checksum = checksumFor(manifest, archive);
  const bytes = bounded(await load(archive, 12 * 1024 * 1024), 12 * 1024 * 1024, archive);
  if (createHash('sha256').update(bytes).digest('hex') !== checksum) throw new Error('The archive checksum does not match the signed manifest. Nothing was installed.');
  const temporary = await mkdtemp(join(tmpdir(), 'gate-keeper-install-'));
  try {
    const archivePath = join(temporary, archive);
    await writeFile(archivePath, bytes);
    const listing = spawnSync('tar', ['-tf', archivePath], { encoding: 'utf8', timeout: 30000, maxBuffer: 65536 });
    if (listing.error || listing.status !== 0) throw new Error('The verified archive could not be read. Ensure tar is installed and retry.');
    if (listing.stdout.trimEnd() !== binaryName) throw new Error('The verified archive has unexpected contents. Nothing was installed.');
    // Extract to bounded stdout so an archive cannot write paths or exhaust disk before size validation.
    const extraction = spawnSync('tar', ['-xOf', archivePath, '--', binaryName], { timeout: 30000, maxBuffer: 10485760 });
    if (extraction.error || extraction.status !== 0) throw new Error('The verified executable could not be extracted. Nothing was installed.');
    if (extraction.stdout.length < 1 || extraction.stdout.length > 10485760) throw new Error('The verified archive does not contain one executable within the native size budget.');
    const binary = join(temporary, binaryName);
    await writeFile(binary, extraction.stdout, { flag: 'wx' });
    if (platform !== 'win32') await chmod(binary, 0o755);
    await mkdir(dirname(target), { recursive: true });
    await copyFile(binary, target, constants.COPYFILE_EXCL);
    return target;
  } finally {
    if (dirname(temporary) !== resolve(tmpdir())) throw new Error('The installer cleanup path is outside its temporary directory.');
    await rm(temporary, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (Number(process.versions.node.split('.')[0]) < 26) throw new Error('Install Node.js 26 or later before running this installer.');
    const options = {};
    for (let index = 2; index < process.argv.length; index++) {
      const option = process.argv[index];
      if (option === '--musl') { options.musl = true; continue; }
      const key = { '--version': 'version', '--public-key': 'publicKeyPath', '--destination': 'destination', '--source-directory': 'sourceDirectory' }[option];
      if (!key || options[key] !== undefined || !process.argv[index + 1] || process.argv[index + 1].startsWith('--')) throw new Error('Use --version VERSION --public-key TRUSTED.pem --destination DIRECTORY, with optional --source-directory DIRECTORY or --musl.');
      options[key] = process.argv[++index];
    }
    if (!options.publicKeyPath) throw new Error('Supply --public-key with an independently trusted public key file. The installer never downloads its trust key.');
    const keyInfo = await lstat(options.publicKeyPath);
    if (!keyInfo.isFile() || keyInfo.size > 16384) throw new Error('The trusted public key file is not a regular file within 16 KiB.');
    options.publicKey = await readFile(options.publicKeyPath);
    const installed = await installRelease(options);
    process.stdout.write(`Installed ${installed}. Add its directory to PATH, then run gatekeeper --version.\n`);
  } catch (error) {
    process.stderr.write(`Installation failed: ${error.message}\n`);
    process.exitCode = 1;
  }
}
