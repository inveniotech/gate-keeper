// P3-00: real native matcher, validator, scalar-span and redaction agreement.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { RuntimeMatcher, InputLimitError, validate } from '../../middleware/node/runtime.ts';
import { ConsentCheck } from '../../middleware/node/consent.ts';
import { ResponseFilter, ResponseFilterError } from '../../middleware/node/response.ts';
import './check-review.mjs';

const vectors = JSON.parse(readFileSync(new URL('./vectors.json', import.meta.url), 'utf8'));
const runtime = new RuntimeMatcher(vectors.rules, [], undefined, vectors.packs);
assert.deepEqual(runtime.versions(), { adapterVersion: '0.1.0', packs: vectors.packs.map(({ id, version, contentDigest }) => ({ id, version, contentDigest })).sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0) });
let checked = 0;
function verify(sample, current) {
  assert.deepEqual(current.matches(sample.text), sample.runtimeExpected, `Native findings differ: ${sample.name}`);
  for (const surface of ['log', 'response', 'value'])
    assert.deepEqual(current.classify(sample.text, { surface, fieldName: 'synthetic' }), sample.classification, `Classification differs: ${sample.name}`);
  const redacted = current.redact(sample.text);
  assert.equal(redacted, sample.redacted, `Redaction differs: ${sample.name}`);
  assert.equal(current.redact(redacted), redacted, `Idempotence differs: ${sample.name}`);
  checked++;
}
for (const sample of vectors.cases) {
  const actual = runtime.matches(sample.text, sample.path).map(({ ruleId, severity, start, length }) => ({ ruleId, severity, start, length }));
  const expected = sample.expected.map(({ ruleId, severity, start, length }) => ({ ruleId, severity, start, length }));
  assert.deepEqual(actual, expected, `Scanner per-rule parity differs: ${sample.name}`);
  verify(sample, runtime);
}
for (const sample of vectors.syntheticRuntimeCases) verify(sample, new RuntimeMatcher(sample.rules));
for (const name of ['digits-length', 'nuban', 'ng-phone-prefix', 'email-not-example-domain']) {
  const data = JSON.parse(readFileSync(new URL(`./validator-${name}.json`, import.meta.url), 'utf8'));
  for (const item of data.cases) assert.equal(validate(item.text, { ...data, name }), item.accepted, `Validator differs: ${name}`);
}
const limit = '😀'.repeat(65_536);
assert.equal(runtime.redact(limit), limit);
assert.throws(() => runtime.redact(limit + 'x'), InputLimitError);
assert.throws(() => runtime.redact('\ud800'), TypeError);
assert.throws(() => runtime.classify('', { surface: 'migration' }), TypeError);
assert.throws(() => runtime.classify('', { surface: 'value', fieldName: '' }), TypeError);
const optional = structuredClone(vectors.syntheticRuntimeCases[0].rules[0]);
optional.redaction = { optional: true };
const messages = [];
const disabled = new RuntimeMatcher([optional], [optional.id], (message) => messages.push(message));
assert.equal(disabled.redact('label:mark'), 'label:mark');
assert.equal(messages.length, 1);
assert.throws(() => new RuntimeMatcher(vectors.syntheticRuntimeCases[0].rules, [optional.id], () => {}), TypeError);
const behaviour = JSON.parse(readFileSync(new URL('./behaviour-vectors.json', import.meta.url), 'utf8'));
for (const sample of behaviour.consent.cases) {
  let calls = 0; const events = []; const controller = new AbortController();
  const provider = sample.provider === 'absent' ? undefined : {
    async getState() {
      calls++;
      if (sample.provider === 'provider-timeout') { const internal = new AbortController(); internal.abort(); internal.signal.throwIfAborted(); }
      if (sample.provider === 'caller-cancelled-during') { controller.abort(); controller.signal.throwIfAborted(); }
      if (sample.provider === 'throws') throw new Error('synthetic-sensitive-marker');
      return sample.provider;
    },
  };
  const consent = new ConsentCheck(provider, (event) => events.push(event));
  if (sample.provider === 'cancelled') controller.abort();
  let state; let granted;
  try { state = await consent.check('synthetic-subject', 'acme.marketing-sms', controller.signal); }
  catch (error) { if (!controller.signal.aborted) throw error; state = 'cancelled'; }
  try { granted = await consent.isGranted('synthetic-subject', 'acme.marketing-sms', controller.signal); }
  catch (error) { if (!controller.signal.aborted) throw error; granted = null; }
  assert.equal(state, sample.expected, `Consent state differs: ${sample.provider}`);
  assert.equal(granted, sample.isGranted, `Consent boolean differs: ${sample.provider}`);
  assert.equal(calls, sample.calls); assert.equal(events.length, sample.diagnostics);
  for (const event of events) {
    assert.deepEqual(Object.keys(event).sort(), ['errorType', 'purpose']);
    assert.ok(!JSON.stringify(event).includes('synthetic-sensitive-marker'));
    assert.ok(!JSON.stringify(event).includes('synthetic-subject'));
  }
}
const consent = new ConsentCheck({ async getState() { return 'denied'; } }, () => {});
for (const sample of behaviour.consent.purposes) {
  if (sample.accepted) assert.equal(await consent.check('synthetic-subject', sample.value), 'denied');
  else await assert.rejects(consent.check('synthetic-subject', sample.value), TypeError);
}
assert.equal(await consent.check('😀'.repeat(1024), 'a.b'), 'denied');
await assert.rejects(consent.check('😀'.repeat(1025), 'a.b'), TypeError);
const responseRuntime = new RuntimeMatcher(behaviour.response.rules);
for (const sample of behaviour.response.cases) {
  const before = structuredClone(sample.body); const filter = new ResponseFilter(responseRuntime, sample.paths);
  const options = { lengthKnown: sample.lengthKnown, headersCommitted: sample.headersCommitted, contentType: sample.contentType };
  if (sample.failed) {
    assert.throws(() => filter.transform(sample.body, options), (error) => error instanceof ResponseFilterError && error.abortRequired === sample.abortRequired, sample.name);
  } else assert.deepEqual(filter.transform(sample.body, options), sample.expected, `Response differs: ${sample.name}`);
  assert.deepEqual(sample.body, before, `Input response was mutated: ${sample.name}`);
}
assert.throws(() => new ResponseFilter(responseRuntime, ['/bad~9path']), TypeError);
const wildcard = new ResponseFilter(responseRuntime, ['/customers/*/phone']);
assert.throws(() => wildcard.transform({ customers: [{ phone: 'marker' }, { phone: 'x'.repeat(65_537) }] }), ResponseFilterError);
assert.throws(() => wildcard.transform({ customers: [{ phone: '\ud800' }] }), ResponseFilterError);
console.log(`Node native matching/redaction passed ${checked} cases; four validators, consent and ${behaviour.response.cases.length} atomic response cases passed.`);
