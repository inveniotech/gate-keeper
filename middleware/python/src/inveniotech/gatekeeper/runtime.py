"""Bounded matching over already validated, verified rule models, without I/O."""

from bisect import bisect_right
from collections.abc import Callable, Sequence
from copy import deepcopy
from typing import Literal, NotRequired, TypedDict

from .version import ADAPTER_VERSION


class Range(TypedDict):
    """Inclusive ASCII scalar interval."""

    low: str
    high: str


ShapePart = TypedDict(
    "ShapePart",
    {
        "min": int,
        "max": int,
        "emit": NotRequired[bool],
        "literal": NotRequired[str],
        "class": NotRequired[str],
        "ranges": NotRequired[list[Range]],
        "oneOf": NotRequired[list[str]],
    },
)


class Validator(TypedDict):
    """Parameters of a closed native validator."""

    name: str
    length: NotRequired[int]
    prefixes: NotRequired[list[str]]


class Location(TypedDict):
    """Scanner-only location labels; never filters runtime input."""

    kind: str
    paths: NotRequired[list[str]]


class Pattern(TypedDict):
    """Validated bounded shape and acceptance conditions."""

    shape: list[ShapePart]
    boundary: str
    validator: NotRequired[Validator]
    keywords: NotRequired[list[str]]
    window: NotRequired[int]
    location: NotRequired[Location]
    exclusions: NotRequired[list[str]]
    excludePrefixes: NotRequired[list[str]]
    excludeValues: NotRequired[list[str]]


class Matcher(TypedDict):
    """Exactly one pattern or token list."""

    pattern: NotRequired[Pattern]
    list: NotRequired[dict[str, list[str]]]


class Rule(TypedDict):
    """Verified rule data consumed by the native core."""

    id: str
    category: str
    severity: str
    matcher: Matcher
    redaction: NotRequired[dict[str, bool]]


class Finding(TypedDict):
    """Metadata-only finding with zero-based Unicode scalar offsets."""

    ruleId: str
    category: str
    severity: str
    start: int
    length: int


class Context(TypedDict):
    """Classification label which cannot select rules."""

    surface: Literal["log", "response", "value"]
    fieldName: NotRequired[str]


class PackVersion(TypedDict):
    """Exact verified pack identity."""

    id: str
    version: str
    contentDigest: str


class VersionInfo(TypedDict):
    """Adapter version and ordinally sorted pack identities."""

    adapterVersion: str
    packs: list[PackVersion]


class InputLimitError(Exception):
    """Input or expanded result exceeds the fixed scalar cap."""


class PackLoadError(Exception):
    """Signed pack could not be verified or is incompatible."""


def scalars(text: str) -> str:
    """Validate native Unicode before indexing; raise ValueError or InputLimitError."""
    if not isinstance(text, str) or any(0xD800 <= ord(c) <= 0xDFFF for c in text):
        raise ValueError("Supply valid Unicode text without unpaired surrogates.")
    if len(text) > 65536:
        raise InputLimitError(
            "Middleware input or result exceeds 65,536 Unicode scalars. Split the value."
        )
    return text


def fold(text: str) -> str:
    """Apply only the rule-pack ASCII fold, independent of locale."""
    return text.translate(str.maketrans("ABCDEFGHIJKLMNOPQRSTUVWXYZ", "abcdefghijklmnopqrstuvwxyz"))


def digit(c: str) -> bool:
    return len(c) == 1 and "0" <= c <= "9"


def alnum(c: str) -> bool:
    return digit(c) or len(c) == 1 and ("A" <= c <= "Z" or "a" <= c <= "z")


def validate(value: str, parameters: Validator) -> bool:
    """Apply a named validator; unknown names raise PackLoadError."""
    digits = [int(c) for c in value if digit(c)]
    name = parameters["name"]
    if name == "digits-length":
        return len(digits) == parameters.get("length")
    if name == "ng-phone-prefix":
        return any(value.startswith(p) for p in parameters.get("prefixes", []))
    if name == "nuban":
        return (
            len(digits) == 10
            and (
                10
                - sum(n * w for n, w in zip(digits[:9], (3, 7, 3, 3, 7, 3, 3, 7, 3), strict=True))
                % 10
            )
            % 10
            == digits[9]
        )
    if name == "email-not-example-domain":
        at = value.rfind("@")
        if at < 1 or at == len(value) - 1:
            return False
        domain = fold(value[at + 1 :])
        return not any(
            domain == r or domain.endswith("." + r)
            for r in ("example.com", "example.net", "example.org", "test", "invalid", "localhost")
        )
    raise PackLoadError("Unknown validator. Load a compatible verified rule pack.")


