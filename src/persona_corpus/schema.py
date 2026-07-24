from __future__ import annotations

from dataclasses import dataclass


V2_HEADER = (
    "id",
    "category",
    "category_group",
    "topic_id",
    "semantic_group",
    "output_mode",
    "trigger",
    "required_context",
    "tone",
    "interrupt_cost",
    "cooldown_hours",
    "semantic_cooldown_hours",
    "max_per_day",
    "weight",
    "requires_reply",
    "enabled",
    "text",
    "source_kind",
    "source_reference",
    "rewrite_reason",
)

ARCHIVE_HEADER = (
    "source_line",
    "category",
    "original_text",
    "archive_reason",
    "topic_id",
    "suggested_rewrite",
    "can_recover",
)

REVIEW_HEADER = (
    "review_id",
    "source_line",
    "category",
    "original_text",
    "risk_type",
    "risk_description",
    "suggested_action",
    "suggested_rewrite",
    "default_enabled",
)

PII_REVIEW_HEADER = (
    "review_id",
    "source_line",
    "category",
    "original_text",
    "pii_type",
    "risk_description",
    "suggested_action",
    "suggested_rewrite",
    "default_enabled",
)

SURFACE_MANIFEST_HEADER = (
    "line_id",
    "variant_id",
    "source_line",
    "category",
    "category_group",
    "topic_id",
    "source_reference",
    "text_sha256",
    "source_text_sha256",
)


@dataclass(frozen=True, slots=True)
class ArchiveRow:
    source_line: int
    category: str
    original_text: str
    archive_reason: str
    topic_id: str
    suggested_rewrite: str
    can_recover: bool


@dataclass(frozen=True, slots=True)
class ReviewRow:
    review_id: str
    source_line: int
    category: str
    original_text: str
    risk_type: str
    risk_description: str
    suggested_action: str
    suggested_rewrite: str
    default_enabled: bool


@dataclass(frozen=True, slots=True)
class PiiReviewRow:
    review_id: str
    source_line: int
    category: str
    original_text: str
    pii_type: str
    risk_description: str
    suggested_action: str
    suggested_rewrite: str
    default_enabled: bool


@dataclass(frozen=True, slots=True)
class SurfaceManifestRow:
    line_id: str
    variant_id: str
    source_line: int
    category: str
    category_group: str
    topic_id: str
    source_reference: str
    text_sha256: str
    source_text_sha256: str
