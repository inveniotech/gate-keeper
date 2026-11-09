// SPDX-License-Identifier: Apache-2.0
import { detectionRule, syntheticValues, contextCase } from '../detection-authoring.mjs';

const letters = [['a', 'z'], ['A', 'Z']];
const alphaNumeric = [...letters, ['0', '9']];
const localCharacters = [...alphaNumeric, ...['.', '_', '%', '+', '-'].map(character => [character, character])];
const domainCharacters = [...alphaNumeric, ['-', '-'], ['.', '.']];
const value = syntheticValues()['email-source'];
const [local, domain] = value.split('@');
export default {
  family: 'email', title: 'Email-address detection',
  minEngineVersion: '0.2.0',
  description: 'Finds bounded ASCII email-shaped values at error severity using the existing example-domain validator. Reports the address location without copying the address into results.',
  limits: 'The local part is 1–64 ASCII letters, digits or dot, underscore, percent, plus and hyphen. The domain prefix is 1–64 letters, digits, hyphens or dots and ends in a dot plus a 2–24-letter suffix. This is a detection shape, not full RFC mailbox validation: quoted local parts, internationalised addresses and longer domains are unsupported, and punctuation within a shaped address can be permissive. The email boundary keeps the span inside a longer token: a match cannot start after a punctuation neighbour inside the local part (the oversized-local-part guard), while a sentence-ending dot after the domain does not reject a match. Reserved documentation domains and their subdomains are excluded; the existing synthetic .example corpus domain remains a positive. Public contact and role addresses still require contextual review.',
  fix: 'Remove unnecessary addresses from fixtures and configuration, replace them with generated test data, and mask addresses in diagnostics. Use the existing justified-exception configuration for deliberately retained public contacts.',
  rules: [detectionRule('pii.email', 'pii.email', { shape: [
    { ranges: localCharacters, min: 1, max: 64 }, { literal: '@', min: 1, max: 1 },
    { ranges: domainCharacters, min: 1, max: 64 }, { literal: '.', min: 1, max: 1 },
    { ranges: letters, min: 2, max: 24 }], boundary: 'email', validator: { name: 'email-not-example-domain' } })],
  cases: [contextCase('email', 'missing-at', `const email = "${local}${domain}";`, value),
    contextCase('email', 'missing-domain', `const email = "${local}@";`, value),
    contextCase('email', 'missing-local-part', `const email = "@${domain}";`, value),
    contextCase('email', 'oversized-local-part', `const email = "${'x'.repeat(65)}${local}@${domain}";`, value),
    contextCase('email', 'sentence-dot-tail', `const email = "${value}.";`, value, true),
    contextCase('email', 'trailing-label-tail', `const email = "${value}-ng";`),
    ...['example.com', 'example.org', 'example.net', 'test', 'invalid', 'localhost'].map((reserved, index) =>
      contextCase('email', `reserved-domain-${index}`, `const email = "${local}@mail.${reserved}";`, value))],
};
