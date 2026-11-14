// SPDX-License-Identifier: Apache-2.0
import type { RuntimeMatcher } from './runtime.ts';

export const logFailure = 'Gate-Keeper dropped a log record because redaction failed.';
export function redactLogValue(runtime: RuntimeMatcher, value: unknown, top = true, ancestors = new Set<object>()): unknown {
  if (typeof value === 'string') return runtime.redact(value);
  if (typeof value === 'function') throw new TypeError('Custom log serialisers cannot preserve verified redaction.');
  if (value === null || typeof value !== 'object') return value;
  if (ancestors.has(value)) throw new TypeError('Circular log records cannot be redacted safely.');
  ancestors.add(value);
  try {
    const result: Record<PropertyKey, unknown> | unknown[] = Array.isArray(value) ? [] : {};
    for (const key of Reflect.ownKeys(value)) {
      const descriptor = Object.getOwnPropertyDescriptor(value, key)!;
      if (!descriptor.enumerable) continue;
      const item: unknown = Reflect.get(value, key);
      Object.defineProperty(result, key, { enumerable: true, configurable: true, writable: true,
        value: top && key === 'level' ? item : redactLogValue(runtime, item, false, ancestors) });
    }
    return result;
  } finally { ancestors.delete(value); }
}
