// SPDX-License-Identifier: Apache-2.0
// Deterministic regressions for the P3-00 owner review.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ConsentCheck } from '../../middleware/node/consent.ts';
import { ResponseFilter, ResponseFilterError } from '../../middleware/node/response.ts';
import { RuntimeMatcher, PackLoadError } from '../../middleware/node/runtime.ts';

const events = [];
const ownTimeout = new ConsentCheck({ async getState() {
  const internal = new AbortController(); internal.abort(); internal.signal.throwIfAborted();
} }, (event) => events.push(event));
assert.equal(await ownTimeout.check('synthetic-subject', 'a.b'), 'unknown');
assert.equal(await ownTimeout.isGranted('synthetic-subject', 'a.b'), false);
assert.equal(events.length, 2);
const caller = new AbortController();
const cancelled = new ConsentCheck({ async getState() { caller.abort(); caller.signal.throwIfAborted(); } }, () => assert.fail('Caller cancellation must not be diagnosed.'));
await assert.rejects(cancelled.check('synthetic-subject', 'a.b', caller.signal), (error) => error === caller.signal.reason);

const behaviour = JSON.parse(readFileSync(new URL('./behaviour-vectors.json', import.meta.url), 'utf8'));
const runtime = new RuntimeMatcher(behaviour.response.rules);
const vectors = JSON.parse(readFileSync(new URL('./vectors.json', import.meta.url), 'utf8'));
const fullRuntime = new RuntimeMatcher(vectors.rules);
for (const seed of ['a@.', 'bvn 22222222222222222222 ', '😀[REDACTED:previous.rule] ']) {
  const input = [...seed.repeat(65_536)].slice(0, 65_536).join('');
  assert.equal(fullRuntime.redact(input), input, 'Cap-sized adversarial near misses remain unchanged.');
}
const body = { phone: 'marker' };
const filter = new ResponseFilter(runtime, ['/phone']);
assert.throws(() => filter.transform(body, { contentType: undefined }), ResponseFilterError);
assert.throws(() => filter.transform(body, { contentType: null }), ResponseFilterError);
const failure = new ResponseFilter({ redact() { throw new PackLoadError(); } }, ['/phone']);
assert.throws(() => failure.transform(body, { contentType: 'application/json' }), (error) => error instanceof ResponseFilterError && !error.abortRequired);
assert.deepEqual(body, { phone: 'marker' });
console.log('Node review regressions passed.');
