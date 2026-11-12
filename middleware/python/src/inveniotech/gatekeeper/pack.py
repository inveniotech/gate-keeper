"""Byte-exact signed-pack loading and atomic same-identifier replacement."""

import json
from collections.abc import Mapping, Sequence
from copy import deepcopy
from dataclasses import dataclass
from datetime import date, datetime
from hashlib import sha256, sha512
from pathlib import Path
from typing import cast

from .ed25519 import verify
from .runtime import PackLoadError, PackVersion, Rule, RuntimeMatcher


@dataclass(frozen=True)
class SignedPack:
    """Exact installation bytes and optional detached watermark; no signing keys."""

    content: bytes
    manifest: str
    signature: bytes
    watermark: str | None = None
    watermark_signature: bytes | None = None


@dataclass(frozen=True)
class VerifiedPack:
    """Verified identity and detached rule snapshot."""

    identity: PackVersion
    rules: tuple[Rule, ...]


def _require(condition: object) -> None:
    if not condition:
        raise ValueError("Invalid signed pack data.")


def _object(value: object) -> dict[str, object]:
    _require(isinstance(value, dict))
    return cast(dict[str, object], value)


def _string(value: object) -> str:
    _require(isinstance(value, str) and bool(value))
    return cast(str, value)


def _strings(value: object) -> list[str]:
    _require(isinstance(value, list) and bool(value))
    return [_string(item) for item in cast(list[object], value)]


