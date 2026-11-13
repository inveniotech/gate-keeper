// SPDX-License-Identifier: Apache-2.0
import { createHash, createPublicKey, verify } from 'node:crypto';
import { PackLoadError, RuntimeMatcher } from './runtime.ts';
import type { PackVersion, Rule } from './runtime.ts';

/** Container-free signed bytes. No bytes are re-serialised for signature or digest verification. */
export interface SignedPackInput {
  readonly name: string;
  readonly contentBytes: Uint8Array;
  readonly manifestArmour: string;
  readonly manifestSignature: Uint8Array;
  readonly watermarkArmour?: string;
  readonly watermarkSignature?: Uint8Array;
}
/** Caller-approved raw 32-byte Ed25519 public keys, separated by purpose. No keys ship in this module. */
export interface KeyCatalogue {
  readonly pack: Readonly<Record<string, Uint8Array>>;
  readonly watermark?: Readonly<Record<string, Uint8Array>>;
}
/** A fully verified candidate, including the core and metadata used by installation loading. */
export interface VerifiedPack {
  readonly pack: PackVersion;
  readonly rules: readonly Rule[];
  readonly runtime: RuntimeMatcher;
}

/** Verifies pure Ed25519 with Node crypto. Malformed keys or signatures return false. */
export function verifyEd25519(key: Uint8Array, message: Uint8Array, signature: Uint8Array): boolean {
  if (key.length !== 32 || signature.length !== 64) return false;
  try {
    const publicKey = createPublicKey({ key: { kty: 'OKP', crv: 'Ed25519', x: Buffer.from(key).toString('base64url') }, format: 'jwk' });
    return verify(null, message, publicKey, signature);
  } catch { return false; }
}

const limit = 16 * 1024 * 1024;
const identifier = /^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)*$/;
const assert = (condition: unknown): void => { if (!condition) throw new Error('Invalid pack data.'); };
const string = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.length > 0 && value.every(string);
function object(value: unknown, allowed: string[], required: string[] = []): Record<string, unknown> {
  assert(value !== null && typeof value === 'object' && !Array.isArray(value));
  const result = value as Record<string, unknown>;
  assert(Object.keys(result).every((key) => allowed.includes(key)) && required.every((key) => Object.hasOwn(result, key)));
  return result;
}

// Bound nesting before JSON.parse allocates a tree. Track object keys to reject duplicate JSON properties.
function parse(text: string, maxBytes: number): unknown {
  assert(Buffer.byteLength(text) > 0 && Buffer.byteLength(text) <= maxBytes && text.charCodeAt(0) !== 0xfeff);
  const stack: ({ keys: Set<string>; object: boolean } )[] = [];
  let expectingKey = false;
  for (let at = 0; at < text.length; at++) {
    const c = text[at];
    if (c === '"') {
      const start = at++;
      for (; at < text.length; at++) {
        if (text[at] === '\\') at++;
        else if (text[at] === '"') break;
      }
      assert(at < text.length);
      if (expectingKey && stack.at(-1)?.object) {
        const key = JSON.parse(text.slice(start, at + 1)) as string;
        const keys = stack.at(-1)!.keys; assert(!keys.has(key)); keys.add(key);
      }
      expectingKey = false;
    } else if (c === '{' || c === '[') {
      stack.push({ keys: new Set(), object: c === '{' }); assert(stack.length <= 64); expectingKey = c === '{';
    } else if (c === '}' || c === ']') { stack.pop(); expectingKey = false; }
    else if (c === ',') expectingKey = stack.at(-1)?.object ?? false;
    else if (c === ':') expectingKey = false;
  }
  return JSON.parse(text) as unknown;
}

function armour(text: string, watermark: boolean): { header: string; payload: string; data: Record<string, unknown> } {
  assert(typeof text === 'string' && text.length <= 8192);
  const lines = text.split('\n'); if (lines.at(-1) === '') lines.pop();
  const clean = lines.map((line) => line.endsWith('\r') ? line.slice(0, -1) : line);
  const label = watermark ? 'WATERMARK' : 'MANIFEST';
  assert(clean.length === 4 && clean[0] === `-----BEGIN GATE-KEEPER RULE PACK ${label}-----`
    && clean[3] === `-----END GATE-KEEPER RULE PACK ${label}-----` && clean[1]!.startsWith('Key-Id: '));
  const payload = clean[2]!;
  const fields = watermark ? ['keyId', 'contentDigest', 'organisationName', 'licenceId', 'generatedAt'] : ['keyId', 'packId', 'version', 'contentHashes'];
  const data = object(parse(payload, 4096), fields, fields);
  assert(Object.keys(data).join(',') === fields.join(',') && fields.filter((key) => key !== 'contentHashes').every((key) => string(data[key])));
  let inString = false;
  for (let at = 0; at < payload.length; at++) {
    if (inString && payload[at] === '\\') at++;
    else if (payload[at] === '"') inString = !inString;
    else if (!inString) assert(!/[ \t\r\n]/.test(payload[at]!));
  }
  assert(clean[1]!.slice(8) === data.keyId);
  return { header: clean[1]!.slice(8), payload, data };
}

