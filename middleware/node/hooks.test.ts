// SPDX-License-Identifier: Apache-2.0
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createExpressMiddleware } from './express.ts';
import { createNestInterceptor } from './nest.ts';
import { createPinoHooks } from './pino.ts';
import { createWinstonFormat } from './winston.ts';
import { RuntimeMatcher } from './runtime.ts';
import { ResponseFilterError } from './response.ts';
import type { ObservableLike, Observer } from './nest.ts';

const runtime = new RuntimeMatcher([{ id: 'test.marker', category: 'synthetic', severity: 'note', matcher: { list: { package: ['synthetic-marker'] } } }]);
const source = 'synthetic-marker'; const safe = '[REDACTED:test.marker]';
function response(contentType: unknown = 'application/json', headersSent = false) {
  return { headersSent, written: [] as unknown[], destroyed: 0,
    getHeader() { return contentType; }, destroy() { this.destroyed++; },
    json(value: unknown) { this.written.push(value); return this; }, send(value: unknown) { this.written.push(value); return this; } };
}
test('Express should transform a protected JSON tree atomically', () => {
  const res = response(); const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); });
  res.json({ value: source, other: source });
  assert.deepEqual(res.written, [{ value: safe, other: source }]); assert.deepEqual(errors, []);
});
for (const body of [{}, { value: null }]) test(`Express should handle missing and null protected leaves ${JSON.stringify(body)}`, () => {
  const res = response(); const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); }); res.json(body);
  if ('value' in body) { assert.equal(res.written.length, 0); assert.ok(errors[0] instanceof ResponseFilterError); }
  else assert.deepEqual(res.written, [{}]);
});
for (const body of ['unparsed text', Buffer.from('{}'), { pipe() {} }]) test('Express should refuse non-JSON send before writing', () => {
  const res = response(); const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); }); res.send(body);
  assert.equal(res.written.length, 0); assert.ok(errors[0] instanceof ResponseFilterError);
});
for (const type of [undefined, null, '', 'text/plain']) test(`Express should refuse absent or non-JSON content type ${type}`, () => {
  const res = response(type === undefined ? null : type); const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); }); res.json({ value: source });
  assert.equal(res.written.length, 0); assert.ok(errors[0] instanceof ResponseFilterError);
});
test('Express should abort committed responses without writing', () => {
  const res = response('application/json', true); createExpressMiddleware(runtime, ['/value'], 4096)({}, res, () => {}); res.json({ value: source });
  assert.equal(res.destroyed, 1); assert.equal(res.written.length, 0);
});
test('Express should bound both original and expanded JSON', () => {
  const res = response(); const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 29)({}, res, (error) => { if (error) errors.push(error); }); res.json({ value: source });
  assert.equal(res.written.length, 0); assert.ok(errors[0] instanceof ResponseFilterError);
});
test('Express should preserve real json-to-send delegation without filtering twice', () => {
  const res = response(); res.json = function (value) { this.send(JSON.stringify(value)); return this; };
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, () => {}); res.json({ value: source });
  assert.deepEqual(res.written, [JSON.stringify({ value: safe })]);
});
test('Express should pass through unchanged without configured paths', () => {
  const res = response(); const json = res.json; const send = res.send;
  createExpressMiddleware(runtime, [], 4096)({}, res, () => {});
  assert.equal(res.json, json); assert.equal(res.send, send);
});
function observable(value: unknown): ObservableLike {
  return { subscribe(observer: Observer) { observer.next?.(value); observer.complete?.(); return { unsubscribe() {} }; },
    pipe(operator) { return operator(this); } };
}
test('Nest should transform once and support an outer pipe operator', () => {
  const res = response(); const values: unknown[] = []; let handlerCalls = 0;
  const result = createNestInterceptor(runtime, ['/value'], 4096).intercept({ switchToHttp: () => ({ getResponse: () => res }) }, { handle() { handlerCalls++; return observable({ value: source }); } });
  result.pipe((input) => input).subscribe({ next: (value) => values.push(value) });
  assert.deepEqual(values, [{ value: safe }]); assert.equal(handlerCalls, 1);
});
for (const committed of [false, true]) test(`Nest should error and abort only committed failure ${committed}`, () => {
  const res = response('application/json', committed); const errors: unknown[] = []; const values: unknown[] = [];
  createNestInterceptor(runtime, ['/value'], 4096).intercept({ switchToHttp: () => ({ getResponse: () => res }) }, { handle: () => observable({ value: null }) })
    .subscribe({ next: (value) => values.push(value), error: (error) => errors.push(error) });
  assert.deepEqual(values, []); assert.ok(errors[0] instanceof ResponseFilterError); assert.equal(res.destroyed, committed ? 1 : 0);
});
test('Pino should redact message strings and merging-object properties except level', () => {
  const emitted: unknown[][] = []; const owner = { marker: true }; let receiver: unknown;
  createPinoHooks(runtime).logMethod.call(owner, [{ level: source, value: source }, source], function (...args) { receiver = this; emitted.push(args); });
  assert.deepEqual(emitted, [[{ level: source, value: safe }, safe]]); assert.equal(receiver, owner);
});
test('Winston should redact message and fields without mutating caller records', () => {
  const input = { level: source, message: source, value: source }; const result = createWinstonFormat(runtime)(input);
  assert.deepEqual(result, { level: source, message: safe, value: safe }); assert.equal(input.message, source);
});
test('Logging hooks should drop all original text on redaction failure', () => {
  const oversized = 'x'.repeat(65_537); const emitted: unknown[][] = [];
  createPinoHooks(runtime).logMethod([source, oversized], (...args) => emitted.push(args));
  assert.deepEqual(emitted, [['Gate-Keeper dropped a log record because redaction failed.']]);
  assert.deepEqual(createWinstonFormat(runtime)({ level: 'info', message: oversized, other: source }), { level: 'error', message: 'Gate-Keeper dropped a log record because redaction failed.' });
});
test('Logging hooks should reject custom serialisers that could reintroduce original text', () => {
  const record = { value: source, toJSON() { return { value: source }; } }; const emitted: unknown[][] = [];
  createPinoHooks(runtime).logMethod([record], (...args) => emitted.push(args));
  assert.deepEqual(emitted, [['Gate-Keeper dropped a log record because redaction failed.']]);
  assert.equal(createWinstonFormat(runtime)({ message: source, record }).message, 'Gate-Keeper dropped a log record because redaction failed.');
});
test('Express should accept JSON exactly at the host byte bound', () => {
  const res = response(); createExpressMiddleware(runtime, ['/missing'], 12)({}, res, () => {}); res.json({ ok: true });
  assert.deepEqual(res.written, [{ ok: true }]);
});
test('Express should retain configured protection when caller mutates its path array', () => {
  const paths = ['/value']; const hook = createExpressMiddleware(runtime, paths, 4096); paths.length = 0;
  const res = response(); hook({}, res, () => {}); res.json({ value: source });
  assert.deepEqual(res.written, [{ value: safe }]);
});
test('Nest should retain configured protection when caller mutates its path array', () => {
  const paths = ['/value']; const hook = createNestInterceptor(runtime, paths, 4096); paths.length = 0;
  const values: unknown[] = [];
  hook.intercept({ switchToHttp: () => ({ getResponse: () => response() }) }, { handle: () => observable({ value: source }) })
    .subscribe({ next: (value) => values.push(value) });
  assert.deepEqual(values, [{ value: safe }]);
});
test('Nest should pass the subscriber argument through a structural lift operator', () => {
  const values: unknown[] = [];
  const input = observable({ value: source });
  const lifted = { ...input, lift(operator: { call(observer: Observer, upstream: ObservableLike): unknown }): ObservableLike {
    return { ...input, subscribe(observer: Observer) { return operator.call(observer, input) as ReturnType<ObservableLike['subscribe']>; } };
  } };
  createNestInterceptor(runtime, ['/value'], 4096).intercept({ switchToHttp: () => ({ getResponse: () => response() }) }, { handle: () => lifted })
    .subscribe({ next: (value) => values.push(value) });
  assert.deepEqual(values, [{ value: safe }]);
});
function nativeResponse() {
  return { ...response(),
    write(...args: unknown[]) { this.written.push(args[0]); return true; },
    end(...args: unknown[]) { if (args[0] !== undefined) this.written.push(args[0]); return this; },
  };
}
for (const method of ['write', 'end'] as const) for (const committed of [false, true]) test(`Express should refuse native ${method} before bytes with committed=${committed}`, () => {
  const res = nativeResponse(); res.headersSent = committed; const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); });
  res[method](source);
  assert.deepEqual(res.written, []); assert.equal(res.destroyed, committed ? 1 : 0);
  if (!committed) assert.ok(errors[0] instanceof ResponseFilterError);
});
test('Express should refuse a pipe-shaped writer before any chunk escapes', () => {
  const res = nativeResponse(); const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); });
  ({ pipe(destination: typeof res) { destination.write(source); destination.end(); } }).pipe(res);
  assert.deepEqual(res.written, []); assert.ok(errors[0] instanceof ResponseFilterError);
});
test('Express should keep all body methods closed after a rejected response', () => {
  const res = nativeResponse(); const errors: unknown[] = [];
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); });
  res.write(source); res.json({ value: source }); res.send(source); res.end(source);
  assert.deepEqual(res.written, []); assert.equal(errors.length, 1);
});
for (const empty of [false, true]) test(`Express should permit filtered json-send-end delegation with empty=${empty}`, () => {
  const res = nativeResponse(); const errors: unknown[] = [];
  res.json = function (value) { return this.send(JSON.stringify(value)); };
  res.send = function (value) { return empty ? this.end() : this.end(value); };
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); });
  res.json({ value: source });
  assert.deepEqual(res.written, empty ? [] : [JSON.stringify({ value: safe })]); assert.deepEqual(errors, []);
});
test('Express should preserve native methods without configured paths', () => {
  const res = nativeResponse(); const write = res.write; const end = res.end;
  createExpressMiddleware(runtime, [], 4096)({}, res, () => {});
  assert.equal(res.write, write); assert.equal(res.end, end); res.write(source); res.end(source);
  assert.deepEqual(res.written, [source, source]);
});
test('Express should reject reentrant JSON during trusted delegation', () => {
  const res = nativeResponse(); const errors: unknown[] = [];
  res.json = function () { this.json({ value: source }); return this.end(source); };
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); });
  res.json({ value: source });
  assert.deepEqual(res.written, []); assert.equal(errors.length, 1);
});
test('Express should close trusted delegation before a native completion callback runs', () => {
  const res = nativeResponse(); const errors: unknown[] = [];
  res.json = function (value) { return this.send(JSON.stringify(value)); };
  res.send = function (value) { return this.end(value, () => { this.write(source); }); };
  res.end = function (...args) { if (args[0] !== undefined) this.written.push(args[0]); if (typeof args[1] === 'function') args[1](); return this; };
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => { if (error) errors.push(error); });
  res.json({ value: source });
  assert.deepEqual(res.written, [JSON.stringify({ value: safe })]); assert.ok(errors[0] instanceof ResponseFilterError);
});
test('Express should preserve native callback arguments and return values during filtered delegation', () => {
  const res = nativeResponse(); const callback = () => {}; const observed: unknown[][] = [];
  res.write = function (...args) { assert.equal(this, res); observed.push(args); return false; };
  res.end = function (...args) { assert.equal(this, res); observed.push(args); return this; };
  res.json = function (value) { return this.send(JSON.stringify(value)); };
  res.send = function (value) { assert.equal(this.write(value, 'utf8', callback), false); return this.end(callback); };
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, () => {});
  assert.equal(res.json({ value: source }), res);
  assert.deepEqual(observed, [[JSON.stringify({ value: safe }), 'utf8', callback], [callback]]);
});
test('Express should latch rejection before invoking a reentrant error handler', () => {
  const res = nativeResponse(); let errors = 0;
  createExpressMiddleware(runtime, ['/value'], 4096)({}, res, (error) => {
    if (error) { errors++; res.json({ value: source }); res.end(source); }
  });
  res.write(source);
  assert.deepEqual(res.written, []); assert.equal(errors, 1);
});