def accepts(part: ShapePart, c: str) -> bool:
    kind = part.get("class")
    return (
        digit(c)
        if kind == "digit"
        else alnum(c)
        if kind == "alnum"
        else any(r["low"] <= c <= r["high"] for r in part.get("ranges", []))
    )


def shape_matcher(
    parts: list[ShapePart], text: str
) -> Callable[[int], tuple[int, int | None, int | None] | None]:
    folded = fold(text)
    alternatives = [
        sorted(p.get("oneOf", [p["literal"]] if "literal" in p else []), key=lambda w: (-len(w), w))
        for p in parts
    ]
    runs: list[list[int] | None] = []
    for part, words in zip(parts, alternatives, strict=True):
        run = [0] * (len(text) + 1)
        for at in range(len(text) - 1, -1, -1):
            if accepts(part, text[at]):
                run[at] = min(part["max"], run[at + 1] + 1)
        runs.append(None if words else run)
    memo: dict[tuple[int, int], tuple[int, int | None, int | None] | None] = {}

    def walk(index: int, at: int) -> tuple[int, int | None, int | None] | None:
        if index == len(parts):
            return at, None, None
        key = index, at
        if key in memo:
            return memo[key]
        part = parts[index]
        run = runs[index]
        levels: list[list[int]] = [[at]]
        if run is not None:
            levels.extend([[at + n] for n in range(1, run[at] + 1)])
        else:
            for _ in range(part["max"]):
                ends = {
                    pos + len(word)
                    for pos in levels[-1]
                    for word in alternatives[index]
                    if folded.startswith(fold(word), pos)
                }
                levels.append(sorted(ends, reverse=True))
                if not ends:
                    break
        for count in range(min(part["max"], len(levels) - 1), part["min"] - 1, -1):
            for end in levels[count]:
                tail = walk(index + 1, end)
                if tail is not None:
                    result = (tail[0], at, end - at) if part.get("emit", False) else tail
                    memo[key] = result
                    return result
        memo[key] = None
        return None

    return lambda at: walk(0, at)


def date_shape(text: str) -> bool:
    if len(text) != 8 or not all(digit(c) for c in text):
        return False
    year, month, day = int(text[:4]), int(text[4:6]), int(text[6:])
    return (
        1900 <= year <= 2100
        and 1 <= month <= 12
        and 1 <= day <= (31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31)[month - 1]
    )


def excluded(text: str, start: int, length: int, pattern: Pattern) -> bool:
    value = text[start : start + length]
    if value in pattern.get("excludeValues", []) or any(
        value.startswith(p) for p in pattern.get("excludePrefixes", [])
    ):
        return True
    for name in pattern.get("exclusions", []):
        if (
            name == "repeated-digits"
            and len(value) > 1
            and digit(value[0])
            and len(set(value)) == 1
        ):
            return True
        if name == "sequential-digits" and len(value) > 1 and all(digit(c) for c in value):
            steps = {(int(b) - int(a)) % 10 for a, b in zip(value, value[1:], strict=False)}
            if steps in ({1}, {9}):
                return True
        if (
            name == "hex-fragment"
            and len(value) > 1
            and all(c in "0123456789abcdefABCDEF" for c in value)
            and any(c in "abcdefABCDEF" for c in value)
        ):
            return True
        if name == "version-string":
            left, right = start, start + length
            while left > 0 and text[left - 1] in "0123456789.":
                left -= 1
            if left > 0 and text[left - 1] in "vV" and (left == 1 or not alnum(text[left - 2])):
                left -= 1
            while right < len(text) and text[right] in "0123456789.":
                right += 1
            candidate = text[left:right].lstrip("vV").split(".")
            if len(candidate) > 1 and all(p and all(digit(c) for c in p) for p in candidate):
                return True
        if name in ("uuid-fragment", "date-or-timestamp"):
            if name == "date-or-timestamp" and date_shape(value):
                return True
            reach = 36 if name == "uuid-fragment" else 32
            for at in range(max(0, start - reach), min(len(text), start + length + reach)):
                tail = text[at : at + 36]
                width = 0
                if (
                    name == "uuid-fragment"
                    and len(tail) >= 36
                    and all(
                        tail[i] == "-"
                        if i in (8, 13, 18, 23)
                        else tail[i] in "0123456789abcdefABCDEF"
                        for i in range(36)
                    )
                ):
                    width = 36
                if name == "date-or-timestamp" and len(tail) >= 10:
                    if (
                        tail[4] in "-/"
                        and tail[7] == tail[4]
                        and date_shape(tail[:4] + tail[5:7] + tail[8:10])
                    ):
                        width = 10
                        if (
                            tail[4] == "-"
                            and len(tail) >= 19
                            and tail[10] == "T"
                            and tail[13] == tail[16] == ":"
                            and all(digit(tail[i]) for i in (11, 12, 14, 15, 17, 18))
                        ):
                            width = 19
                    elif tail[2] == tail[5] == "-" and date_shape(
                        tail[6:10] + tail[3:5] + tail[:2]
                    ):
                        width = 10
                if width and start < at + width and at < start + length:
                    return True
    return False


