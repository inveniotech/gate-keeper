// SPDX-License-Identifier: Apache-2.0
import { ResponseFilter, ResponseFilterError } from './response.ts';
import { transformJson, validateBound } from './http.ts';
import type { HttpResponse } from './http.ts';
import type { RuntimeMatcher } from './runtime.ts';

/** Minimal observer; provider exceptions and protected response errors use the error channel. */
export interface Observer { next?(value: unknown): void; error?(error: unknown): void; complete?(): void }
/** UnsubscribeHandle ownership stays with the caller and cancellation delegates to the source. */
export interface UnsubscribeHandle { unsubscribe(): void }
/** The source surface accepted by both the minimal observable and a framework-owned observable. */
export interface ObservableSource { subscribe(observer: Observer): UnsubscribeHandle | void }
/** Structural RxJS operator; call receives the downstream observer as its first argument. */
export interface LiftOperator { call(observer: Observer, upstream: ObservableSource): UnsubscribeHandle | void }
/** Dependency-free observable surface. Real RxJS sources retain their own lift and pipe implementations. */
export interface ObservableLike extends ObservableSource {
  pipe(operator: (source: ObservableLike) => ObservableLike): ObservableLike;
}
/** The HTTP subset of a Nest execution context. */
export interface NestContext { switchToHttp(): { getResponse(): HttpResponse } }
/** The next Nest handler provides an observable without requiring an RxJS import. */
export interface NestHandler { handle(): ObservableLike }

/** Creates a Nest HTTP interceptor with an explicit bound and no framework runtime dependency. */
export function createNestInterceptor(runtime: RuntimeMatcher, paths: readonly string[], maxBodyBytes: number) {
  const filter = new ResponseFilter(runtime, paths); validateBound(maxBodyBytes);
  const protectedResponse = paths.length > 0;
  function intercept<Result extends ObservableSource>(context: NestContext, next: { handle(): Result & { lift(operator: LiftOperator): Result } }): Result;
  function intercept(context: NestContext, next: NestHandler): ObservableLike;
  function intercept(context: NestContext, next: { handle(): ObservableSource }): ObservableSource {
      const source = next.handle(); if (!protectedResponse) return source;
      const response = context.switchToHttp().getResponse();
      const subscribe = (observer: Observer): UnsubscribeHandle => {
        let stopped = false; let upstream: UnsubscribeHandle | void;
        const stop = () => { stopped = true; upstream?.unsubscribe(); };
        upstream = source.subscribe({
          next(value) {
            if (stopped) return;
            let safe: unknown;
            try { safe = transformJson(filter, response, value, maxBodyBytes); }
            catch (error: unknown) {
              stop();
              if (response.headersSent || error instanceof ResponseFilterError && error.abortRequired) response.destroy(error instanceof Error ? error : undefined);
              observer.error?.(error); return;
            }
            observer.next?.(safe);
          },
          error(error) { if (!stopped) { stop(); observer.error?.(error); } },
          complete() { if (!stopped) { stop(); observer.complete?.(); } },
        });
        if (stopped) upstream?.unsubscribe();
        return { unsubscribe: stop };
      };
      const result: ObservableLike & { '@@observable'(): ObservableLike } = {
        subscribe,
        pipe(operator) { return operator(this); },
        '@@observable'() { return this; },
      };
      // RxJS operators use lift, and Nest recognises RxJS observables via lift and subscribe.
      const lifted = source as ObservableSource & { lift?: (operator: LiftOperator) => ObservableSource };
      if (typeof lifted.lift === 'function') return lifted.lift({ call(observer) { return subscribe(observer); } });
      return result;
  }
  return { intercept };
}
