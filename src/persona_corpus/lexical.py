from __future__ import annotations

import re
import unicodedata
from types import MappingProxyType

from .contract import PERSONA_CONTRACT


_POLICY = PERSONA_CONTRACT.lexical_exposure["seasoning"]
SEASONING_SUBSTRING_MARKERS = tuple(_POLICY["substring_markers"])
SEASONING_TOKEN_PATTERNS = MappingProxyType(
    {str(marker): str(pattern) for marker, pattern in _POLICY["token_patterns"].items()}
)
SEASONING_MARKERS = SEASONING_SUBSTRING_MARKERS + tuple(SEASONING_TOKEN_PATTERNS)
CATCHPHRASES = SEASONING_MARKERS

_NORMALIZED_SUBSTRINGS = tuple(
    (marker, unicodedata.normalize("NFKC", marker).casefold())
    for marker in SEASONING_SUBSTRING_MARKERS
)
_COMPILED_TOKEN_PATTERNS = tuple(
    (marker, re.compile(pattern, re.IGNORECASE))
    for marker, pattern in SEASONING_TOKEN_PATTERNS.items()
)


def match_seasoning_markers(text: object) -> tuple[str, ...]:
    """Return canonical seasoning markers using the shared Unicode/boundary rules."""
    if not isinstance(text, str) or not text:
        return ()
    normalized = unicodedata.normalize("NFKC", text).casefold()
    matches = [
        marker for marker, folded in _NORMALIZED_SUBSTRINGS if folded in normalized
    ]
    matches.extend(
        marker
        for marker, pattern in _COMPILED_TOKEN_PATTERNS
        if pattern.search(normalized) is not None
    )
    return tuple(matches)


def contains_seasoning_marker(text: object) -> bool:
    return bool(match_seasoning_markers(text))


__all__ = [
    "CATCHPHRASES",
    "SEASONING_MARKERS",
    "SEASONING_SUBSTRING_MARKERS",
    "SEASONING_TOKEN_PATTERNS",
    "contains_seasoning_marker",
    "match_seasoning_markers",
]
