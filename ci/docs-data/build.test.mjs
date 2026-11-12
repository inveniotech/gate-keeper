// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { ruleEntries, relatedNearMisses, listedCommands, captureCommands, cliReferencePages, docsDataFiles, authoredSources, binaryCandidates } from './build.mjs';
import { packVersion } from '../rule-packs/detection-authoring.mjs';

test('the default CLI candidates use the current assembly name on Windows and Unix', () => {
  const project = readFileSync(new URL('../../cli/InvenioTech.GateKeeper.Cli.csproj', import.meta.url), 'utf8');
  const assembly = project.match(/<AssemblyName>([^<]+)<\/AssemblyName>/)[1];
  const folder = new URL('../../cli/bin/Release/net10.0/', import.meta.url);
  assert.deepEqual(binaryCandidates('linux'), [fileURLToPath(new URL(assembly, folder))]);
  assert.deepEqual(binaryCandidates('win32'), [
    fileURLToPath(new URL(`${assembly}.exe`, folder)),
    fileURLToPath(new URL(assembly, folder)),
  ]);
});

const register = {
  status: 'approved-in-principle',
  reviewedBy: 'owner',
  reviewedOn: '2026-11-05',
  legalReview: { status: 'pending' },
  entries: [
    {
      category: 'pii.example',
      status: 'approved-in-principle',
      citation: { title: 'Review synthetic identifiers.', ndpa: 'Sections 1', gaid: 'Article 1' },
      explanation: 'Synthetic identifiers need review.',
      limit: 'A synthetic value proves nothing.',
    },
    {
      category: 'pii.second',
      status: 'approved-in-principle',
      citation: { title: 'Review synthetic second identifiers.', ndpa: 'Sections 2', gaid: 'Article 2' },
      explanation: 'Second identifiers need review.',
      limit: 'A second synthetic value proves nothing.',
    },
  ],
};

const source = () => ({
  family: 'example',
  title: 'Example detection',
  description: 'Finds synthetic example values.',
  limits: 'Bounded shape.',
  fix: 'Remove the values.',
  cases: [
    { name: 'p2-example-near-miss', path: 'src/context.ts', expected: [] },
    { name: 'p2-example-positive', path: 'src/context.ts', expected: [{ ruleId: 'pii.example', path: 'src/context.ts', line: 1, column: 14 }] },
    { name: 'p2-second-positive', path: 'src/context.ts', expected: [{ ruleId: 'pii.second', path: 'src/context.ts', line: 1, column: 14 }] },
  ],
});

const pack = () => ({
  id: 'invenio.example',
  version: packVersion,
  rules: [
    {
      id: 'pii.example',
      category: 'pii.example',
      severity: 'error',
      sinceVersion: packVersion,
      citation: { title: 'Review synthetic identifiers.', ndpa: 'Sections 1', gaid: 'Article 1' },
      suppressionHints: ['Mask the value. Record a justified exception through the existing configuration.'],
    },
    {
      id: 'pii.second',
      category: 'pii.second',
      severity: 'note',
      sinceVersion: packVersion,
      citation: { title: 'Review synthetic second identifiers.', ndpa: 'Sections 2', gaid: 'Article 2' },
      suppressionHints: ['Mask the second value. Record a justified exception through the existing configuration.'],
    },
  ],
});

test('a detection source without reference documentation fails the rule metadata build', () => {
  for (const field of ['description', 'limits', 'fix']) {
    const stripped = { ...source() };
    stripped[field] = ' ';
    assert.throws(() => ruleEntries([stripped], [pack()], register), /reference documentation/);
  }
});

test('a rule category outside the citation register fails the rule metadata build', () => {
  const unknown = pack();
  unknown.rules[0].category = 'pii.unknown';
  assert.throws(() => ruleEntries([source()], [unknown], register), /citation register/);
});

test('a pack without an authored source fails the rule metadata build', () => {
  assert.throws(() => ruleEntries([], [pack()], register), /authored detection source/);
  const other = { ...source(), family: 'other' };
  assert.throws(() => ruleEntries([other], [pack()], register), /generated pack document/);
});

test('a pack version that differs from the authored version fails the rule metadata build', () => {
  const shifted = pack();
  shifted.version = '2000.1.0';
  assert.throws(() => ruleEntries([source()], [shifted], register), /pack version/);
});

