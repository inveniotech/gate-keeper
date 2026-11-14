"""Execute the native Python core against unchanged engine expectations."""

import json

from python_fixture import ROOT, RuntimeMatcher, validate


def main() -> None:
    """Check findings, classification, redaction, validators and versions."""
    vectors = json.loads((ROOT / "tests/conformance/vectors.json").read_text())
    runtime = RuntimeMatcher(vectors["rules"], packs=vectors["packs"])
    assert runtime.versions() == vectors["runtimeVersions"]
    for sample in vectors["cases"] + vectors["syntheticRuntimeCases"]:
        current = RuntimeMatcher(sample["rules"]) if "rules" in sample else runtime
        assert current.matches(sample["text"]) == sample["runtimeExpected"], sample[
            "name"
        ]
        for surface in ("log", "response", "value"):
            assert (
                current.classify(
                    sample["text"], {"surface": surface, "fieldName": "synthetic"}
                )
                == sample["classification"]
            ), sample["name"]
        assert current.redact(sample["text"]) == sample["redacted"], sample["name"]
        assert current.redact(sample["redacted"]) == sample["redacted"], sample["name"]
    for name in (
        "digits-length",
        "nuban",
        "ng-phone-prefix",
        "email-not-example-domain",
    ):
        data = json.loads(
            (ROOT / f"tests/conformance/validator-{name}.json").read_text()
        )
        for sample in data["cases"]:
            assert (
                validate(sample["text"], {**data, "name": name}) == sample["accepted"]
            ), name
    print("Python native matcher, classification, redaction and validators passed.")


if __name__ == "__main__":
    main()
