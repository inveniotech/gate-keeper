// SPDX-License-Identifier: Apache-2.0
import { labelledIdentifier, syntheticValues, contextCase } from '../detection-authoring.mjs';

const value = syntheticValues()['nin-log'];
export default {
  family: 'nin', title: 'National identity number detection',
  description: 'Finds eleven-digit values within 128 scalar values of national-identity labels or common delimited NIN label spellings. Reports the value location at error severity.',
  limits: 'Literal prefilters fold ASCII case and do not parse code. Delimiters after the short NIN spelling avoid the incidental substring in words such as scanning; supported forms include assignment, JSON keys, prose and colon-delimited labels. No public check digit or issued-identity validation is assumed. Existing digit, date, version, UUID and documented-example exclusions apply. Unlabelled identifiers and unsafe field handling are separate rules.',
  fix: 'Remove unnecessary identity values from source, configuration and fixtures. Mask them before logging and minimise authorised responses. Use a justified exception only where retention is necessary.',
  rules: [labelledIdentifier('nin', ['nin=', 'nin =', 'nin"', "nin'", 'nin ', 'nin:', 'nationalId', 'nationalIdentity', 'national_identity', 'national_id', 'national identity'])],
  cases: [contextCase('nin', 'without-label', `const identifier = "${value}";`, value),
    contextCase('nin', 'distant-label', `const nin = "${' '.repeat(129)}${value}";`, value),
    contextCase('nin', 'incidental-keyword', `const scanning = "${value}";`, value),
    contextCase('nin', 'label-after-value', `const identifier = "${value}"; // NIN label`, value, true)],
};
