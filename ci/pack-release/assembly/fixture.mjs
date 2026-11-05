// SPDX-License-Identifier: Apache-2.0
// This test-only generator uses the existing development seed. It must never sign a released pack.
import { createPrivateKey, sign } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { assemblePack } from './build.mjs';

/** Generates the assembly round-trip fixture with an explicitly development-only key. */
export function fixtureFiles() {
  const keys = JSON.parse(readFileSync(new URL('../../../tests/vectors/pack/development-public-key.json', import.meta.url)));
  const key = keys.keys.find(item => item.keyId === 'gkpk-dev-1' && item.label === 'development-only' && item.purpose === 'pack-signing');
  if (!key) throw new Error('The labelled development-only pack key is unavailable.');
  const metadata = { id: 'invenio.synthetic.assembly', version: '2027.1.0', provenance: 'bundled', minEngineVersion: '0.1.0',
    coverageDate: '2027-01-01', contentIssuedAt: '2027-01-01', keyId: key.keyId };
  const source = id => ({ schemaVersion: 1, id: 'invenio.synthetic.source', version: metadata.version,
    provenance: 'bundled', minEngineVersion: '0.1.0', coverageDate: metadata.coverageDate, contentIssuedAt: metadata.contentIssuedAt,
    rules: [{ id, severity: 'error', category: 'synthetic', citation: { title: 'Synthetic assembly acceptance; no legal claim.' },
      sinceVersion: metadata.version, matcher: { list: { package: ['gatekeeper-assembly-probe'] } } }] });
  const { content, manifest } = assemblePack([source('pii.synthetic.zeta'), source('pii.synthetic.alpha')], metadata);
  const privateKey = createPrivateKey({ key: Buffer.concat([Buffer.from('302e020100300506032b657004220420', 'hex'), Buffer.from(key.seedHex, 'hex')]), format: 'der', type: 'pkcs8' });
  const signature = sign(null, manifest, privateKey).toString('hex');
  const armour = ['-----BEGIN GATE-KEEPER RULE PACK MANIFEST-----', `Key-Id: ${key.keyId}`, manifest.toString(),
    '-----END GATE-KEEPER RULE PACK MANIFEST-----', ''].join('\n');
  return new Map([['assembly.pack.json', content], ['assembly.manifest.armour', Buffer.from(armour)],
    ['assembly.signature.hex', Buffer.from(signature + '\n')], ['assembly.manifest.payload.json', manifest]]);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  if (process.argv.length !== 3 || !['--write', '--check'].includes(process.argv[2])) throw new Error('Use --write or --check for the development fixture.');
  for (const [name, bytes] of fixtureFiles()) {
    const target = new URL(`../../../tests/vectors/pack/${name}`, import.meta.url);
    if (process.argv[2] === '--write') writeFileSync(target, bytes);
    else if (!readFileSync(target).equals(bytes)) throw new Error('The development assembly fixture differs. Regenerate it.');
  }
  console.log('Development-only assembly fixture verified.');
}
