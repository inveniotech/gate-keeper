"""Signature and signed-pack acceptance tests using development fixtures only."""

import json
import re
import unittest
import tempfile
from pathlib import Path
from unittest.mock import patch
from copy import deepcopy
from importlib import import_module

from python_fixture import (
    ROOT,
    Installation,
    PackLoadError,
    SignedPack,
    verify_signed_pack,
    verify,
)


class PackTests(unittest.TestCase):
    """Verify exact signed bytes, never reserialised content."""

    def test_packaged_installation_should_require_complete_trusted_media(self) -> None:
        fixture = ROOT / "tests/vectors/pack"
        keys = {
            item["keyId"]: bytes.fromhex(item["publicKeyHex"])
            for item in json.loads((fixture / "development-public-key.json").read_text())["keys"]
        }
        with tempfile.TemporaryDirectory() as temporary:
            package = Path(temporary)
            embedded = package / "embedded"
            embedded.mkdir()
            with patch("inveniotech.gatekeeper.pack.files", return_value=package):
                with self.assertRaises(PackLoadError):
                    Installation.from_package(keys)
                for suffix, source in (
                    ("json", "pack.pack.json"),
                    ("manifest.armour", "pack.manifest.armour"),
                    ("signature.hex", "pack.signature.hex"),
                ):
                    (embedded / f"pack.{suffix}").write_bytes((fixture / source).read_bytes())
                installation = Installation.from_package(keys)
                self.assertEqual(installation.runtime.versions()["packs"][0]["id"], "invenio.ng.example.p1")
                (embedded / "pack.signature.hex").unlink()
                with self.assertRaises(PackLoadError):
                    Installation.from_package(keys)
                (embedded / "pack.signature.hex").write_bytes(
                    (fixture / "pack.signature.hex").read_bytes()
                )
                with self.assertRaises(PackLoadError):
                    Installation.from_package({})
                (embedded / "pack.json").write_bytes(b"tampered")
                with self.assertRaises(PackLoadError):
                    Installation.from_package(keys)

    def test_path_loader_should_bound_media_and_reject_present_invalid_watermark(
        self,
    ) -> None:
        folder = ROOT / "tests/vectors/pack"
        keys = {
            k["keyId"]: bytes.fromhex(k["publicKeyHex"])
            for k in json.loads((folder / "development-public-key.json").read_text())[
                "keys"
            ]
        }

        def signed(stem):
            return SignedPack(
                (folder / f"{stem}.pack.json").read_bytes(),
                (folder / f"{stem}.manifest.armour").read_text(),
                bytes.fromhex((folder / f"{stem}.signature.hex").read_text()),
            )

        active = Installation([signed("pack")], keys, keys)
        initial = active.runtime
        with tempfile.TemporaryDirectory() as temporary:
            path = import_module("pathlib").Path(temporary) / "pack.json"
            path.write_bytes((folder / "next-line.pack.json").read_bytes())
            path.with_suffix(".manifest.armour").write_text(
                (folder / "next-line.manifest.armour").read_text()
            )
            path.with_suffix(".signature.hex").write_text(
                (folder / "next-line.signature.hex").read_text()
            )
            path.with_suffix(".watermark.manifest.armour").write_text(
                (folder / "watermark.manifest.armour").read_text()
            )
            path.with_suffix(".watermark.signature.hex").write_text(
                (folder / "watermark-tampered-signature.signature.hex").read_text()
            )
            with self.assertRaises(PackLoadError):
                active.replace_from_path(path)
            self.assertIs(active.runtime, initial)
            path.with_suffix(".watermark.signature.hex").unlink()
            with self.assertRaises(PackLoadError):
                active.replace_from_path(path)
            path.with_suffix(".watermark.manifest.armour").unlink()
            path.with_suffix(".manifest.armour").write_text("x" * 8193)
            with self.assertRaises(PackLoadError):
                active.replace_from_path(path)
            self.assertIs(active.runtime, initial)

    def test_pack_schema_should_reject_invalid_optional_policy_and_nested_metadata(
        self,
    ) -> None:
        original = json.loads((ROOT / "tests/vectors/pack/pack.pack.json").read_text())[
            "rules"
        ][0]
        validator = import_module("inveniotech.gatekeeper.pack")._rules
        for key, value in (
            ("redaction", {"optional": "false"}),
            ("redaction", {"unknown": True}),
            ("profiles", ["unknown"]),
            ("profiles", ["default", "default"]),
            ("suppressionHints", [4]),
            ("processor", {"dataCategories": [], "usualRegion": "synthetic"}),
        ):
            with self.subTest(key=key, value=value):
                rule = deepcopy(original)
                rule[key] = value
                with self.assertRaises((ValueError, PackLoadError)):
                    validator([rule])
        for metadata in (
            {"window": 0},
            {"location": {"kind": "unknown"}},
            {"location": {"kind": "log", "paths": ["../escape"]}},
        ):
            rule = deepcopy(original)
            rule["matcher"]["pattern"].update(metadata)
            with self.assertRaises((ValueError, PackLoadError)):
                validator([rule])

    def test_pack_schema_should_translate_valid_range_pairs_for_native_core(
        self,
    ) -> None:
        original = json.loads((ROOT / "tests/vectors/pack/pack.pack.json").read_text())[
            "rules"
        ][0]
        original["matcher"]["pattern"]["shape"] = [
            {"min": 11, "max": 11, "ranges": [["0", "9"]]}
        ]
        rules = import_module("inveniotech.gatekeeper.pack")._rules([original])
        self.assertEqual(
            rules[0]["matcher"]["pattern"]["shape"][0]["ranges"],
            [{"low": "0", "high": "9"}],
        )

    def test_pack_schema_should_allow_unicode_list_tokens(self) -> None:
        rule = json.loads((ROOT / "tests/vectors/pack/pack.pack.json").read_text())[
            "rules"
        ][0]
        rule["matcher"] = {"list": {"package": ["synthetic-😀"]}}
        rules = import_module("inveniotech.gatekeeper.pack")._rules([rule])
        self.assertEqual(rules[0]["matcher"]["list"]["package"], ["synthetic-😀"])

    def test_watermark_armour_should_reject_nonstring_identity_metadata(self) -> None:
        module = import_module("inveniotech.gatekeeper.pack")
        lines = (
            (ROOT / "tests/vectors/pack/watermark.manifest.armour")
            .read_text()
            .splitlines()
        )
        data = json.loads(lines[2])
        data["organisationName"] = 42
        lines[2] = json.dumps(data, ensure_ascii=False, separators=(",", ":"))
        with self.assertRaises(ValueError):
            module._armour("\n".join(lines), True)

    def test_verifier_should_reject_noncanonical_and_small_order_points(self) -> None:
        identity = b"\x01" + b"\x00" * 31
        self.assertFalse(verify(identity, b"", identity + b"\x00" * 32))
        self.assertFalse(
            verify((2**255 - 19).to_bytes(32, "little"), b"", b"\x00" * 64)
        )

    def test_installation_should_replace_only_newer_same_identifier_atomically(
        self,
    ) -> None:
        folder = ROOT / "tests/vectors/pack"
        keys = {
            k["keyId"]: bytes.fromhex(k["publicKeyHex"])
            for k in json.loads((folder / "development-public-key.json").read_text())[
                "keys"
            ]
        }

        def load(stem: str) -> SignedPack:
            content = folder / f"{stem}.pack.json"
            return SignedPack(
                content.read_bytes()
                if content.exists()
                else (folder / "pack.pack.json").read_bytes(),
                (folder / f"{stem}.manifest.armour").read_text(),
                bytes.fromhex((folder / f"{stem}.signature.hex").read_text()),
            )

        active = Installation([load("pack")], keys)
        initial = active.runtime
        for stem in (
            "pack",
            "conflict",
            "rival",
            "tampered-content",
            "tampered-signature",
            "unknown-key",
            "key-id-mismatch",
            "schema-major",
            "newer-engine",
        ):
            with self.subTest(stem):
                with self.assertRaises(PackLoadError):
                    active.replace(load(stem))
                self.assertIs(active.runtime, initial)
        active.replace(load("next-line"))
        self.assertEqual(active.runtime.versions()["packs"][0]["version"], "2027.2.0")
        with self.assertRaises(PackLoadError):
            active.replace(load("pack"))

    def test_loader_should_verify_optional_watermark_binding_and_signature(
        self,
    ) -> None:
        folder = ROOT / "tests/vectors/pack"
        keys = {
            k["keyId"]: bytes.fromhex(k["publicKeyHex"])
            for k in json.loads((folder / "development-public-key.json").read_text())[
                "keys"
            ]
        }
        for stem, signature in (
            ("watermark", "watermark"),
            ("watermark-digest-mismatch", "watermark-digest-mismatch"),
            ("watermark", "watermark-tampered-signature"),
        ):
            candidate = SignedPack(
                (folder / "pack.pack.json").read_bytes(),
                (folder / "pack.manifest.armour").read_text(),
                bytes.fromhex((folder / "pack.signature.hex").read_text()),
                (folder / f"{stem}.manifest.armour").read_text(),
                bytes.fromhex((folder / f"{signature}.signature.hex").read_text()),
            )
            if stem == signature == "watermark":
                self.assertTrue(verify_signed_pack(candidate, keys, keys).rules)
            else:
                with self.assertRaises(PackLoadError):
                    verify_signed_pack(candidate, keys, keys)

    def test_verifier_should_match_existing_licence_signature_vectors_without_entitlement_policy(
        self,
    ) -> None:
        folder = ROOT / "tests/vectors/licence"
        key = bytes.fromhex(
            json.loads((folder / "development-public-key.json").read_text())[
                "publicKeyHex"
            ]
        )
        for stem in (
            "claims-one-time",
            "claims-with-updates",
            "claims-dates-out-of-order",
            "tampered-signature",
            "tampered-payload",
        ):
            lines = (folder / f"{stem}.gklic").read_text().splitlines()
            self.assertEqual(
                verify(
                    key,
                    lines[3].encode(),
                    bytes.fromhex(lines[2].removeprefix("Signature: ")),
                ),
                stem.startswith("claims-"),
                stem,
            )

    def test_verifier_should_accept_rfc8032_empty_message_and_reject_tampering(
        self,
    ) -> None:
        key = bytes.fromhex(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"
        )
        signature = bytes.fromhex(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"
        )
        self.assertTrue(verify(key, b"", signature))
        self.assertFalse(verify(key, b"x", signature))
        self.assertFalse(verify(key, b"", signature[:63]))

    def test_verifier_should_accept_all_existing_rfc8032_vectors(self) -> None:
        source = (ROOT / "tests/Signing.Tests/Rfc8032Vectors.cs").read_text()
        blocks = re.findall(r'new\(\s*"TEST.*?(?=\n\s*new\(|\n\s*};)', source, re.S)
        self.assertEqual(len(blocks), 4)
        for block in blocks:
            fields = re.findall(r"TestHelpers.Hex\((.*?)\)", block, re.S)
            decoded = [
                bytes.fromhex("".join(re.findall(r'"([0-9a-f]+)"', field)))
                for field in fields
            ]
            _, key, *rest = decoded
            message, signature = (b"", rest[0]) if len(rest) == 1 else rest
            self.assertTrue(verify(key, message, signature))
            self.assertFalse(verify(key, message + b"x", signature))

    def test_loader_should_match_existing_pack_vectors(self) -> None:
        folder = ROOT / "tests/vectors/pack"
        keys = {
            k["keyId"]: bytes.fromhex(k["publicKeyHex"])
            for k in json.loads((folder / "development-public-key.json").read_text())[
                "keys"
            ]
        }
        for stem in (
            "pack",
            "assembly",
            "next-line",
            "plan-gated",
            "tampered-content",
            "tampered-signature",
            "schema-major",
            "newer-engine",
            "coverage-after-issued",
            "base-version-wrong",
            "local-rule-in-bundled",
        ):
            with self.subTest(stem):
                candidate = SignedPack(
                    (folder / f"{stem}.pack.json").read_bytes(),
                    (folder / f"{stem}.manifest.armour").read_text(),
                    bytes.fromhex((folder / f"{stem}.signature.hex").read_text()),
                )
                if stem in ("pack", "assembly", "next-line", "plan-gated"):
                    self.assertTrue(verify_signed_pack(candidate, keys).rules)
                else:
                    with self.assertRaises(PackLoadError):
                        verify_signed_pack(candidate, keys)


if __name__ == "__main__":
    unittest.main()
