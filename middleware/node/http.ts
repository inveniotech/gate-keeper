// SPDX-License-Identifier: Apache-2.0
import { ResponseFilter, ResponseFilterError } from './response.ts';

/** Minimal host response shared by the framework adapters. Content type must come from the host. */
export interface HttpResponse {
  readonly headersSent: boolean;
  getHeader(name: string): unknown;
  destroy(error?: Error): unknown;
}
export function validateBound(maxBodyBytes: number): void {
  if (!Number.isSafeInteger(maxBodyBytes) || maxBodyBytes < 1) throw new TypeError('Supply a positive, finite JSON response byte bound.');
}
export function transformJson(filter: ResponseFilter, response: HttpResponse, body: unknown, maxBodyBytes: number): unknown {
  const fail = (): never => { throw new ResponseFilterError(response.headersSent); };
  try {
    if (response.headersSent) return fail();
    // Only inert JSON trees can be protected. Accessors and toJSON methods could change bytes after filtering.
    const seen = new Set<object>(); let estimated = 0;
    const visit = (value: unknown, depth: number): void => {
      if (depth > 64) return fail();
      if (value === null || typeof value === 'boolean' || typeof value === 'number' && Number.isFinite(value) || typeof value === 'string') estimated += Buffer.byteLength(JSON.stringify(value));
      else if (typeof value === 'object' && value !== null) {
        if (seen.has(value) || !Array.isArray(value) && ![Object.prototype, null].includes(Object.getPrototypeOf(value))) return fail();
        seen.add(value); estimated += 2;
        if (typeof Reflect.get(value, 'toJSON') === 'function') return fail();
        const descriptors = Object.getOwnPropertyDescriptors(value); let count = 0;
        if (Array.isArray(value) && Object.keys(value).length !== value.length) return fail();
        for (const [key, descriptor] of Object.entries(descriptors)) {
          if (!descriptor.enumerable) continue;
          if (!Object.hasOwn(descriptor, 'value')) return fail();
          if (count++) estimated++;
          if (!Array.isArray(value)) estimated += Buffer.byteLength(JSON.stringify(key)) + 1;
          visit(descriptor.value, depth + 1);
        }
        seen.delete(value);
      } else return fail();
      if (estimated > maxBodyBytes) return fail();
    };
    visit(body, 0);
    if (Buffer.byteLength(JSON.stringify(body)) > maxBodyBytes) return fail();
    const contentType = response.getHeader('content-type');
    const result = filter.transform(body, { lengthKnown: true, headersCommitted: response.headersSent, contentType: typeof contentType === 'string' ? contentType : null });
    if (Buffer.byteLength(JSON.stringify(result)) > maxBodyBytes) return fail();
    return result;
  } catch (error: unknown) { if (error instanceof ResponseFilterError) throw error; return fail(); }
}
