"""Run a real FastAPI application with bounded response filtering."""
import json
import logging
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "middleware/python/src"))
from fastapi import FastAPI
from fastapi.testclient import TestClient
from inveniotech.gatekeeper import ConsentCheck, ConsentState, RuntimeMatcher
from inveniotech.gatekeeper.fastapi import FastAPIMiddleware
from inveniotech.gatekeeper.logging import RedactingFilter


def main() -> None:
    """Exercise framework registration, redaction and asynchronous consent."""
    vectors = json.loads((ROOT / "tests/conformance/vectors.json").read_text())
    sample = vectors["syntheticRuntimeCases"][0]
    runtime = RuntimeMatcher(sample["rules"])
    app = FastAPI()
    app.add_middleware(FastAPIMiddleware, runtime=runtime, paths=["/protected"])

    class Provider:
        async def get_state(self, subject_id: str, purpose: str) -> ConsentState:
            return "denied"

    consent = ConsentCheck(None, Provider(), lambda event: None)

    @app.get("/")
    async def endpoint() -> dict[str, object]:
        record = logging.LogRecord("synthetic", logging.INFO, "", 0, "%s", (sample["text"],), None)
        RedactingFilter(runtime).filter(record)
        assert record.getMessage() == sample["redacted"]
        return {"protected": sample["text"], "granted": await consent.ais_granted("synthetic-subject", "acme.marketing-sms")}

    with TestClient(app) as client:
        response = client.get("/")
    assert response.json() == {"protected": sample["redacted"], "granted": False}


if __name__ == "__main__":
    main()
