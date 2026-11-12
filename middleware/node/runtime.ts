// SPDX-License-Identifier: Apache-2.0
/** Native runtime matching over already validated and verified rule data. No I/O. */
import { adapterVersion } from './version.ts';
export interface ShapePart {
  readonly min: number; readonly max: number; readonly emit?: boolean;
  readonly literal?: string; readonly class?: 'digit' | 'alnum';
  readonly ranges?: readonly { readonly low: string; readonly high: string }[];
  readonly oneOf?: readonly string[];
}
export interface Validator { readonly name: string; readonly length?: number; readonly prefixes?: readonly string[] }
export interface PatternRule {
  readonly shape: readonly ShapePart[]; readonly boundary: 'none' | 'digit' | 'alnum' | 'email';
  readonly validator?: Validator; readonly keywords?: readonly string[]; readonly window?: number;
  readonly location?: { readonly kind: string; readonly paths?: readonly string[] };
  readonly exclusions?: readonly string[]; readonly excludePrefixes?: readonly string[]; readonly excludeValues?: readonly string[];
}
export interface Rule {
  readonly id: string; readonly category: string; readonly severity: 'error' | 'warning' | 'note';
  readonly redaction?: { readonly optional: boolean };
  readonly matcher: { readonly pattern?: PatternRule; readonly list?: { readonly package?: readonly string[]; readonly hostname?: readonly string[] } };
}
export interface Finding { readonly ruleId: string; readonly category: string; readonly severity: string; readonly start: number; readonly length: number }
export interface Context { readonly surface: 'log' | 'response' | 'value'; readonly fieldName?: string }
export interface PackVersion { readonly id: string; readonly version: string; readonly contentDigest: string }
export class InputLimitError extends Error { constructor() { super('Middleware input or result exceeds 65,536 Unicode scalars. Split the value.'); this.name = 'InputLimitError'; } }
/** Pack verification failure. Incompatible replacements require an adapter upgrade rather than fallback. */
export class PackLoadError extends Error {
  readonly incompatible: boolean;
  constructor(message = 'Middleware rule data is invalid. Load a verified compatible signed pack.', incompatible = false) {
    super(message); this.name = 'PackLoadError'; this.incompatible = incompatible;
  }
}

const fold = (value: string): string => value.replace(/[A-Z]/g, (c) => c.toLowerCase());
const printable = (value: string): boolean => value.length > 0 && !/[^\x20-\x7e]/.test(value);
const ordinal = (a: string, b: string): number => a < b ? -1 : a > b ? 1 : 0;
const digit = (value: string | undefined): boolean => value !== undefined && /^[0-9]$/.test(value);
const alnum = (value: string | undefined): boolean => value !== undefined && /^[0-9A-Za-z]$/.test(value);
const token = (value: string | undefined): boolean => value !== undefined && /^[0-9A-Za-z._+\-]$/.test(value);
// The email boundary is asymmetric (IQ-91): a leading '.' blocks a match, but a trailing '.' does not.
const emailLeading = (value: string | undefined): boolean => value !== undefined && /^[0-9A-Za-z._%+\-]$/.test(value);
const emailTrailing = (value: string | undefined): boolean => value !== undefined && /^[0-9A-Za-z_%+\-]$/.test(value);
const mask = /\[REDACTED:[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*)*\]/g;

/** Validates Unicode and the rule-pack section 7.5 scalar cap. */
export function scalars(text: string): string[] {
  if (typeof text !== 'string') throw new TypeError('Middleware input must be a Unicode string.');
  const result: string[] = [];
  for (const c of text) {
    if (c.length === 1 && c.charCodeAt(0) >= 0xd800 && c.charCodeAt(0) <= 0xdfff)
      throw new TypeError('Middleware input must contain valid Unicode scalars.');
    result.push(c); if (result.length > 65_536) throw new InputLimitError();
  }
  return result;
}