test('rule entries carry the reference fields the website renders', () => {
  const [entry] = ruleEntries([source()], [pack()], register);
  assert.equal(entry.identifier, 'pii.example');
  assert.equal(entry.family, 'invenio.example');
  assert.equal(entry.title, 'Example detection');
  assert.equal(entry.severity, 'error');
  assert.equal(entry.sinceVersion, packVersion);
  assert.equal(entry.detects, 'Finds synthetic example values.');
  assert.equal(entry.whyItMatters, 'Synthetic identifiers need review.');
  assert.deepEqual(entry.citation, { title: 'Review synthetic identifiers.', ndpa: 'Sections 1', gaid: 'Article 1' });
  assert.equal(entry.limits, 'Bounded shape.');
  assert.equal(entry.remediation, 'Remove the values.');
  assert.equal(entry.exceptionGuidance, 'Mask the value. Record a justified exception through the existing configuration.');
  assert.equal(entry.referencePage, 'rules/example.md');
  assert.equal(entry.processor, undefined);
});

test('examples partition positives by rule and share near-misses', () => {
  const entries = ruleEntries([source()], [pack()], register);
  const example = entries.find(entry => entry.identifier === 'pii.example');
  const second = entries.find(entry => entry.identifier === 'pii.second');
  assert.deepEqual(example.examples.map(item => [item.name, item.kind]),
    [['p2-example-near-miss', 'near-miss'], ['p2-example-positive', 'positive']]);
  assert.deepEqual(second.examples.map(item => [item.name, item.kind]),
    [['p2-example-near-miss', 'near-miss'], ['p2-second-positive', 'positive']]);
});

test('a rule without authored cases takes its examples from the corpus', () => {
  const corpus = [
    { name: 'p2-location-example-log', path: 'src/log.ts', expected: [{ ruleId: 'pii.example' }] },
    { name: 'p2-location-example-log-shape-near-miss', path: 'src/log.ts', expected: [] },
    { name: 'p2-location-other-log-shape-near-miss', path: 'src/log.ts', expected: [] },
  ];
  const [entry] = ruleEntries([{ ...source(), cases: [] }], [pack()], register, corpus);
  assert.deepEqual(entry.examples.map(item => [item.name, item.kind]),
    [['p2-location-example-log', 'positive'], ['p2-location-example-log-shape-near-miss', 'near-miss']]);
});

test('a rule without an authored positive takes the corpus positives', () => {
  const corpus = [
    { name: 'p2-location-example-log', path: 'src/log.ts', expected: [{ ruleId: 'pii.example' }] },
  ];
  const sourceWithNearMiss = {
    ...source(),
    cases: [{ name: 'p2-example-near-miss', path: 'src/context.ts', expected: [] }],
  };
  const [entry] = ruleEntries([sourceWithNearMiss], [pack()], register, corpus);
  assert.deepEqual(entry.examples.map(item => [item.name, item.kind]),
    [['p2-example-near-miss', 'near-miss'], ['p2-location-example-log', 'positive']]);
});

test('corpus near-misses attach to the family, inventory kind and field rule they exercise', () => {
  const cases = [
    { name: 'p2-location-bvn-log-shape-near-miss', expected: [] },
    { name: 'p2-location-bvn-api-model-shape-near-miss', expected: [] },
    { name: 'p2-location-email-log-shape-near-miss', expected: [] },
    { name: 'p2-field-bvn-masked', expected: [] },
    { name: 'p2-international-phone-log-overlong', expected: [] },
    { name: 'p2-location-bvn-log', expected: [{ ruleId: 'pii.bvn.log' }] },
  ];
  const names = rule => relatedNearMisses(rule, cases).map(item => item.name);
  assert.deepEqual(names({ id: 'pii.bvn' }), ['p2-location-bvn-log-shape-near-miss', 'p2-location-bvn-api-model-shape-near-miss', 'p2-field-bvn-masked']);
  assert.deepEqual(names({ id: 'pii.bvn.log' }), ['p2-location-bvn-log-shape-near-miss']);
  assert.deepEqual(names({ id: 'pii.bvn.field.api' }), ['p2-field-bvn-masked']);
  assert.deepEqual(names({ id: 'pii.email.log' }), ['p2-location-email-log-shape-near-miss']);
  assert.deepEqual(names({ id: 'pii.phone.international.log' }), ['p2-international-phone-log-overlong']);
});

test('duplicate pack identifiers or rule identifiers fail the rule metadata build', () => {
  assert.throws(() => ruleEntries([source()], [pack(), pack()], register), /Pack identifiers must be unique/);
  const otherSource = { ...source(), family: 'other' };
  const otherPack = { ...pack(), id: 'invenio.other' };
  assert.throws(() => ruleEntries([source(), otherSource], [pack(), otherPack], register), /more than one pack/);
});

test('entries are sorted by identifier and carry processor metadata', () => {
  const withProcessor = pack();
  withProcessor.rules[1].processor = { dataCategories: ['synthetic contact details'], usualRegion: 'Deployment-dependent' };
  const entries = ruleEntries([source()], [withProcessor], register);
  assert.deepEqual(entries.map(entry => entry.identifier), ['pii.example', 'pii.second']);
  assert.deepEqual(entries[1].processor, { dataCategories: ['synthetic contact details'], usualRegion: 'Deployment-dependent' });
});

