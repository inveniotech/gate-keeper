// SPDX-License-Identifier: Apache-2.0
import { ResponseFilter, ResponseFilterError } from './response.ts';
import { transformJson, validateBound } from './http.ts';
import type { HttpResponse } from './http.ts';
import type { RuntimeMatcher } from './runtime.ts';

/** Structural Express response. The host must set a JSON content type before calling json. */
export interface ExpressResponse extends HttpResponse {
  json(body: unknown): unknown;
  send(body: unknown): unknown;
  /** Native writes are rejected outside synchronous filtered JSON delegation. */
  write?(...args: never[]): unknown;
  /** Native completion is rejected outside synchronous filtered JSON delegation, including empty bodies. */
  end?(...args: never[]): unknown;
}
/** Express error callback. Protected failures never pass a body to the original response methods. */
export type Next = (error?: unknown) => void;

/** Installs atomic JSON filtering with an explicit host byte bound; throws for invalid paths or bounds at start-up. */
export function createExpressMiddleware(runtime: RuntimeMatcher, paths: readonly string[], maxBodyBytes: number) {
  const filter = new ResponseFilter(runtime, paths); validateBound(maxBodyBytes);
  const protectedResponse = paths.length > 0;
  return (_request: unknown, response: ExpressResponse, next: Next): void => {
    if (!protectedResponse) { next(); return; }
    const json = response.json; const send = response.send;
    let sendingVerifiedJson = false; let failed = false;
    const failure = (error: ResponseFilterError): unknown => {
      if (failed) return response;
      // Latch before notifying the host: error handlers can synchronously try another body method.
      failed = true;
      if (response.headersSent || error.abortRequired) response.destroy(error); else next(error);
      return response;
    };
    response.json = function (body: unknown): unknown {
      if (failed) return response;
      if (sendingVerifiedJson) return failure(new ResponseFilterError(response.headersSent));
      let safe: unknown;
      try { safe = transformJson(filter, response, body, maxBodyBytes); }
      catch (error: unknown) { return failure(error instanceof ResponseFilterError ? error : new ResponseFilterError(response.headersSent)); }
      sendingVerifiedJson = true;
      try { return json.call(this, safe); } finally { sendingVerifiedJson = false; }
    };
    response.send = function (body: unknown): unknown {
      if (failed) return response;
      if (sendingVerifiedJson) return send.call(this, body);
      return failure(new ResponseFilterError(response.headersSent));
    };
    for (const method of ['write', 'end'] as const) {
      const original = response[method];
      if (original === undefined) continue;
      response[method] = function (...args: never[]): unknown {
        if (failed) return method === 'write' ? false : response;
        if (sendingVerifiedJson) {
          sendingVerifiedJson = false;
          try { return original.apply(this, args); } finally { sendingVerifiedJson = true; }
        }
        failure(new ResponseFilterError(response.headersSent));
        return method === 'write' ? false : response;
      };
    }
    next();
  };
}
