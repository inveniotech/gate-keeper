"""Standard-library logging integration."""

import logging

from .runtime import RuntimeMatcher


class RedactingFilter(logging.Filter):
    """Redact the formatted record message before a handler emits it."""

    def __init__(self, runtime: RuntimeMatcher) -> None:
        super().__init__()
        self._runtime = runtime

    def filter(self, record: logging.LogRecord) -> bool:
        """Replace message and arguments; failures propagate without emitting original text."""
        record.msg = self._runtime.redact(record.getMessage())
        record.args = ()
        return True
