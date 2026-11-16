// SPDX-License-Identifier: Apache-2.0
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { validateNpmPackage, validateVersion } from './validate.mjs';

test('Rejects a package whose runtime stamp differs from its package version', () => {
  assert.throws(() => validateVersion('0.1.0', '0.2.0'), /version/);
});
test('Rejects npm archives that omit declarations or a public entry point', () => {
  const metadata = { name: '@inveniotech/gate-keeper', version: '0.1.0', files: [{ path: 'README.md' }] };
  assert.throws(() => validateNpmPackage(metadata, '0.1.0'), /missing/);
});
test('Rejects npm archives that leak tests or development files', () => {
  const files = ['README.md', 'LICENSE', 'package.json', ...['esm', 'cjs'].flatMap(kind =>
    ['', 'express', 'nest', 'pino', 'winston'].flatMap(name => {
      const base = `dist/${kind}/${name || 'index'}`;
      return [`${base}.js`, `${base}.d.ts`];
    })), 'pack.test.ts'].map(path => ({ path }));
  assert.throws(() => validateNpmPackage({ name: '@inveniotech/gate-keeper', version: '0.1.0', files }, '0.1.0'), /unexpected/);
});
