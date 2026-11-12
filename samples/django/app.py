"""Run the Django integration against an existing synthetic conformance case."""
import json
import logging
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "middleware/python/src"))
from django.conf import settings
from django.http import JsonResponse
from django.test import RequestFactory
from inveniotech.gatekeeper import ConsentCheck, RuntimeMatcher
from inveniotech.gatekeeper.django import DjangoMiddleware
from inveniotech.gatekeeper.logging import RedactingFilter


def main() -> None:
    """Exercise the real Django response and request objects without a server."""
    if not settings.configured:
        settings.configure(DEFAULT_CHARSET="utf-8")
    vectors = json.loads((ROOT / "tests/conformance/vectors.json").read_text())
    sample = vectors["syntheticRuntimeCases"][0]
    runtime = RuntimeMatcher(sample["rules"])
    record = logging.LogRecord("synthetic", logging.INFO, "", 0, "%s", (sample["text"],), None)
    RedactingFilter(runtime).filter(record)
    assert record.getMessage() == sample["redacted"]
    middleware = DjangoMiddleware(lambda request: JsonResponse({"protected": sample["text"]}), runtime, ["/protected"])
    response = middleware(RequestFactory().get("/"))
    assert json.loads(response.content)["protected"] == sample["redacted"]
    consent = ConsentCheck(lambda subject, purpose: "denied", None, lambda event: None)
    assert not consent.is_granted("synthetic-subject", "acme.marketing-sms")


if __name__ == "__main__":
    main()
