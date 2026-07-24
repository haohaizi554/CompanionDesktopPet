from __future__ import annotations

import json
import hashlib
import math
import re
from dataclasses import dataclass
from pathlib import Path
from types import MappingProxyType
from typing import Any, Mapping

from .models import CorpusLine


DEFAULT_EDITORIAL_MANIFEST_PATH = (
    Path(__file__).resolve().parents[2] / "config" / "persona-editorial-manifest.json"
)
_TOP_LEVEL_KEYS = frozenset(
    {
        "schema_version",
        "catalog_variant_decisions",
        "identity_policy",
        "identity_easter_eggs",
    }
)
_IDENTITY_KEYS = frozenset(
    {
        "variant_id",
        "source_line",
        "source_reference",
        "text_sha256",
        "allowed_markers",
        "category",
        "category_group",
        "cooldown_hours",
        "max_per_day",
        "weight",
        "text",
    }
)
_IDENTITY_OPTIONAL_KEYS = frozenset({"topic_id"})
_POLICY_KEYS = frozenset(
    {
        "allowed_markers",
        "forbidden_markers",
        "required_category",
        "required_category_group",
        "minimum_cooldown_hours",
        "max_per_day",
        "maximum_weight",
    }
)
_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")
_HASH = re.compile(r"^[0-9a-f]{64}$")
_LEGACY_REFERENCE = re.compile(
    r"^legacy:(\d+);topic:([A-Za-z0-9._-]+);variant:([A-Za-z0-9._-]+)$"
)
_CATALOG_REFERENCE = re.compile(
    r"^catalog:([A-Za-z0-9._:-]+);variant:([A-Za-z0-9._-]+)$"
)


class EditorialManifestError(ValueError):
    """The machine-readable editorial adjudication manifest is invalid."""


def _json_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise EditorialManifestError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def _reject_constant(value: str) -> None:
    raise EditorialManifestError(f"non-finite JSON number {value!r}")


def _string_tuple(value: object, name: str) -> tuple[str, ...]:
    if (
        not isinstance(value, list)
        or not value
        or any(not isinstance(item, str) or not item for item in value)
        or len(value) != len(set(value))
    ):
        raise EditorialManifestError(f"{name} must be a non-empty unique string array")
    return tuple(value)


@dataclass(frozen=True, slots=True)
class IdentityEasterEggAdjudication:
    line_id: str
    variant_id: str
    source_line: int | None
    source_reference: str
    topic_id: str
    text_sha256: str
    allowed_markers: tuple[str, ...]
    category: str
    category_group: str
    cooldown_hours: float
    max_per_day: int
    weight: float
    text: str


@dataclass(frozen=True, slots=True)
class EditorialManifest:
    schema_version: int
    adjudicated_variants: tuple[str, ...]
    retired_variants: tuple[str, ...]
    allowed_identity_markers: tuple[str, ...]
    forbidden_identity_markers: tuple[str, ...]
    identity_easter_eggs: Mapping[str, IdentityEasterEggAdjudication]


