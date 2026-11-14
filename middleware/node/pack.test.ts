// SPDX-License-Identifier: Apache-2.0
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapedFixture, fixture, keys, signedDocument } from './test-fixtures.ts';
import { readFileSync } from 'node:fs';
import { verifyEd25519, verifySignedPack } from './pack.ts';
import { PackLoadError } from './runtime.ts';

for (const stem of ['pack', 'next-line', 'plan-gated']) test(`Signed pack should load verified ${stem}`, () => {
  const loaded = verifySignedPack(fixture(stem), keys);
  assert.equal(loaded.runtime.versions().packs[0]!.id, loaded.pack.id);
  assert.equal(loaded.runtime.redact(''), '');
});
for (const stem of ['tampered-content', 'tampered-signature', 'unknown-key', 'key-id-mismatch', 'schema-major', 'newer-engine', 'local-rule-in-bundled', 'base-version-wrong', 'coverage-after-issued'])
  test(`Signed pack should reject ${stem}`, () => assert.throws(() => verifySignedPack(fixture(stem), keys), PackLoadError));
test('Signed pack should verify CRLF armour without changing signed bytes', () => {
  const input = fixture(); input.manifestArmour = input.manifestArmour.replace(/\r?\n/g, '\r\n');
  assert.equal(verifySignedPack(input, keys).pack.version, '2027.1.0');
});
for (const kind of ['watermark', 'watermark-digest-mismatch', 'watermark-tampered-signature']) test(`Signed pack should check ${kind}`, () => {
  const read = (name: string) => readFileSync(new URL('../../tests/vectors/pack/' + name, import.meta.url));
  const input = { ...fixture(), watermarkArmour: read(`${kind === 'watermark-tampered-signature' ? 'watermark' : kind}.manifest.armour`).toString(),
    watermarkSignature: Buffer.from(read(`${kind}.signature.hex`).toString().trim(), 'hex') };
  if (kind === 'watermark') assert.equal(verifySignedPack(input, keys).pack.version, '2027.1.0');
  else assert.throws(() => verifySignedPack(input, keys), PackLoadError);
});
test('Signed pack should reject half watermark and malformed armour', () => {
  assert.throws(() => verifySignedPack({ ...fixture(), watermarkArmour: 'invalid' }, keys), PackLoadError);
  assert.throws(() => verifySignedPack({ ...fixture(), manifestArmour: 'invalid' }, keys), PackLoadError);
  assert.throws(() => verifySignedPack({ ...fixture(), manifestSignature: new Uint8Array(63) }, keys), PackLoadError);
});
for (const [name, change] of [
  ['local provenance', (doc: Record<string, unknown>) => { doc.provenance = 'local'; }],
  ['unknown content property', (doc: Record<string, unknown>) => { doc.unrecognised = true; }],
  ['invalid rule severity', (doc: Record<string, unknown>) => { (doc.rules as Record<string, unknown>[])[0]!.severity = 'unsafe'; }],
  ['non-boolean optional flag', (doc: Record<string, unknown>) => { (doc.rules as Record<string, unknown>[])[0]!.redaction = { optional: 'false' }; }],
  ['invalid calendar month', (doc: Record<string, unknown>) => { doc.version = '2027.13.0'; }],
] as const) test(`Signed pack should reject verified ${name}`, () => assert.throws(() => verifySignedPack(signedDocument(change), keys), PackLoadError));
test('Signed pack errors should name recovery and never echo content', () => {
  const input = fixture('tampered-content');
  assert.throws(() => verifySignedPack(input, keys), (error: unknown) => error instanceof PackLoadError && error.message.includes(input.name)
    && error.message.includes('Reinstall') && !error.message.includes(input.contentBytes.toString()));
});
for (const watermark of [false, true]) test(`Signed pack should verify exact legally escaped ${watermark ? 'watermark' : 'manifest'} bytes`, () => {
  assert.equal(verifySignedPack(escapedFixture(watermark), keys).pack.version, '2027.1.0');
});

// RFC 8032 section 7.1, TEST 1, TEST 2 and TEST 3; public verification material only.
const rfc = [
  ['d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a', '', 'e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b'],
  ['3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c', '72', '92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00'],
  ['fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025', 'af82', '6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a'],
];
for (const [index, vector] of rfc.entries()) test(`Ed25519 should verify RFC 8032 TEST ${index + 1} and reject tampering`, () => {
  const [key, message, signature] = vector.map((hex) => Buffer.from(hex, 'hex'));
  assert.equal(verifyEd25519(key!, message!, signature!), true);
  signature![0] = signature![0]! ^ 1;
  assert.equal(verifyEd25519(key!, message!, signature!), false);
});
