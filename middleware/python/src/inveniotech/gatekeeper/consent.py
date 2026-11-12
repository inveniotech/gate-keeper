"""Uncached synchronous and asynchronous consent-provider mapping."""

import asyncio
from collections.abc import Awaitable, Callable
from typing import Literal, TypedDict

from .runtime import scalars

ConsentState = Literal["granted", "denied", "unknown"]
SyncProvider = Callable[[str, str], str]
AsyncProvider = Callable[[str, str], Awaitable[str]]


class ConsentDiagnostic(TypedDict):
    """Safe diagnostic containing no subject, provider message or stack trace."""

    purpose: str | None
    errorType: str


def arguments(subject: str, purpose: str) -> None:
    if not isinstance(subject, str) or not 1 <= len(subject) <= 1024:
        raise ValueError("Supply a subject of 1 to 1,024 Unicode scalars.")
    scalars(subject)
    if not isinstance(purpose, str) or len(purpose) > 164:
        raise ValueError("Supply a valid namespaced consent purpose.")
    parts = purpose.split(".")
    if not 2 <= len(parts) <= 5 or any(
        not 1 <= len(p) <= 32
        or not "a" <= p[0] <= "z"
        or any(not ("a" <= c <= "z" or "0" <= c <= "9" or c == "-") for c in p)
        for p in parts
    ):
        raise ValueError("Supply a valid namespaced consent purpose.")


def state(value: str) -> ConsentState:
    if value == "granted":
        return "granted"
    if value == "denied":
        return "denied"
    return "unknown"


def caller_cancelled() -> bool:
    try:
        task = asyncio.current_task()
    except RuntimeError:
        return False
    return task is not None and task.cancelling() > 0


class ConsentCheck:
    """Stateless helper. Application providers must be safe for concurrent calls."""

    def __init__(
        self,
        provider: SyncProvider | None,
        async_provider: AsyncProvider | None,
        diagnostic: Callable[[ConsentDiagnostic], None],
    ) -> None:
        self._provider = provider
        self._async_provider = async_provider
        self._diagnostic = diagnostic
        if provider is None and async_provider is None:
            diagnostic({"purpose": None, "errorType": "NoConsentProvider"})

    def check(self, subject_id: str, purpose: str) -> ConsentState:
        """Validate first, call only the sync provider, and map failures to unknown."""
        arguments(subject_id, purpose)
        if caller_cancelled():
            raise asyncio.CancelledError()
        if self._provider is None:
            return "unknown"
        try:
            answer = self._provider(subject_id, purpose)
            if caller_cancelled():
                raise asyncio.CancelledError()
            return state(answer)
        except asyncio.CancelledError as error:
            if caller_cancelled():
                raise
            self._diagnostic({"purpose": purpose, "errorType": type(error).__name__})
            return "unknown"
        except Exception as error:
            if caller_cancelled():
                raise asyncio.CancelledError() from None
            self._diagnostic({"purpose": purpose, "errorType": type(error).__name__})
            return "unknown"

    def is_granted(self, subject_id: str, purpose: str) -> bool:
        """Perform the same synchronous check; only granted returns true."""
        return self.check(subject_id, purpose) == "granted"

    async def acheck(self, subject_id: str, purpose: str) -> ConsentState:
        """Await only the async provider; native caller cancellation propagates."""
        arguments(subject_id, purpose)
        task = asyncio.current_task()
        if task is not None and task.cancelling():
            raise asyncio.CancelledError()
        if self._async_provider is None:
            return "unknown"
        try:
            answer = await self._async_provider(subject_id, purpose)
            if task is not None and task.cancelling():
                raise asyncio.CancelledError()
            return state(answer)
        except asyncio.CancelledError as error:
            if task is not None and task.cancelling():
                raise
            self._diagnostic({"purpose": purpose, "errorType": type(error).__name__})
            return "unknown"
        except Exception as error:
            if caller_cancelled():
                raise asyncio.CancelledError() from None
            self._diagnostic({"purpose": purpose, "errorType": type(error).__name__})
            return "unknown"

    async def ais_granted(self, subject_id: str, purpose: str) -> bool:
        """Perform the same asynchronous check; only granted returns true."""
        return await self.acheck(subject_id, purpose) == "granted"