/** The closed validator registry, implemented natively from rule-pack section 8.1. */
export function validate(value: string, parameters: Validator): boolean {
  const digits = [...value].filter((c) => digit(c)).map(Number);
  switch (parameters.name) {
    case 'digits-length': return digits.length === parameters.length;
    case 'ng-phone-prefix': return (parameters.prefixes ?? []).some((prefix) => value.startsWith(prefix));
    case 'nuban': {
      const weights = [3, 7, 3, 3, 7, 3, 3, 7, 3];
      return digits.length === 10 && (10 - digits.slice(0, 9).reduce((sum, n, i) => sum + n * weights[i]!, 0) % 10) % 10 === digits[9];
    }
    case 'email-not-example-domain': {
      const at = value.lastIndexOf('@');
      if (at < 1 || at === value.length - 1) return false;
      const domain = fold(value.slice(at + 1));
      return !['example.com', 'example.net', 'example.org', 'test', 'invalid', 'localhost']
        .some((reserved) => domain === reserved || (domain.length > reserved.length + 1 && domain.endsWith('.' + reserved)));
    }
    default: throw new PackLoadError();
  }
}

interface ShapeMatch { readonly end: number; readonly emitStart?: number; readonly emitLength?: number }
interface PreparedPart { readonly source: ShapePart; readonly alternatives: readonly (readonly string[])[] }

function prepare(parts: readonly ShapePart[]): PreparedPart[] {
  return parts.map((source) => ({ source, alternatives: [...(source.oneOf ?? (source.literal !== undefined ? [source.literal] : []))]
    .sort((a, b) => [...b].length - [...a].length || ordinal(a, b)).map((word) => [...fold(word)]) }));
}

// One memo per rule/segment shares suffix work across every candidate start.
function shapeMatcher(parts: readonly PreparedPart[], text: readonly string[]): (start: number) => ShapeMatch | null {
  const memo = new Map<number, ShapeMatch | null>();
  const folded = text.map(fold);
  const accepts = (part: ShapePart, c: string | undefined): boolean => part.class === 'digit' ? digit(c) : part.class === 'alnum' ? alnum(c)
    : c !== undefined && (part.ranges ?? []).some((r) => c >= r.low && c <= r.high);
  const runs = parts.map(({ source, alternatives }) => {
    if (alternatives.length) return null;
    const run = new Uint16Array(text.length + 1);
    for (let at = text.length - 1; at >= 0; at--) if (accepts(source, text[at])) run[at] = Math.min(source.max, run[at + 1]! + 1);
    return run;
  });
  function atom(prepared: PreparedPart, at: number): number[] {
    if (prepared.alternatives.length) return prepared.alternatives
      .filter((word) => at + word.length <= text.length && word.every((c, i) => folded[at + i] === c)).map((word) => at + word.length);
    const c = text[at];
    return accepts(prepared.source, c) ? [at + 1] : [];
  }
  function walk(index: number, at: number): ShapeMatch | null {
    if (index === parts.length) return { end: at };
    const key = index * (text.length + 1) + at;
    if (memo.has(key)) return memo.get(key)!;
    const prepared = parts[index]!; const part = prepared.source;
    const run = runs[index];
    if (run) {
      for (let count = run[at]!; count >= part.min; count--) {
        const tail = walk(index + 1, at + count);
        if (tail) {
          const result = part.emit ? { end: tail.end, emitStart: at, emitLength: count } : tail;
          memo.set(key, result); return result;
        }
      }
      memo.set(key, null); return null;
    }
    const levels: number[][] = [[at]];
    for (let count = 1; count <= part.max; count++) {
      const next = [...new Set(levels[count - 1]!.flatMap((position) => atom(prepared, position)))].sort((a, b) => b - a);
      levels.push(next); if (next.length === 0) break;
    }
    for (let count = Math.min(part.max, levels.length - 1); count >= part.min; count--) {
      for (const end of levels[count]!) {
        const tail = walk(index + 1, end);
        if (tail) {
          const result = part.emit ? { end: tail.end, emitStart: at, emitLength: end - at } : tail;
          memo.set(key, result); return result;
        }
      }
    }
    memo.set(key, null); return null;
  }
  return (start) => walk(0, start);
}

