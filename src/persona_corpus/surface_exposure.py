from __future__ import annotations

import unicodedata
from dataclasses import dataclass


SURFACE_RECENT_WINDOW = 20
OPENING_WIDTH = 4
ENDING_WIDTH = 6
TEMPLATE_OPENING_WIDTH = 2
TEMPLATE_ENDING_WIDTH = 4


@dataclass(frozen=True, slots=True)
class SurfaceExposure:
    opening: str
    ending: str
    template: str


def _normalized_surface_text(text: object) -> str:
    if not isinstance(text, str):
        return ""
    normalized = unicodedata.normalize("NFKC", text).casefold()
    return "".join(
        character
        for character in normalized
        if not character.isspace()
        and not unicodedata.category(character).startswith(("P", "C", "Z"))
    )


def surface_exposure(text: object) -> SurfaceExposure:
    """Build stable playback-only keys; these never affect corpus identity."""

    normalized = _normalized_surface_text(text)
    if not normalized:
        return SurfaceExposure("", "", "")
    opening = normalized[:OPENING_WIDTH]
    ending = normalized[-ENDING_WIDTH:]
    template = (
        normalized[:TEMPLATE_OPENING_WIDTH]
        + "|"
        + normalized[-TEMPLATE_ENDING_WIDTH:]
    )
    return SurfaceExposure(opening, ending, template)


__all__ = [
    "ENDING_WIDTH",
    "OPENING_WIDTH",
    "SURFACE_RECENT_WINDOW",
    "SurfaceExposure",
    "surface_exposure",
]
