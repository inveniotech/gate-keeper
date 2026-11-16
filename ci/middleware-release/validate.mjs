// SPDX-License-Identifier: Apache-2.0
import assert from 'node:assert/strict';
export function validateVersion(actual, expected) {
  assert.match(expected, /^\d+\.\d+\.\d+$/);
  assert.equal(actual, expected, 'Package version must match the runtime version.');
}
export function validateNpmPackage(metadata, version) {
  assert.equal(metadata.name, '@inveniotech/gate-keeper');
  validateVersion(metadata.version, version);
  const paths = new Set(metadata.files.map(file => file.path));
  for (const path of ['README.md', 'LICENSE', 'package.json', ...['esm', 'cjs'].flatMap(kind =>
    ['index', 'express', 'nest', 'pino', 'winston'].flatMap(name => [`dist/${kind}/${name}.js`, `dist/${kind}/${name}.d.ts`]))]) {
    assert.ok(paths.has(path), `Package is missing ${path}.`);
  }
  for (const path of paths) {
    assert.ok(['README.md', 'LICENSE', 'package.json'].includes(path) || /^dist\/(esm|cjs)\/(package\.json|[\w-]+\.(js|d\.ts))$/.test(path), `Package contains unexpected file ${path}.`);
  }
}
