// SPDX-License-Identifier: Apache-2.0
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { checkCorrection, parseCalendarVersion } from '../corrections/check.mjs';

function civilDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || Number(value.slice(0, 4)) < 1
    || !Number.isFinite(Date.parse(`${value}T00:00:00Z`))
    || new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) !== value) throw new Error('Supply valid civil dates.');
}

function sorted(value) {
  if (Array.isArray(value)) return value.map(sorted);
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(Object.keys(value).sort().map(key => [key, sorted(value[key])]));
  }
  return value;
}

function validateContent(bytes) {
  if (bytes.length > 16 * 1024 * 1024) throw new Error('A content pack exceeds 16 MiB.');
  const script = fileURLToPath(new URL('./Validate-Content.ps1', import.meta.url));
  const result = spawnSync('pwsh', ['-NoProfile', '-File', script], { input: bytes, timeout: 10000, maxBuffer: 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error('Content does not validate against the rule-pack schema, or the schema tool is unavailable.');
}

/** Assembles schema-valid deterministic content and exact unsigned manifest bytes; never signs or reads keys. */
export function assemblePack(inputs, metadata, base) {
  const allowed = new Set(['id', 'version', 'provenance', 'minEngineVersion', 'coverageDate', 'contentIssuedAt', 'baseVersion', 'keyId']);
  if (!metadata || Object.keys(metadata).some(key => !allowed.has(key))
    || typeof metadata.id !== 'string' || !metadata.id || !['bundled', 'plan-gated'].includes(metadata.provenance)
    || !/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(metadata.keyId ?? '')) throw new Error('Supply the explicit release metadata and public signing-key identifier.');
  const version = parseCalendarVersion(metadata.version);
  civilDate(metadata.coverageDate);
  civilDate(metadata.contentIssuedAt);
  if (metadata.coverageDate > metadata.contentIssuedAt || (version[2] === 0 && metadata.coverageDate !== metadata.contentIssuedAt)
    || (version[2] === 0 && metadata.baseVersion !== undefined)) throw new Error('The pack dates or base version are inconsistent with its line.');
  if (!Array.isArray(inputs) || inputs.length === 0) throw new Error('Supply at least one content document.');
  const rules = [];
  const ids = new Set();
  for (const source of inputs) {
    validateContent(Buffer.from(JSON.stringify(source)));
    for (const rule of source.rules) {
      parseCalendarVersion(rule.sinceVersion);
      if (rule.id.startsWith('local.') || ids.has(rule.id)) throw new Error('Release rule identifiers must be unique and cannot use the local namespace.');
      ids.add(rule.id);
      rules.push(structuredClone(rule));
    }
  }
  rules.sort((left, right) => left.id < right.id ? -1 : left.id > right.id ? 1 : 0);
  const document = { schemaVersion: 1, id: metadata.id, version: metadata.version, provenance: metadata.provenance,
    minEngineVersion: metadata.minEngineVersion, coverageDate: metadata.coverageDate, contentIssuedAt: metadata.contentIssuedAt,
    ...(metadata.baseVersion !== undefined ? { baseVersion: metadata.baseVersion } : {}), rules };
  if (version[2] > 0) checkCorrection(base, document);
  const content = Buffer.from(JSON.stringify(sorted(document), null, 2) + '\n');
  validateContent(content);
  const contentHashes = { sha256: createHash('sha256').update(content).digest('hex'), sha512: createHash('sha512').update(content).digest('hex') };
  const manifest = Buffer.from(JSON.stringify({ keyId: metadata.keyId, packId: metadata.id, version: metadata.version, contentHashes }));
  if (manifest.length > 4096) throw new Error('The unsigned manifest exceeds 4,096 bytes.');
  return { content, manifest };
}

function readDocument(path) {
  if (statSync(path).size > 16 * 1024 * 1024) throw new Error('An input exceeds 16 MiB.');
  return JSON.parse(readFileSync(path, 'utf8'));
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  try {
    const { values, positionals } = parseArgs({ options: { metadata: { type: 'string' }, output: { type: 'string' }, base: { type: 'string' } }, allowPositionals: true });
    if (!values.metadata || !values.output || positionals.length === 0) throw new Error('Supply metadata, output and source content paths.');
    const result = assemblePack(positionals.map(readDocument), readDocument(values.metadata), values.base ? readDocument(values.base) : undefined);
    const destination = resolve(values.output);
    mkdirSync(destination);
    writeFileSync(resolve(destination, 'pack.json'), result.content, { flag: 'wx' });
    writeFileSync(resolve(destination, 'manifest.payload.json'), result.manifest, { flag: 'wx' });
    console.log('Assembled validated content and an unsigned manifest. No signing or release has occurred.');
  } catch {
    console.error('Pack assembly failed. Check source schemas, unique rule identifiers, explicit release metadata, correction base and a new output directory.');
    process.exitCode = 2;
  }
}
