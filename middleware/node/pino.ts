// SPDX-License-Identifier: Apache-2.0
import type { RuntimeMatcher } from './runtime.ts';
import { logFailure, redactLogValue } from './logging.ts';

/** Creates pino's logMethod hook. Redaction failure emits one fixed message and drops all original arguments. */
export function createPinoHooks(runtime: RuntimeMatcher) {
  return {
    logMethod(this: unknown, args: unknown[], method: (this: unknown, ...args: unknown[]) => unknown): unknown {
      let safe: unknown[];
      try { safe = args.map((value) => redactLogValue(runtime, value)); }
      catch { safe = [logFailure]; }
      return method.apply(this, safe);
    },
  };
}
