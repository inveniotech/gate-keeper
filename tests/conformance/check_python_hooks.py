"""Fail-closed host-protocol tests without installing frameworks into the core."""

from importlib import import_module
import json
import logging
import unittest

from python_fixture import ROOT, RuntimeMatcher, ResponseFilterError

FastAPIMiddleware = import_module("inveniotech.gatekeeper.fastapi").FastAPIMiddleware
RedactingFilter = import_module("inveniotech.gatekeeper.logging").RedactingFilter


class HookTests(unittest.IsolatedAsyncioTestCase):
    """Exercise the byte-commit boundary and fully formatted logging messages."""

    def setUp(self) -> None:
        sample = json.loads((ROOT / "tests/conformance/vectors.json").read_text())[
            "syntheticRuntimeCases"
        ][0]
        self.runtime = RuntimeMatcher(sample["rules"])
        self.sample = sample

    def test_logging_should_redact_formatted_message_and_clear_arguments(self) -> None:
        record = logging.LogRecord(
            "synthetic", logging.INFO, "", 0, "%s", (self.sample["text"],), None
        )
        self.assertTrue(RedactingFilter(self.runtime).filter(record))
        self.assertEqual(record.getMessage(), self.sample["redacted"])
        self.assertEqual(record.args, ())

    async def test_asgi_should_send_only_complete_filtered_body(self) -> None:
        raw = json.dumps({"protected": self.sample["text"]}).encode()
        emitted = []

        async def app(scope, receive, send):
            await send(
                {
                    "type": "http.response.start",
                    "status": 200,
                    "headers": [
                        (b"content-type", b"application/json"),
                        (b"content-length", str(len(raw)).encode()),
                    ],
                }
            )
            await send({"type": "http.response.body", "body": raw})

        async def receive():
            return {"type": "http.request"}

        async def send(message):
            emitted.append(message)

        await FastAPIMiddleware(app, self.runtime, ["/protected"])(
            {"type": "http"}, receive, send
        )
        self.assertEqual(
            json.loads(emitted[1]["body"]), {"protected": self.sample["redacted"]}
        )

    async def test_asgi_should_send_no_bytes_when_protected_response_streams(
        self,
    ) -> None:
        for mode in ("missing-length", "streaming", "wrong-type", "oversized"):
            with self.subTest(mode):
                emitted = []

                async def app(scope, receive, send):
                    headers = [
                        (
                            b"content-type",
                            b"application/json"
                            if mode != "wrong-type"
                            else b"text/plain",
                        )
                    ]
                    if mode != "missing-length":
                        headers.append(
                            (
                                b"content-length",
                                b"9999999" if mode == "oversized" else b"2",
                            )
                        )
                    await send(
                        {
                            "type": "http.response.start",
                            "status": 200,
                            "headers": headers,
                        }
                    )
                    await send(
                        {
                            "type": "http.response.body",
                            "body": b"{}",
                            "more_body": mode == "streaming",
                        }
                    )

                async def receive():
                    return {"type": "http.request"}

                async def send(message):
                    emitted.append(message)

                with self.assertRaises(ResponseFilterError):
                    await FastAPIMiddleware(app, self.runtime, ["/protected"])(
                        {"type": "http"}, receive, send
                    )
                self.assertEqual(emitted, [])


if __name__ == "__main__":
    unittest.main()
