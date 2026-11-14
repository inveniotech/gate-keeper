"""Atomic filtering of an already parsed, host-bounded JSON tree."""

from collections.abc import Sequence
from copy import deepcopy
from typing import NotRequired, TypedDict

from .runtime import RuntimeMatcher

Json = None | bool | int | float | str | list["Json"] | dict[str, "Json"]


class HostState(TypedDict):
    """Host facts; protected responses require an explicit JSON content type."""

    lengthKnown: NotRequired[bool]
    headersCommitted: NotRequired[bool]
    contentType: NotRequired[str | None]


class ResponseFilterError(Exception):
    """Protected response failed; the host must abort when abort_required is true."""

    def __init__(self, abort_required: bool) -> None:
        super().__init__(
            "The protected response could not be filtered safely. Do not send its body."
        )
        self.abort_required = abort_required


class ResponseFilter:
    """Select explicit pointers and array wildcards on a private copy before redaction."""

    def __init__(self, runtime: RuntimeMatcher, paths: Sequence[str]) -> None:
        self._runtime = runtime
        parsed: list[tuple[tuple[str, bool], ...]] = []
        for pointer in paths:
            if not isinstance(pointer, str) or pointer and not pointer.startswith("/"):
                raise ValueError("Use a JSON Pointer beginning with a slash.")
            parts: list[tuple[str, bool]] = []
            for raw in pointer[1:].split("/") if pointer else []:
                key = ""
                at = 0
                while at < len(raw):
                    if raw[at] == "~":
                        if at + 1 >= len(raw) or raw[at + 1] not in "012":
                            raise ValueError("Use only ~0, ~1 or ~2 in protected JSON paths.")
                        key += {"0": "~", "1": "/", "2": "*"}[raw[at + 1]]
                        at += 2
                    else:
                        key += raw[at]
                        at += 1
                parts.append((key, raw == "*"))
            parsed.append(tuple(parts))
        self._paths = tuple(parsed)

    def transform(self, body: Json, state: HostState | None = None) -> Json:
        """Return the complete transformed copy or raise before exposing partial output."""
        host = state or {}
        if not self._paths:
            return deepcopy(body)
        abort = host.get("headersCommitted", False)
        media = (host.get("contentType") or "").split(";")[0].strip().lower()
        if (
            not host.get("lengthKnown", True)
            or abort
            or not (
                media == "application/json"
                or media.startswith("application/")
                and media.endswith("+json")
            )
        ):
            raise ResponseFilterError(abort)
        holder: dict[str, Json] = {"body": deepcopy(body)}
        slots: list[tuple[dict[str, Json] | list[Json], str | int]] = []

        def get(parent: dict[str, Json] | list[Json], key: str | int) -> Json:
            if isinstance(parent, list) and isinstance(key, int):
                return parent[key]
            if isinstance(parent, dict) and isinstance(key, str):
                return parent[key]
            raise ResponseFilterError(abort)

        def select(
            parent: dict[str, Json] | list[Json],
            key: str | int,
            path: tuple[tuple[str, bool], ...],
            depth: int,
        ) -> None:
            if depth == len(path):
                if not any(p is parent and k == key for p, k in slots):
                    slots.append((parent, key))
                return
            node = get(parent, key)
            segment, wildcard = path[depth]
            if wildcard:
                if not isinstance(node, list):
                    raise ResponseFilterError(abort)
                for index in range(len(node)):
                    select(node, index, path, depth + 1)
            elif isinstance(node, list):
                if (
                    not segment
                    or any(c not in "0123456789" for c in segment)
                    or len(segment) > 1
                    and segment[0] == "0"
                    or len(segment) > 16
                    or int(segment) >= len(node)
                ):
                    raise ResponseFilterError(abort)
                select(node, int(segment), path, depth + 1)
            elif isinstance(node, dict):
                if segment in node:
                    select(node, segment, path, depth + 1)
            else:
                raise ResponseFilterError(abort)

        try:
            for path in self._paths:
                select(holder, "body", path, 0)
            for parent, key in slots:
                value = get(parent, key)
                if not isinstance(value, str):
                    raise ResponseFilterError(abort)
                redacted = self._runtime.redact(value)
                if isinstance(parent, list) and isinstance(key, int):
                    parent[key] = redacted
                elif isinstance(parent, dict) and isinstance(key, str):
                    parent[key] = redacted
        except Exception as error:
            raise ResponseFilterError(abort) from error
        return holder["body"]
