// SPDX-License-Identifier: Apache-2.0
import bvn from './bvn.mjs';
import nin from './nin.mjs';
import phone from './phone.mjs';
import { detectionRule, digit, digitExclusions, syntheticValues } from '../detection-authoring.mjs';

const alternative = values => ({ oneOf: values, min: 1, max: 1 });
const literal = (value, emit = false) => ({ literal: value, min: 1, max: 1, ...(emit ? { emit: true } : {}) });
const rules = [];
const values = syntheticValues();
const scopes = [
  ['log', 'pii.in-logs', null],
  ['fixture', 'pii.in-fixtures', ['**/fixtures/**', '**/__tests__/**', '**/*.spec.*', '**/seeds/**', '**/seed.*']],
  ['configuration', 'pii.in-configuration', ['**/config/**', '**/.env', '**/.env.*', '**/appsettings*.json', '**/application*.yml', '**/application*.yaml', '**/*.properties']],
  ['migration', 'pii.in-migrations', ['**/migrations/**', '**/Migrations/**']],
  ['api-model', 'pii.in-api-models', ['**/api/models/**', '**/api/responses/**', '**/Models/**', '**/Responses/**']],
];
const emailCharacters = [['a', 'z'], ['A', 'Z'], ['0', '9'], ['.', '.'], ['_', '_'], ['+', '+'], ['-', '-']];
const email = { shape: [{ ranges: emailCharacters, min: 1, max: 64 }, literal('@'),
  { ranges: [['a', 'z'], ['A', 'Z'], ['0', '9'], ['-', '-']], min: 1, max: 64 }, literal('.'),
  { ranges: [['a', 'z'], ['A', 'Z'], ['.', '.']], min: 2, max: 24 }], boundary: 'alnum', validator: { name: 'email-not-example-domain' } };
const account = { shape: [digit(10)], boundary: 'digit', keywords: ['account', 'nuban'], window: 128,
  validator: { name: 'nuban' }, exclusions: digitExclusions,
  excludeValues: [values['nuban-documented-account-one'], values['nuban-documented-account-two']] };
const patterns = [
  ['bvn', bvn.rules[0].matcher.pattern], ['nin', nin.rules[0].matcher.pattern],
  ['nuban', account], ['phone', phone.rules[0].matcher.pattern], ['phone.international', phone.rules[1].matcher.pattern], ['email', email],
];
const logCalls = ['console.log(', 'logger.info(', 'logger.LogInformation(', 'logger.LogWarning(', 'logger.LogError(', 'logger.debug(', 'logger.warn(', 'logger.error(', 'Log.Information(', 'print('];
for (const [family, original] of patterns) {
  for (const [kind, category, paths] of scopes) {
    const pattern = structuredClone(original);
    if (kind === 'log') {
      if (family === 'bvn' || family === 'nin' || family === 'nuban') {
        const labels = family === 'bvn' ? ['bvn', 'bankVerificationNumber', 'bank_verification']
          : family === 'nin' ? ['nin ', 'nin"', "nin'", 'nin=', 'nin:', 'nationalId', 'national_identity'] : ['accountNumber', 'account_number', 'nuban'];
        pattern.shape[0].emit = true;
        pattern.shape = [alternative(labels), { ranges: [[' ', '/'], [':', '~']], min: 0, max: 32 }, ...pattern.shape];
      }
      pattern.keywords = logCalls;
      pattern.window = 128;
    }
    pattern.location = { kind, ...(paths ? { paths } : {}) };
    rules.push(detectionRule(`pii.${family}.${kind === 'api-model' ? 'api' : kind}`, category, pattern));
  }
}
for (const family of ['bvn', 'nin']) {
  const member = family === 'bvn' ? 'Bvn' : 'Nin';
  const forms = [
    ['log', 'log', 'pii.in-logs', [alternative(['console.log(', 'logger.info("{}", ', `Log.Information("{${member}}", `, 'print(', 'logger.info(']), { literal: 'customer.', min: 0, max: 1 }, literal(family, true), literal(')')]],
    ['migration', 'migration', 'pii.in-migrations', [alternative(['migrationBuilder.AddColumn<string>(name: "', 'table.string("', 'varchar("']), literal(family, true), literal('"')]],
    ['api', 'api-model', 'pii.in-api-models', [alternative(['return Results.Json(new { ', 'res.json({ ', 'return ResponseEntity.ok(mapOf("']), literal(family, true), alternative([' = customer.', ': customer.', '" to customer.'])]],
  ];
  for (const [suffix, kind, category, shape] of forms) {
    rules.push(detectionRule(`pii.${family}.field.${suffix}`, category,
      { shape, boundary: 'none', location: { kind, ...(kind === 'migration' ? { paths: ['**/migrations/**', '**/Migrations/**'] } : {}) } }));
  }
}
export default {
  family: 'locations', title: 'Personal identifier location heuristics',
  description: 'Finds labelled bank verification and national identity numbers, account numbers, Nigerian mobile numbers and email addresses in logging, fixtures, configuration, migrations and API models. All location and unsafe-field rules use error severity and stamp the corresponding inventory kind. Location rules increase context precision; they retain the existing high severity of identifier rules.',
  limits: 'These bounded source heuristics do not parse syntax, follow data flow or establish that code executes. Supported field forms include direct variables and customer properties passed directly to console.log, logger.info, Log.Information and print; AddColumn<string>, table.string and varchar declarations under migration directories; and Results.Json, res.json and ResponseEntity.ok response construction. Formatting and receiver spellings outside these forms are not exhaustive. Literal log rules use call keywords within 128 scalar values; a nearby call can belong to another statement. BVN, NIN and account log rules additionally require a label before the value with at most 32 printable non-digit characters between them. Email addresses use a bounded ASCII shape, not full RFC mailbox syntax. Account validation uses the existing bank-code-011 baseline; other bank codes remain outside validated coverage. National and international phone rules reuse the pinned prefix data. Paths are case-sensitive pack globs, not semantic classification. A column declaration cannot establish encryption at rest. Base and location rules intentionally coexist with owner authorisation; counts represent findings rather than unique identifiers. Findings request safeguard review; they do not prove a legal breach.',
  fix: 'Mask identifiers before logging, minimise response fields and review storage safeguards. Replace fixture and configuration identifiers with documented synthetic data. Record narrowly scoped justified exceptions through existing configuration when necessary.',
  rules,
};
