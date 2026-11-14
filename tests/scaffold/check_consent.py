"""Run the Python consent template against the real adapter without installing packages."""

import sys
import types
import unittest
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "middleware/python/src"))
module = types.ModuleType("consent_scaffold")
sys.modules[module.__name__] = module
exec(compile((ROOT / "cli/ConsentTemplates/python/consent_provider.py").read_text(encoding="utf-8"), "consent_provider.py", "exec"), module.__dict__)


class ConsentScaffoldTests(unittest.TestCase):
    """Exercise the generated provider's append-only semantics."""

    def test_latest_event_and_complete_history(self) -> None:
        time = datetime(2026, 11, 14, tzinfo=timezone.utc)
        provider = module.EventConsentProvider(lambda: time)
        self.assertEqual(provider.get_state("subject-1", "marketing.email"), "denied")
        provider.record("subject-1", "marketing.email", True, "settings", "v1")
        first = provider.history()
        self.assertEqual(provider.get_state("subject-1", "marketing.email"), "granted")
        self.assertEqual(provider.get_state("subject-2", "marketing.email"), "denied")
        self.assertEqual(provider.get_state("subject-1", "marketing.sms"), "denied")
        provider.record("subject-1", "marketing.email", False, "settings", "v1")
        self.assertEqual(provider.get_state("subject-1", "marketing.email"), "denied")
        provider.record("subject-1", "marketing.email", True, "settings", "v2")
        self.assertEqual(provider.get_state("subject-1", "marketing.email"), "granted")
        self.assertEqual(len(first), 1)
        self.assertEqual([event.kind for event in provider.history()], ["granted", "withdrawn", "granted"])
        self.assertEqual(provider.history()[2], module.ConsentEvent("subject-1", "marketing.email", "granted", time, "settings", "v2"))
        with self.assertRaises(ValueError):
            provider.record("subject-1", "invalid", True, "settings", "v1")
        self.assertEqual(len(provider.history()), 3)


if __name__ == "__main__":
    unittest.main()