const rootHelp = [
  'Description:',
  '  Scans a repository.',
  '',
  'Usage:',
  '  gatekeeper-ndpa-check [command] [options]',
  '',
  'Options:',
  '  -?, -h, --help  Show help and usage information',
  '',
  'Commands:',
  '  scaffold  Scaffolds a helper',
  '  scan      Scans a repository',
  '',
].join('\n');

test('the command names are read from the commands section of a help text', () => {
  assert.deepEqual(listedCommands(rootHelp), ['scaffold', 'scan']);
  assert.deepEqual(listedCommands('Description:\n  No commands here.\n'), []);
});

test('a malformed line inside a commands section fails the capture', () => {
  const malformed = 'Commands:\n scan\n';
  assert.throws(() => listedCommands(malformed), /not understood/);
});

test('the capture walks nested commands, normalises line endings and sorts deterministically', () => {
  const pages = {
    '': 'Description:\n  Root.\n\nCommands:\n  scaffold  Scaffolds\n  scan  Scans\n\n',
    scaffold: 'Description:\r\n  Scaffolds.\r\n\r\nCommands:\r\n  consent  Consent helper\r\n\r\n',
    scan: 'Description:\n  Scans.\n',
    'scaffold consent': 'Description:\n  Consent helper.\n',
  };
  const run = path => pages[path.join(' ')];
  const commands = captureCommands(run);
  assert.deepEqual(commands.map(command => command.path), [[], ['scaffold'], ['scaffold', 'consent'], ['scan']]);
  assert.equal(commands.find(command => command.path.join(' ') === 'scaffold').help,
    'Description:\n  Scaffolds.\n\nCommands:\n  consent  Consent helper\n\n');
  const again = captureCommands(run);
  assert.deepEqual(commands, again);
});

test('cli reference pages embed the exact help bytes with LF endings', () => {
  const pages = cliReferencePages([{ path: ['scan'], help: 'Description:\n  Scans.\n' }]);
  const page = pages.get('docs-data/cli/scan.md');
  assert.deepEqual(page, Buffer.from([
    '# gatekeeper-ndpa-check scan',
    '',
    '<!-- Generated by node ci/docs-data/build.mjs --write. -->',
    '',
    'The exact help text of this build follows, captured from the command line itself, so the reference cannot drift from behaviour.',
    '',
    '```text',
    'Description:',
    '  Scans.',
    '```',
    '',
  ].join('\n'), 'utf8'));
  assert.ok(!page.includes('\r'));
  assert.ok(cliReferencePages([{ path: [], help: 'Description:\n  Root.\n' }]).has('docs-data/cli/index.md'));
  assert.ok(cliReferencePages([{ path: ['scaffold', 'consent'], help: 'x\n' }]).has('docs-data/cli/scaffold-consent.md'));
});

test('the real repository metadata covers every authored rule and generated page', async () => {
  const files = await docsDataFiles(() => 'Description:\n  This stub command line lists no commands.\n');
  const document = JSON.parse(files.get('docs-data/rules.json'));
  assert.equal(document.schemaVersion, 1);
  assert.equal(document.packVersion, packVersion);
  const identifiers = document.rules.map(entry => entry.identifier);
  assert.deepEqual(identifiers, [...new Set(identifiers)].sort());
  for (const entry of document.rules) {
    for (const field of ['identifier', 'family', 'title', 'severity', 'sinceVersion', 'detects', 'whyItMatters',
      'limits', 'remediation', 'exceptionGuidance', 'referencePage']) assert.ok(typeof entry[field] === 'string' && entry[field].trim(), `${entry.identifier} has an empty ${field}.`);
    assert.deepEqual(Object.keys(entry.citation).sort(), ['gaid', 'ndpa', 'title']);
    assert.ok(entry.examples.some(example => example.kind === 'positive'), `${entry.identifier} has no true example.`);
    assert.ok(entry.examples.some(example => example.kind === 'near-miss'), `${entry.identifier} has no near-miss example.`);
    for (const example of entry.examples) {
      assert.ok(['positive', 'near-miss'].includes(example.kind));
      assert.doesNotMatch(example.name, /\s/);
    }
    assert.ok(files.has(`docs-data/${entry.referencePage}`), `${entry.identifier} points at a missing reference page.`);
  }
  const authored = await authoredSources();
  const covered = new Set(document.rules.map(entry => entry.family));
  for (const source of authored) assert.ok(covered.has(`invenio.${source.family}`), `the ${source.family} family is missing from the index.`);
  assert.ok(document.rules.length >= authored.length, 'the index carries at least one rule per authored family.');
  const committed = JSON.parse(readFileSync(new URL('../../docs-data/rules.json', import.meta.url)));
  assert.deepEqual(committed, document);
});