/** Parses the calendar version numerically; rejects invalid calendar months and unsafe integers. */
export function calendarVersion(value: unknown): readonly number[] {
  assert(typeof value === 'string' && /^[0-9]{4}\.[0-9]{1,2}\.[0-9]+$/.test(value));
  const parts = (value as string).split('.').map(Number);
  assert(parts.every(Number.isSafeInteger) && parts[0]! >= 1 && parts[1]! >= 1 && parts[1]! <= 12);
  return parts;
}
function date(value: unknown): string {
  assert(typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value));
  const text = value as string;
  assert(Number(text.slice(0, 4)) > 0 && new Date(text).toISOString().slice(0, 10) === text);
  return text;
}
function validateRule(value: unknown): Rule {
  const r = object(value, ['id', 'severity', 'category', 'citation', 'sinceVersion', 'redaction', 'profiles', 'suppressionHints', 'processor', 'matcher'], ['id', 'severity', 'category', 'citation', 'sinceVersion', 'matcher']);
  assert(string(r.id) && identifier.test(r.id) && !r.id.startsWith('local.') && string(r.category) && ['error', 'warning', 'note'].includes(r.severity as string));
  calendarVersion(r.sinceVersion);
  const citation = object(r.citation, ['title', 'ndpa', 'gaid'], ['title']); assert(Object.values(citation).every(string));
  if (r.redaction !== undefined) { const redaction = object(r.redaction, ['optional']); assert(redaction.optional === undefined || typeof redaction.optional === 'boolean'); }
  if (r.profiles !== undefined) assert(strings(r.profiles) && r.profiles.length <= 2 && new Set(r.profiles).size === r.profiles.length && r.profiles.every((p) => ['default', 'deep'].includes(p)));
  if (r.suppressionHints !== undefined) assert(strings(r.suppressionHints));
  if (r.processor !== undefined) { const p = object(r.processor, ['dataCategories', 'usualRegion'], ['dataCategories', 'usualRegion']); assert(strings(p.dataCategories) && string(p.usualRegion)); }
  const matcher = object(r.matcher, ['pattern', 'list']); assert(Object.keys(matcher).length === 1);
  if (matcher.list !== undefined) { const list = object(matcher.list, ['package', 'hostname']); assert(Object.keys(list).length > 0 && Object.values(list).every(strings)); }
  else {
    const p = object(matcher.pattern, ['shape', 'boundary', 'validator', 'keywords', 'window', 'location', 'exclusions', 'excludePrefixes', 'excludeValues'], ['shape', 'boundary']);
    assert(Array.isArray(p.shape));
    for (const part of p.shape as unknown[]) {
      const s = object(part, ['min', 'max', 'emit', 'literal', 'class', 'ranges', 'oneOf'], ['min', 'max']);
      assert(s.emit === undefined || typeof s.emit === 'boolean');
      if (s.oneOf !== undefined) assert(strings(s.oneOf));
      if (s.ranges !== undefined) { assert(Array.isArray(s.ranges)); for (const range of s.ranges as unknown[]) { const pair = object(range, ['low', 'high'], ['low', 'high']); assert(string(pair.low) && string(pair.high)); } }
    }
    for (const field of ['keywords', 'exclusions', 'excludePrefixes', 'excludeValues']) if (p[field] !== undefined) assert(strings(p[field]));
    if (p.keywords !== undefined) assert((p.keywords as string[]).every((word) => /^[\x20-\x7e]+$/.test(word)));
    if (p.window !== undefined) assert(Number.isInteger(p.window) && (p.window as number) >= 1 && (p.window as number) <= 4096);
    if (p.exclusions !== undefined) assert(new Set(p.exclusions as string[]).size === (p.exclusions as string[]).length && (p.exclusions as string[]).every((name) => ['repeated-digits', 'sequential-digits', 'hex-fragment', 'uuid-fragment', 'version-string', 'date-or-timestamp'].includes(name)));
    if (p.validator !== undefined) {
      const v = object(p.validator, ['name', 'length', 'prefixes'], ['name']);
      if (v.name === 'digits-length') assert(Number.isInteger(v.length) && (v.length as number) >= 1 && (v.length as number) <= 256);
      else assert(v.length === undefined);
      if (v.name === 'ng-phone-prefix') assert(strings(v.prefixes) && v.prefixes.every((prefix) => /^[0-9]+$/.test(prefix)));
      else assert(v.prefixes === undefined);
    }
    if (p.location !== undefined) {
      const l = object(p.location, ['kind', 'paths'], ['kind']); assert(['log', 'migration', 'api-model', 'configuration', 'fixture', 'source'].includes(l.kind as string));
      if (l.paths !== undefined) assert(strings(l.paths) && l.paths.every((path) => /^[A-Za-z0-9._*/-]+$/.test(path) && !path.startsWith('/') && path.split('/').every((segment) => segment !== '..' && segment !== '.' && segment.length > 0)));
    }
  }
  // The existing core owns shape bounds, atom validity and the closed validator registry.
  return r as unknown as Rule;
}

