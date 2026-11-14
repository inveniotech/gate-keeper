// SPDX-License-Identifier: Apache-2.0
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { sorted } from '../pack-release/assembly/build.mjs';
/** Copies authored detection rules into the explicitly unsigned local dogfood namespace. */
export function buildPack() {
  const names = ['bvn', 'email', 'locations', 'nin', 'phone', 'processors'];
  const sources = names.map(name => JSON.parse(readFileSync(new URL('../../rule-packs/' + name + '.pack.json', import.meta.url))));
  const rules = sources.flatMap(pack => pack.rules.map(rule => ({ ...rule, id: 'local.' + rule.id, sinceVersion: 1 })));
  rules.sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
  if (new Set(rules.map(rule => rule.id)).size !== rules.length) throw new Error('Dogfood rule identifiers must be unique.');
  return JSON.stringify(sorted({ schemaVersion: 1, id: 'local.dogfood', version: 1,
    minEngineVersion: sources.map(pack => pack.minEngineVersion).sort((a,b) => a.split('.').map(Number).reduce((order, part, index) => order || part - Number(b.split('.')[index]), 0)).at(-1),
    provenance: 'local', rules }), null, 2) + '\n';
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [mode, path] = process.argv.slice(2);
  if (!['--write', '--check'].includes(mode) || !path) throw new Error('Use build-pack.mjs --write|--check <pack path>.');
  const bytes = buildPack();
  if (mode === '--write') writeFileSync(path, bytes);
  else if (readFileSync(path, 'utf8') !== bytes) throw new Error('Regenerate the dogfood pack from the authored detection rules.');
}
