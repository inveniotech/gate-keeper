"""Python-specific consent and atomic response acceptance tests."""

import asyncio
import json
import unittest

from python_fixture import (
    ROOT,
    RuntimeMatcher,
    InputLimitError,
    ConsentCheck,
    ResponseFilter,
    ResponseFilterError,
)


class BehaviourTests(unittest.IsolatedAsyncioTestCase):
    """Exercise host semantics in addition to unchanged engine fixtures."""

    async def test_consent_should_preserve_cancellation_when_provider_cleanup_throws(
        self,
    ) -> None:
        events = []

        async def provider(subject: str, purpose: str) -> str:
            asyncio.current_task().cancel()
            try:
                await asyncio.get_running_loop().create_future()
            except asyncio.CancelledError:
                raise RuntimeError("synthetic-sensitive-marker") from None

        check = ConsentCheck(None, provider, events.append)
        with self.assertRaises(asyncio.CancelledError):
            await asyncio.create_task(check.acheck("synthetic-subject", "a.b"))
        self.assertEqual(events, [])

    def test_sync_consent_should_map_internal_cancellation_to_unknown(self) -> None:
        events = []

        def provider(subject: str, purpose: str) -> str:
            raise asyncio.CancelledError()

        check = ConsentCheck(provider, None, events.append)
        self.assertEqual(check.check("synthetic-subject", "a.b"), "unknown")
        self.assertEqual(events, [{"purpose": "a.b", "errorType": "CancelledError"}])

    def test_response_should_match_all_engine_vectors(self) -> None:
        data = json.loads(
            (ROOT / "tests/conformance/behaviour-vectors.json").read_text()
        )["response"]
        runtime = RuntimeMatcher(data["rules"])
        for sample in data["cases"]:
            with self.subTest(sample["name"]):
                before = json.dumps(sample["body"], sort_keys=True)
                response = ResponseFilter(runtime, sample["paths"])
                options = {
                    k: sample[k]
                    for k in ("lengthKnown", "headersCommitted", "contentType")
                }
                if sample["failed"]:
                    with self.assertRaises(ResponseFilterError) as caught:
                        response.transform(sample["body"], options)
                    self.assertEqual(
                        caught.exception.abort_required, sample["abortRequired"]
                    )
                else:
                    self.assertEqual(
                        response.transform(sample["body"], options), sample["expected"]
                    )
                self.assertEqual(json.dumps(sample["body"], sort_keys=True), before)

    async def test_consent_should_await_only_async_provider(self) -> None:
        calls = []

        def sync(subject: str, purpose: str) -> str:
            calls.append("sync")
            return "denied"

        async def asynchronous(subject: str, purpose: str) -> str:
            calls.append("async")
            return "granted"

        check = ConsentCheck(sync, asynchronous, lambda event: None)
        self.assertEqual(
            check.check("synthetic-subject", "acme.marketing-sms"), "denied"
        )
        self.assertTrue(
            await check.ais_granted("synthetic-subject", "acme.marketing-sms")
        )
        self.assertEqual(calls, ["sync", "async"])

    async def test_consent_should_propagate_caller_cancellation(self) -> None:
        async def cancelled(subject: str, purpose: str) -> str:
            asyncio.current_task().cancel()
            raise asyncio.CancelledError()

        check = ConsentCheck(None, cancelled, lambda event: None)
        with self.assertRaises(asyncio.CancelledError):
            await asyncio.create_task(check.acheck("synthetic-subject", "a.b"))

    async def test_consent_should_match_every_provider_vector(self) -> None:
        data = json.loads(
            (ROOT / "tests/conformance/behaviour-vectors.json").read_text()
        )["consent"]
        for sample in data["cases"]:
            with self.subTest(sample["provider"]):
                calls = []
                events = []
                cancelled = sample["provider"] == "cancelled"

                async def provider(subject: str, purpose: str) -> str:
                    nonlocal cancelled
                    calls.append(1)
                    if sample["provider"] == "caller-cancelled-during":
                        cancelled = True
                        asyncio.current_task().cancel()
                        raise asyncio.CancelledError()
                    if sample["provider"] == "provider-timeout":
                        raise asyncio.CancelledError()
                    if sample["provider"] == "throws":
                        raise RuntimeError("synthetic-sensitive-marker")
                    return sample["provider"]

                check = ConsentCheck(
                    None,
                    None if sample["provider"] == "absent" else provider,
                    events.append,
                )

                async def run() -> tuple[str, bool | None]:
                    if cancelled:
                        asyncio.current_task().cancel()
                    try:
                        answer = await check.acheck(
                            "synthetic-subject", "acme.marketing-sms"
                        )
                    except asyncio.CancelledError:
                        answer = "cancelled"
                        asyncio.current_task().uncancel()
                    if cancelled:
                        asyncio.current_task().cancel()
                    try:
                        granted = await check.ais_granted(
                            "synthetic-subject", "acme.marketing-sms"
                        )
                    except asyncio.CancelledError:
                        granted = None
                        asyncio.current_task().uncancel()
                    return answer, granted

                task = asyncio.create_task(run())
                try:
                    answer, granted = await task
                except asyncio.CancelledError:
                    answer, granted = "cancelled", None
                self.assertEqual(answer, sample["expected"])
                self.assertEqual(granted, sample["isGranted"])
                self.assertEqual(len(calls), sample["calls"])
                self.assertEqual(len(events), sample["diagnostics"])

    def test_consent_should_validate_before_provider_and_report_only_metadata(
        self,
    ) -> None:
        events = []
        calls = []

        def throws(subject: str, purpose: str) -> str:
            calls.append(1)
            raise RuntimeError("synthetic-sensitive-marker")

        check = ConsentCheck(throws, None, events.append)
        data = json.loads(
            (ROOT / "tests/conformance/behaviour-vectors.json").read_text()
        )["consent"]
        for sample in data["purposes"]:
            if sample["accepted"]:
                self.assertEqual(
                    check.check("synthetic-subject", sample["value"]), "unknown"
                )
            else:
                before = len(calls)
                with self.assertRaises(ValueError):
                    check.check("synthetic-subject", sample["value"])
                self.assertEqual(len(calls), before)
        self.assertNotIn("synthetic-sensitive-marker", str(events))
        self.assertNotIn("synthetic-subject", str(events))

    def test_runtime_should_reject_invalid_unicode_and_expansion(self) -> None:
        vectors = json.loads((ROOT / "tests/conformance/vectors.json").read_text())
        runtime = RuntimeMatcher(vectors["rules"])
        self.assertEqual(runtime.redact("😀" * 65536), "😀" * 65536)
        with self.assertRaises(InputLimitError):
            runtime.redact("😀" * 65537)
        with self.assertRaises(ValueError):
            runtime.redact("\ud800")


if __name__ == "__main__":
    unittest.main()
