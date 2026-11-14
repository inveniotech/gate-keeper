// SPDX-License-Identifier: Apache-2.0
import { detectionRule, digit, syntheticValues, contextCase } from '../detection-authoring.mjs';

// Full mobile blocks in the NCC March 2024 table: Airtel, EMTS, Glo and MTN.
const nationalDestinationCodes = ['701', '703', '704', '705', '706', '707', '708', '802', '803', '805', '806', '807', '808', '809',
  '810', '811', '812', '813', '814', '815', '816', '817', '818', '901', '902', '903', '904', '905', '906', '907', '908', '909', '911', '912', '913', '915', '916'];
const value = syntheticValues()['phone-log'];
const international = '234' + value.slice(1);
const internationalRuleId = 'pii.phone.international';
const internationalCase = (suffix, text, expected, matched = international) => {
  const item = contextCase('phone', suffix, text, matched, expected);
  if (expected) item.expected[0].ruleId = internationalRuleId;
  return item;
};
export default {
  family: 'phone', title: 'Nigerian mobile-number detection',
  description: 'Finds contiguous national mobile numbers with the existing prefix validator and international 234 or +234 numbers with exact prefix alternatives. Both rules report error findings. Prefixes are pack data.',
  limits: 'The prefix snapshot is the NCC March 2024 numbering plan, reviewed on 6 November 2026. It covers full mobile blocks for Airtel, EMTS, Glo and MTN, including the former Visafone 704 block. It excludes fixed lines, shared-service blocks and partial 702 allocations. It is a pinned baseline, not a claim of exhaustive 2026 allocation coverage. Spaces, hyphens and parentheses inside a number are not supported. Prefixes do not establish assignment, current carrier, processing purpose or an unlawful disclosure. International alternatives include the plus sign in the finding span when present; the digits-only validator cannot validate that sign, so these alternatives encode the same prefix data in the shape.',
  fix: 'Replace unnecessary telephone data in source and fixtures with generated data; mask values before logging or returning them. Record a narrowly scoped justified exception where retention is necessary.',
  sources: ['https://old.ncc.gov.ng/docman-main/technical-standards/1184-national-numbering-plan-1',
    'SHA-256: 9e1eb816a3b4f95d249f4321f68fa1fd367f88268e618e9ff03e161b56404e3b'],
  rules: [detectionRule('pii.phone', 'pii.phone', { shape: [digit(11)], boundary: 'alnum',
    validator: { name: 'ng-phone-prefix', prefixes: nationalDestinationCodes.map(code => '0' + code) } }),
  detectionRule(internationalRuleId, 'pii.phone', { shape: [{ oneOf: nationalDestinationCodes.flatMap(code => ['234' + code, '+234' + code]), min: 1, max: 1 }, digit(7)], boundary: 'alnum' })],
  cases: [internationalCase('international', `const phone = "${international}";`, true),
    internationalCase('international-plus', `const phone = "+${international}";`, true, '+' + international),
    internationalCase('international-truncated', `const phone = "${international.slice(0, -1)}";`, false),
    internationalCase('international-overlong', `const phone = "${international}${value[0]}";`, false),
    contextCase('phone', 'national-overlong', `const phone = "${value}${value[0]}";`, value),
    contextCase('phone', 'embedded-in-token', `const phone = "prefix${value}suffix";`, value)],
};
