from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any, Iterable

from .contract import PERSONA_CONTRACT


HISTORY_SCHEMA_VERSION = 1
_ROOT_KEYS = frozenset({"schema_version", "records"})
_RECORD_REQUIRED_KEYS = frozenset(
    {
        "selected_id",
        "played_at",
        "category",
        "category_group",
        "semantic_group",
        "output_mode",
        "trigger",
        "interrupt_cost",
    }
)
_RECORD_OPTIONAL_KEYS = frozenset(
    {
        "was_dry_sharp",
        "was_seasoning",
        "surface_opening",
        "surface_ending",
        "surface_template",
        "source_tier",
    }
)
_RECORD_KEYS = _RECORD_REQUIRED_KEYS | _RECORD_OPTIONAL_KEYS
_CATEGORY_GROUPS = frozenset(
    {
        "technical",
        "growth",
        "career",
        "daily_care",
        "emotional_reflection",
        "character_life",
        "easter_egg",
        "system_ambient",
    }
)
_OUTPUT_MODES = frozenset({"self_talk", "ambient", "user_direct", "system_observe"})
_TRIGGERS = frozenset(
    {
        "any",
        "app_start",
        "morning",
        "noon",
        "afternoon",
        "evening",
        "late_night",
        "day_changed",
        "weekday",
        "weekend",
        "holiday",
        "anniversary",
        "long_silence",
        "ide_foreground",
        "long_active",
        "idle_return",
        "story_timer",
    }
)


class HistoryFormatError(ValueError):
    """Persisted selection history violates its versioned JSON contract."""


def _aware(value: datetime) -> bool:
    return isinstance(value, datetime) and value.tzinfo is not None and value.utcoffset() is not None


def _instant(value: datetime) -> datetime:
    return value.astimezone(UTC)


@dataclass(frozen=True, slots=True)
class HistoryRecord:
    selected_id: str
    played_at: datetime
    category: str
    category_group: str
    semantic_group: str
    output_mode: str
    trigger: str
    interrupt_cost: int
    was_dry_sharp: bool = False
    was_seasoning: bool | None = None
    surface_opening: str = ""
    surface_ending: str = ""
    surface_template: str = ""
    source_tier: str = "authored"

    def __post_init__(self) -> None:
        for name in ("selected_id", "category", "semantic_group"):
            value = getattr(self, name)
            if not isinstance(value, str) or not value or value != value.strip():
                raise HistoryFormatError(f"{name} must be a non-blank trimmed string")
        if not _aware(self.played_at):
            raise HistoryFormatError("played_at must be timezone-aware")
        object.__setattr__(self, "played_at", self.played_at.astimezone(UTC))
        if self.category_group not in _CATEGORY_GROUPS:
            raise HistoryFormatError("category_group is not controlled")
        if self.output_mode not in _OUTPUT_MODES:
            raise HistoryFormatError("output_mode is not controlled")
        if self.trigger not in _TRIGGERS:
            raise HistoryFormatError("trigger is not controlled")
        if self.source_tier not in {"authored", "legacy"}:
            raise HistoryFormatError("source_tier is not controlled")
        if (
            isinstance(self.interrupt_cost, bool)
            or not isinstance(self.interrupt_cost, int)
            or not 0 <= self.interrupt_cost <= 5
        ):
            raise HistoryFormatError("interrupt_cost must be an integer in [0, 5]")
        if (
            type(self.was_dry_sharp) is not bool
            or (self.was_seasoning is not None and type(self.was_seasoning) is not bool)
        ):
            raise HistoryFormatError("playback exposure flags must be boolean or legacy null")
        for name in ("surface_opening", "surface_ending", "surface_template"):
            value = getattr(self, name)
            if not isinstance(value, str) or value != value.strip():
                raise HistoryFormatError(f"{name} must be a trimmed string")


def _reject_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number {value!r}")


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def _parse_timestamp(value: object) -> datetime:
    if not isinstance(value, str) or not value:
        raise HistoryFormatError("played_at must be a non-empty ISO timestamp")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise HistoryFormatError(f"invalid played_at timestamp {value!r}") from error
    if not _aware(parsed):
        raise HistoryFormatError("played_at must be timezone-aware")
    return parsed


