// SPDX-License-Identifier: Apache-2.0
import type { RuntimeMatcher } from './runtime.ts';
import { logFailure, redactLogValue } from './logging.ts';

/** The logform information contract, including required level and message fields. */
export interface WinstonInfo { level: string; message: unknown; [key: string | symbol]: unknown }

/** Creates a winston transform for format(factory). Failures replace the entire record with fixed safe metadata. */
export function createWinstonFormat(runtime: RuntimeMatcher): (info: WinstonInfo) => WinstonInfo {
  return (info) => {
    try { return redactLogValue(runtime, info) as WinstonInfo; }
    catch { return { level: 'error', message: logFailure, ...(Object.hasOwn(info, Symbol.for('level')) ? { [Symbol.for('level')]: 'error' } : {}) }; }
  };
}
