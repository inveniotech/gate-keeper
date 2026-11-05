// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { checkCorrection } from './check.mjs';

const base = JSON.parse(readFileSync(new URL('../../../docs/specs/examples/rule-pack/minimal.json', import.meta.url)));
const correction = () => ({ ...structuredClone(base), version: '2027.1.1', baseVersion: base.version, contentIssuedAt: '2027-01-02' });
test('a correction keeps or removes base identifiers', () => {
  assert.doesNotThrow(() => checkCorrection(base, correction()));
  const expanded = { ...base, rules: [...base.rules, { ...base.rules[0], id: 'pii.synthetic' }] };
  assert.doesNotThrow(() => checkCorrection(expanded, correction()));
});
test('a correction rejects additional or duplicate identifiers', () => {
  const added = correction();
  added.rules.push({ ...base.rules[0], id: 'pii.synthetic' });
  assert.throws(() => checkCorrection(base, added));
  const duplicate = correction();
  duplicate.rules.push(duplicate.rules[0]);
  assert.throws(() => checkCorrection(base, duplicate));
});
test('a correction rejects a different pack, provenance, line, base or non-increasing patch', () => {
  for (const change of [{ id: 'another.pack' }, { provenance: 'local' }, { version: '2027.2.1' },
    { baseVersion: '2027.1.1' }, { baseVersion: '2026.1.0' }, { version: '2027.1.0' }, { version: '2027.13.1' }]) {
    assert.throws(() => checkCorrection(base, { ...correction(), ...change }));
  }
  assert.throws(() => checkCorrection({ ...base, version: '2027.1.1' }, correction()));
});
test('calendar components are compared numerically', () => {
  const september = { ...base, version: '2027.9.0' };
  assert.doesNotThrow(() => checkCorrection(september, { ...correction(), version: '2027.09.10', baseVersion: '2027.9.0' }));
  assert.throws(() => checkCorrection(september, { ...correction(), version: '2027.10.1', baseVersion: '2027.9.0' }));
});
