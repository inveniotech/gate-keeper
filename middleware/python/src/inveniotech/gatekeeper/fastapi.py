"""FastAPI/Starlette ASGI middleware with bounded, atomic response handling."""

import json
from collections.abc import Awaitable, Callable, Sequence
from typing import cast

from .response import Json, ResponseFilter, ResponseFilterError
from .runtime import RuntimeMatcher

Message = dict[str, object]
Receive = Callable[[], Awaitable[Message]]
Send = Callable[[Message], Awaitable[None]]
App = Callable[[Message, Receive, Send], Awaitable[None]]


class FastAPIMiddleware:
    """Hold a declared bounded JSON response until filtering completes; never consume streams."""

    def __init__(
        self, app: App, runtime: RuntimeMatcher, paths: Sequence[str], max_bytes: int = 1048576
    ) -> None:
        self._app = app
        self._filter = ResponseFilter(runtime, paths)
        self._protected = bool(paths)
        if max_bytes < 1:
            raise ValueError("Supply a positive host response byte bound.")
        self._max_bytes = max_bytes

    async def __call__(self, scope: Message, receive: Receive, send: Send) -> None:
        """Send no protected headers or body until the complete bounded value is safe."""
        if scope.get("type") != "http" or not self._protected:
            await self._app(scope, receive, send)
            return
        start: Message | None = None
        declared = 0
        content_type = ""
        committed = False

        async def filtered_send(message: Message) -> None:
            nonlocal start, declared, content_type, committed
            if message.get("type") == "http.response.start":
                headers = cast(list[tuple[bytes, bytes]], message.get("headers", []))
                values = {k.lower(): v for k, v in headers}
                try:
                    declared = int(values.get(b"content-length", b"-1"))
                    content_type = values.get(b"content-type", b"").decode("ascii")
                except (ValueError, UnicodeError):
                    raise ResponseFilterError(committed) from None
                if declared < 0 or declared > self._max_bytes or start is not None:
                    raise ResponseFilterError(committed)
                start = dict(message)
            elif message.get("type") == "http.response.body":
                raw = message.get("body", b"")
                if (
                    start is None
                    or not isinstance(raw, bytes)
                    or message.get("more_body", False)
                    or len(raw) != declared
                ):
                    raise ResponseFilterError(committed)
                try:
                    body = cast(Json, json.loads(raw.decode("utf-8")))
                    result = self._filter.transform(
                        body, {"contentType": content_type, "lengthKnown": True}
                    )
                    encoded = json.dumps(
                        result, ensure_ascii=False, sort_keys=True, separators=(",", ":")
                    ).encode("utf-8")
                    if len(encoded) > self._max_bytes:
                        raise ResponseFilterError(committed)
                except (ValueError, UnicodeError, ResponseFilterError):
                    raise ResponseFilterError(committed) from None
                headers = cast(list[tuple[bytes, bytes]], start.get("headers", []))
                start["headers"] = [
                    (k, v) for k, v in headers if k.lower() != b"content-length"
                ] + [(b"content-length", str(len(encoded)).encode("ascii"))]
                committed = True
                await send(start)
                await send({"type": "http.response.body", "body": encoded, "more_body": False})
            else:
                raise ResponseFilterError(committed)

        await self._app(scope, receive, filtered_send)
