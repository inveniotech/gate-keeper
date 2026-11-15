// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, writeFile, readFile, rm, readdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { prepareAlphaRelease, validateAlphaTag } from './alpha-release.mjs';

const rids = ['linux-arm64', 'linux-musl-arm64', 'linux-musl-x64', 'linux-x64', 'osx-arm64', 'osx-x64', 'win-arm64', 'win-x64'];
const context = { tag: 'cli-v0.2.0-alpha.1', engineVersion: '0.2.0', commit: 'a'.repeat(40), repository: 'inveniotech/gate-keeper' };

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'gate-keeper-alpha-test-'));
  t.after(async () => {
    assert.equal(dirname(root), resolve(tmpdir()));
    await rm(root, { recursive: true, force: true });
  });
  const input = join(root, 'input');
  for (const rid of rids) {
    const folder = join(input, `native-${rid}`);
    await mkdir(folder, { recursive: true });
    const archive = `gatekeeper-${rid}.${rid.startsWith('win-') ? 'zip' : 'tar.gz'}`;
    const bytes = Buffer.from(`synthetic archive for ${rid}`);
    const sha256 = createHash('sha256').update(bytes).digest('hex');
    await writeFile(join(folder, archive), bytes);
    await writeFile(join(folder, `gatekeeper-${rid}.json`), JSON.stringify({ product: 'gatekeeper', productVersion: '0.2.0', runtimeIdentifier: rid, binaryBytes: 3, archive, sha256 }));
    await writeFile(join(folder, `gatekeeper-${rid}.sha256`), `${sha256}  ${archive}\n`);
  }
  await mkdir(join(input, 'native-release-review'));
  await writeFile(join(input, 'native-release-review', 'release-notes.md'), '# Synthetic benchmark measurements\n');
  return { input, output: join(root, 'output') };
}

test('alpha tag validation accepts a numbered alpha of the stamped engine version', () => {
  assert.equal(validateAlphaTag(context.tag, context.engineVersion), '0.2.0-alpha.1');
});

test('alpha tag validation rejects stable, malformed and mismatched versions', () => {
  for (const tag of ['cli-v0.2.0', 'cli-v0.2.0-beta.1', 'cli-v0.2.0-alpha.01', 'cli-v0.2.0-alpha.0', 'cli-v1.0.0-alpha.1', '../cli-v0.2.0-alpha.1', 'cli-v0.2.0-alpha.1\n']) {
    assert.throws(() => validateAlphaTag(tag, context.engineVersion), /alpha tag/);
  }
});

test('release assembly emits eight archives with exact sorted checksums and unsigned notes', async t => {
  const paths = await fixture(t);
  await prepareAlphaRelease({ ...paths, ...context });
  const sums = await readFile(join(paths.output, 'SHA256SUMS'), 'utf8');
  const entries = sums.trimEnd().split('\n');
  assert.equal(entries.length, 8);
  assert.deepEqual(entries.map(line => line.slice(66)), [...entries.map(line => line.slice(66))].sort());
  for (const line of entries) {
    assert.match(line, /^[a-f0-9]{64}  gatekeeper-[a-z0-9-]+\.(zip|tar\.gz)$/);
    const bytes = await readFile(join(paths.output, line.slice(66)));
    assert.equal(createHash('sha256').update(bytes).digest('hex'), line.slice(0, 64));
  }
  const notes = await readFile(join(paths.output, 'release-notes.md'), 'utf8');
  assert.match(notes, /Windows binaries are unsigned/);
  assert.match(notes, /no detached Ed25519 signature/);
  assert.match(notes, /not notarised/);
  assert.match(notes, /Synthetic benchmark measurements/);
  assert.match(notes, new RegExp(context.commit));
  assert.match(notes, /engine version 0\.2\.0/);
  assert.equal((await readdir(paths.output)).length, 10);
  const second = `${paths.output}-second`;
  await prepareAlphaRelease({ ...paths, ...context, output: second });
  for (const name of await readdir(paths.output)) {
    assert.deepEqual(await readFile(join(paths.output, name)), await readFile(join(second, name)));
  }
});

for (const [name, mutate, message] of [
  ['a missing platform', async input => rm(join(input, 'native-win-arm64'), { recursive: true }), /win-arm64/],
  ['a tampered archive', async input => writeFile(join(input, 'native-win-x64', 'gatekeeper-win-x64.zip'), 'tampered'), /checksum/],
  ['an incorrect sidecar', async input => writeFile(join(input, 'native-win-x64', 'gatekeeper-win-x64.sha256'), 'incorrect\n'), /sidecar/],
  ['an empty archive', async input => writeFile(join(input, 'native-win-x64', 'gatekeeper-win-x64.zip'), ''), /empty/],
  ['mismatched metadata', async input => {
    const path = join(input, 'native-win-x64', 'gatekeeper-win-x64.json');
    const record = JSON.parse(await readFile(path, 'utf8'));
    record.productVersion = '9.0.0';
    await writeFile(path, JSON.stringify(record));
  }, /metadata/],
  ['an unexpected archive', async input => writeFile(join(input, 'native-win-x64', 'unexpected.zip'), 'unexpected'), /unexpected/i],
  ['a traversal path in metadata', async input => {
    const path = join(input, 'native-win-x64', 'gatekeeper-win-x64.json');
    const record = JSON.parse(await readFile(path, 'utf8'));
    record.archive = '../../outside.zip';
    await writeFile(path, JSON.stringify(record));
  }, /metadata/],
  ['a binary over the size budget', async input => {
    const path = join(input, 'native-win-x64', 'gatekeeper-win-x64.json');
    const record = JSON.parse(await readFile(path, 'utf8'));
    record.binaryBytes = 10485761;
    await writeFile(path, JSON.stringify(record));
  }, /size budget/],
  ['missing measurements', async input => rm(join(input, 'native-release-review', 'release-notes.md')), /release-notes/],
]) {
  test(`release assembly rejects ${name} before writing release assets`, async t => {
    const paths = await fixture(t);
    await mutate(paths.input);
    await assert.rejects(prepareAlphaRelease({ ...paths, ...context }), message);
    await assert.rejects(readdir(paths.output), { code: 'ENOENT' });
  });
}

test('release assembly refuses to overwrite existing release assets', async t => {
  const paths = await fixture(t);
  await mkdir(paths.output);
  await writeFile(join(paths.output, 'sentinel'), 'retain');
  await assert.rejects(prepareAlphaRelease({ ...paths, ...context }), /exist/i);
  assert.equal(await readFile(join(paths.output, 'sentinel'), 'utf8'), 'retain');
});