class SelectionHistory:
    __slots__ = ("_records",)

    def __init__(self, records: Iterable[HistoryRecord] = ()) -> None:
        self._records: list[HistoryRecord] = []
        for record in records:
            self.append(record)

    @property
    def records(self) -> tuple[HistoryRecord, ...]:
        return tuple(self._records)

    def __len__(self) -> int:
        return len(self._records)

    def append(self, record: HistoryRecord) -> None:
        if not isinstance(record, HistoryRecord):
            raise HistoryFormatError("history entries must be HistoryRecord values")
        if self._records and _instant(record.played_at) <= _instant(self._records[-1].played_at):
            raise HistoryFormatError("history timestamps must be strictly increasing")
        self._records.append(record)

    def validate_for(self, now: datetime) -> None:
        if not _aware(now):
            raise HistoryFormatError("now must be timezone-aware")
        if self._records and _instant(self._records[-1].played_at) > _instant(now):
            raise HistoryFormatError("history contains a future record")

    def to_json(self) -> str:
        value = {
            "schema_version": HISTORY_SCHEMA_VERSION,
            "records": [
                {
                    "selected_id": record.selected_id,
                    "played_at": record.played_at.isoformat(timespec="microseconds"),
                    "category": record.category,
                    "category_group": record.category_group,
                    "semantic_group": record.semantic_group,
                    "output_mode": record.output_mode,
                    "trigger": record.trigger,
                    "interrupt_cost": record.interrupt_cost,
                    "was_dry_sharp": record.was_dry_sharp,
                    "was_seasoning": record.was_seasoning,
                    "surface_opening": record.surface_opening,
                    "surface_ending": record.surface_ending,
                    "surface_template": record.surface_template,
                    "source_tier": record.source_tier,
                }
                for record in self._records
            ],
        }
        return json.dumps(
            value,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        ) + "\n"

    @classmethod
    def from_json(cls, text: str, *, now: datetime | None = None) -> SelectionHistory:
        if not isinstance(text, str):
            raise HistoryFormatError("history JSON must be text")
        try:
            value = json.loads(
                text,
                object_pairs_hook=_unique_object,
                parse_constant=_reject_constant,
            )
        except (json.JSONDecodeError, ValueError) as error:
            raise HistoryFormatError(f"invalid history JSON: {error}") from error
        if not isinstance(value, dict) or set(value) != _ROOT_KEYS:
            raise HistoryFormatError("history root must use exactly schema_version and records")
        if (
            type(value.get("schema_version")) is not int
            or value.get("schema_version") != HISTORY_SCHEMA_VERSION
        ):
            raise HistoryFormatError(f"history schema_version must be {HISTORY_SCHEMA_VERSION}")
        raw_records = value.get("records")
        if not isinstance(raw_records, list):
            raise HistoryFormatError("history records must be an array")
        records: list[HistoryRecord] = []
        for index, raw in enumerate(raw_records):
            if (
                not isinstance(raw, dict)
                or not _RECORD_REQUIRED_KEYS <= set(raw) <= _RECORD_KEYS
            ):
                raise HistoryFormatError(f"history record {index} uses unknown or missing keys")
            try:
                record = HistoryRecord(
                    selected_id=raw["selected_id"],
                    played_at=_parse_timestamp(raw["played_at"]),
                    category=raw["category"],
                    category_group=raw["category_group"],
                    semantic_group=raw["semantic_group"],
                    output_mode=raw["output_mode"],
                    trigger=raw["trigger"],
                    interrupt_cost=raw["interrupt_cost"],
                    was_dry_sharp=raw.get("was_dry_sharp", False),
                    was_seasoning=raw.get("was_seasoning"),
                    surface_opening=raw.get("surface_opening", ""),
                    surface_ending=raw.get("surface_ending", ""),
                    surface_template=raw.get("surface_template", ""),
                    source_tier=raw.get(
                        "source_tier",
                        PERSONA_CONTRACT.source_tier["missing_history_default"],
                    ),
                )
            except (KeyError, TypeError, HistoryFormatError) as error:
                if isinstance(error, HistoryFormatError):
                    raise HistoryFormatError(f"history record {index}: {error}") from error
                raise HistoryFormatError(f"history record {index} is malformed") from error
            records.append(record)
        result = cls(records)
        if now is not None:
            result.validate_for(now)
        return result
