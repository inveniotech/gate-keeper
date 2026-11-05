// SPDX-License-Identifier: Apache-2.0
import { spawnSync } from 'node:child_process';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));

/** Builds the P2-03 acceptance inputs from the documented generator, without inventing identifier values. */
export function buildIdentifierCases() {
  const generated = spawnSync(process.execPath, [join(root, 'New-CorpusValues.mjs')], { encoding: 'utf8' });
  if (generated.status !== 0) throw new Error('The documented identifier generator failed.');
  const values = Object.fromEntries(generated.stdout.split('\n').filter(line => line.includes(' = ')).map(line => line.split(' = ')));
  const families = [
    { family: 'bvn', label: 'bankVerificationNumber', value: values['bvn-log'], near: values['bvn-documented-example'] },
    { family: 'nin', label: 'nationalId', value: values['nin-log'], near: values['bvn-documented-example'] },
    { family: 'nuban', label: 'accountNumber', value: values['nuban-source'], near: values['nuban-wrong-check-digit'] },
    { family: 'phone', label: 'phone', value: values['phone-log'], near: values['phone-not-nigerian'] },
    { family: 'email', label: 'email', value: values['email-source'], near: values['email-placeholder-primary'] },
  ];
  const result = [];
  for (const row of families) {
    if (!row.value || !row.near) throw new Error('A documented synthetic identifier label is missing.');
    for (const [language, extension, declaration] of [['dotnet', 'cs', 'var'], ['node', 'ts', 'const'], ['jvm', 'kt', 'val']]) {
      const text = `${declaration} ${row.label} = "${row.value}";\n`;
      const path = `src/example.${extension}`;
      result.push({ name: `p2-${row.family}-${language}-source`, path, text,
        expected: [{ ruleId: `pii.${row.family}`, path, line: 1, column: text.indexOf(row.value) + 1, triage: 'true-positive' }] });
    }
    for (const [kind, value] of [['shape-near-miss', 'not-an-identifier'], ['validator-near-miss', row.near]]) {
      result.push({ name: `p2-${row.family}-${kind}`, path: 'src/example.ts', text: `const ${row.label} = "${value}";\n`, expected: [] });
    }
  }
  return result.sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0);
}

/** Writes or checks generated case files and the generator's marked corpus README section. */
export function synchroniseCases(cases, marker, title, description, mode) {
  if (!['--write', '--check'].includes(mode)) throw new Error('Use --write to regenerate or --check to verify acceptance cases.');
  for (const item of cases) {
    const files = [[join(root, item.name, 'repo', item.path), item.text],
      [join(root, item.name, 'expected.json'), JSON.stringify(item.expected, null, 2) + '\n']];
    for (const [path, text] of files) {
      if (mode === '--write') { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, text); }
      else if (readFileSync(path, 'utf8') !== text) throw new Error(`Generated identifier case differs: ${item.name}. Regenerate it with --write.`);
    }
  }
  const heading = `<!-- generated-${marker}-cases:start -->`;
  const ending = `<!-- generated-${marker}-cases:end -->`;
  const section = [heading, `## ${title}`, '', description, '',
    '| Case | Expected rule |', '|---|---|',
    ...cases.map(item => `| \`${item.name}\` | ${item.expected.length ? `\`${item.expected[0].ruleId}\`` : 'No finding'} |`), '', ending].join('\n');
  const readmePath = join(root, 'README.md');
  const current = readFileSync(readmePath, 'utf8');
  const start = current.indexOf(heading);
  const end = current.indexOf(ending);
  if ((start === -1) !== (end === -1) || (start !== -1 && end < start)) throw new Error('The generated README section is malformed.');
  const updated = start === -1 ? current.trimEnd() + '\n\n' + section + '\n'
    : current.slice(0, start) + section + current.slice(end + ending.length);
  if (mode === '--write') writeFileSync(readmePath, updated);
  else if (current !== updated) throw new Error('The corpus README differs. Regenerate identifier cases with --write.');
  console.log(`Verified ${cases.length} synthetic ${marker} acceptance cases; detection remains pending the approved pack.`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  if (process.argv.length !== 3) throw new Error('Supply one generation mode.');
  synchroniseCases(buildIdentifierCases(), 'identifier', 'Phase 2 identifier acceptance cases',
    'Regenerate with `node tests/corpus/New-IdentifierCases.mjs --write` and verify with `--check`. All personal identifiers come from the documented generator above. These inputs establish expectations before production rules; their existence does not claim measured recall or precision.', process.argv[2]);
}