def _pairs(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        _require(key not in result)
        result[key] = value
    return result


def _parse(raw: bytes, limit: int) -> dict[str, object]:
    _require(0 < len(raw) <= limit and not raw.startswith(b"\xef\xbb\xbf"))
    text = raw.decode("utf-8", errors="strict")
    depth = 0
    quoted = False
    escaped = False
    for c in text:
        if quoted:
            if escaped:
                escaped = False
            elif c == "\\":
                escaped = True
            elif c == '"':
                quoted = False
        elif c == '"':
            quoted = True
        elif c in "[{":
            depth += 1
            _require(depth <= 64)
        elif c in "]}":
            depth -= 1
    return _object(
        json.loads(text, object_pairs_hook=_pairs, parse_constant=lambda _: _require(False))
    )


def calendar_version(value: object) -> tuple[int, int, int]:
    """Parse numeric calendar precedence; invalid values raise ValueError."""
    parts = _string(value).split(".")
    _require(
        len(parts) == 3
        and len(parts[0]) == 4
        and 1 <= len(parts[1]) <= 2
        and all(p and all(c in "0123456789" for c in p) and len(p) < 17 for p in parts)
    )
    year, month, patch = (int(p) for p in parts)
    _require(year >= 1 and 1 <= month <= 12 and patch <= 9007199254740991)
    return year, month, patch


def _armour(text: str, watermark: bool) -> tuple[bytes, dict[str, object]]:
    _require(len(text) <= 8192)
    lines = text.splitlines()
    label = "WATERMARK" if watermark else "MANIFEST"
    _require(
        len(lines) == 4
        and lines[0] == f"-----BEGIN GATE-KEEPER RULE PACK {label}-----"
        and lines[3] == f"-----END GATE-KEEPER RULE PACK {label}-----"
    )
    payload = lines[2].encode("utf-8")
    data = _parse(payload, 4096)
    fields = (
        ["keyId", "contentDigest", "organisationName", "licenceId", "generatedAt"]
        if watermark
        else ["keyId", "packId", "version", "contentHashes"]
    )
    _require(list(data) == fields and lines[1] == "Key-Id: " + _string(data["keyId"]))
    _require(json.dumps(data, ensure_ascii=False, separators=(",", ":")) == lines[2])
    for field in fields:
        if field != "contentHashes":
            _string(data[field])
    return payload, data


def _rules(value: object) -> tuple[Rule, ...]:
    _require(isinstance(value, list) and bool(value))
    rules: list[Rule] = []
    for item in cast(list[object], value):
        rule = deepcopy(_object(item))
        _require(
            set(rule)
            <= {
                "id",
                "category",
                "severity",
                "citation",
                "sinceVersion",
                "matcher",
                "redaction",
                "profiles",
                "suppressionHints",
                "processor",
            }
        )
        identifier = _string(rule.get("id"))
        _require(
            not identifier.startswith("local.")
            and all(
                p and "a" <= p[0] <= "z" and all("a" <= c <= "z" or "0" <= c <= "9" for c in p)
                for p in identifier.split(".")
            )
        )
        _require(rule.get("severity") in ("error", "warning", "note"))
        _string(rule.get("category"))
        citation = _object(rule.get("citation"))
        _require(
            "title" in citation
            and set(citation) <= {"title", "ndpa", "gaid"}
            and all(isinstance(v, str) and v for v in citation.values())
        )
        calendar_version(rule.get("sinceVersion"))
        if "redaction" in rule:
            redaction = _object(rule["redaction"])
            _require(
                set(redaction) <= {"optional"}
                and ("optional" not in redaction or type(redaction["optional"]) is bool)
            )
        if "profiles" in rule:
            profiles = _strings(rule["profiles"])
            _require(
                len(profiles) <= 2
                and len(set(profiles)) == len(profiles)
                and all(p in ("default", "deep") for p in profiles)
            )
        if "suppressionHints" in rule:
            _strings(rule["suppressionHints"])
        if "processor" in rule:
            processor = _object(rule["processor"])
            _require(set(processor) == {"dataCategories", "usualRegion"})
            _strings(processor["dataCategories"])
            _string(processor["usualRegion"])
        matcher = _object(rule.get("matcher"))
        _require(len(matcher) == 1 and set(matcher) <= {"pattern", "list"})
        if "pattern" in matcher:
            pattern = _object(matcher["pattern"])
            shape = pattern.get("shape")
            _require(isinstance(shape, list))
            for raw_part in cast(list[object], shape):
                part = _object(raw_part)
                if "ranges" in part:
                    pairs = part["ranges"]
                    _require(isinstance(pairs, list) and bool(pairs))
                    translated: list[dict[str, str]] = []
                    for raw_pair in cast(list[object], pairs):
                        _require(isinstance(raw_pair, list) and len(raw_pair) == 2)
                        pair = cast(list[object], raw_pair)
                        translated.append({"low": _string(pair[0]), "high": _string(pair[1])})
                    part["ranges"] = translated
            if "location" in pattern:
                location = _object(pattern["location"])
                _require(
                    set(location) <= {"kind", "paths"}
                    and location.get("kind")
                    in ("log", "migration", "api-model", "configuration", "fixture", "source")
                )
                if "paths" in location:
                    for path in _strings(location["paths"]):
                        _require(
                            all(
                                c
                                in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._*/-"
                                for c in path
                            )
                            and not path.startswith("/")
                            and all(p and p not in (".", "..") for p in path.split("/"))
                        )
        rules.append(cast(Rule, rule))
    RuntimeMatcher(rules)
    return tuple(rules)


def verify_signed_pack(
    candidate: SignedPack,
    keys: Mapping[str, bytes],
    watermark_keys: Mapping[str, bytes] | None = None,
) -> VerifiedPack:
    """Verify manifest, hashes, compatibility and detached watermark or raise PackLoadError."""
    try:
        payload, manifest = _armour(candidate.manifest, False)
        _require(verify(keys.get(_string(manifest["keyId"]), b""), payload, candidate.signature))
        hashes = _object(manifest["contentHashes"])
        _require(
            list(hashes) == ["sha256", "sha512"]
            and sha256(candidate.content).hexdigest() == hashes["sha256"]
            and sha512(candidate.content).hexdigest() == hashes["sha512"]
        )
        doc = _parse(candidate.content, 16 * 1024 * 1024)
        _require(
            set(doc)
            <= {
                "schemaVersion",
                "id",
                "version",
                "provenance",
                "minEngineVersion",
                "coverageDate",
                "contentIssuedAt",
                "baseVersion",
                "rules",
            }
        )
        _require(
            type(doc.get("schemaVersion")) is int
            and doc.get("schemaVersion") == 1
            and doc.get("provenance") in ("bundled", "plan-gated")
        )
        minimum = _string(doc.get("minEngineVersion")).split(".")
        _require(
            len(minimum) == 3
            and all(
                p.isascii() and p.isdigit() and len(p) < 10 and (p == "0" or not p.startswith("0"))
                for p in minimum
            )
        )
        _require(tuple(int(p) for p in minimum) <= (0, 2, 0))
        version = calendar_version(doc.get("version"))
        coverage = _string(doc.get("coverageDate"))
        issued = _string(doc.get("contentIssuedAt"))
        _require(
            date.fromisoformat(coverage).isoformat() == coverage
            and date.fromisoformat(issued).isoformat() == issued
        )
        if version[2] == 0:
            _require("baseVersion" not in doc and coverage == issued)
        else:
            _require(
                calendar_version(doc.get("baseVersion")) == (*version[:2], 0) and coverage <= issued
            )
        _require(doc.get("id") == manifest["packId"] and doc.get("version") == manifest["version"])
        identity: PackVersion = {
            "id": _string(doc["id"]),
            "version": _string(doc["version"]),
            "contentDigest": _string(hashes["sha256"]),
        }
        rules = _rules(doc.get("rules"))
        if any(r["matcher"].get("pattern", {}).get("boundary") == "email" for r in rules):
            _require(tuple(int(p) for p in minimum) >= (0, 2, 0))
        _require((candidate.watermark is None) == (candidate.watermark_signature is None))
        if candidate.watermark is not None and candidate.watermark_signature is not None:
            mark_payload, mark = _armour(candidate.watermark, True)
            _require(
                verify(
                    (watermark_keys or {}).get(_string(mark["keyId"]), b""),
                    mark_payload,
                    candidate.watermark_signature,
                )
            )
            generated = _string(mark["generatedAt"])
            _require(
                datetime.strptime(generated, "%Y-%m-%dT%H:%M:%SZ").strftime("%Y-%m-%dT%H:%M:%SZ")
                == generated
                and mark["contentDigest"] == identity["contentDigest"]
            )
        return VerifiedPack(identity, rules)
    except (
        ValueError,
        TypeError,
        KeyError,
        AttributeError,
        IndexError,
        OverflowError,
        RecursionError,
        PackLoadError,
    ):
        raise PackLoadError(
            "Signed pack verification failed. Reinstall compatible trusted release media; upgrade the adapter for unsupported schema or engine versions."
        ) from None


class Installation:
    """Verified embedded packs and atomic newer same-ID replacement; no network or licence work."""

    def __init__(
        self,
        embedded: Sequence[SignedPack],
        keys: Mapping[str, bytes],
        watermark_keys: Mapping[str, bytes] | None = None,
    ) -> None:
        if not embedded:
            raise PackLoadError("No embedded packs. Install trusted release media.")
        self._keys = dict(keys)
        self._watermark_keys = dict(watermark_keys or {})
        packs = tuple(verify_signed_pack(p, self._keys, self._watermark_keys) for p in embedded)
        if len({p.identity["id"] for p in packs}) != len(packs):
            raise PackLoadError(
                "Duplicate embedded pack identifiers. Reinstall trusted release media."
            )
        self._runtime = self._build(packs)
        self._packs = packs

    @staticmethod
    def _build(packs: tuple[VerifiedPack, ...]) -> RuntimeMatcher:
        return RuntimeMatcher(
            [r for p in packs for r in p.rules], packs=[p.identity for p in packs]
        )

    @property
    def runtime(self) -> RuntimeMatcher:
        """Return the last completely verified immutable core."""
        return self._runtime

    def replace(self, candidate: SignedPack) -> None:
        """Reject invalid, equal, older and different-ID candidates without altering active rules."""
        verified = verify_signed_pack(candidate, self._keys, self._watermark_keys)
        current = next(
            (p for p in self._packs if p.identity["id"] == verified.identity["id"]), None
        )
        if current is None or calendar_version(verified.identity["version"]) <= calendar_version(
            current.identity["version"]
        ):
            raise PackLoadError(
                "Replacement is not newer for an embedded identifier. Install a newer signed pack with the same identifier."
            )
        packs = tuple(verified if p is current else p for p in self._packs)
        runtime = self._build(packs)
        self._packs, self._runtime = packs, runtime

    def replace_from_path(self, path: Path) -> None:
        """Read explicit content and adjacent manifest/signature files; unreadable media fails atomically."""

        def bounded_read(file: Path, maximum: int) -> bytes:
            with file.open("rb") as stream:
                data = stream.read(maximum + 1)
            _require(len(data) <= maximum)
            return data

        try:
            watermark = path.with_suffix(".watermark.manifest.armour")
            watermark_signature = path.with_suffix(".watermark.signature.hex")
            _require(watermark.exists() == watermark_signature.exists())
            candidate = SignedPack(
                bounded_read(path, 16 * 1024 * 1024),
                bounded_read(path.with_suffix(".manifest.armour"), 8192).decode("utf-8"),
                bytes.fromhex(
                    bounded_read(path.with_suffix(".signature.hex"), 256).decode("ascii")
                ),
                bounded_read(watermark, 8192).decode("utf-8") if watermark.exists() else None,
                bytes.fromhex(bounded_read(watermark_signature, 256).decode("ascii"))
                if watermark_signature.exists()
                else None,
            )
        except (OSError, ValueError):
            raise PackLoadError(
                "Cannot read signed replacement. Supply readable trusted release media."
            ) from None
        self.replace(candidate)
