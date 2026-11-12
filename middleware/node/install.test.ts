// SPDX-License-Identifier: Apache-2.0
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { loadInstallation } from './install.ts';
import { fixture, keys, signedDocument } from './test-fixtures.ts';
import { PackLoadError } from './runtime.ts';

function replacement(stem: string, run: (path: string) => void, half = false, input = fixture(stem)) {
  const path = mkdtempSync(join(tmpdir(), 'gate-keeper-node-'));
  try {
    writeFileSync(join(path, 'pack.json'), input.contentBytes);
    writeFileSync(join(path, 'manifest.armour'), input.manifestArmour);
    writeFileSync(join(path, 'manifest.signature'), input.manifestSignature);
    if (half) writeFileSync(join(path, 'watermark.armour'), 'invalid');
    run(path);
  } finally { rmSync(path, { recursive: true, force: true }); }
}
test('Installation should refuse absent or unverifiable embedded packs', () => {
  assert.throws(() => loadInstallation({ embedded: [], keys }), PackLoadError);
  assert.throws(() => loadInstallation({ embedded: [fixture('tampered-signature')], keys }), PackLoadError);
  assert.throws(() => loadInstallation({ embedded: [fixture(), fixture('tampered-signature')], keys }), PackLoadError);
});
test('Installation should activate a newer same-id replacement', () => replacement('next-line', (replacementDirectory) => {
  assert.equal(loadInstallation({ embedded: [fixture()], keys, replacementDirectory }).versions().packs[0]!.version, '2027.2.0');
}));
for (const stem of ['pack', 'rival']) test(`Installation should refuse equal or different-id ${stem}`, () => replacement(stem, (replacementDirectory) => {
  assert.throws(() => loadInstallation({ embedded: [fixture()], keys, replacementDirectory }), PackLoadError);
}));
test('Installation should refuse an older replacement', () => replacement('pack', (replacementDirectory) => {
  assert.throws(() => loadInstallation({ embedded: [fixture('next-line')], keys, replacementDirectory }), PackLoadError);
}));
for (const stem of ['tampered-signature', 'tampered-content']) test(`Installation should retain active pack after ${stem}`, () => replacement(stem, (replacementDirectory) => {
  const events: string[] = [];
  assert.equal(loadInstallation({ embedded: [fixture()], keys, replacementDirectory, diagnostic: (message) => events.push(message) }).versions().packs[0]!.version, '2027.1.0');
  assert.equal(events.length, 1);
}));
test('Installation should discard a half watermark replacement', () => replacement('next-line', (replacementDirectory) => {
  assert.equal(loadInstallation({ embedded: [fixture()], keys, replacementDirectory }).versions().packs[0]!.version, '2027.1.0');
}, true));
for (const stem of ['schema-major', 'newer-engine']) test(`Installation should refuse incompatible replacement ${stem}`, () => replacement(stem, (replacementDirectory) => {
  assert.throws(() => loadInstallation({ embedded: [fixture()], keys, replacementDirectory }), PackLoadError);
}));
test('Installation should refuse a newer schema even when it adds fields unknown to schema one', () => replacement('pack', (replacementDirectory) => {
  assert.throws(() => loadInstallation({ embedded: [fixture()], keys, replacementDirectory }),
    (error: unknown) => error instanceof PackLoadError && error.incompatible);
}, false, signedDocument((document) => { document.schemaVersion = 2; document.newFeature = true; })));
test('Installation should refuse a newer engine requirement even when the document also adds unknown fields', () => replacement('pack', (replacementDirectory) => {
  assert.throws(() => loadInstallation({ embedded: [fixture()], keys, replacementDirectory }),
    (error: unknown) => error instanceof PackLoadError && error.incompatible);
}, false, signedDocument((document) => { document.minEngineVersion = '100.0.0'; document.newFeature = true; })));
test('Installation should refuse mandatory and unknown disablement', () => {
  for (const id of ['pii.bvn', 'unknown.rule']) assert.throws(() => loadInstallation({ embedded: [fixture()], keys, disabled: [id], diagnostic: () => {} }), TypeError);
});
test('Installation should disable only verified optional rules and emit one diagnostic', () => {
  const input = signedDocument((doc) => {
    const rule = (doc.rules as Record<string, unknown>[])[0]!;
    rule.redaction = { optional: true }; rule.matcher = { list: { package: ['synthetic-marker'] } };
  });
  const messages: string[] = [];
  const runtime = loadInstallation({ embedded: [input], keys, disabled: ['pii.bvn'], diagnostic: (message) => messages.push(message) });
  assert.equal(runtime.redact('synthetic-marker'), 'synthetic-marker');
  assert.equal(loadInstallation({ embedded: [input], keys }).redact('synthetic-marker'), '[REDACTED:pii.bvn]');
  assert.deepEqual(messages, ['Disabled optional redaction rules: pii.bvn']);
  assert.throws(() => loadInstallation({ embedded: [input], keys, disabled: ['pii.bvn'] }), TypeError);
});
test('Installation should retain verified embedded packs when replacement is unreadable', () => {
  const path = mkdtempSync(join(tmpdir(), 'gate-keeper-node-'));
  try { assert.equal(loadInstallation({ embedded: [fixture()], keys, replacementDirectory: path }).versions().packs[0]!.version, '2027.1.0'); }
  finally { rmSync(path, { recursive: true, force: true }); }
});
