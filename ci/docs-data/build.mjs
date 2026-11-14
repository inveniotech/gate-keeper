// SPDX-License-Identifier: Apache-2.0
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { detectionFiles } from '../rule-packs/build-detection.mjs';
import { packVersion } from '../rule-packs/detection-authoring.mjs';
import { approvedCitation } from '../rule-packs/citations.mjs';
import { sorted } from '../pack-release/assembly/build.mjs';

/**
 * Generates the committed docs-data reference files: the per-rule metadata
 * index (`docs-data/rules.json`) and the command-line reference pages
 * (`docs-data/cli/*.md`). The command-line pages embed the exact help text
 * captured from the built binary, so the reference cannot drift from
 * behaviour. Run with `--write` after `dotnet build cli -c Release`, and with
 * `--check` (as continuous integration does) to verify the committed bytes.
 */

/** Reads the authored detection sources, the same authoring inputs build-detection.mjs consumes. */
export async function authoredSources() {
  const folder = new URL('../rule-packs/rules/', import.meta.url);
  const sources = [];
  for (const file of readdirSync(folder).filter(name => name.endsWith('.mjs')).sort()) {
    const { default: source } = await import(new URL(file, folder));
    sources.push(source);
  }
  return sources.sort((left, right) => left.family < right.family ? -1 : left.family > right.family ? 1 : 0);
}

/** Builds one self-contained reference entry per rule from a pack document, its authored source, the register and its examples. */
export function ruleEntry(rule, source, pack, register, examples) {
  const entry = register.entries.find(item => item.category === rule.category);
  if (!entry) throw new Error(`The category ${rule.category} of rule ${rule.id} is not in the citation register.`);
  approvedCitation(register, rule.category);
  return {
    identifier: rule.id,
    family: pack.id,
    title: source.title,
    severity: rule.severity,
    sinceVersion: rule.sinceVersion,
    detects: source.description,
    whyItMatters: entry.explanation,
    citation: { title: rule.citation.title, ndpa: rule.citation.ndpa, gaid: rule.citation.gaid },
    limits: source.limits,
    remediation: source.fix,
    exceptionGuidance: rule.suppressionHints.join(' '),
    ...(rule.processor ? { processor: { dataCategories: rule.processor.dataCategories, usualRegion: rule.processor.usualRegion } } : {}),
    examples,
    referencePage: `rules/${source.family}.md`,
  };
}

/** Reads the words in a synthetic case name, so a corpus near-miss attaches to the rule it exercises. */
function caseWords(name) {
  return new Set(name.toLowerCase().split(/[^a-z0-9]+/).filter(Boolean));
}

/** Selects the corpus near-misses that name a rule's family and, where it has one, its inventory kind. */
export function relatedNearMisses(rule, cases) {
  const parts = rule.id.split('.').slice(1);
  const words = new Set([parts[0]]);
  if (parts.includes('field')) {
    words.add('field');
  } else {
    if (parts.includes('international')) words.add('international');
    if (parts.at(-1) !== parts[0]) words.add(parts.at(-1));
  }
  return cases.filter(item => item.expected.length === 0
    && [...words].every(word => caseWords(item.name).has(word)));
}

/**
 * Builds the true and near-miss examples of one rule. Authored cases come first; the location
 * corpus supplies the families whose acceptance cases live under tests/corpus rather than in the
 * authored source. A rule that has no authored positive also takes its positives from the corpus.
 */
export function ruleExamples(rule, authoredCases, corpusCases = []) {
  const examples = authoredCases
    .filter(item => item.expected.length === 0 || item.expected.some(match => match.ruleId === rule.id))
    .map(item => ({ name: item.name, path: item.path, kind: item.expected.length === 0 ? 'near-miss' : 'positive' }));
  const positive = cases => cases
    .filter(item => item.expected.some(match => match.ruleId === rule.id))
    .map(item => ({ name: item.name, path: item.path, kind: 'positive' }));
  if (authoredCases.length === 0) {
    examples.push(...positive(corpusCases));
    examples.push(...relatedNearMisses(rule, corpusCases)
      .map(item => ({ name: item.name, path: item.path, kind: 'near-miss' })));
  } else if (!examples.some(example => example.kind === 'positive')) {
    examples.push(...positive(corpusCases));
  }
  const unique = new Map(examples.map(example => [`${example.kind}:${example.name}`, example]));
  return [...unique.values()].sort((left, right) => {
    if (left.name !== right.name) return left.name < right.name ? -1 : 1;
    return left.kind < right.kind ? -1 : left.kind > right.kind ? 1 : 0;
  });
}

/** Builds every rule entry, failing the docs-data build when a family ships without reference documentation. */
export function ruleEntries(sources, packs, register, corpusCases = []) {
  if (new Set(packs.map(pack => pack.id)).size !== packs.length)
    throw new Error('Pack identifiers must be unique across the docs-data artefact.');
  const byFamily = new Map(packs.map(pack => [pack.id, pack]));
  if (packs.some(pack => !sources.some(source => `invenio.${source.family}` === pack.id)))
    throw new Error('A generated pack document has no authored detection source.');
  const seen = new Set();
  const entries = [];
  for (const source of sources) {
    if (!source.description?.trim() || !source.limits?.trim() || !source.fix?.trim())
      throw new Error(`The ${source.family} detection source needs reference documentation before its rules can reach the website.`);
    const pack = byFamily.get(`invenio.${source.family}`);
    if (!pack) throw new Error(`No generated pack document matches the ${source.family} detection source.`);
    if (pack.version !== packVersion) throw new Error(`The ${source.family} pack version differs from the authored pack version.`);
    for (const rule of pack.rules) {
      if (seen.has(rule.id)) throw new Error(`The rule identifier ${rule.id} appears in more than one pack.`);
      seen.add(rule.id);
      entries.push(ruleEntry(rule, source, pack, register, ruleExamples(rule, source.cases ?? [], corpusCases)));
    }
  }
  entries.sort((left, right) => left.identifier < right.identifier ? -1 : left.identifier > right.identifier ? 1 : 0);
  return entries;
}

