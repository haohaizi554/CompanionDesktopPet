from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path
from types import MappingProxyType
from typing import Any, Mapping


DEFAULT_CONTRACT_PATH = (
    Path(__file__).resolve().parents[2] / "config" / "persona-contract.json"
)
_TOP_LEVEL_KEYS = frozenset(
    {
        "schema_version",
        "category_groups",
        "categories",
        "controlled_values",
        "scheduler",
        "dry_sharp",
        "temporal",
        "lineage",
    }
)


class PersonaContractError(ValueError):
    """The shared persona contract is malformed or internally inconsistent."""


def _json_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise PersonaContractError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def _reject_constant(value: str) -> None:
    raise PersonaContractError(f"non-finite JSON number {value!r}")


def _freeze(value: Any) -> Any:
    if isinstance(value, dict):
        return MappingProxyType({str(key): _freeze(item) for key, item in value.items()})
    if isinstance(value, list):
        return tuple(_freeze(item) for item in value)
    return value


def _string_tuple(value: object, name: str) -> tuple[str, ...]:
    if (
        not isinstance(value, list)
        or not value
        or any(not isinstance(item, str) or not item for item in value)
        or len(value) != len(set(value))
    ):
        raise PersonaContractError(f"{name} must be a non-empty unique string array")
    return tuple(value)


def _mapping(value: object, name: str) -> dict[str, Any]:
    if not isinstance(value, dict) or not value or any(not isinstance(key, str) for key in value):
        raise PersonaContractError(f"{name} must be a non-empty JSON object")
    return value


@dataclass(frozen=True, slots=True)
class PersonaContract:
    schema_version: int
    category_groups: tuple[str, ...]
    categories: Mapping[str, str]
    output_modes: frozenset[str]
    tones: frozenset[str]
    source_kinds: frozenset[str]
    context_tokens: frozenset[str]
    mvp_triggers: frozenset[str]
    future_triggers: frozenset[str]
    scheduler: Mapping[str, object]
    dry_sharp: Mapping[str, object]
    temporal: Mapping[str, object]
    lineage: Mapping[str, object]
    raw: Mapping[str, object]


def load_persona_contract(path: Path = DEFAULT_CONTRACT_PATH) -> PersonaContract:
    path = Path(path)
    try:
        raw = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_json_pairs,
            parse_constant=_reject_constant,
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise PersonaContractError(f"{path}: invalid persona contract: {error}") from error
    if not isinstance(raw, dict) or set(raw) != _TOP_LEVEL_KEYS:
        raise PersonaContractError(
            f"persona contract must contain exactly {sorted(_TOP_LEVEL_KEYS)!r}"
        )
    if type(raw.get("schema_version")) is not int or raw["schema_version"] != 1:
        raise PersonaContractError("schema_version must be integer 1")

    category_groups = _string_tuple(raw.get("category_groups"), "category_groups")
    categories_raw = _mapping(raw.get("categories"), "categories")
    if any(
        not isinstance(category, str)
        or not category
        or not isinstance(group, str)
        or group not in category_groups
        for category, group in categories_raw.items()
    ):
        raise PersonaContractError("every category must map to one declared category_group")
    if set(categories_raw.values()) != set(category_groups):
        raise PersonaContractError("categories must cover every declared category_group")

    controlled = _mapping(raw.get("controlled_values"), "controlled_values")
    expected_controlled = {
        "output_modes",
        "tones",
        "source_kinds",
        "context_tokens",
        "mvp_triggers",
        "future_triggers",
    }
    if set(controlled) != expected_controlled:
        raise PersonaContractError("controlled_values uses an unexpected key set")
    output_modes = frozenset(_string_tuple(controlled["output_modes"], "output_modes"))
    tones = frozenset(_string_tuple(controlled["tones"], "tones"))
    source_kinds = frozenset(_string_tuple(controlled["source_kinds"], "source_kinds"))
    context_tokens = frozenset(
        _string_tuple(controlled["context_tokens"], "context_tokens")
    )
    mvp_triggers = frozenset(
        _string_tuple(controlled["mvp_triggers"], "mvp_triggers")
    )
    future_triggers = frozenset(
        _string_tuple(controlled["future_triggers"], "future_triggers")
    )
    if mvp_triggers & future_triggers:
        raise PersonaContractError("MVP and future trigger partitions must be disjoint")

    scheduler = _mapping(raw.get("scheduler"), "scheduler")
    if set(scheduler) != {
        "category_group_weights",
        "output_mode_targets",
        "runtime_limits",
        "acceptance",
    }:
        raise PersonaContractError("scheduler uses an unexpected key set")
    group_weights = _mapping(
        scheduler.get("category_group_weights"), "scheduler.category_group_weights"
    )
    if set(group_weights) != set(category_groups) or any(
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
        or float(value) < 0
        for value in group_weights.values()
    ):
        raise PersonaContractError("scheduler weights must cover every category_group")
    if abs(sum(float(value) for value in group_weights.values()) - 1.0) > 1e-9:
        raise PersonaContractError("scheduler category_group weights must sum to 1.0")

    dry_sharp = _mapping(raw.get("dry_sharp"), "dry_sharp")
    temporal = _mapping(raw.get("temporal"), "temporal")
    lineage = _mapping(raw.get("lineage"), "lineage")
    if "dry_sharp" not in tones:
        raise PersonaContractError("dry_sharp policy requires the dry_sharp controlled tone")

    frozen_raw = _freeze(raw)
    return PersonaContract(
        schema_version=1,
        category_groups=category_groups,
        categories=MappingProxyType(
            {str(category): str(group) for category, group in categories_raw.items()}
        ),
        output_modes=output_modes,
        tones=tones,
        source_kinds=source_kinds,
        context_tokens=context_tokens,
        mvp_triggers=mvp_triggers,
        future_triggers=future_triggers,
        scheduler=frozen_raw["scheduler"],
        dry_sharp=frozen_raw["dry_sharp"],
        temporal=frozen_raw["temporal"],
        lineage=frozen_raw["lineage"],
        raw=frozen_raw,
    )


PERSONA_CONTRACT = load_persona_contract()
CATEGORY_GROUPS = frozenset(PERSONA_CONTRACT.category_groups)
CATEGORY_GROUP_BY_CATEGORY = PERSONA_CONTRACT.categories
OUTPUT_MODES = PERSONA_CONTRACT.output_modes
TONES = PERSONA_CONTRACT.tones
SOURCE_KINDS = PERSONA_CONTRACT.source_kinds
ALLOWED_CONTEXT_TOKENS = PERSONA_CONTRACT.context_tokens
MVP_TRIGGERS = PERSONA_CONTRACT.mvp_triggers
FUTURE_TRIGGERS = PERSONA_CONTRACT.future_triggers
TRIGGERS = MVP_TRIGGERS | FUTURE_TRIGGERS


def category_group_for(category: str) -> str:
    try:
        return CATEGORY_GROUP_BY_CATEGORY[category]
    except (KeyError, TypeError) as error:
        raise PersonaContractError(f"unknown persona category {category!r}") from error


__all__ = [
    "ALLOWED_CONTEXT_TOKENS",
    "CATEGORY_GROUP_BY_CATEGORY",
    "CATEGORY_GROUPS",
    "DEFAULT_CONTRACT_PATH",
    "FUTURE_TRIGGERS",
    "MVP_TRIGGERS",
    "OUTPUT_MODES",
    "PERSONA_CONTRACT",
    "PersonaContract",
    "PersonaContractError",
    "SOURCE_KINDS",
    "TONES",
    "TRIGGERS",
    "category_group_for",
    "load_persona_contract",
]
