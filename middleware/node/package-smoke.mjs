// SPDX-License-Identifier: Apache-2.0
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
const require = createRequire(import.meta.url);
for (const subpath of ['', '/express', '/nest', '/pino', '/winston']) {
  const esm = await import('@inveniotech/gate-keeper' + subpath);
  const cjs = require('@inveniotech/gate-keeper' + subpath);
  assert.deepEqual(Object.keys(esm).sort(), Object.keys(cjs).sort());
  if (!subpath) {
    for (const module of [esm, cjs]) {
      assert.equal(typeof module.loadInstallation, 'function');
      assert.throws(() => module.loadInstallation({ embedded: [], keys: { pack: {} } }), module.PackLoadError);
    }
  }
}
const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url)));
assert.deepEqual(pkg.dependencies, {});
const directory = mkdtempSync(fileURLToPath(new URL('./.package-smoke-', import.meta.url)));
try {
  const consumer = `import { loadInstallation } from '@inveniotech/gate-keeper';
import { createExpressMiddleware } from '@inveniotech/gate-keeper/express';
import { createNestInterceptor } from '@inveniotech/gate-keeper/nest';
import { createPinoHooks } from '@inveniotech/gate-keeper/pino';
import { createWinstonFormat } from '@inveniotech/gate-keeper/winston';
const runtime = loadInstallation({ embedded: [], keys: { pack: {} } });
createExpressMiddleware(runtime, [], 1024);
createNestInterceptor(runtime, [], 1024);
createPinoHooks(runtime);
createWinstonFormat(runtime);

// Independent structural registration contracts from Express RequestHandler,
// Nest NestInterceptor/CallHandler, RxJS Observable/Operator, pino Hooks/LogFn,
// and logform TransformFunction/TransformableInfo. No framework package is installed.
interface HostResponse {
  headersSent: boolean;
  getHeader(name: string): string | number | readonly string[] | undefined;
  destroy(error?: Error): this;
  json(body?: unknown): this;
  send(body?: unknown): this;
  write(chunk: string | Uint8Array, callback?: (error?: Error | null) => void): boolean;
  write(chunk: string | Uint8Array, encoding: string, callback?: (error?: Error | null) => void): boolean;
  end(callback?: () => void): this;
  end(chunk: string | Uint8Array, callback?: () => void): this;
  end(chunk: string | Uint8Array, encoding: string, callback?: () => void): this;
}
type HostExpressHandler = (request: { url: string }, response: HostResponse, next: (error?: unknown) => void) => void;
const expressRegistration: HostExpressHandler = createExpressMiddleware(runtime, ['/value'], 4096);
interface HostObserver { next(value: unknown): void; error(error: unknown): void; complete(): void }
interface HostHandle { unsubscribe(): void }
interface HostOperator { call(observer: HostObserver, source: HostObservable): HostHandle | void }
interface HostObservable {
  source?: HostObservable;
  operator?: HostOperator;
  lift(operator?: HostOperator): HostObservable;
  subscribe(observer: Partial<HostObserver>): HostHandle;
  pipe(...operators: readonly ((source: HostObservable) => HostObservable)[]): HostObservable;
}
interface HostContext { switchToHttp(): { getResponse(): HostResponse } }
interface HostNestInterceptor { intercept(context: HostContext, next: { handle(): HostObservable }): HostObservable | Promise<HostObservable> }
const nestRegistration: HostNestInterceptor = createNestInterceptor(runtime, ['/value'], 4096);
interface HostLogMethod {
  (object: unknown, message?: string, ...arguments_: unknown[]): void;
  (message: string, ...arguments_: unknown[]): void;
}
interface HostPinoHooks { logMethod(this: { level: string }, args: unknown[], method: HostLogMethod, level: number): void }
const pinoRegistration: HostPinoHooks = createPinoHooks(runtime);
interface HostWinstonInfo { level: string; message: unknown; [key: string | symbol]: unknown }
type HostTransformFunction = (info: HostWinstonInfo, options?: unknown) => HostWinstonInfo | boolean;
declare function hostWinstonFormat(transform: HostTransformFunction): () => { transform: HostTransformFunction };
const winstonRegistration = hostWinstonFormat(createWinstonFormat(runtime));
void [expressRegistration, nestRegistration, pinoRegistration, winstonRegistration];
`;
  for (const extension of ['mts', 'cts']) {
    const file = join(directory, `consumer.${extension}`); writeFileSync(file, consumer);
    const result = spawnSync(process.execPath, [fileURLToPath(new URL('./node_modules/typescript/bin/tsc', import.meta.url)), '--noEmit', '--strict', '--target', 'ES2022', '--module', 'NodeNext', '--moduleResolution', 'NodeNext', file], { stdio: 'inherit' });
    assert.equal(result.status, 0, `${extension} consumers must resolve declarations without source files or Node type dependencies.`);
  }
} finally { rmSync(directory, { recursive: true, force: true }); }
console.log('ESM and CommonJS exports and consumer declarations load and refuse an absent signed installation.');
