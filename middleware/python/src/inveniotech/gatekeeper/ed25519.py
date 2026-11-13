"""Dependency-free pure Ed25519 verification using RFC 8032 section 5.1.

Only public-data verification is implemented. This module must never be used
for signing: Python integer arithmetic is not constant time.
"""

from hashlib import sha512

_P = 2**255 - 19
_L = 2**252 + 27742317777372353535851937790883648493
_D = -121665 * pow(121666, _P - 2, _P) % _P
_I = pow(2, (_P - 1) // 4, _P)
Point = tuple[int, int, int, int]
_IDENTITY: Point = (0, 1, 1, 0)


def _add(a: Point, b: Point) -> Point:
    x, y, z, t = a
    u, v, w, s = b
    aa = (y - x) * (v - u) % _P
    bb = (y + x) * (v + u) % _P
    cc = 2 * _D * t * s % _P
    dd = 2 * z * w % _P
    e, f, g, h = bb - aa, dd - cc, dd + cc, bb + aa
    return e * f % _P, g * h % _P, f * g % _P, e * h % _P


def _multiply(point: Point, scalar: int) -> Point:
    result = _IDENTITY
    while scalar:
        if scalar & 1:
            result = _add(result, point)
        point = _add(point, point)
        scalar >>= 1
    return result


def _equal(a: Point, b: Point) -> bool:
    return (a[0] * b[2] - b[0] * a[2]) % _P == 0 and (a[1] * b[2] - b[1] * a[2]) % _P == 0


def _decode(encoded: bytes) -> Point | None:
    if len(encoded) != 32:
        return None
    value = int.from_bytes(encoded, "little")
    sign, y = value >> 255, value & (2**255 - 1)
    if y >= _P:
        return None
    y2 = y * y % _P
    x2 = (y2 - 1) * pow(_D * y2 + 1, _P - 2, _P) % _P
    x = pow(x2, (_P + 3) // 8, _P)
    if x * x % _P != x2:
        x = x * _I % _P
    if x * x % _P != x2 or x == 0 and sign:
        return None
    if x & 1 != sign:
        x = _P - x
    return x, y, 1, x * y % _P


def verify(public_key: bytes, message: bytes, signature: bytes) -> bool:
    """Verify canonical pure Ed25519 bytes; malformed or small-order inputs fail."""
    if len(public_key) != 32 or len(signature) != 64:
        return False
    public = _decode(public_key)
    r = _decode(signature[:32])
    s = int.from_bytes(signature[32:], "little")
    base = _decode(
        bytes.fromhex("5866666666666666666666666666666666666666666666666666666666666666")
    )
    if public is None or r is None or base is None or s >= _L:
        return False
    if _equal(_multiply(public, 8), _IDENTITY) or _equal(_multiply(r, 8), _IDENTITY):
        return False
    challenge = (
        int.from_bytes(sha512(signature[:32] + public_key + message).digest(), "little") % _L
    )
    return _equal(_multiply(base, s), _add(r, _multiply(public, challenge)))
