// SPDX-License-Identifier: Apache-2.0
import { scalars } from './runtime.ts';

export type ConsentState = 'granted' | 'denied' | 'unknown';
export interface ConsentProvider { getState(subjectId: string, purpose: string, signal?: AbortSignal): Promise<ConsentState> }
export interface ConsentDiagnostic { readonly purpose: string | null; readonly errorType: string }

/** Stateless consent mapping, with application-owned provider and no retained answers. */
export class ConsentCheck {
  private readonly provider: ConsentProvider | undefined;
  private readonly diagnostic: (event: ConsentDiagnostic) => void;
  constructor(provider: ConsentProvider | undefined, diagnostic: (event: ConsentDiagnostic) => void) {
    this.provider = provider; this.diagnostic = diagnostic;
    if (!provider) diagnostic({ purpose: null, errorType: 'NoConsentProvider' });
  }
  /** Checks arguments before provider invocation and preserves caller cancellation. */
  async check(subjectId: string, purpose: string, signal?: AbortSignal): Promise<ConsentState> {
    if (typeof subjectId !== 'string' || subjectId.length === 0 || subjectId.length > 2048 || scalars(subjectId).length > 1024)
      throw new TypeError('Supply a subject of 1 to 1,024 Unicode scalars.');
    const match = typeof purpose === 'string' ? /^[a-z][a-z0-9-]{0,31}(\.[a-z][a-z0-9-]{0,31}){1,4}$/.exec(purpose) : null;
    if (!match || match[0].length !== purpose.length || purpose.length > 164) throw new TypeError('Supply a valid namespaced consent purpose.');
    signal?.throwIfAborted();
    if (!this.provider) return 'unknown';
    try {
      const state: unknown = await this.provider.getState(subjectId, purpose, signal);
      signal?.throwIfAborted();
      return state === 'granted' || state === 'denied' || state === 'unknown' ? state : 'unknown';
    } catch (error: unknown) {
      if (signal?.aborted || error instanceof Error && error.name === 'AbortError') throw error;
      this.diagnostic({ purpose, errorType: error instanceof Error ? error.constructor.name : 'NonErrorFailure' });
      return 'unknown';
    }
  }
  /** Uses the identical check, diagnostics and cancellation semantics. */
  async isGranted(subjectId: string, purpose: string, signal?: AbortSignal): Promise<boolean> {
    return await this.check(subjectId, purpose, signal) === 'granted';
  }
}
