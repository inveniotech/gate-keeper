// SPDX-License-Identifier: Apache-2.0
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildIdentifierCases, synchroniseCases } from './New-IdentifierCases.mjs';

/** Builds P2-04 inputs from the existing synthetic identifier cases and explicit field-handling examples. */
export function buildLocationCases() {
  const cases = [];
  const identifierCases = buildIdentifierCases();
  for (const source of identifierCases.filter(item => item.name.endsWith('-node-source'))) {
    const family = source.expected[0].ruleId.slice(4);
    const [, label, value] = source.text.match(/^const (\w+) = "([^"]+)";/);
    const locations = [
      ['log', 'src/logging.ts', `console.log("${label}", "${value}");\n`],
      ['fixture', 'fixtures/customer.json', `{"${label}":"${value}"}\n`],
      ['configuration', 'config/appsettings.json', `{"${label}":"${value}"}\n`],
      ['migration', 'migrations/001_customer.sql', `INSERT INTO customer (bank_code, ${label}) VALUES ('011', '${value}');\n`],
      ['api-model', 'api/models/customer-response.ts', `export const customerResponse = { ${label}: "${value}" };\n`],
    ];
    for (const [kind, path, text] of locations) {
      cases.push({ name: `p2-location-${family}-${kind}`, mode: 'value', kind, path, text,
        expected: [{ ruleId: `pii.${family}`, path, line: 1, column: text.indexOf(value) + 1, triage: 'true-positive' },
          { ruleId: `pii.${family}.${kind === 'api-model' ? 'api' : kind}`, path, line: 1, column: text.indexOf(value) + 1, triage: 'true-positive' }] });
    }
  }
  for (const item of cases.filter(entry => entry.name.startsWith('p2-location-phone-'))) {
    const value = item.text.slice(item.expected[0].column - 1).match(/^[^"']+/)[0];
    const international = '234' + value.slice(1);
    for (const [variant, replacement] of [['international', international], ['international-plus', '+' + international]]) {
      cases.push({ ...item, name: `p2-international-phone-${item.kind}-${variant}`, mode: 'international',
        text: item.text.replace(value, replacement), expected: item.expected.map(entry => ({ ...entry,
          ruleId: entry.ruleId.replace('pii.phone', 'pii.phone.international') })) });
    }
    cases.push({ ...item, name: `p2-international-phone-${item.kind}-overlong`, mode: 'near-miss',
      text: item.text.replace(value, international + value[0]), expected: [] });
  }
  for (const family of ['bvn', 'nin']) {
    const member = family === 'bvn' ? 'Bvn' : 'Nin';
    const forms = [
      ['dotnet', 'cs', 'log', `Log.Information("{${member}}", customer.${member});\n`, `customer.${member}`],
      ['node', 'ts', 'log', `console.log(customer.${family});\n`, `customer.${family}`],
      ['jvm', 'kt', 'log', `logger.info("{}", customer.${family});\n`, `customer.${family}`],
      ['dotnet-variable', 'cs', 'log', `Log.Information("{${member}}", ${family});\n`, `${family});`],
      ['node-variable', 'ts', 'log', `console.log(${family});\n`, `${family});`],
      ['jvm-variable', 'kt', 'log', `logger.info("{}", ${family});\n`, `${family});`],
      ['dotnet', 'cs', 'migration', `migrationBuilder.AddColumn<string>(name: "${family}", table: "customers", nullable: true);\n`, `"${family}"`],
      ['node', 'ts', 'migration', `table.string("${family}");\n`, `"${family}"`],
      ['jvm', 'kt', 'migration', `val ${family} = varchar("${family}", 11);\n`, `"${family}"`],
      ['dotnet', 'cs', 'api', `return Results.Json(new { ${family} = customer.${member} });\n`, `${family} =`],
      ['node', 'ts', 'api', `res.json({ ${family}: customer.${family} });\n`, `${family}:`],
      ['jvm', 'kt', 'api', `return ResponseEntity.ok(mapOf("${family}" to customer.${family}));\n`, `"${family}"`],
    ];
    for (const [language, extension, kind, text, anchor] of forms) {
      const path = `${kind === 'migration' ? 'migrations' : kind === 'api' ? 'api' : 'src'}/example.${extension}`;
      // The finding points to the field token, excluding quotes and receiver qualification.
      const offset = text.indexOf(anchor) + (anchor.startsWith('customer.') ? 'customer.'.length : anchor.startsWith('"') ? 1 : 0);
      cases.push({ name: `p2-field-${family}-${kind}-${language}`, mode: 'field', kind: kind === 'api' ? 'api-model' : kind, path, text,
        expected: [{ ruleId: `pii.${family}.field.${kind}`, path, line: 1, column: offset + 1, triage: 'true-positive' }] });
    }
  }
  for (const item of [...cases].filter(entry => entry.mode === 'value')) {
    const value = item.text.slice(item.expected[0].column - 1).match(/^[^"']+/)[0];
    cases.push({ ...item, name: item.name + '-shape-near-miss', mode: 'near-miss',
      text: item.text.replace(value, 'not-an-identifier'), expected: [] });
    const family = item.name.split('-')[2];
    const rejected = identifierCases.find(entry => entry.name === `p2-${family}-validator-near-miss`).text.match(/"([^"\n]+)"/)[1];
    cases.push({ ...item, name: item.name + '-validator-near-miss', mode: 'near-miss',
      text: item.text.replace(value, rejected), expected: [] });
  }
  for (const family of ['bvn', 'nin']) {
    for (const [suffix, text] of [
      ['masked', `console.log(mask(customer.${family}));\n`],
      ['unrelated-field', `console.log(customer.${family}Status);\n`],
      ['different-statement', `console.log(customer.name); const ${family} = customer.${family};\n`],
      ['ordinary-source', `const ${family} = customer.${family};\n`],
      ['protected-column', `table.string("encrypted_${family}");\n`],
      ['unrelated-response', `res.json({ ${family}Status: customer.status });\n`],
    ]) cases.push({ name: `p2-field-${family}-${suffix}`, mode: 'near-miss', kind: 'source',
      path: suffix === 'protected-column' ? 'migrations/example.ts' : 'src/example.ts', text, expected: [] });
  }
  return cases.sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  if (process.argv.length !== 3) throw new Error('Supply one generation mode.');
  synchroniseCases(buildLocationCases(), 'location', 'Phase 2 location acceptance cases',
    'Regenerate with `node tests/corpus/New-LocationCases.mjs --write` and verify with `--check`. Literal identifiers reuse the documented generator. Field-handling cases contain no literal personal identifier. Base and location findings are intentional separate observations. Detection tests verify exact locations, severity and inventory kinds through the loaded source packs.', process.argv[2]);
}
