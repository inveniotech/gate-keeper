// SPDX-License-Identifier: Apache-2.0
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { approvedCitation } from './citations.mjs';

export const packVersion = '2026.11.0';
export const packDate = '2026-11-06';
export const digitExclusions = ['repeated-digits', 'sequential-digits', 'hex-fragment', 'uuid-fragment', 'version-string', 'date-or-timestamp'];
export const digit = (min, max = min) => ({ class: 'digit', min, max });

/** Reads synthetic values only from the documented deterministic generator. */
export function syntheticValues() {
  const generated = spawnSync(process.execPath, [fileURLToPath(new URL('../../tests/corpus/New-CorpusValues.mjs', import.meta.url))], { encoding: 'utf8' });
  if (generated.status !== 0) throw new Error('The documented synthetic generator failed.');
  return Object.fromEntries(generated.stdout.split('\n').filter(line => line.includes(' = ')).map(line => line.split(' = ')));
}

/** Supplies the recorded owner-authorised citation without changing its wording or review status. */
export function detectionRule(id, category, pattern, severity = 'error') {
  const register = JSON.parse(readFileSync(new URL('../../rule-packs/citations.json', import.meta.url)));
  return { id, category, severity, citation: approvedCitation(register, category), sinceVersion: packVersion,
    matcher: { pattern }, suppressionHints: ['Remove unnecessary personal data or mask it before use. Record a justified exception through the existing configuration if retention is necessary.'] };
}

/** A labelled identifier uses the existing bounded literal-keyword prefilter, not data-flow inference. */
export function labelledIdentifier(family, keywords) {
  const values = syntheticValues();
  return detectionRule(`pii.${family}`, `pii.${family}`, { shape: [digit(11)], boundary: 'digit',
    keywords, window: 128, validator: { name: 'digits-length', length: 11 }, exclusions: digitExclusions,
    excludeValues: [values['bvn-documented-example']] });
}

/** Builds a small context near-miss without adding a personal identifier outside the documented generator. */
export function contextCase(family, suffix, text, value, expected = false) {
  const path = 'src/context.ts';
  return { name: `p2-${family}-${suffix}`, path, text: text + '\n', expected: expected
    ? [{ ruleId: `pii.${family}`, path, line: 1, column: text.indexOf(value) + 1, triage: 'true-positive' }] : [] };
}
