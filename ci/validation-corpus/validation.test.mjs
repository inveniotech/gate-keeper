// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validateManifest, checkInspection } from './validation.mjs';

const row = { repository: 'example/project', revision: 'a'.repeat(40), licence: 'MIT', licencePath: 'LICENSE',
  licenceSha256: 'b'.repeat(64), pinnedOn: '2026-11-05', stack: 'dotnet', role: 'client-library', triage: 'pending' };
test('validation manifest rejects unsafe repository paths, floating refs and unsupported licences', () => {
  for (const change of [{ repository: '../escape' }, { revision: 'master' }, { licence: 'GPL-3.0' },
    { licencePath: '../LICENSE' }, { licenceSha256: 'unknown' }, { pinnedOn: '2026-02-30' }]) {
    assert.throws(() => validateManifest({ schemaVersion: 1, repositories: [{ ...row, ...change }] }));
  }
});
test('validation manifest rejects duplicate or unsorted pins', () => {
  assert.throws(() => validateManifest({ schemaVersion: 1, repositories: [row, row] }));
  assert.throws(() => validateManifest({ schemaVersion: 1, repositories: [row, { ...row, repository: 'a/project' }] }));
});
test('checkout eligibility enforces exact file and byte bounds', () => {
  for (const eligibleFiles of [200, 20000]) assert.doesNotThrow(() => checkInspection({ eligibleFiles, totalBytes: 268435456 }));
  for (const inspection of [{ eligibleFiles: 199, totalBytes: 1 }, { eligibleFiles: 20001, totalBytes: 1 },
    { eligibleFiles: 200, totalBytes: 268435457 }, { eligibleFiles: 200.5, totalBytes: 1 }]) {
    assert.throws(() => checkInspection(inspection));
  }
});
test('the committed pins cover each launch stack and a Nigerian-company publisher', () => {
  const manifest = JSON.parse(readFileSync(new URL('../../tests/corpus/validation/repos.json', import.meta.url)));
  validateManifest(manifest);
  assert.ok(manifest.repositories.some(repo => repo.role === 'nigerian-company'));
  for (const stack of ['dotnet', 'jvm', 'node']) assert.ok(manifest.repositories.some(repo => repo.stack === stack));
});
test('the committed pins record the owner selection acceptance with reviewer metadata', () => {
  const manifest = JSON.parse(readFileSync(new URL('../../tests/corpus/validation/repos.json', import.meta.url)));
  const accepted = new Date(`${manifest.reviewedOn}T00:00:00Z`);
  assert.ok(!Number.isNaN(accepted.getTime()));
  assert.equal(accepted.toISOString().slice(0, 10), manifest.reviewedOn);
  assert.equal(manifest.status, 'accepted');
  assert.equal(manifest.reviewedBy, 'owner');
  for (const repo of manifest.repositories) {
    assert.ok(accepted >= new Date(`${repo.pinnedOn}T00:00:00Z`), 'The acceptance cannot precede its pin date.');
  }
});
