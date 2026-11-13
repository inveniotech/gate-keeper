// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildPack } from './build-pack.mjs';
test('dogfood includes every authored detection rule with only local identity and version changes', () => {
  const actual = JSON.parse(buildPack());
  const expected = ['bvn', 'email', 'locations', 'nin', 'phone', 'processors'].flatMap(name =>
    JSON.parse(readFileSync(new URL('../../rule-packs/' + name + '.pack.json', import.meta.url))).rules
      .map(rule => ({ ...rule, id: 'local.' + rule.id, sinceVersion: 1 })));
  expected.sort((a,b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
  assert.deepEqual(actual.rules, expected);
  assert.equal(actual.provenance, 'local');
  assert.equal(actual.rules.length, new Set(actual.rules.map(rule => rule.id)).size);
  assert.equal(buildPack(), buildPack());
  assert.equal(readFileSync(new URL('../../.gatekeeper/dogfood.pack.json', import.meta.url), 'utf8'), buildPack());
});