def segments(text: str) -> list[tuple[int, int]]:
    result: list[tuple[int, int]] = []
    cursor = 0
    at = text.find("[REDACTED:")
    while at >= 0:
        end = text.find("]", at + 10)
        identifier = text[at + 10 : end] if end >= 0 else ""
        if identifier and all(
            p and "a" <= p[0] <= "z" and all("a" <= c <= "z" or digit(c) for c in p)
            for p in identifier.split(".")
        ):
            result.append((cursor, at))
            cursor = end + 1
        at = text.find("[REDACTED:", at + 1)
    result.append((cursor, len(text)))
    return result


def selection(finding: Finding) -> tuple[int, int, str]:
    return finding["start"], -finding["length"], finding["ruleId"]


def check_rule(rule: Rule) -> None:
    """Reject invalid runtime shape bounds before allocating matching state."""

    def require(condition: object) -> None:
        if not condition:
            raise PackLoadError("Invalid bounded rule model. Load a compatible verified pack.")

    def printable(value: str) -> bool:
        return isinstance(value, str) and bool(value) and all(" " <= c <= "~" for c in value)

    matcher = rule["matcher"]
    if "redaction" in rule:
        redaction = rule["redaction"]
        require(
            isinstance(redaction, dict)
            and set(redaction) <= {"optional"}
            and ("optional" not in redaction or type(redaction["optional"]) is bool)
        )
    require(("pattern" in matcher) != ("list" in matcher))
    if "list" in matcher:
        tokens = matcher["list"]
        require(bool(tokens) and set(tokens) <= {"package", "hostname"})
        require(
            all(
                isinstance(words, list)
                and bool(words)
                and all(isinstance(w, str) and bool(w) for w in words)
                for words in tokens.values()
            )
        )
        return
    pattern = matcher["pattern"]
    require(
        set(pattern)
        <= {
            "shape",
            "boundary",
            "validator",
            "keywords",
            "window",
            "location",
            "exclusions",
            "excludePrefixes",
            "excludeValues",
        }
    )
    require(pattern["boundary"] in ("none", "digit", "alnum", "email"))
    parts = pattern["shape"]
    require(isinstance(parts, list) and 1 <= len(parts) <= 32)
    require(sum(bool(p.get("emit")) for p in parts) <= 1)
    total = 0
    for part in parts:
        require(set(part) <= {"min", "max", "emit", "literal", "class", "ranges", "oneOf"})
        require(
            type(part["min"]) is int
            and type(part["max"]) is int
            and 0 <= part["min"] <= part["max"] <= 64
            and part["max"] > 0
        )
        require("emit" not in part or type(part["emit"]) is bool)
        require(sum(k in part for k in ("literal", "class", "ranges", "oneOf")) == 1)
        width = 1
        if "literal" in part:
            require(printable(part["literal"]))
            width = len(part["literal"])
        if "oneOf" in part:
            words = part["oneOf"]
            require(
                isinstance(words, list)
                and bool(words)
                and part["max"] == 1
                and all(printable(w) for w in words)
            )
            width = max(len(w) for w in words)
        if "class" in part:
            require(part["class"] in ("digit", "alnum"))
        if "ranges" in part:
            ranges = part["ranges"]
            require(isinstance(ranges, list) and bool(ranges))
            require(
                all(
                    set(r) == {"low", "high"}
                    and printable(r["low"])
                    and printable(r["high"])
                    and len(r["low"]) == len(r["high"]) == 1
                    and r["low"] <= r["high"]
                    for r in ranges
                )
            )
        total += part["max"] * width
    require(total <= 256)
    for values in (
        pattern.get("keywords"),
        pattern.get("exclusions"),
        pattern.get("excludePrefixes"),
        pattern.get("excludeValues"),
    ):
        if values is not None:
            require(
                isinstance(values, list)
                and bool(values)
                and all(isinstance(v, str) and bool(v) for v in values)
            )
    if pattern.get("keywords"):
        require(all(printable(w) for w in pattern["keywords"]))
        require(type(pattern.get("window")) is int and 1 <= pattern.get("window", 0) <= 4096)
    if "window" in pattern:
        require(type(pattern["window"]) is int and 1 <= pattern["window"] <= 4096)
    require(len(set(pattern.get("exclusions", []))) == len(pattern.get("exclusions", [])))
    require(
        all(
            n
            in (
                "repeated-digits",
                "sequential-digits",
                "hex-fragment",
                "uuid-fragment",
                "version-string",
                "date-or-timestamp",
            )
            for n in pattern.get("exclusions", [])
        )
    )
    validator = pattern.get("validator")
    if validator is not None:
        require(set(validator) <= {"name", "length", "prefixes"})
        name = validator["name"]
        require(name in ("digits-length", "ng-phone-prefix", "nuban", "email-not-example-domain"))
        if name == "digits-length":
            require(type(validator.get("length")) is int and 1 <= validator.get("length", 0) <= 256)
        else:
            require("length" not in validator)
        if name == "ng-phone-prefix":
            prefixes = validator.get("prefixes", [])
            require(bool(prefixes) and all(p and all(digit(c) for c in p) for p in prefixes))
        else:
            require("prefixes" not in validator)


