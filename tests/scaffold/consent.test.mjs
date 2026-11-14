// SPDX-License-Identifier: Apache-2.0
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

test('generated Node provider preserves history and maps the latest event for each pair', async () => {
  const folder = await mkdtemp(join(tmpdir(), 'gatekeeper-scaffold-'));
  try {
    const template = await readFile(new URL('../../cli/ConsentTemplates/node/consent-provider.ts', import.meta.url), 'utf8');
    const runtime = new URL('../../middleware/node/consent.ts', import.meta.url).href;
    // Resolve the package import to the same source contract without requiring a published package.
    await writeFile(join(folder, 'provider.ts'), template.replace('@inveniotech/gate-keeper', runtime));
    const { EventConsentProvider } = await import(pathToFileURL(join(folder, 'provider.ts')).href);
    const provider = new EventConsentProvider(() => new Date('2026-11-14T00:00:00Z'));
    assert.equal(await provider.getState('subject-1', 'marketing.email'), 'denied');
    await provider.record('subject-1', 'marketing.email', true, 'settings', 'v1');
    const first = provider.history();
    assert.equal(await provider.getState('subject-1', 'marketing.email'), 'granted');
    assert.equal(await provider.getState('subject-2', 'marketing.email'), 'denied');
    assert.equal(await provider.getState('subject-1', 'marketing.sms'), 'denied');
    await provider.record('subject-1', 'marketing.email', false, 'settings', 'v1');
    assert.equal(await provider.getState('subject-1', 'marketing.email'), 'denied');
    await provider.record('subject-1', 'marketing.email', true, 'settings', 'v2');
    assert.equal(await provider.getState('subject-1', 'marketing.email'), 'granted');
    assert.equal(first.length, 1);
    assert.deepEqual(provider.history().map(event => event.kind), ['granted', 'withdrawn', 'granted']);
    assert.deepEqual(provider.history()[2], { subjectId: 'subject-1', purpose: 'marketing.email', kind: 'granted', time: '2026-11-14T00:00:00.000Z', channel: 'settings', noticeVersion: 'v2' });
    assert.throws(() => { first[0].kind = 'withdrawn'; }, TypeError);
    await assert.rejects(provider.record('subject-1', 'invalid', true, 'settings', 'v1'), TypeError);
    const controller = new AbortController();
    controller.abort();
    await assert.rejects(provider.record('subject-1', 'marketing.email', true, 'settings', 'v1', controller.signal), { name: 'AbortError' });
    assert.equal(provider.history().length, 3);
  } finally { await rm(folder, { recursive: true, force: true }); }
});
