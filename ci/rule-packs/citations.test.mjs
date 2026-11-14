// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { approvedCitation } from './citations.mjs';

test('citation lookup refuses a draft register and unknown categories', () => {
  const entry = { category: 'pii.bvn', status: 'draft', citation: { title: 'Synthetic review test.' } };
  assert.throws(() => approvedCitation({ status: 'draft', entries: [entry] }, 'pii.bvn'), /approval/);
  assert.throws(() => approvedCitation({ status: 'approved', entries: [] }, 'pii.unknown'), /approval/);
});
test('citation lookup requires both register and category approval with review metadata', () => {
  const citation = { title: 'Synthetic review test.', ndpa: 'Synthetic section.', gaid: 'Synthetic article.' };
  const register = { status: 'approved', reviewedBy: 'owner', reviewedOn: '2026-11-05',
    entries: [{ category: 'pii.bvn', status: 'approved', citation }] };
  assert.deepEqual(approvedCitation(register, 'pii.bvn'), citation);
  assert.throws(() => approvedCitation({ ...register, reviewedBy: '' }, 'pii.bvn'), /approval/);
  assert.throws(() => approvedCitation({ ...register, entries: [{ ...register.entries[0], status: 'draft' }] }, 'pii.bvn'), /approval/);
});
test('citation lookup honours owner approval in principle while retaining pending legal review', () => {
  const citation = { title: 'Synthetic review test.', ndpa: 'Synthetic section.', gaid: 'Synthetic article.' };
  const register = { status: 'approved-in-principle', reviewedBy: 'owner', reviewedOn: '2026-11-05', legalReview: { status: 'pending' },
    entries: [{ category: 'pii.bvn', status: 'approved-in-principle', citation }] };
  assert.deepEqual(approvedCitation(register, 'pii.bvn'), citation);
  assert.throws(() => approvedCitation({ ...register, legalReview: undefined }, 'pii.bvn'), /approval/);
  assert.throws(() => approvedCitation({ ...register, reviewedBy: '' }, 'pii.bvn'), /approval/);
  assert.throws(() => approvedCitation({ ...register, entries: [{ ...register.entries[0], status: 'draft' }] }, 'pii.bvn'), /approval/);
});
test('the citation register covers the initial rule categories and pins official sources', () => {
  const register = JSON.parse(readFileSync(new URL('../../rule-packs/citations.json', import.meta.url)));
  assert.equal(register.schemaVersion, 1);
  assert.ok(['draft', 'approved-in-principle', 'approved'].includes(register.status));
  const categories = register.entries.map(entry => entry.category);
  assert.deepEqual(categories, [...new Set(categories)].sort());
  for (const category of ['consent.missing', 'pii.account', 'pii.bvn', 'pii.nin', 'pii.phone', 'pii.email',
    'pii.ng-11-digit-id', 'pii.in-logs', 'pii.in-fixtures', 'pii.in-configuration', 'pii.in-migrations', 'pii.in-api-models', 'transfer.processor']) {
    assert.ok(categories.includes(category), category);
  }
  for (const source of register.sources) {
    assert.equal(new URL(source.url).hostname, 'ndpc.gov.ng');
    assert.match(source.sha256, /^[a-f0-9]{64}$/);
  }
  for (const entry of register.entries) {
    assert.ok(['draft', 'approved-in-principle', 'approved'].includes(entry.status));
    for (const field of ['title', 'ndpa', 'gaid']) assert.ok(entry.citation[field]?.trim());
    assert.ok(entry.explanation?.trim());
    assert.ok(entry.limit?.trim());
    if (entry.status !== 'draft') approvedCitation(register, entry.category);
  }
});
