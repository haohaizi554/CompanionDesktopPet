"""Compatibility facade for Persona Corpus validation.

Validation rule families live under :mod:`src.persona_corpus.validation_rules`.
This module keeps the original public and private import surface stable.
"""

from __future__ import annotations

import calendar
import hashlib
import json
import math
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence

from .contract import (
    ALLOWED_CONTEXT_TOKENS,
    CATEGORY_GROUPS,
    EXPANDED_RUNTIME_ROWS,
    FUTURE_TRIGGERS,
    MVP_TRIGGERS,
    OUTPUT_MODES,
    PERSONA_CONTRACT,
    SOURCE_KINDS,
    TONES,
    TRIGGERS,
)
from .editorial import is_exact_identity_easter_egg
from .lexical import (
    SEASONING_MARKERS as CATCHPHRASES,
    contains_seasoning_marker,
)
from .loader import CorpusFormatError, load_v2
from .models import CorpusLine
from .normalization import normalize_text
from .surface_safety import (
    TECHNICAL_DEICTIC_OBJECT_MARKERS,
    TECHNICAL_USER_ENVIRONMENT_MARKERS,
)
from .validation_rules.allowlist_rules import (
    ALLOWLISTABLE_CODES,
    ALLOWLIST_KEYS,
    _apply_allowlist,
)
from .validation_rules.config_rules import (
    CONTEXT_TOKEN_PATTERN,
    RUNTIME_LIMIT_KEYS,
    TOP_LEVEL_CONFIG_KEYS,
    _valid_int_limit,
    _validate_context_and_triggers,
    _validate_output_targets,
    _validate_runtime_limits,
    _validate_weight_map,
    validate_config,
)
from .validation_rules.content_rules import (
    COMMON_CHINESE_GIVEN_NAMES,
    COMMON_CHINESE_SURNAMES,
    CONTEXTUAL_CHINESE_NAME_PATTERN,
    DIRECT_STATE_PATTERNS,
    ID_PATTERN,
    LABELED_CHINESE_NAME_PATTERN,
    NAME_CONTEXT_MARKERS,
    PII_MARKERS,
    PII_PATTERNS,
    STRONG_EMOTION_MARKERS,
    TECHNICAL_CURRENT_PATTERNS,
    _has_identity_marker,
    _looks_like_non_identity_pii,
    _required_context_tokens,
    _trigger_context_conflict,
    _validate_line,
)
from .validation_rules.core import (
    ValidationInputError,
    ValidationIssue,
    ValidationReport,
    _Issues,
    _is_finite_number,
    _is_integer,
    _json_pairs,
    _reject_json_constant,
    load_json_object,
    normalized_text_sha256,
    scheduler_config_sha256,
)
from .validation_rules.editorial_rules import validate_dry_sharp_contract
from .validation_rules.lineage_rules import (
    LineageRegistry,
    build_repository_registry,
    validate_lineage_registry,
    validate_lineage_structure,
)
from .validation_rules.orchestration import (
    FORMAT_ERROR_CODES,
    VALIDATION_GROUPS,
    format_report,
    validate_corpus,
    validate_file,
)
from .validation_rules.safety_rules import validate_safety_preflight
from .validation_rules.schema_rules import validate_schema_contract
from .validation_rules.simulation_rules import (
    SIMULATION_ATTEMPT_KEYS,
    SIMULATION_CONTEXT_KEYS,
    SIMULATION_DAYPARTS,
    SIMULATION_EVENTS,
    SIMULATION_KEYS,
    _SimulationAttempt,
    _SimulationOutput,
    _expected_daypart,
    _parse_simulation_timestamp,
    _simulation_context_token_matches,
    _simulation_context_valid,
    _simulation_issues,
    _simulation_trigger_matches,
    _valid_optional_boolean,
)
from .validation_rules.surface_rules import (
    SURFACE_CARTESIAN_TOPIC_HARD_MAX,
    SURFACE_CATCHPHRASE_HARD_MAX,
    SURFACE_ENDING_HARD_MAX,
    SURFACE_OPENING_HARD_MAX,
    SURFACE_TOPIC_FACE_HARD_MAX,
    _cartesian_grid_issues,
    _distribution_issues,
    _has_cartesian_grid,
    _surface_inventory_issues,
)


# Preserve the historical facade identity used by introspection and pickling.
_FACADE_CALLABLES = (
    ValidationInputError,
    ValidationIssue,
    ValidationReport,
    format_report,
    load_json_object,
    normalized_text_sha256,
    scheduler_config_sha256,
    validate_config,
    validate_corpus,
    validate_file,
)
for _facade_callable in _FACADE_CALLABLES:
    _facade_callable.__module__ = __name__
del _facade_callable, _FACADE_CALLABLES


__all__ = [
    "FORMAT_ERROR_CODES",
    "VALIDATION_GROUPS",
    "ValidationInputError",
    "ValidationIssue",
    "ValidationReport",
    "format_report",
    "load_json_object",
    "normalized_text_sha256",
    "scheduler_config_sha256",
    "validate_config",
    "validate_corpus",
    "validate_file",
]