/** Verifies one pack or throws PackLoadError naming the supplied pack and recovery action, never content. */
export function verifySignedPack(input: SignedPackInput, keys: KeyCatalogue): VerifiedPack {
  let stage = 'manifest signature or format'; let incompatible = false;
  try {
    const manifest = armour(input.manifestArmour, false);
    const hashes = object(manifest.data.contentHashes, ['sha256', 'sha512'], ['sha256', 'sha512']);
    assert(Object.keys(hashes).join(',') === 'sha256,sha512' && typeof hashes.sha256 === 'string' && /^[a-f0-9]{64}$/.test(hashes.sha256)
      && typeof hashes.sha512 === 'string' && /^[a-f0-9]{128}$/.test(hashes.sha512));
    const key = Object.hasOwn(keys.pack, manifest.header) ? keys.pack[manifest.header] : undefined;
    assert(key && verifyEd25519(key, Buffer.from(manifest.payload), input.manifestSignature));
    stage = 'content digest';
    assert(input.contentBytes.length > 0 && input.contentBytes.length <= limit);
    assert(createHash('sha256').update(input.contentBytes).digest('hex') === hashes.sha256 && createHash('sha512').update(input.contentBytes).digest('hex') === hashes.sha512);
    stage = 'content schema or engine compatibility (engine 0.2.0); upgrade the adapter if required';
    const text = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(input.contentBytes);
    const parsed = parse(text, limit);
    // A future schema can introduce fields unknown to version one and must still refuse start-up.
    if (parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)) {
      const facts = parsed as Record<string, unknown>;
      if (Object.hasOwn(facts, 'schemaVersion')) incompatible = facts.schemaVersion !== 1;
      if (typeof facts.minEngineVersion === 'string' && /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(facts.minEngineVersion)) {
        const minimum = facts.minEngineVersion.split('.').map(Number);
        incompatible ||= !(minimum[0] === 0 && (minimum[1]! < 2 || minimum[1] === 2 && minimum[2] === 0));
      }
    }
    assert(!incompatible);
    const doc = object(parsed, ['schemaVersion', 'id', 'version', 'provenance', 'minEngineVersion', 'coverageDate', 'contentIssuedAt', 'baseVersion', 'rules'], ['schemaVersion', 'id', 'version', 'provenance', 'minEngineVersion', 'coverageDate', 'contentIssuedAt', 'rules']);
    assert(doc.schemaVersion === 1 && string(doc.id) && ['bundled', 'plan-gated'].includes(doc.provenance as string));
    assert(typeof doc.minEngineVersion === 'string' && /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(doc.minEngineVersion));
    const minimum = (doc.minEngineVersion as string).split('.').map(Number);
    incompatible = !(minimum[0] === 0 && (minimum[1]! < 2 || minimum[1] === 2 && minimum[2] === 0));
    assert(minimum.every(Number.isSafeInteger) && !incompatible);
    const version = calendarVersion(doc.version); const coverage = date(doc.coverageDate); const issued = date(doc.contentIssuedAt);
    if (version[2] === 0) assert(doc.baseVersion === undefined && coverage === issued);
    else { const base = calendarVersion(doc.baseVersion); assert(base[0] === version[0] && base[1] === version[1] && base[2] === 0 && coverage <= issued); }
    assert(doc.id === manifest.data.packId && doc.version === manifest.data.version && Array.isArray(doc.rules));
    const rules = (doc.rules as unknown[]).map(validateRule);
    const pack = Object.freeze({ id: doc.id as string, version: doc.version as string, contentDigest: hashes.sha256 as string });
    const runtime = new RuntimeMatcher(rules, [], undefined, [pack]);
    stage = 'watermark signature, format or digest';
    assert((input.watermarkArmour === undefined) === (input.watermarkSignature === undefined));
    if (input.watermarkArmour !== undefined) {
      const block = armour(input.watermarkArmour, true);
      const key = keys.watermark && Object.hasOwn(keys.watermark, block.header) ? keys.watermark[block.header] : undefined;
      assert(key && verifyEd25519(key, Buffer.from(block.payload), input.watermarkSignature!));
      const generated = block.data.generatedAt as string;
      assert(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/.test(generated) && new Date(generated).toISOString() === generated.replace('Z', '.000Z'));
      assert(block.data.contentDigest === pack.contentDigest);
    }
    return Object.freeze({ pack, rules: Object.freeze(rules), runtime });
  } catch { throw new PackLoadError(`The signed pack '${input.name}' failed its ${stage} checks. Reinstall a compatible signed pack from trusted release media.`, incompatible); }
}
