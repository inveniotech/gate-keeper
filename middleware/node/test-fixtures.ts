// SPDX-License-Identifier: Apache-2.0
import { readFileSync } from 'node:fs';
import { createHash, createPrivateKey, sign } from 'node:crypto';
const root = new URL('../../tests/vectors/pack/', import.meta.url);
const read = (name: string) => readFileSync(new URL(name, root));
const catalogue = JSON.parse(read('development-public-key.json').toString()) as { keys: { keyId: string; publicKeyHex: string; seedHex: string; purpose: string }[] };
export const keys = {
  pack: Object.fromEntries(catalogue.keys.filter((key) => key.purpose === 'pack-signing').map((key) => [key.keyId, Buffer.from(key.publicKeyHex, 'hex')])),
  watermark: Object.fromEntries(catalogue.keys.filter((key) => key.purpose === 'pack-watermark').map((key) => [key.keyId, Buffer.from(key.publicKeyHex, 'hex')])),
};
export function fixture(stem = 'pack') {
  return { name: stem, contentBytes: read(`${['unknown-key', 'key-id-mismatch'].includes(stem) ? 'pack' : stem}.pack.json`),
    manifestArmour: read(`${stem}.manifest.armour`).toString(), manifestSignature: Buffer.from(read(`${stem}.signature.hex`).toString().trim(), 'hex') };
}

/** Re-sign synthetic negative cases with the repository's labelled development-only fixture key. */
export function signedDocument(change: (document: Record<string, unknown>) => void) {
  const input = fixture(); const document = JSON.parse(input.contentBytes.toString()) as Record<string, unknown>;
  change(document); const contentBytes = Buffer.from(JSON.stringify(document));
  const payload = JSON.stringify({ keyId: catalogue.keys[0]!.keyId, packId: document.id, version: document.version,
    contentHashes: { sha256: createHash('sha256').update(contentBytes).digest('hex'), sha512: createHash('sha512').update(contentBytes).digest('hex') } });
  const privateKey = createPrivateKey({ key: { kty: 'OKP', crv: 'Ed25519', x: Buffer.from(catalogue.keys[0]!.publicKeyHex, 'hex').toString('base64url'), d: Buffer.from(catalogue.keys[0]!.seedHex, 'hex').toString('base64url') }, format: 'jwk' });
  return { ...input, contentBytes, manifestArmour: input.manifestArmour.split('\n').map((line, index) => index === 2 ? payload : line).join('\n'), manifestSignature: sign(null, Buffer.from(payload), privateKey) };
}

/** Exercises legal JSON escaping while signing the exact escaped bytes with fixture keys. */
export function escapedFixture(watermark = false) {
  const input = fixture(); const lines = (watermark ? read('watermark.manifest.armour').toString() : input.manifestArmour).split('\n');
  const payload = lines[2]!.replace(watermark ? /("organisationName":")([^"\\])/ : /("packId":")([^"\\])/, (_match, prefix: string, first: string) => prefix + '\\u' + first.charCodeAt(0).toString(16).padStart(4, '0'));
  assertEscaped(payload); lines[2] = payload;
  const key = catalogue.keys[watermark ? 1 : 0]!;
  const privateKey = createPrivateKey({ key: { kty: 'OKP', crv: 'Ed25519', x: Buffer.from(key.publicKeyHex, 'hex').toString('base64url'), d: Buffer.from(key.seedHex, 'hex').toString('base64url') }, format: 'jwk' });
  const signature = sign(null, Buffer.from(payload), privateKey);
  return watermark ? { ...input, watermarkArmour: lines.join('\n'), watermarkSignature: signature } : { ...input, manifestArmour: lines.join('\n'), manifestSignature: signature };
}
function assertEscaped(payload: string) { if (!payload.includes('\\u')) throw new Error('The escaping fixture was not generated.'); }
