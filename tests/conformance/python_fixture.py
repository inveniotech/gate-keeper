"""Source-tree imports belong to the conformance fixture, not the runtime core."""

from importlib import import_module
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "middleware/python/src"))
runtime = import_module("inveniotech.gatekeeper.runtime")
RuntimeMatcher = runtime.RuntimeMatcher
InputLimitError = runtime.InputLimitError
PackLoadError = runtime.PackLoadError
validate = runtime.validate
ConsentCheck = import_module("inveniotech.gatekeeper.consent").ConsentCheck
response = import_module("inveniotech.gatekeeper.response")
ResponseFilter = response.ResponseFilter
ResponseFilterError = response.ResponseFilterError
pack = import_module("inveniotech.gatekeeper.pack")
SignedPack = pack.SignedPack
Installation = pack.Installation
verify_signed_pack = pack.verify_signed_pack
verify = import_module("inveniotech.gatekeeper.ed25519").verify
