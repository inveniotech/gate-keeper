// SPDX-License-Identifier: Apache-2.0
import { copyFileSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';

// Only the repository's labelled development media is copied; no production key is read or generated.
const source = new URL('../tests/vectors/pack/', import.meta.url);
const destination = new URL('../artifacts/sample-media/', import.meta.url);
mkdirSync(destination, { recursive: true });
for (const name of ['pack.pack.json', 'pack.manifest.armour', 'pack.signature.hex']) copyFileSync(new URL(name, source), new URL(name, destination));
const catalogue = JSON.parse(readFileSync(new URL('development-public-key.json', source), 'utf8'));
const keys = catalogue.keys.map(({ keyId, purpose, publicKeyHex }) => ({ keyId, publicKeyHex, purpose }));
writeFileSync(new URL('public-keys.json', destination), JSON.stringify({ keys }, null, 2) + '\n');
