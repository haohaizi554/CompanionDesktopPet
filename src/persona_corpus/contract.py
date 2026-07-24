from __future__ import annotations

import json
import math
import re
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
        "inventory",
        "release_inventory",
        "category_groups",
        "categories",
        "controlled_values",
        "scheduler",
        "dry_sharp",
        "lexical_exposure",
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


def _row_range(value: object, name: str) -> tuple[int, int]:
    if (
        not isinstance(value, list)
        or len(value) != 2
        or any(type(item) is not int or item <= 0 for item in value)
        or value[0] > value[1]
    ):
        raise PersonaContractError(
            f"{name} must be a two-item positive ascending integer array"
        )
    return value[0], value[1]


@dataclass(frozen=True, slots=True)
class PersonaContract:
    schema_version: int
    inventory: Mapping[str, tuple[int, int]]
    release_inventory: Mapping[str, int]
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
    lexical_exposure: Mapping[str, object]
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

    inventory_raw = _mapping(raw.get("inventory"), "inventory")
    if set(inventory_raw) != {"curated_core", "expanded_runtime"}:
        raise PersonaContractError("inventory uses an unexpected key set")
    inventory = {
        name: _row_range(value, f"inventory.{name}")
        for name, value in inventory_raw.items()
    }
    release_inventory_raw = _mapping(raw.get("release_inventory"), "release_inventory")
    expected_release_inventory = {
        "expanded_runtime_rows",
        "semantic_scene_count",
        "legacy_surface_rows",
    }
    if (
        set(release_inventory_raw) != expected_release_inventory
        or any(type(value) is not int or value <= 0 for value in release_inventory_raw.values())
    ):
        raise PersonaContractError(
            "release_inventory must contain exactly three positive integer counts"
        )
    release_inventory = {
        str(name): int(value) for name, value in release_inventory_raw.items()
    }

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
        "category_group_output_modes",
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

    group_modes = _mapping(
        scheduler.get("category_group_output_modes"),
        "scheduler.category_group_output_modes",
    )
    if set(group_modes) != set(category_groups) or any(
        not isinstance(mode, str) or mode not in output_modes
        for mode in group_modes.values()
    ):
        raise PersonaContractError(
            "scheduler category_group_output_modes must map every group to one output mode"
        )
    mode_targets = _mapping(
        scheduler.get("output_mode_targets"), "scheduler.output_mode_targets"
    )
    if set(mode_targets) != set(output_modes) or any(
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
        or float(value) < 0
        for value in mode_targets.values()
    ):
        raise PersonaContractError("scheduler output_mode_targets must cover every mode")
    aggregated = {mode: 0.0 for mode in output_modes}
    for group, weight in group_weights.items():
        aggregated[str(group_modes[group])] += float(weight)
    if any(
        abs(aggregated[mode] - float(mode_targets[mode])) > 1e-9
        for mode in output_modes
    ):
        raise PersonaContractError(
            "scheduler output_mode_targets must equal category-group weight aggregation"
        )

    dry_sharp = _mapping(raw.get("dry_sharp"), "dry_sharp")
    lexical_exposure = _mapping(raw.get("lexical_exposure"), "lexical_exposure")
    temporal = _mapping(raw.get("temporal"), "temporal")
    lineage = _mapping(raw.get("lineage"), "lineage")
    if "dry_sharp" not in tones:
        raise PersonaContractError("dry_sharp policy requires the dry_sharp controlled tone")
    expected_dry_sharp_keys = {
        "scene_hash_namespace",
        "scene_assignment_field",
        "scene_hash_threshold",
        "scene_inventory_target",
        "scene_inventory_acceptance",
        "scene_inventory_enforcement_profile",
        "bootstrap_minimum_scenes",
        "row_inventory_policy",
        "playback_target",
        "playback_acceptance",
        "recent_window",
        "recent_max",
        "forbidden_category_groups",
        "forbidden_triggers",
        "forbidden_context_tokens",
    }
    if set(dry_sharp) != expected_dry_sharp_keys:
        raise PersonaContractError("dry_sharp uses an unexpected key set")
    scene_hash_threshold = dry_sharp.get("scene_hash_threshold")
    if (
        isinstance(scene_hash_threshold, bool)
        or not isinstance(scene_hash_threshold, (int, float))
        or not math.isfinite(float(scene_hash_threshold))
        or not 0 < float(scene_hash_threshold) <= 1
    ):
        raise PersonaContractError("dry_sharp.scene_hash_threshold must be in (0, 1]")
    if (
        dry_sharp.get("scene_hash_namespace") != "persona-dry-sharp-scene-v1"
        or dry_sharp.get("scene_assignment_field") != "semantic_group"
        or dry_sharp.get("row_inventory_policy") != "observation_only"
    ):
        raise PersonaContractError(
            "dry_sharp scene assignment must be stable and row inventory observation-only"
        )
    scene_target = dry_sharp.get("scene_inventory_target")
    enforcement_profile = dry_sharp.get("scene_inventory_enforcement_profile")
    bootstrap_scenes = dry_sharp.get("bootstrap_minimum_scenes")
    if (
        isinstance(scene_target, bool)
        or not isinstance(scene_target, (int, float))
        or not math.isfinite(float(scene_target))
        or not 0 <= float(scene_target) <= 1
        or enforcement_profile != "expanded_runtime"
        or type(bootstrap_scenes) is not int
        or bootstrap_scenes <= 0
    ):
        raise PersonaContractError("dry_sharp scene inventory limits are invalid")
    scene_acceptance = dry_sharp.get("scene_inventory_acceptance")
    if (
        not isinstance(scene_acceptance, list)
        or len(scene_acceptance) != 2
        or any(
            isinstance(value, bool)
            or not isinstance(value, (int, float))
            or not math.isfinite(float(value))
            for value in scene_acceptance
        )
        or not 0 <= float(scene_acceptance[0]) <= float(scene_acceptance[1]) <= 1
    ):
        raise PersonaContractError(
            "dry_sharp.scene_inventory_acceptance must be an ascending ratio range"
        )
    if not float(scene_acceptance[0]) <= float(scene_target) <= float(scene_acceptance[1]):
        raise PersonaContractError(
            "dry_sharp.scene_inventory_target must be inside its acceptance range"
        )

    if set(lexical_exposure) != {"seasoning"}:
        raise PersonaContractError("lexical_exposure uses an unexpected key set")
    seasoning = _mapping(
        lexical_exposure.get("seasoning"), "lexical_exposure.seasoning"
    )
    expected_seasoning_keys = {
        "normalization",
        "substring_markers",
        "token_patterns",
        "identity_markers_excluded",
        "inventory_profiles",
        "playback_acceptance",
        "recent_window",
        "recent_max",
    }
    if set(seasoning) != expected_seasoning_keys:
        raise PersonaContractError("lexical seasoning uses an unexpected key set")
    substring_markers = _string_tuple(
        seasoning.get("substring_markers"), "seasoning.substring_markers"
    )
    identity_exclusions = _string_tuple(
        seasoning.get("identity_markers_excluded"),
        "seasoning.identity_markers_excluded",
    )
    token_patterns = _mapping(
        seasoning.get("token_patterns"), "seasoning.token_patterns"
    )
    if (
        seasoning.get("normalization") != "NFKC_casefold"
        or any(not isinstance(pattern, str) or not pattern for pattern in token_patterns.values())
        or set(substring_markers) & set(token_patterns)
        or (set(substring_markers) | set(token_patterns)) & set(identity_exclusions)
    ):
        raise PersonaContractError("lexical seasoning marker policy is invalid")
    try:
        for pattern in token_patterns.values():
            re.compile(str(pattern), re.IGNORECASE)
    except re.error as error:
        raise PersonaContractError(f"invalid seasoning token regex: {error}") from error
    inventory_profiles = seasoning.get("inventory_profiles")
    playback_acceptance = seasoning.get("playback_acceptance")
    recent_window = seasoning.get("recent_window")
    recent_max = seasoning.get("recent_max")
    if (
        not isinstance(playback_acceptance, list)
        or len(playback_acceptance) != 2
        or any(
            isinstance(value, bool)
            or not isinstance(value, (int, float))
            or not math.isfinite(float(value))
            for value in playback_acceptance
        )
        or not 0 <= float(playback_acceptance[0]) <= float(playback_acceptance[1]) <= 1
        or type(recent_window) is not int
        or recent_window <= 0
        or type(recent_max) is not int
        or not 0 <= recent_max <= recent_window
    ):
        raise PersonaContractError("lexical seasoning exposure limits are invalid")
    if (
        not isinstance(inventory_profiles, dict)
        or set(inventory_profiles) != {"curated_core", "expanded_runtime"}
        or inventory_profiles.get("expanded_runtime")
        != {"policy": "observation_only"}
    ):
        raise PersonaContractError("lexical seasoning inventory profiles are invalid")
    core_inventory = inventory_profiles.get("curated_core")
    if (
        not isinstance(core_inventory, dict)
        or set(core_inventory) != {"policy", "maximum"}
        or core_inventory.get("policy") != "maximum"
        or isinstance(core_inventory.get("maximum"), bool)
        or not isinstance(core_inventory.get("maximum"), (int, float))
        or not math.isfinite(float(core_inventory["maximum"]))
        or not 0 <= float(core_inventory["maximum"]) <= 1
    ):
        raise PersonaContractError("curated-core seasoning inventory policy is invalid")
    expected_lineage_keys = {
        "topic_id_role",
        "semantic_group_topic_policy",
        "editorial_variant_topic_binding",
        "surface_variant_topic_binding",
        "catalog_variant_topic_binding",
        "semantic_scene_signature_fields",
        "legacy_source_min_line",
        "legacy_source_max_line",
        "legacy_reference_pattern",
        "catalog_reference_pattern",
    }
    if set(lineage) != expected_lineage_keys:
        raise PersonaContractError("lineage uses an unexpected key set")
    if (
        lineage.get("topic_id_role") != "row_editorial_lineage"
        or lineage.get("semantic_group_topic_policy") != "may_span_topics"
        or lineage.get("editorial_variant_topic_binding") != "variant_prefix"
        or lineage.get("surface_variant_topic_binding")
        != "source_reference_topic_token"
        or lineage.get("catalog_variant_topic_binding") != "catalog_registry"
    ):
        raise PersonaContractError("lineage topic-binding policy is invalid")
    signature_fields = _string_tuple(
        lineage.get("semantic_scene_signature_fields"),
        "lineage.semantic_scene_signature_fields",
    )
    expected_signature_fields = {
        "category",
        "category_group",
        "output_mode",
        "trigger",
        "required_context",
        "tone",
        "cooldown_hours",
        "semantic_cooldown_hours",
        "max_per_day",
        "interrupt_cost",
        "weight",
        "requires_reply",
        "enabled",
    }
    if set(signature_fields) != expected_signature_fields or "topic_id" in signature_fields:
        raise PersonaContractError(
            "semantic scene signature must contain runtime metadata and exclude topic_id"
        )

    frozen_raw = _freeze(raw)
    return PersonaContract(
        schema_version=1,
        inventory=MappingProxyType(inventory),
        release_inventory=MappingProxyType(release_inventory),
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
        lexical_exposure=frozen_raw["lexical_exposure"],
        temporal=frozen_raw["temporal"],
        lineage=frozen_raw["lineage"],
        raw=frozen_raw,
    )


