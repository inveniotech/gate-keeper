// SPDX-License-Identifier: Apache-2.0
import { closeSync, existsSync, fstatSync, openSync, readSync } from 'node:fs';
import { join } from 'node:path';
import { calendarVersion, verifySignedPack } from './pack.ts';
import type { KeyCatalogue, SignedPackInput, VerifiedPack } from './pack.ts';
import { PackLoadError, RuntimeMatcher } from './runtime.ts';

/** Installation inputs are explicit release bytes and approved keys, never scanner configuration or a licence. */
export interface InstallationOptions {
  readonly embedded: readonly SignedPackInput[];
  readonly keys: KeyCatalogue;
  readonly disabled?: readonly string[];
  readonly diagnostic?: (message: string) => void;
  readonly replacementDirectory?: string;
}
function readBounded(path: string, cap: number): Uint8Array {
  const file = openSync(path, 'r');
  try {
    const stat = fstatSync(file);
    if (!stat.isFile() || stat.size > cap) throw new PackLoadError('The replacement file exceeds its bound. Reinstall the signed replacement.');
    const bytes = Buffer.alloc(cap + 1); let used = 0;
    while (used <= cap) {
      const count = readSync(file, bytes, used, bytes.length - used, null);
      if (count === 0) break; used += count;
    }
    if (used > cap) throw new PackLoadError('The replacement file exceeds its bound. Reinstall the signed replacement.');
    return bytes.subarray(0, used);
  } finally { closeSync(file); }
}
function readReplacement(directory: string): SignedPackInput {
  const read = (name: string, cap: number) => readBounded(join(directory, name), cap);
  const watermark = existsSync(join(directory, 'watermark.armour'));
  const signature = existsSync(join(directory, 'watermark.signature'));
  if (watermark !== signature) throw new PackLoadError('The replacement watermark is incomplete. Supply both watermark files or neither.');
  return {
    name: directory, contentBytes: read('pack.json', 16 * 1024 * 1024),
    manifestArmour: new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(read('manifest.armour', 8192)),
    manifestSignature: read('manifest.signature', 64),
    ...(watermark ? { watermarkArmour: new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(read('watermark.armour', 8192)), watermarkSignature: read('watermark.signature', 64) } : {}),
  };
}

/** Loads atomically or refuses start-up. Invalid replacement bytes retain verified embedded packs; incompatible versions refuse. */
export function loadInstallation(options: InstallationOptions): RuntimeMatcher {
  if (options.embedded.length === 0) throw new PackLoadError('No embedded signed pack was supplied. Install trusted release pack bytes before starting Gate-Keeper.');
  const active = options.embedded.map((input) => verifySignedPack(input, options.keys));
  if (new Set(active.map((candidate) => candidate.pack.id)).size !== active.length)
    throw new PackLoadError('Embedded packs repeat a pack identifier. Supply one trusted release per identifier.');
  if (options.replacementDirectory !== undefined) {
    let candidate: VerifiedPack | undefined;
    try { candidate = verifySignedPack(readReplacement(options.replacementDirectory), options.keys); }
    catch (error: unknown) {
      if (error instanceof PackLoadError && error.incompatible) throw error;
      options.diagnostic?.('The signed replacement was rejected. Retaining the verified embedded packs; reinstall the replacement from trusted release media.');
    }
    if (candidate) {
      const at = active.findIndex((pack) => pack.pack.id === candidate.pack.id);
      if (at < 0) throw new PackLoadError('The signed replacement has no matching embedded pack identifier. Supply a replacement for an embedded pack.');
      const left = calendarVersion(candidate.pack.version); const right = calendarVersion(active[at]!.pack.version);
      const difference = left.map((value, index) => value - right[index]!).find((value) => value !== 0) ?? 0;
      if (difference <= 0) throw new PackLoadError(`The signed replacement for '${candidate.pack.id}' is not newer. Supply a strictly newer calendar version.`);
      active[at] = candidate;
    }
  }
  return new RuntimeMatcher(active.flatMap((candidate) => candidate.rules), options.disabled, options.diagnostic, active.map((candidate) => candidate.pack));
}
