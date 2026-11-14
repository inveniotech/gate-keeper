// Build-only declarations for the Node built-ins used here. Consumers see only standard JavaScript types.
// Keep this narrow surface alongside the pinned compiler; no runtime or additional type dependency is shipped.
declare const Buffer: {
  from(value: Uint8Array | string, encoding?: string): Uint8Array & { toString(encoding?: string): string };
  alloc(size: number): Uint8Array;
  byteLength(value: string): number;
};
declare module 'node:crypto' {
  export function createPublicKey(options: { key: { kty: string; crv: string; x: string }; format: 'jwk' }): object;
  export function verify(algorithm: null, message: Uint8Array, key: object, signature: Uint8Array): boolean;
  export function createHash(algorithm: string): { update(bytes: Uint8Array): { digest(encoding: 'hex'): string } };
}
declare module 'node:fs' {
  export function openSync(path: string, flags: string): number;
  export function closeSync(file: number): void;
  export function existsSync(path: string): boolean;
  export function fstatSync(file: number): { size: number; isFile(): boolean };
  export function readSync(file: number, buffer: Uint8Array, offset: number, length: number, position: null): number;
}
declare module 'node:path' { export function join(...paths: string[]): string; }