function keywordIndex(text: readonly string[], pattern: PatternRule): (start: number, end: number) => boolean {
  if (!pattern.keywords?.length) return () => true;
  const folded = text.map(fold); const hits: { start: number; end: number }[] = [];
  for (const keyword of pattern.keywords) {
    const word = [...fold(keyword)];
    for (let at = 0; at + word.length <= text.length; at++)
      if (word.every((c, i) => folded[at + i] === c)) hits.push({ start: at, end: at + word.length });
  }
  hits.sort((a, b) => a.start - b.start); let furthest = 0;
  for (const hit of hits) { furthest = Math.max(furthest, hit.end); hit.end = furthest; }
  return (start, end) => {
    let low = 0; let high = hits.length;
    while (low < high) { const mid = (low + high) >>> 1; if (hits[mid]!.start <= end + pattern.window!) low = mid + 1; else high = mid; }
    return low > 0 && hits[low - 1]!.end >= start - pattern.window!;
  };
}

function excluded(text: readonly string[], start: number, length: number, pattern: PatternRule): boolean {
  const value = text.slice(start, start + length).join('');
  if (pattern.excludeValues?.includes(value) || pattern.excludePrefixes?.some((prefix) => value.startsWith(prefix))) return true;
  const date = (year: number, month: number, day: number): boolean => year >= 1900 && year <= 2100 && day >= 1
    && day <= ([31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1] ?? 0);
  for (const name of pattern.exclusions ?? []) {
    if (name === 'repeated-digits' && /^([0-9])\1+$/.test(value)) return true;
    if (name === 'sequential-digits' && /^[0-9]{2,}$/.test(value)) {
      const steps = [...value].slice(1).map((c, i) => (Number(c) - Number(value[i]) + 10) % 10);
      if (steps.every((n) => n === 1) || steps.every((n) => n === 9)) return true;
    }
    if (name === 'hex-fragment' && /^[0-9A-Fa-f]{2,}$/.test(value) && /[A-Fa-f]/.test(value)) return true;
    if (name === 'version-string') {
      let left = start; let right = start + length;
      while (left > 0 && /^[0-9.]$/.test(text[left - 1]!)) left--;
      if (left > 0 && /^[vV]$/.test(text[left - 1]!) && (left === 1 || !alnum(text[left - 2]))) left--;
      while (right < text.length && /^[0-9.]$/.test(text[right]!)) right++;
      if (/^[vV]?[0-9]+(?:\.[0-9]+)+$/.test(text.slice(left, right).join(''))) return true;
    }
    if (name === 'uuid-fragment' || name === 'date-or-timestamp') {
      if (name === 'date-or-timestamp' && /^[0-9]{8}$/.test(value) && date(+value.slice(0, 4), +value.slice(4, 6), +value.slice(6, 8))) return true;
      const reach = name === 'uuid-fragment' ? 36 : 32;
      for (let at = Math.max(0, start - reach); at < Math.min(text.length, start + length + reach); at++) {
        const tail = text.slice(at, at + 36).join(''); let width = 0;
        if (name === 'uuid-fragment' && /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/.test(tail)) width = 36;
        if (name === 'date-or-timestamp') {
          const ymd = /^([0-9]{4})([-/])([0-9]{2})\2([0-9]{2})/.exec(tail);
          const dmy = /^([0-9]{2})-([0-9]{2})-([0-9]{4})/.exec(tail);
          if (ymd && date(+ymd[1]!, +ymd[3]!, +ymd[4]!)) width = ymd[2] === '-' && /^.{10}T[0-9]{2}:[0-9]{2}:[0-9]{2}/.test(tail) ? 19 : 10;
          else if (dmy && date(+dmy[3]!, +dmy[2]!, +dmy[1]!)) width = 10;
        }
        if (width > 0 && start < at + width && at < start + length) return true;
      }
    }
  }
  return false;
}

