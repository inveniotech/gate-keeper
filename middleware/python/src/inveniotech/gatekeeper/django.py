"""Django response middleware. Import this integration only in a Django host."""

import json
from collections.abc import Callable, Sequence
from typing import Protocol, cast

from .response import Json, ResponseFilter, ResponseFilterError
from .runtime import RuntimeMatcher


class DjangoResponse(Protocol):
    """Minimal mutable response surface supplied by Django."""

    content: bytes
    streaming: bool
    charset: str

    def get(self, header: str, default: str = "") -> str: ...
    def __setitem__(self, header: str, value: str) -> None: ...


class DjangoMiddleware:
    """Filter bounded JSON before Django sends headers; streaming responses fail closed."""

    def __init__(
        self,
        get_response: Callable[[object], DjangoResponse],
        runtime: RuntimeMatcher,
        paths: Sequence[str],
        max_bytes: int = 1048576,
    ) -> None:
        self._get_response = get_response
        self._filter = ResponseFilter(runtime, paths)
        self._protected = bool(paths)
        if max_bytes < 1:
            raise ValueError("Supply a positive host response byte bound.")
        self._max_bytes = max_bytes

    def __call__(self, request: object) -> DjangoResponse:
        """Return only fully transformed responses; failures occur before host transmission."""
        response = self._get_response(request)
        if not self._protected:
            return response
        if response.streaming:
            raise ResponseFilterError(False)
        try:
            if len(response.content) > self._max_bytes:
                raise ResponseFilterError(False)
            body = cast(Json, json.loads(response.content.decode(response.charset)))
            result = self._filter.transform(
                body, {"contentType": response.get("Content-Type"), "lengthKnown": True}
            )
            content = json.dumps(
                result, ensure_ascii=False, sort_keys=True, separators=(",", ":")
            ).encode(response.charset)
            if len(content) > self._max_bytes:
                raise ResponseFilterError(False)
            response.content = content
            response["Content-Length"] = str(len(content))
            return response
        except (ValueError, UnicodeError, ResponseFilterError):
            raise ResponseFilterError(False) from None
