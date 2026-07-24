from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime
from typing import Mapping, Sequence

from ..context import PersonaContext
from ..models import CorpusLine
from .constraints import AdversarialSuiteResult
from .metrics import DistributionPolicy
from .scenarios import InventoryCoverage, ScenarioCoverage


def combine_hard_violations(
    natural: Sequence[str],
    adversarial: Sequence[str],
) -> list[str]:
    """A run is clean only when both independent suites are clean."""

    return sorted(set(natural) | set(adversarial))


@dataclass(frozen=True, slots=True)
class SimulationAttempt:
    seed: int
    attempted_at: datetime
    context: PersonaContext
    row: CorpusLine | None

    @property
    def selected_id(self) -> str | None:
        return self.row.id if self.row is not None else None

    def context_payload(self) -> dict[str, object]:
        return {
            "event": self.context.event,
            "daypart": self.context.daypart,
            "weekday": self.context.weekday,
            "is_weekend": self.context.is_weekend,
            "holiday": self.context.holiday,
            "anniversary_days": self.context.anniversary_days,
            "minutes_since_last_output": float(self.context.minutes_since_last_output),
            "ide_foreground": self.context.ide_foreground,
            "active_minutes": self.context.active_minutes,
            "idle_return": self.context.idle_return,
            "fullscreen": self.context.fullscreen,
        }

    def validation_payload(self) -> dict[str, object]:
        return {
            "seed": self.seed,
            "attempted_at": self.attempted_at.isoformat(timespec="seconds"),
            "context": self.context_payload(),
            "selected_id": self.selected_id,
        }


@dataclass(frozen=True, slots=True)
class SeedMetrics:
    seed: int
    attempts: int
    outputs: int
    none_count: int
    group_counts: Mapping[str, int]
    group_ratio: Mapping[str, float]
    mode_counts: Mapping[str, int]
    mode_ratio: Mapping[str, float]
    anomalies: tuple[str, ...]


@dataclass(slots=True)
class SimulationReport:
    schema_version: int
    corpus_sha256: str
    scheduler_config_sha256: str
    subseed_derivation_version: str
    distribution_policy: DistributionPolicy
    scenario_coverage: ScenarioCoverage
    inventory_coverage: InventoryCoverage
    adversarial_result: AdversarialSuiteResult
    days: int
    seeds: tuple[int, ...]
    attempts: tuple[SimulationAttempt, ...]
    total_attempts: int
    output_count: int
    none_count: int
    average_outputs_per_day: float
    max_outputs_per_hour: int
    group_counts: dict[str, int]
    group_ratio: dict[str, float]
    mode_counts: dict[str, int]
    mode_ratio: dict[str, float]
    technical_ratio: float
    easter_egg_ratio: float
    user_direct_ratio: float
    id_cooldown_repeats: int
    semantic_cooldown_repeats: int
    adjacent_same_category_group: int
    adjacent_technical: int
    adjacent_daily_care: int
    adjacent_emotional_reflection: int
    adjacent_care: int
    average_text_length: float
    length_distribution: dict[str, float]
    common_openings: dict[int, list[tuple[str, int]]]
    common_endings: dict[int, list[tuple[str, int]]]
    catchphrase_ratio: float
    catchphrase_counts: dict[str, int]
    question_count: int
    unmet_context_count: int
    per_seed: dict[int, SeedMetrics]
    per_seed_anomalies: dict[int, list[str]]
    natural_hard_violations: list[str]
    adversarial_hard_violations: list[str]
    hard_violations: list[str]

    def to_validation_payload(self) -> dict[str, object]:
        return {
            "schema_version": int(self.schema_version),
            "corpus_sha256": self.corpus_sha256,
            "scheduler_config_sha256": self.scheduler_config_sha256,
            "days": self.days,
            "seeds": list(self.seeds),
            "attempts": [attempt.validation_payload() for attempt in self.attempts],
        }

    def to_validation_json(self) -> bytes:
        return (
            json.dumps(
                self.to_validation_payload(),
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            )
            + "\n"
        ).encode("utf-8")