/** A reusable immutable rule core. The caller supplies verified load data, never scan policy. */
export class RuntimeMatcher {
  private readonly rules: readonly Rule[];
  private readonly packs: readonly PackVersion[];
  private readonly shapes: ReadonlyMap<Rule, readonly PreparedPart[]>;
  constructor(rules: readonly Rule[], disabled: readonly string[] = [], diagnostic?: (message: string) => void, packs: readonly PackVersion[] = []) {
    if (rules.length === 0 || new Set(rules.map((rule) => rule.id)).size !== rules.length) throw new PackLoadError();
    for (const id of disabled) if (!rules.some((rule) => rule.id === id && rule.redaction?.optional))
      throw new TypeError('Only a loaded optional rule may be disabled.');
    if (disabled.length && !diagnostic) throw new TypeError('Optional disablement needs a start-up diagnostic.');
    if (disabled.length) diagnostic!('Disabled optional redaction rules: ' + [...new Set(disabled)].sort().join(', '));
    this.rules = structuredClone(rules.filter((rule) => !disabled.includes(rule.id)));
    this.packs = packs.map(({ id, version, contentDigest }) => Object.freeze({ id, version, contentDigest })).sort((a, b) => ordinal(a.id, b.id));
    for (const rule of this.rules) {
      const p = rule.matcher.pattern;
      if (Boolean(p) === Boolean(rule.matcher.list)) throw new PackLoadError();
      if (p && (!['none', 'digit', 'alnum', 'email'].includes(p.boundary)
        || p.validator && !['digits-length', 'nuban', 'ng-phone-prefix', 'email-not-example-domain'].includes(p.validator.name)
        || p.keywords?.length && (!Number.isInteger(p.window) || p.window! < 1 || p.window! > 4096))) throw new PackLoadError();
      if (p && (p.shape.length < 1 || p.shape.length > 32 || p.shape.filter((part) => part.emit).length > 1
        || p.shape.some((part) => !Number.isInteger(part.min) || !Number.isInteger(part.max)
          || part.min < 0 || part.min > part.max || part.max > 64 || part.max < 1 || part.oneOf && (part.max !== 1 || part.oneOf.length === 0)
          || part.class !== undefined && !['digit', 'alnum'].includes(part.class)
          || part.literal !== undefined && !printable(part.literal)
          || part.oneOf?.some((word) => !printable(word))
          || part.ranges !== undefined && (part.ranges.length === 0 || part.ranges.some((r) => r.low.length !== 1 || r.high.length !== 1 || !printable(r.low) || !printable(r.high) || r.low > r.high))
          || [part.literal, part.class, part.ranges, part.oneOf].filter((atom) => atom !== undefined).length !== 1)
        || p.shape.reduce((sum, part) => sum + part.max * (part.literal ? [...part.literal].length : part.oneOf ? Math.max(...part.oneOf.map((word) => [...word].length)) : 1), 0) > 256))
        throw new PackLoadError();
    }
    this.shapes = new Map(this.rules.filter((rule) => rule.matcher.pattern).map((rule) => [rule, prepare(rule.matcher.pattern!.shape)]));
  }

  /** Returns exact loaded identities; application input never enters version reporting. */
  versions(): Readonly<{ adapterVersion: string; packs: readonly PackVersion[] }> {
    return Object.freeze({ adapterVersion, packs: Object.freeze([...this.packs]) });
  }