PERSONA_CONTRACT = load_persona_contract()
CURATED_CORE_ROWS = PERSONA_CONTRACT.inventory["curated_core"]
EXPANDED_RUNTIME_ROWS = PERSONA_CONTRACT.inventory["expanded_runtime"]
RELEASE_INVENTORY = PERSONA_CONTRACT.release_inventory
CATEGORY_GROUPS = frozenset(PERSONA_CONTRACT.category_groups)
CATEGORY_GROUP_BY_CATEGORY = PERSONA_CONTRACT.categories
OUTPUT_MODES = PERSONA_CONTRACT.output_modes
TONES = PERSONA_CONTRACT.tones
SOURCE_KINDS = PERSONA_CONTRACT.source_kinds
ALLOWED_CONTEXT_TOKENS = PERSONA_CONTRACT.context_tokens
MVP_TRIGGERS = PERSONA_CONTRACT.mvp_triggers
FUTURE_TRIGGERS = PERSONA_CONTRACT.future_triggers
TRIGGERS = MVP_TRIGGERS | FUTURE_TRIGGERS
SEMANTIC_SCENE_SIGNATURE_FIELDS = tuple(
    PERSONA_CONTRACT.lineage["semantic_scene_signature_fields"]
)


def category_group_for(category: str) -> str:
    try:
        return CATEGORY_GROUP_BY_CATEGORY[category]
    except (KeyError, TypeError) as error:
        raise PersonaContractError(f"unknown persona category {category!r}") from error


__all__ = [
    "ALLOWED_CONTEXT_TOKENS",
    "CATEGORY_GROUP_BY_CATEGORY",
    "CATEGORY_GROUPS",
    "CURATED_CORE_ROWS",
    "DEFAULT_CONTRACT_PATH",
    "FUTURE_TRIGGERS",
    "EXPANDED_RUNTIME_ROWS",
    "MVP_TRIGGERS",
    "OUTPUT_MODES",
    "PERSONA_CONTRACT",
    "RELEASE_INVENTORY",
    "PersonaContract",
    "PersonaContractError",
    "SOURCE_KINDS",
    "SEMANTIC_SCENE_SIGNATURE_FIELDS",
    "TONES",
    "TRIGGERS",
    "category_group_for",
    "load_persona_contract",
]
