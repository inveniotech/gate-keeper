// SPDX-License-Identifier: Apache-2.0
import { labelledIdentifier, syntheticValues, contextCase } from '../detection-authoring.mjs';

const value = syntheticValues()['bvn-log'];
export default {
  family: 'bvn', title: 'Bank verification number detection',
  description: 'Finds an eleven-digit value within 128 scalar values of a BVN or bank-verification label, including labels after the value. Reports the value location at error severity.',
  limits: 'Literal keywords are ASCII case-insensitive substrings. Nearby labels provide context, not proof of assignment, data flow or an issued BVN. No public check-digit algorithm is assumed. Longer digit runs, repeated/sequential examples, dates, versions, hex and UUID fragments are excluded by the existing engine controls. Unlabelled identifiers and unsafe field handling are separate rules.',
  fix: 'Remove unnecessary identifiers from source and test data, use generated fixtures, and mask identifiers before logging. If retention is necessary, use the existing justified-exception configuration and review its scope.',
  rules: [labelledIdentifier('bvn', ['bvn', 'bankVerification', 'bank_verification', 'bank verification'])],
  cases: [contextCase('bvn', 'without-label', `const identifier = "${value}";`, value),
    contextCase('bvn', 'distant-label', `const bvn = "${' '.repeat(129)}${value}";`, value),
    contextCase('bvn', 'label-after-value', `const identifier = "${value}"; // BVN`, value, true)],
};
