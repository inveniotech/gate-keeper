"""Compare Python regex candidate spans with engine-generated UTF-16 spans."""

import base64
from pathlib import Path
import re


def check_patterns() -> int:
    """Fail on a portability disagreement without changing any generated pattern."""
    checked = 0
    for line in (
        Path(__file__)
        .with_name("patterns.tsv")
        .read_text(encoding="utf-8")
        .splitlines()
    ):
        if line.startswith("#"):
            continue
        identifier, encoded, text, *expected = line.split("\t")
        pattern = base64.b64decode(encoded).decode().replace("(?<emit>", "(?P<emit>")
        value = base64.b64decode(text).decode()
        offsets = [0]
        for char in value:
            offsets.append(offsets[-1] + (2 if ord(char) > 0xFFFF else 1))
        actual = []
        for match in re.finditer(pattern, value):
            start, end = (
                match.span("emit") if "emit" in match.re.groupindex else match.span()
            )
            actual.append(
                f"{offsets[match.start()]}:{offsets[match.end()] - offsets[match.start()]}:{offsets[start]}:{offsets[end] - offsets[start]}"
            )
        assert ",".join(actual) == (expected[0] if expected else ""), identifier
        checked += 1
    assert checked > 0
    return checked


if __name__ == "__main__":
    print(f"Python regex portability passed: {check_patterns()} cases.")
