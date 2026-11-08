// SPDX-License-Identifier: Apache-2.0
import { RuntimeMatcher } from './runtime.ts';

export class ResponseFilterError extends Error {
  readonly abortRequired: boolean;
  constructor(abortRequired: boolean) {
    super('The protected response could not be filtered safely. Do not send its body.');
    this.name = 'ResponseFilterError'; this.abortRequired = abortRequired;
  }
}
interface Segment { readonly key: string; readonly wildcard: boolean }
interface HostState { readonly lengthKnown?: boolean; readonly headersCommitted?: boolean; readonly contentType?: string }
type Container = Record<string, unknown> | unknown[];

/** Atomic filtering of an already parsed, host-bounded JSON tree. */
export class ResponseFilter {
  private readonly runtime: RuntimeMatcher;
  private readonly paths: readonly (readonly Segment[])[];
  constructor(runtime: RuntimeMatcher, paths: readonly string[]) {
    this.runtime = runtime;
    this.paths = paths.map((pointer) => {
      if (typeof pointer !== 'string' || pointer !== '' && !pointer.startsWith('/')) throw new TypeError('Use a JSON Pointer.');
      if (pointer === '') return [];
      return pointer.slice(1).split('/').map((raw) => {
        if (/~(?![012])/.test(raw)) throw new TypeError('Use only ~0, ~1 or ~2 in protected JSON paths.');
        return { key: raw.replace(/~[012]/g, (escape) => escape === '~0' ? '~' : escape === '~1' ? '/' : '*'), wildcard: raw === '*' };
      });
    });
  }
  /** A framework must abort when a failure has abortRequired=true. No body is committed here. */
  transform(body: unknown, state: HostState = {}): unknown {
    const abort = state.headersCommitted ?? false;
    if (this.paths.length === 0) return structuredClone(body);
    const media = (state.contentType ?? 'application/json').split(';')[0]!.trim().toLowerCase();
    const failure = (): never => { throw new ResponseFilterError(abort); };
    if (state.lengthKnown === false || abort || !(media === 'application/json' || media.startsWith('application/') && media.endsWith('+json'))) failure();
    const holder: Record<string, unknown> = { body: structuredClone(body) };
    const selected: { parent: Container; key: string | number }[] = [];
    const get = (parent: Container, key: string | number): unknown => Array.isArray(parent) ? parent[Number(key)] : parent[String(key)];
    const select = (parent: Container, key: string | number, path: readonly Segment[], depth: number): void => {
      if (depth === path.length) { if (!selected.some((slot) => slot.parent === parent && slot.key === key)) selected.push({ parent, key }); return; }
      const node = get(parent, key); const segment = path[depth]!;
      if (segment.wildcard) {
        if (!Array.isArray(node)) return failure();
        for (let at = 0; at < node.length; at++) select(node, at, path, depth + 1);
      } else if (Array.isArray(node)) {
        if (!/^(0|[1-9][0-9]*)$/.test(segment.key) || Number(segment.key) >= node.length) failure();
        select(node, Number(segment.key), path, depth + 1);
      } else if (node !== null && typeof node === 'object') {
        if (Object.hasOwn(node, segment.key)) select(node as Record<string, unknown>, segment.key, path, depth + 1);
      } else failure();
    };
    for (const path of this.paths) select(holder, 'body', path, 0);
    try {
      for (const slot of selected) {
        const value = get(slot.parent, slot.key); if (typeof value !== 'string') return failure();
        const redacted = this.runtime.redact(value);
        if (Array.isArray(slot.parent)) slot.parent[Number(slot.key)] = redacted; else slot.parent[String(slot.key)] = redacted;
      }
    } catch { failure(); }
    return holder.body;
  }
}