class RuntimeMatcher:
    """Reusable core; input rule data must already be validated and verified."""

    def __init__(
        self,
        rules: Sequence[Rule],
        disabled: Sequence[str] = (),
        diagnostic: Callable[[str], None] | None = None,
        packs: Sequence[PackVersion] = (),
    ) -> None:
        if not rules or len({r["id"] for r in rules}) != len(rules):
            raise PackLoadError("Supply non-empty verified rules with unique identifiers.")
        for rule in rules:
            check_rule(rule)
        if (
            any(
                not any(
                    r["id"] == identifier and r.get("redaction", {}).get("optional", False)
                    for r in rules
                )
                for identifier in disabled
            )
            or disabled
            and diagnostic is None
        ):
            raise ValueError("Disable only loaded optional rules and supply a start-up diagnostic.")
        if disabled and diagnostic is not None:
            diagnostic("Disabled optional redaction rules: " + ", ".join(sorted(set(disabled))))
        self._rules = tuple(deepcopy(r) for r in rules if r["id"] not in disabled)
        self._packs = tuple(
            PackVersion(id=p["id"], version=p["version"], contentDigest=p["contentDigest"])
            for p in sorted(packs, key=lambda p: p["id"])
        )
        for rule in self._rules:
            check_rule(rule)

    def versions(self) -> VersionInfo:
        """Return detached version metadata, without application inputs."""
        return {"adapterVersion": ADAPTER_VERSION, "packs": deepcopy(list(self._packs))}

    def matches(self, text: str) -> list[Finding]:
        """Return per-rule findings; invalid Unicode and oversized text fail before indexing."""
        scalars(text)
        result: list[Finding] = []
        for begin, end in segments(text):
            chars = text[begin:end]
            folded = fold(chars)
            for rule in self._rules:

                def add(start: int, length: int, rule: Rule = rule, begin: int = begin) -> None:
                    result.append(
                        {
                            "ruleId": rule["id"],
                            "category": rule["category"],
                            "severity": rule["severity"],
                            "start": begin + start,
                            "length": length,
                        }
                    )

                pattern = rule["matcher"].get("pattern")
                if pattern is None:
                    for words in rule["matcher"].get("list", {}).values():
                        for word in words:
                            at = chars.find(word)
                            while at >= 0:
                                before = chars[at - 1] if at else ""
                                after = chars[at + len(word) : at + len(word) + 1]
                                if not (alnum(before) or before and before in "._+-") and not (
                                    alnum(after) or after and after in "._+-"
                                ):
                                    add(at, len(word))
                                at = chars.find(word, at + 1)
                    continue
                if any(
                    p["min"] > 0
                    and ("literal" in p or "oneOf" in p)
                    and not any(
                        fold(w) in folded
                        for w in p.get("oneOf", [p["literal"]] if "literal" in p else [])
                    )
                    for p in pattern["shape"]
                ):
                    continue
                hits: list[tuple[int, int]] = []
                for keyword in pattern.get("keywords", []):
                    at = folded.find(fold(keyword))
                    while at >= 0:
                        hits.append((at, at + len(keyword)))
                        at = folded.find(fold(keyword), at + 1)
                if pattern.get("keywords") and not hits:
                    continue
                hits.sort()
                starts = [h[0] for h in hits]
                furthest: list[int] = []
                for _, hit_end in hits:
                    furthest.append(max(hit_end, furthest[-1] if furthest else 0))
                boundary = pattern["boundary"]

                def blocked(c: str, trailing: bool = False, boundary: str = boundary) -> bool:
                    return (
                        digit(c)
                        if boundary == "digit"
                        else alnum(c)
                        if boundary == "alnum"
                        else bool(c) and (alnum(c) or c in ("_%+-" if trailing else "._%+-"))
                        if boundary == "email"
                        else False
                    )

                match_at = shape_matcher(pattern["shape"], chars)
                at = 0
                while at < len(chars):
                    if blocked(chars[at - 1] if at else ""):
                        at += 1
                        continue
                    found = match_at(at)
                    if found is None or found[0] <= at:
                        at += 1
                        continue
                    finish, emitted, emitted_length = found
                    start = emitted if emitted is not None else at
                    length = emitted_length if emitted_length is not None else finish - at
                    index = bisect_right(starts, finish + pattern.get("window", 0))
                    near = (
                        not pattern.get("keywords")
                        or index > 0
                        and furthest[index - 1] >= at - pattern.get("window", 0)
                    )
                    validator = pattern.get("validator")
                    if (
                        length
                        and not blocked(chars[finish : finish + 1], True)
                        and near
                        and (
                            validator is None or validate(chars[start : start + length], validator)
                        )
                        and not excluded(chars, start, length, pattern)
                    ):
                        add(start, length)
                        at = finish
                    else:
                        at += 1
        return sorted(result, key=lambda f: (f["start"], f["ruleId"], f["length"]))

    def classify(self, text: str, context: Context) -> Finding | None:
        """Choose earliest, longest, then ordinal identifier; invalid context raises ValueError."""
        if (
            not isinstance(context, dict)
            or context.get("surface") not in ("log", "response", "value")
            or "fieldName" in context
            and (not isinstance(context["fieldName"], str) or not context["fieldName"])
        ):
            raise ValueError("Supply a valid surface and non-empty optional field name.")
        return min(self.matches(text), key=selection, default=None)

    def redact(self, text: str) -> str:
        """Replace emitted spans in deterministic overlap order, preserving literal masks."""
        scalars(text)
        output: list[str] = []
        cursor = 0
        size = 0
        for finding in sorted(self.matches(text), key=selection):
            start = finding["start"]
            if start < cursor:
                continue
            size += start - cursor + 11 + len(finding["ruleId"])
            if size > 65536:
                raise InputLimitError("Redacted result exceeds 65,536 scalars. Split the value.")
            output.extend((text[cursor:start], "[REDACTED:" + finding["ruleId"] + "]"))
            cursor = start + finding["length"]
        output.append(text[cursor:])
        return scalars("".join(output))
