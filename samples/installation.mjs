// SPDX-License-Identifier: Apache-2.0
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { loadInstallation, ConsentCheck } from '@inveniotech/gate-keeper';
import { createPinoHooks } from '@inveniotech/gate-keeper/pino';
import pino from 'pino';

/** Reads an explicitly supplied installation; development trust is never implicit. */
export function installation(directory, keyFile) {
  const catalogue = JSON.parse(readFileSync(keyFile, 'utf8'));
  if (catalogue.keys.some(key => 'seedHex' in key)) throw new Error('Supply a public-only key catalogue; remove private key material before starting.');
  const pack = Object.fromEntries(catalogue.keys.filter(key => key.purpose === 'pack-signing').map(key => [key.keyId, Buffer.from(key.publicKeyHex, 'hex')]));
  return loadInstallation({ embedded: [{ name: 'sample', contentBytes: readFileSync(join(directory, 'pack.pack.json')),
    manifestArmour: readFileSync(join(directory, 'pack.manifest.armour'), 'utf8'),
    manifestSignature: Buffer.from(readFileSync(join(directory, 'pack.signature.hex'), 'utf8').trim(), 'hex') }], keys: { pack } });
}

/** Configures a real pino logger and a fixed synthetic provider, without retaining consent in the adapter. */
export function services(runtime, sink) {
  const logger = pino({ timestamp: false, base: null, hooks: createPinoHooks(runtime) }, sink);
  const consent = new ConsentCheck({ async getState(subject, purpose) {
    return subject === 'synthetic-subject' && purpose === 'acme.marketing-sms' ? 'granted' : 'denied';
  } }, diagnostic => logger.warn(diagnostic));
  return { logger, consent };
}
