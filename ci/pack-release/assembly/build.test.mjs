// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, mkdtempSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { assemblePack } from './build.mjs';

const source = JSON.parse(readFileSync(new URL('../../../docs/specs/examples/rule-pack/minimal.json', import.meta.url)));
const metadata = { id: 'invenio.synthetic.assembly', version: '2027.1.0', provenance: 'bundled', minEngineVersion: '0.1.0',
  coverageDate: '2027-01-01', contentIssuedAt: '2027-01-01', keyId: 'gkpk-dev-1' };
test('assembly sorts rules and object keys and hashes the exact content bytes', () => {
  const other = { ...source, rules: [{ ...source.rules[0], id: 'pii.synthetic' }] };
  const first = assemblePack([source, other], metadata);
  const reverseKeys = value => Array.isArray(value) ? value.map(reverseKeys)
    : value !== null && typeof value === 'object' ? Object.fromEntries(Object.entries(value).reverse().map(([key, item]) => [key, reverseKeys(item)])) : value;
  const reversed = reverseKeys(other);
  const second = assemblePack([reversed, source], metadata);
  assert.deepEqual(first, second);
  assert.deepEqual(JSON.parse(first.content).rules.map(rule => rule.id), ['pii.bvn', 'pii.synthetic']);
  const manifest = JSON.parse(first.manifest);
  for (const algorithm of ['sha256', 'sha512']) assert.equal(manifest.contentHashes[algorithm], createHash(algorithm).update(first.content).digest('hex'));
  assert.match(first.manifest.toString(), /^\{"keyId":"gkpk-dev-1","packId":/);
  assert.equal(first.manifest.at(-1), 125);
  assert.equal(first.content.at(-1), 10);
});
test('the committed development assembly fixture is exactly reproducible', async () => {
  const { fixtureFiles } = await import('./fixture.mjs');
  for (const [name, bytes] of fixtureFiles()) {
    assert.deepEqual(readFileSync(new URL(`../../../tests/vectors/pack/${name}`, import.meta.url)), bytes, name);
  }
});
test('assembly rejects invalid versions, dates, duplicate rules and schema-invalid content', () => {
  for (const change of [{ version: '2027.13.0' }, { coverageDate: '2027-02-30' }, { contentIssuedAt: '2026-12-31' },
    { contentIssuedAt: '2027-01-02' }, { version: '2027.1.1' }, { minEngineVersion: 'invalid' }, { keyId: 'key\nheader' }]) {
    assert.throws(() => assemblePack([source], { ...metadata, ...change }));
  }
  assert.throws(() => assemblePack([source, source], metadata));
  assert.throws(() => assemblePack([{ ...source, rules: [{ ...source.rules[0], unexpected: 'synthetic' }] }], metadata));
});
test('assembly checks corrections against the supplied pinned base', () => {
  const base = JSON.parse(assemblePack([source], metadata).content);
  const corrected = { ...metadata, version: '2027.1.1', baseVersion: '2027.1.0', contentIssuedAt: '2027-01-02' };
  assert.doesNotThrow(() => assemblePack([source], corrected, base));
  assert.throws(() => assemblePack([{ ...source, rules: [{ ...source.rules[0], id: 'pii.new' }] }], corrected, base));
});
test('the command refuses existing destinations and leaves no output for invalid input', () => {
  const root = mkdtempSync(join(tmpdir(), 'gate-keeper-assembly-'));
  try {
    const input = join(root, 'source.json');
    const settings = join(root, 'metadata.json');
    writeFileSync(input, JSON.stringify(source));
    writeFileSync(settings, JSON.stringify(metadata));
    const output = join(root, 'output');
    const script = fileURLToPath(new URL('./build.mjs', import.meta.url));
    const invoke = destination => spawnSync(process.execPath, [script, '--metadata', settings, '--output', destination, input], { encoding: 'utf8' });
    assert.equal(invoke(output).status, 0);
    const original = readFileSync(join(output, 'pack.json'));
    assert.notEqual(invoke(output).status, 0);
    assert.deepEqual(readFileSync(join(output, 'pack.json')), original);
    writeFileSync(input, '{}');
    assert.notEqual(invoke(join(root, 'invalid-output')).status, 0);
    assert.equal(existsSync(join(root, 'invalid-output')), false);
  } finally {
    assert.equal(dirname(root), resolve(tmpdir()));
    rmSync(root, { recursive: true, force: true });
  }
});
