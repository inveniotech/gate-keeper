// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, copyFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

test('dogfood runs the real CLI and retains evidence for findings while rejecting tool failures', () => {
  const runner = fileURLToPath(new URL('./scan.mjs', import.meta.url));
  const cli = process.env.GATEKEEPER_TEST_BINARY ? resolve(process.env.GATEKEEPER_TEST_BINARY)
    : fileURLToPath(new URL('../../cli/bin/Release/net10.0/gatekeeper.dll', import.meta.url));
  assert.ok(existsSync(cli), 'Build cli in Release before running this integration test.');
  const work = mkdtempSync(join(tmpdir(), 'gate-keeper-dogfood-'));
  try {
    const root = join(work, 'repo');
    const config = join(root, '.gatekeeper');
    mkdirSync(config, { recursive: true });
    writeFileSync(join(config, 'gatekeeper.toml'), 'schemaVersion = 1\n[scan]\nhistory = false\n[packs]\nlocal = [".gatekeeper/probe.json"]\n');
    copyFileSync(new URL('../zero-egress/probe.pack.json', import.meta.url), join(config, 'probe.json'));
    const invoke = (out, date = '2026-11-05') => spawnSync(process.execPath, [runner, cli, root, date, out], { encoding: 'utf8' });
    const clean = invoke(join(work, 'clean'));
    assert.equal(clean.status, 0, clean.stderr);
    writeFileSync(join(root, 'example.txt'), 'gatekeeper-corpus-probe\n');
    const findings = invoke(join(work, 'findings'));
    assert.equal(findings.status, 1, findings.stderr);
    assert.doesNotMatch(findings.stdout + findings.stderr, /gatekeeper-corpus-probe|example.txt/);
    const evidence = readFileSync(join(work, 'findings/evidence.json'), 'utf8');
    assert.doesNotMatch(evidence, /gatekeeper-corpus-probe/);
    assert.equal(JSON.parse(evidence).findings.length, 1);
    assert.equal(JSON.parse(evidence).findings[0].ruleId, 'local.egress.probe');
    assert.equal(invoke(join(work, 'findings')).status, 2, 'Existing evidence cannot be overwritten.');
    assert.equal(readFileSync(join(work, 'findings/evidence.json'), 'utf8'), evidence);
    assert.equal(invoke(join(root, 'nested-output')).status, 2, 'Output cannot affect the scanned tree.');
    assert.equal(invoke(join(work, 'invalid-date'), '2026-02-30').status, 2);
    writeFileSync(join(config, 'gatekeeper.toml'), 'schemaVersion = 999\n');
    const invalid = invoke(join(work, 'invalid-config'));
    assert.equal(invalid.status, 2);
    assert.equal(existsSync(join(work, 'invalid-config/evidence.json')), false);
    assert.doesNotMatch(invalid.stdout + invalid.stderr, /999|stack|schemaVersion/);
  } finally {
    assert.equal(dirname(work), resolve(tmpdir()));
    rmSync(work, { recursive: true, force: true });
  }
});
