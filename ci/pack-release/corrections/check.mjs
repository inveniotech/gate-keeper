// SPDX-License-Identifier: Apache-2.0
import { readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

function version(value) {
  if (typeof value !== 'string' || !/^\d{4}\.\d{1,2}\.\d+$/.test(value)) throw new Error('Use a calendar pack version.');
  const parts = value.split('.').map(Number);
  if (!parts.every(Number.isSafeInteger) || parts[1] < 1 || parts[1] > 12) throw new Error('The calendar pack version is invalid.');
  return parts;
}

function identifiers(pack) {
  if (!Array.isArray(pack.rules) || pack.rules.length === 0) throw new Error('A content pack must contain rules.');
  const ids = new Set();
  for (const rule of pack.rules) {
    if (!/^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)*$/.test(rule?.id ?? '') || ids.has(rule.id)) {
      throw new Error('Rule identifiers must be valid and unique.');
    }
    ids.add(rule.id);
  }
  return ids;
}

/** Rejects corrections that change identity or line, or introduce identifiers outside their .0 base. */
export function checkCorrection(base, correction) {
  if (!base || !correction || typeof base.id !== 'string' || !base.id
    || base.schemaVersion !== 1 || correction.schemaVersion !== 1 || correction.id !== base.id
    || !['bundled', 'plan-gated'].includes(base.provenance) || correction.provenance !== base.provenance) {
    throw new Error('A correction must retain the content-pack identity, schema and provenance.');
  }
  const original = version(base.version);
  const candidate = version(correction.version);
  const declared = version(correction.baseVersion);
  if (original[2] !== 0 || candidate[2] <= original[2] || declared[2] !== 0
    || original[0] !== candidate[0] || original[1] !== candidate[1]
    || original[0] !== declared[0] || original[1] !== declared[1]) {
    throw new Error('Use the pinned .0 base from the same calendar line and a newer correction patch.');
  }
  const baseIds = identifiers(base);
  const correctionIds = identifiers(correction);
  for (const id of correctionIds) {
    if (!baseIds.has(id)) throw new Error('A correction adds a rule identifier. Start a new pack line instead.');
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  try {
    if (process.argv.length !== 4) throw new Error('Supply a pinned base content file and correction content file.');
    const documents = process.argv.slice(2).map(path => {
      if (statSync(path).size > 16 * 1024 * 1024) throw new Error('A pack input exceeds 16 MiB.');
      return JSON.parse(readFileSync(path, 'utf8'));
    });
    checkCorrection(...documents);
    console.log('Correction identity, calendar line and rule identifier subset verified.');
  } catch {
    console.error('Correction check failed. Verify the pinned .0 base, pack identity, calendar versions and rule identifier subset.');
    process.exitCode = 1;
  }
}