def load_editorial_manifest(
    path: Path = DEFAULT_EDITORIAL_MANIFEST_PATH,
) -> EditorialManifest:
    path = Path(path)
    try:
        raw = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_json_pairs,
            parse_constant=_reject_constant,
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise EditorialManifestError(f"{path}: invalid editorial manifest: {error}") from error
    if not isinstance(raw, dict) or set(raw) != _TOP_LEVEL_KEYS:
        raise EditorialManifestError(
            f"editorial manifest must contain exactly {sorted(_TOP_LEVEL_KEYS)!r}"
        )
    if type(raw.get("schema_version")) is not int or raw["schema_version"] != 1:
        raise EditorialManifestError("schema_version must be integer 1")

    decisions = raw.get("catalog_variant_decisions")
    if not isinstance(decisions, dict) or set(decisions) != {"adjudicated", "retired"}:
        raise EditorialManifestError("catalog_variant_decisions has an unexpected key set")
    adjudicated = _string_tuple(decisions["adjudicated"], "adjudicated variants")
    retired = _string_tuple(decisions["retired"], "retired variants")
    if set(adjudicated) & set(retired):
        raise EditorialManifestError("adjudicated and retired variants must be disjoint")
    if any(_ID.fullmatch(item) is None for item in (*adjudicated, *retired)):
        raise EditorialManifestError("catalog decision variant IDs use invalid syntax")

    policy = raw.get("identity_policy")
    if not isinstance(policy, dict) or set(policy) != _POLICY_KEYS:
        raise EditorialManifestError("identity_policy has an unexpected key set")
    allowed = _string_tuple(policy["allowed_markers"], "allowed identity markers")
    forbidden = _string_tuple(policy["forbidden_markers"], "forbidden identity markers")
    if set(allowed) & set(forbidden):
        raise EditorialManifestError("allowed and forbidden identity markers must be disjoint")
    required_category = policy.get("required_category")
    required_group = policy.get("required_category_group")
    minimum_cooldown = policy.get("minimum_cooldown_hours")
    maximum_weight = policy.get("maximum_weight")
    required_max_per_day = policy.get("max_per_day")
    if (
        not isinstance(required_category, str)
        or not required_category
        or not isinstance(required_group, str)
        or not required_group
        or isinstance(minimum_cooldown, bool)
        or not isinstance(minimum_cooldown, (int, float))
        or not math.isfinite(float(minimum_cooldown))
        or isinstance(maximum_weight, bool)
        or not isinstance(maximum_weight, (int, float))
        or not math.isfinite(float(maximum_weight))
        or type(required_max_per_day) is not int
    ):
        raise EditorialManifestError("identity_policy contains invalid limits")

    identities = raw.get("identity_easter_eggs")
    if not isinstance(identities, dict) or not identities:
        raise EditorialManifestError("identity_easter_eggs must be a non-empty object")
    parsed: dict[str, IdentityEasterEggAdjudication] = {}
    for line_id, value in identities.items():
        if (
            _ID.fullmatch(line_id) is None
            or not isinstance(value, dict)
            or not _IDENTITY_KEYS <= set(value) <= _IDENTITY_KEYS | _IDENTITY_OPTIONAL_KEYS
        ):
            raise EditorialManifestError(f"identity adjudication {line_id!r} is malformed")
        variant_id = value.get("variant_id")
        source_line = value.get("source_line")
        reference = value.get("source_reference")
        topic_id = value.get("topic_id")
        digest = value.get("text_sha256")
        markers = _string_tuple(value.get("allowed_markers"), f"{line_id}.allowed_markers")
        cooldown = value.get("cooldown_hours")
        max_per_day = value.get("max_per_day")
        weight = value.get("weight")
        text = value.get("text")
        if (
            not isinstance(variant_id, str)
            or _ID.fullmatch(variant_id) is None
            or (source_line is not None and (type(source_line) is not int or source_line <= 0))
            or not isinstance(reference, str)
            or (topic_id is not None and (not isinstance(topic_id, str) or _ID.fullmatch(topic_id) is None))
            or not isinstance(digest, str)
            or _HASH.fullmatch(digest) is None
            or not set(markers) <= set(allowed)
            or value.get("category") != required_category
            or value.get("category_group") != required_group
            or isinstance(cooldown, bool)
            or not isinstance(cooldown, (int, float))
            or float(cooldown) < float(minimum_cooldown)
            or max_per_day != required_max_per_day
            or isinstance(weight, bool)
            or not isinstance(weight, (int, float))
            or float(weight) > float(maximum_weight)
            or not isinstance(text, str)
        ):
            raise EditorialManifestError(f"identity adjudication {line_id!r} violates policy")
        legacy = _LEGACY_REFERENCE.fullmatch(reference)
        catalog = _CATALOG_REFERENCE.fullmatch(reference)
        if (legacy is None) == (catalog is None):
            raise EditorialManifestError(f"identity adjudication {line_id!r} has invalid lineage")
        reference_variant = (legacy or catalog).group(3 if legacy else 2)  # type: ignore[union-attr]
        if reference_variant != variant_id:
            raise EditorialManifestError(f"identity adjudication {line_id!r} variant mismatch")
        if legacy is not None and source_line != int(legacy.group(1)):
            raise EditorialManifestError(f"identity adjudication {line_id!r} source mismatch")
        if legacy is not None and topic_id is not None and topic_id != legacy.group(2):
            raise EditorialManifestError(f"identity adjudication {line_id!r} topic mismatch")
        if catalog is not None and (source_line is not None or not text or topic_id is None):
            raise EditorialManifestError(f"catalog identity {line_id!r} needs exact authored text")
        bound_topic_id = legacy.group(2) if legacy is not None else topic_id
        assert isinstance(bound_topic_id, str)
        if text:
            actual = hashlib.sha256(text.encode("utf-8")).hexdigest()
            if actual != digest:
                raise EditorialManifestError(f"identity adjudication {line_id!r} text hash mismatch")
        parsed[line_id] = IdentityEasterEggAdjudication(
            line_id=line_id,
            variant_id=variant_id,
            source_line=source_line,
            source_reference=reference,
            topic_id=bound_topic_id,
            text_sha256=digest,
            allowed_markers=markers,
            category=required_category,
            category_group=required_group,
            cooldown_hours=float(cooldown),
            max_per_day=int(max_per_day),
            weight=float(weight),
            text=text,
        )

    return EditorialManifest(
        schema_version=1,
        adjudicated_variants=adjudicated,
        retired_variants=retired,
        allowed_identity_markers=allowed,
        forbidden_identity_markers=forbidden,
        identity_easter_eggs=MappingProxyType(parsed),
    )


EDITORIAL_MANIFEST = load_editorial_manifest()


def is_exact_identity_easter_egg(row: CorpusLine) -> bool:
    """Return true only for a row bound byte-for-byte to an adjudicated identity egg."""
    item = EDITORIAL_MANIFEST.identity_easter_eggs.get(row.id)
    if item is None:
        return False
    digest = hashlib.sha256(row.text.encode("utf-8")).hexdigest()
    marker_hits = {
        marker for marker in EDITORIAL_MANIFEST.allowed_identity_markers if marker in row.text
    }
    return (
        row.category == item.category
        and row.category_group == item.category_group
        and row.source_reference == item.source_reference
        and row.topic_id == item.topic_id
        and digest == item.text_sha256
        and marker_hits == set(item.allowed_markers)
        and not any(
            marker in row.text
            for marker in EDITORIAL_MANIFEST.forbidden_identity_markers
        )
        and float(row.cooldown_hours) == item.cooldown_hours
        and row.max_per_day == item.max_per_day
        and float(row.weight) == item.weight
    )


__all__ = [
    "DEFAULT_EDITORIAL_MANIFEST_PATH",
    "EDITORIAL_MANIFEST",
    "EditorialManifest",
    "EditorialManifestError",
    "IdentityEasterEggAdjudication",
    "is_exact_identity_easter_egg",
    "load_editorial_manifest",
]
