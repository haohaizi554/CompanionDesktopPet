from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class LegacyLine:
    source_line: int
    category: str
    text: str


@dataclass(frozen=True, slots=True)
class CorpusLine:
    id: str
    category: str
    category_group: str
    topic_id: str
    semantic_group: str
    output_mode: str
    trigger: str
    required_context: str
    tone: str
    interrupt_cost: int
    cooldown_hours: float
    semantic_cooldown_hours: float
    max_per_day: int
    weight: float
    requires_reply: bool
    enabled: bool
    text: str
    source_kind: str
    source_reference: str
    rewrite_reason: str


@dataclass(frozen=True, slots=True)
class AuditPair:
    left_source_line: int
    right_source_line: int
    similarity: float
    left_text: str
    right_text: str


@dataclass(frozen=True, slots=True)
class AuditResult:
    total_lines: int
    category_counts: dict[str, int]
    exact_duplicate_count: int
    normalized_duplicate_count: int
    question_count: int
    question_examples: list[int]
    high_risk_patterns: dict[str, int]
    high_risk_examples: dict[str, list[int]]
    catchphrase_counts: dict[str, int]
    catchphrase_examples: dict[str, list[int]]
    likely_pii_count: int
    likely_pii_examples: list[int]
    prefix_counts: dict[int, dict[str, int]]
    suffix_counts: dict[int, dict[str, int]]
    text_length_counts: dict[int, int]
    normalized_duplicate_examples: list[tuple[int, int]]
    similar_pair_count: int
    similar_pair_examples: list[AuditPair]