/** Reads the command names a help text lists, so the capture walks the real command tree. */
export function listedCommands(help) {
  const lines = help.split('\n');
  const heading = lines.findIndex(line => line.trim() === 'Commands:');
  if (heading === -1) return [];
  const names = [];
  for (let index = heading + 1; index < lines.length; index++) {
    const line = lines[index];
    if (line.trim() === '') break;
    const name = /^ {2}(\S+)/.exec(line)?.[1];
    if (!name) throw new Error(`A help text lists a command line that is not understood: ${JSON.stringify(line)}.`);
    names.push(name);
  }
  return names.sort((left, right) => left < right ? -1 : left > right ? 1 : 0);
}

/** Captures the exact help text of the root command and of every command it lists, recursively. */
export function captureCommands(run) {
  const commands = [];
  const walk = path => {
    // Help text is normalised to LF here so the reference pages are byte-identical on every platform (rule 22).
    const help = run(path).replace(/\r\n/g, '\n');
    commands.push({ path, help });
    for (const name of listedCommands(help)) walk([...path, name]);
  };
  walk([]);
  commands.sort((left, right) => {
    const leftName = left.path.join(' ');
    const rightName = right.path.join(' ');
    return leftName < rightName ? -1 : leftName > rightName ? 1 : 0;
  });
  return commands;
}

/** Renders one reference page per command, embedding the exact captured help text. */
export function cliReferencePages(commands) {
  const pages = new Map();
  for (const command of commands) {
    const slug = command.path.length === 0 ? 'index' : command.path.join('-');
    const heading = ['gatekeeper-ndpa-check', ...command.path].join(' ');
    const page = `# ${heading}\n\n<!-- Generated by node ci/docs-data/build.mjs --write. -->\n\n`
      + `The exact help text of this build follows, captured from the command line itself, so the reference cannot drift from behaviour.\n\n`
      + '```text\n' + command.help + '```\n';
    pages.set(`docs-data/cli/${slug}.md`, Buffer.from(page, 'utf8'));
  }
  return pages;
}

/** Reads the location acceptance corpus, whose cases carry the examples for rules authored without inline cases. */
export async function locationCorpusCases() {
  const { buildLocationCases } = await import('../../tests/corpus/New-LocationCases.mjs');
  return buildLocationCases();
}

/** Builds the committed reference outputs: the rule metadata index and the command-line reference pages. */
export async function docsDataFiles(run) {
  const register = JSON.parse(readFileSync(new URL('../../rule-packs/citations.json', import.meta.url)));
  const generated = await detectionFiles();
  const sources = await authoredSources();
  const corpusCases = await locationCorpusCases();
  const packs = sources.map(source => {
    const bytes = generated.get(`rule-packs/${source.family}.pack.json`);
    if (!bytes) throw new Error(`The generated pack for ${source.family} is missing.`);
    return JSON.parse(bytes);
  });
  const rules = { schemaVersion: 1, packVersion, rules: ruleEntries(sources, packs, register, corpusCases) };
  const files = cliReferencePages(captureCommands(run));
  files.set('docs-data/rules.json', Buffer.from(JSON.stringify(sorted(rules), null, 2) + '\n', 'utf8'));
  // The generated rule reference pages ship in the same artefact, so this build verifies their bytes as well.
  for (const [name, bytes] of generated) if (name.startsWith('docs-data/rules/')) files.set(name, bytes);
  return files;
}

/** Returns the default Release apphost paths for the requested operating system. */
export function binaryCandidates(platform = process.platform) {
  const folder = fileURLToPath(new URL('../../cli/bin/Release/net10.0/', import.meta.url));
  return platform === 'win32' ? [`${folder}gatekeeper.exe`, `${folder}gatekeeper`]
    : [`${folder}gatekeeper`];
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  const mode = process.argv[2];
  const binary = process.argv[3];
  if (process.argv.length > 4 || !['--write', '--check'].includes(mode) || (binary && !binary.startsWith('--cli=')))
    throw new Error('Use --write or --check, with the built command line as --cli=<path> when it is not in cli/bin/Release.');
  const chosen = binary ? binary.slice('--cli='.length) : binaryCandidates().find(existsSync);
  if (!chosen) throw new Error('The command line is not built yet. Run dotnet build cli -c Release first.');
  const run = path => {
    // Standard output is a pipe here, so System.CommandLine formats help to its fixed maximum width instead of the terminal width.
    const result = spawnSync(chosen, [...path, '--help'], { encoding: 'utf8', maxBuffer: 1024 * 1024 });
    if (result.error || result.status !== 0) throw new Error(`Capturing the help text of ${path.join(' ') || 'the root command'} failed.`);
    return result.stdout;
  };
  const root = new URL('../../', import.meta.url);
  for (const [name, bytes] of await docsDataFiles(run)) {
    const target = fileURLToPath(new URL(name, root));
    if (mode === '--write') { mkdirSync(dirname(target), { recursive: true }); writeFileSync(target, bytes); }
    else if (!readFileSync(target).equals(bytes)) throw new Error(`Generated docs-data output differs: ${name}. Regenerate it.`);
  }
  console.log('The rule metadata index and the command-line reference are verified. No publication occurred.');
}