  /** Returns every rule's own finding; no repository path or scan policy is accepted. */
  matches(text: string): Finding[] {
    const all = scalars(text); const result: Finding[] = [];
    const ranges: [number, number][] = []; let cursor = 0; let utf16Cursor = 0;
    // Regex indices are UTF-16; translate only this fixed reserved-mask grammar.
    for (const found of text.matchAll(mask)) {
      let start = cursor;
      for (let at = utf16Cursor; at < found.index; at++, start++) if (text.codePointAt(at)! > 0xffff) at++;
      ranges.push([cursor, start]); cursor = start + found[0].length; utf16Cursor = found.index + found[0].length;
    }
    ranges.push([cursor, all.length]);
    for (const [begin, end] of ranges) {
      const chars = all.slice(begin, end); const segmentText = chars.join(''); const foldedText = fold(segmentText);
      const scalarOffsets = new Uint32Array(segmentText.length + 1); let utf16 = 0;
      for (let at = 0; at < chars.length; at++) { scalarOffsets[utf16] = at; utf16 += chars[at]!.length; scalarOffsets[utf16] = at + 1; }
      for (const rule of this.rules) {
        const add = (start: number, length: number): void => { result.push({ ruleId: rule.id, category: rule.category, severity: rule.severity, start: begin + start, length }); };
        const p = rule.matcher.pattern;
        if (!p) {
          for (const word of [...(rule.matcher.list!.package ?? []), ...(rule.matcher.list!.hostname ?? [])]) {
            for (let at = segmentText.indexOf(word); at >= 0; at = segmentText.indexOf(word, at + 1))
              if (!token(segmentText[at - 1]) && !token(segmentText[at + word.length])) add(scalarOffsets[at]!, scalarOffsets[at + word.length]! - scalarOffsets[at]!);
          }
          continue;
        }
        if (p.keywords?.length && !p.keywords.some((keyword) => foldedText.includes(fold(keyword)))) continue;
        if (p.shape.some((part) => part.min > 0 && (part.literal !== undefined || part.oneOf !== undefined)
          && !(part.oneOf ?? [part.literal!]).some((word) => foldedText.includes(fold(word))))) continue;
        const shapeAt = shapeMatcher(this.shapes.get(rule)!, chars);
        const keywordNear = keywordIndex(chars, p);
        const leading = p.boundary === 'digit' ? digit : p.boundary === 'alnum' ? alnum : p.boundary === 'email' ? emailLeading : () => false;
        const trailing = p.boundary === 'email' ? emailTrailing : leading;
        let at = 0;
        while (at < chars.length) {
          // A mandatory leading scalar atom cannot begin here; avoid memo entries for impossible starts.
          const first = p.shape[0]!;
          if (first.min > 0 && (first.class === 'digit' ? !digit(chars[at]) : first.class === 'alnum' ? !alnum(chars[at]) : false)) { at++; continue; }
          if (leading(chars[at - 1])) { at++; continue; }
          const found = shapeAt(at);
          if (!found || found.end <= at) { at++; continue; }
          const start = found.emitStart ?? at; const length = found.emitLength ?? found.end - at;
          const value = chars.slice(start, start + length).join('');
          if (length > 0 && !leading(chars[at - 1]) && !trailing(chars[found.end])
            && keywordNear(at, found.end) && (!p.validator || validate(value, p.validator)) && !excluded(chars, start, length, p)) {
            add(start, length); at = found.end;
          } else at++;
        }
      }
    }
    return result.sort((a, b) => a.start - b.start || ordinal(a.ruleId, b.ruleId) || a.length - b.length);
  }

  /** Chooses earliest, longest, then ordinal rule; context cannot filter rules. */
  classify(text: string, context: Context): Finding | null {
    if (!context || !['log', 'response', 'value'].includes(context.surface) || context.fieldName === '')
      throw new TypeError('Use a valid surface and non-empty optional field name.');
    return this.matches(text).sort((a, b) => a.start - b.start || b.length - a.length || ordinal(a.ruleId, b.ruleId))[0] ?? null;
  }

  /** Replaces only emitted spans and reserves literal masks before matching. */
  redact(text: string): string {
    const chars = scalars(text); const output: string[] = []; let cursor = 0; let size = 0;
    for (const finding of this.matches(text).sort((a, b) => a.start - b.start || b.length - a.length || ordinal(a.ruleId, b.ruleId))) {
      if (finding.start < cursor) continue;
      size += finding.start - cursor + 11 + finding.ruleId.length;
      if (size > 65_536) throw new InputLimitError();
      output.push(chars.slice(cursor, finding.start).join(''), `[REDACTED:${finding.ruleId}]`);
      cursor = finding.start + finding.length;
    }
    if (size + chars.length - cursor > 65_536) throw new InputLimitError();
    output.push(chars.slice(cursor).join('')); const result = output.join(''); scalars(result); return result;
  }
}
