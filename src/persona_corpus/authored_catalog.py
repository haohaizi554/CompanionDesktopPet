"""Strict loader for the literal, hash-bound authored persona source.

This module deliberately does not import the legacy content catalog or any
surface-variant machinery.  It reads the independently reviewable authored
TSV batches only and makes their provenance explicit before a future builder
turns them into runtime rows.
"""

from __future__ import annotations

import hashlib
import json
import math
import re
from dataclasses import dataclass, field
from pathlib import Path
from types import MappingProxyType
from typing import Any, Iterator, Mapping

from .contract import PersonaContractError, category_group_for
from .schema import AUTHORED_HEADER, AUTHORED_LEDGER_HEADER


AUTHORED_MANIFEST_FORMAT = "persona-authorship-manifest-v1"
EXPECTED_BATCH_IDS = tuple(f"b{number:03d}" for number in range(1, 101))
ROWS_PER_BATCH = 300
EXPECTED_ENTRY_COUNT = len(EXPECTED_BATCH_IDS) * ROWS_PER_BATCH
APPROVED_REVIEW_STATUS = "approved"
RELATIONSHIP_PROFILES = frozenset(
    {"neutral", "warm_friend", "playful_friend", "nickname_easter_egg"}
)

_MANIFEST_KEYS = frozenset(
    {
        "format",
        "authored_header",
        "batch_count",
        "rows_per_batch",
        "total_rows",
        "batches",
        "root_sha256",
    }
)
_BATCH_MANIFEST_KEYS = frozenset({"row_count", "text_sha256", "metadata_sha256"})
_SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
_VARIANT_ID_PATTERN = re.compile(
    r"^authored\.(b\d{3})\.(?:[a-z0-9]+(?:[._-][a-z0-9]+)*)$"
)
_UTF8_BOM = b"\xef\xbb\xbf"
_TEXT_INDEX = AUTHORED_HEADER.index("text")


class AuthoredCatalogError(ValueError):
    """An authored source batch or authorship manifest is malformed."""


@dataclass(frozen=True, slots=True)
class AuthoredEntry:
    variant_id: str
    batch_id: str
    category: str
    category_group: str
    topic_id: str
    editorial_role: str
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
    relationship_profile: str
    text: str
    review_status: str
    _source_fields: tuple[str, ...] = field(repr=False, compare=False)

    @property
    def source_fields(self) -> tuple[str, ...]:
        """Return the exact UTF-8 TSV values that are covered by digests."""

        return self._source_fields


@dataclass(frozen=True, slots=True)
class AuthoredBatchDigest:
    row_count: int
    text_sha256: str
    metadata_sha256: str


@dataclass(frozen=True, slots=True)
class AuthorshipLedgerRow:
    batch_id: str
    variant_id: str
    text_sha256: str
    metadata_sha256: str
    review_status: str
    relationship_profile: str
    root_sha256: str

    def values(self) -> tuple[str, ...]:
        return (
            self.batch_id,
            self.variant_id,
            self.text_sha256,
            self.metadata_sha256,
            self.review_status,
            self.relationship_profile,
            self.root_sha256,
        )


@dataclass(frozen=True, slots=True)
class AuthoredCatalog:
    entries: tuple[AuthoredEntry, ...]
    batch_digests: Mapping[str, AuthoredBatchDigest]
    root_sha256: str

    def ledger_rows(self) -> Iterator[AuthorshipLedgerRow]:
        """Yield exactly one hash-bound provenance row for every source row."""

        seen_variant_ids: set[str] = set()
        for entry in self.entries:
            if entry.variant_id in seen_variant_ids:
                raise AuthoredCatalogError(
                    f"ledger has duplicate variant_id {entry.variant_id!r}"
                )
            seen_variant_ids.add(entry.variant_id)
            yield AuthorshipLedgerRow(
                batch_id=entry.batch_id,
                variant_id=entry.variant_id,
                text_sha256=_entry_text_sha256(entry),
                metadata_sha256=_entry_metadata_sha256(entry),
                review_status=entry.review_status,
                relationship_profile=entry.relationship_profile,
                root_sha256=self.root_sha256,
            )


def _error(path: Path, line_number: int | None, detail: str) -> AuthoredCatalogError:
    location = str(path) if line_number is None else f"{path}: line {line_number}"
    return AuthoredCatalogError(f"{location}: {detail}")


def _read_tsv_rows(path: Path) -> tuple[tuple[str, ...], ...]:
    try:
        payload = path.read_bytes()
    except OSError as error:
        raise _error(path, None, str(error)) from error
    if payload.startswith(_UTF8_BOM):
        raise _error(path, 1, "UTF-8 BOM is not allowed")
    if b"\x00" in payload:
        raise _error(path, None, "NUL byte is not allowed")

    physical_lines = payload.split(b"\n")
    if physical_lines and physical_lines[-1] == b"":
        physical_lines.pop()
    if not physical_lines:
        raise _error(path, 1, "missing authored TSV header")

    rows: list[tuple[str, ...]] = []
    for line_number, raw_line in enumerate(physical_lines, start=1):
        if raw_line.endswith(b"\r"):
            raw_line = raw_line[:-1]
        try:
            line = raw_line.decode("utf-8", errors="strict")
        except UnicodeError as error:
            raise _error(path, line_number, f"invalid UTF-8: {error}") from error
        if "\r" in line:
            raise _error(path, line_number, "unexpected carriage return")
        if not line:
            raise _error(path, line_number, "blank physical row is not allowed")
        rows.append(tuple(line.split("\t")))
    return tuple(rows)


def _require_nonempty(path: Path, line_number: int, name: str, value: str) -> None:
    if not value or value != value.strip():
        raise _error(path, line_number, f"{name} must be non-empty and trimmed")
    if "\x00" in value or "\n" in value or "\r" in value or "\t" in value:
        raise _error(path, line_number, f"{name} contains a forbidden control character")


def _integer(path: Path, line_number: int, name: str, value: str, minimum: int) -> int:
    try:
        parsed = int(value)
    except ValueError as error:
        raise _error(path, line_number, f"{name} must be an integer") from error
    if str(parsed) != value or parsed < minimum:
        raise _error(path, line_number, f"{name} must be an integer >= {minimum}")
    return parsed


def _number(path: Path, line_number: int, name: str, value: str, minimum: float) -> float:
    try:
        parsed = float(value)
    except ValueError as error:
        raise _error(path, line_number, f"{name} must be a finite number") from error
    if not math.isfinite(parsed) or parsed < minimum:
        raise _error(path, line_number, f"{name} must be a finite number >= {minimum}")
    return parsed


def _parse_entry(path: Path, line_number: int, values: tuple[str, ...]) -> AuthoredEntry:
    if len(values) != len(AUTHORED_HEADER):
        raise _error(
            path,
            line_number,
            f"expected {len(AUTHORED_HEADER)} TSV columns, found {len(values)}",
        )
    row = dict(zip(AUTHORED_HEADER, values, strict=True))
    for name, value in row.items():
        _require_nonempty(path, line_number, name, value)

    batch_id = row["batch_id"]
    if batch_id not in EXPECTED_BATCH_IDS:
        raise _error(path, line_number, f"batch_id must be one of {EXPECTED_BATCH_IDS[0]}..{EXPECTED_BATCH_IDS[-1]}")
    if path.stem != batch_id:
        raise _error(path, line_number, f"batch_id {batch_id!r} does not match filename")

    variant_match = _VARIANT_ID_PATTERN.fullmatch(row["variant_id"])
    if variant_match is None or variant_match.group(1) != batch_id:
        raise _error(
            path,
            line_number,
            "variant_id must be a descriptive lowercase authored identifier for its batch",
        )

    try:
        expected_group = category_group_for(row["category"])
    except PersonaContractError as error:
        raise _error(path, line_number, str(error)) from error
    if row["category_group"] != expected_group:
        raise _error(
            path,
            line_number,
            f"category_group {row['category_group']!r} must match configured category mapping {expected_group!r}",
        )

    if row["relationship_profile"] not in RELATIONSHIP_PROFILES:
        raise _error(
            path,
            line_number,
            "relationship_profile must be one of "
            + ", ".join(sorted(RELATIONSHIP_PROFILES)),
        )
    if row["review_status"] != APPROVED_REVIEW_STATUS:
        raise _error(
            path,
            line_number,
            f"review_status must be {APPROVED_REVIEW_STATUS!r}",
        )

    return AuthoredEntry(
        variant_id=row["variant_id"],
        batch_id=batch_id,
        category=row["category"],
        category_group=row["category_group"],
        topic_id=row["topic_id"],
        editorial_role=row["editorial_role"],
        semantic_group=row["semantic_group"],
        output_mode=row["output_mode"],
        trigger=row["trigger"],
        required_context=row["required_context"],
        tone=row["tone"],
        interrupt_cost=_integer(path, line_number, "interrupt_cost", row["interrupt_cost"], 0),
        cooldown_hours=_number(path, line_number, "cooldown_hours", row["cooldown_hours"], 0),
        semantic_cooldown_hours=_number(
            path,
            line_number,
            "semantic_cooldown_hours",
            row["semantic_cooldown_hours"],
            0,
        ),
        max_per_day=_integer(path, line_number, "max_per_day", row["max_per_day"], 1),
        weight=_number(path, line_number, "weight", row["weight"], 0.000000001),
        relationship_profile=row["relationship_profile"],
        text=row["text"],
        review_status=row["review_status"],
        _source_fields=values,
    )


def _candidate_batch_paths(authored_dir: Path) -> tuple[Path, ...]:
    try:
        children = tuple(authored_dir.iterdir())
    except OSError as error:
        raise AuthoredCatalogError(
            f"{authored_dir}: expected 100 batches b001.tsv..b100.tsv ({error})"
        ) from error
    expected_names = {f"{batch_id}.tsv" for batch_id in EXPECTED_BATCH_IDS}
    unexpected = sorted(
        child.name
        for child in children
        if child.name != ".gitkeep" and child.name not in expected_names
    )
    if unexpected:
        raise AuthoredCatalogError(
            f"{authored_dir}: unexpected authored source entries {unexpected!r}"
        )
    paths = tuple(authored_dir / f"{batch_id}.tsv" for batch_id in EXPECTED_BATCH_IDS)
    missing = [path.name for path in paths if not path.is_file()]
    if missing:
        raise AuthoredCatalogError(
            f"{authored_dir}: expected 100 batches b001.tsv..b100.tsv; missing {missing!r}"
        )
    return paths


def parse_authored_batches(authored_dir: Path) -> tuple[AuthoredEntry, ...]:
    """Parse all 100 literal source batches, without reading a manifest."""

    authored_dir = Path(authored_dir)
    entries: list[AuthoredEntry] = []
    for path in _candidate_batch_paths(authored_dir):
        rows = _read_tsv_rows(path)
        header = rows[0]
        if header != AUTHORED_HEADER:
            raise _error(
                path,
                1,
                "expected exact authored header: " + ",".join(AUTHORED_HEADER),
            )
        batch_rows = rows[1:]
        if len(batch_rows) != ROWS_PER_BATCH:
            raise _error(
                path,
                None,
                f"expected exactly {ROWS_PER_BATCH} authored rows, found {len(batch_rows)}",
            )
        entries.extend(
            _parse_entry(path, line_number, values)
            for line_number, values in enumerate(batch_rows, start=2)
        )

    if len(entries) != EXPECTED_ENTRY_COUNT:
        raise AuthoredCatalogError(
            f"{authored_dir}: expected exactly {EXPECTED_ENTRY_COUNT} authored rows, found {len(entries)}"
        )
    sorted_entries = tuple(sorted(entries, key=lambda entry: (entry.batch_id, entry.variant_id)))
    variant_ids = [entry.variant_id for entry in sorted_entries]
    if len(variant_ids) != len(set(variant_ids)):
        raise AuthoredCatalogError("authored batches contain duplicate variant_id")
    texts = [entry.text for entry in sorted_entries]
    if len(texts) != len(set(texts)):
        raise AuthoredCatalogError("authored batches contain duplicate text")
    return sorted_entries


def _digest_records(domain: str, records: tuple[tuple[str, ...], ...]) -> str:
    digest = hashlib.sha256()
    digest.update(domain.encode("ascii"))
    digest.update(b"\0")
    for record in records:
        for value in record:
            encoded = value.encode("utf-8")
            if b"\0" in encoded:
                raise AuthoredCatalogError("digest input contains a NUL byte")
            digest.update(encoded)
            digest.update(b"\0")
        digest.update(b"\0")
    return digest.hexdigest()


def _entry_text_sha256(entry: AuthoredEntry) -> str:
    return _digest_records("persona-authorship-entry-text-v1", ((entry.variant_id, entry.text),))


def _entry_metadata_sha256(entry: AuthoredEntry) -> str:
    metadata = tuple(
        value for index, value in enumerate(entry.source_fields) if index != _TEXT_INDEX
    )
    return _digest_records("persona-authorship-entry-metadata-v1", (metadata,))


def _batch_digest(entries: tuple[AuthoredEntry, ...]) -> AuthoredBatchDigest:
    if not entries:
        raise AuthoredCatalogError("cannot hash an empty authored batch")
    batch_id = entries[0].batch_id
    if any(entry.batch_id != batch_id for entry in entries):
        raise AuthoredCatalogError("cannot hash entries from different authored batches")
    ordered = tuple(sorted(entries, key=lambda entry: entry.variant_id))
    text_records = tuple((entry.variant_id, entry.text) for entry in ordered)
    metadata_records = tuple(
        tuple(value for index, value in enumerate(entry.source_fields) if index != _TEXT_INDEX)
        for entry in ordered
    )
    return AuthoredBatchDigest(
        row_count=len(ordered),
        text_sha256=_digest_records("persona-authorship-batch-text-v1", text_records),
        metadata_sha256=_digest_records(
            "persona-authorship-batch-metadata-v1", metadata_records
        ),
    )


def _root_sha256(batch_digests: Mapping[str, AuthoredBatchDigest]) -> str:
    records = tuple(
        (
            batch_id,
            str(batch_digests[batch_id].row_count),
            batch_digests[batch_id].text_sha256,
            batch_digests[batch_id].metadata_sha256,
        )
        for batch_id in EXPECTED_BATCH_IDS
    )
    return _digest_records("persona-authorship-root-v1", records)


def build_authorship_manifest_payload(
    entries: tuple[AuthoredEntry, ...],
) -> dict[str, object]:
    """Build the canonical manifest payload from an already strict source set."""

    grouped: dict[str, list[AuthoredEntry]] = {batch_id: [] for batch_id in EXPECTED_BATCH_IDS}
    for entry in entries:
        try:
            grouped[entry.batch_id].append(entry)
        except KeyError as error:
            raise AuthoredCatalogError(f"unexpected batch_id {entry.batch_id!r}") from error
    batch_digests = {
        batch_id: _batch_digest(tuple(grouped[batch_id]))
        for batch_id in EXPECTED_BATCH_IDS
    }
    root_sha256 = _root_sha256(batch_digests)
    return {
        "format": AUTHORED_MANIFEST_FORMAT,
        "authored_header": list(AUTHORED_HEADER),
        "batch_count": len(EXPECTED_BATCH_IDS),
        "rows_per_batch": ROWS_PER_BATCH,
        "total_rows": EXPECTED_ENTRY_COUNT,
        "batches": {
            batch_id: {
                "row_count": digest.row_count,
                "text_sha256": digest.text_sha256,
                "metadata_sha256": digest.metadata_sha256,
            }
            for batch_id, digest in batch_digests.items()
        },
        "root_sha256": root_sha256,
    }


def canonical_manifest_json(payload: Mapping[str, object]) -> str:
    """Serialize a manifest in the one tracked JSON representation."""

    return json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def _json_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise AuthoredCatalogError(f"manifest has duplicate JSON key {key!r}")
        result[key] = value
    return result


def _reject_json_constant(value: str) -> None:
    raise AuthoredCatalogError(f"manifest has non-finite JSON value {value!r}")


def _read_manifest(path: Path) -> dict[str, Any]:
    try:
        payload = path.read_bytes()
    except OSError as error:
        raise AuthoredCatalogError(f"{path}: cannot read authorship manifest: {error}") from error
    if payload.startswith(_UTF8_BOM):
        raise AuthoredCatalogError(f"{path}: UTF-8 BOM is not allowed in manifest")
    try:
        raw = json.loads(
            payload.decode("utf-8", errors="strict"),
            object_pairs_hook=_json_pairs,
            parse_constant=_reject_json_constant,
        )
    except (UnicodeError, json.JSONDecodeError, AuthoredCatalogError) as error:
        raise AuthoredCatalogError(f"{path}: invalid authorship manifest: {error}") from error
    if not isinstance(raw, dict) or set(raw) != _MANIFEST_KEYS:
        raise AuthoredCatalogError(
            f"{path}: manifest must contain exactly {sorted(_MANIFEST_KEYS)!r}"
        )
    return raw


def _require_manifest_integer(
    manifest_path: Path,
    field_name: str,
    value: object,
    expected_value: int,
) -> None:
    if type(value) is not int:
        raise AuthoredCatalogError(
            f"{manifest_path}: {field_name} must be an integer {expected_value}"
        )
    if value != expected_value:
        raise AuthoredCatalogError(
            f"{manifest_path}: {field_name} must equal {expected_value!r}"
        )


def _validate_manifest(
    manifest_path: Path,
    manifest: Mapping[str, Any],
    expected: Mapping[str, object],
) -> tuple[Mapping[str, AuthoredBatchDigest], str]:
    if manifest["format"] != AUTHORED_MANIFEST_FORMAT:
        raise AuthoredCatalogError(f"{manifest_path}: manifest format mismatch")
    if manifest["authored_header"] != list(AUTHORED_HEADER):
        raise AuthoredCatalogError(f"{manifest_path}: authored_header mismatch")
    for key, expected_value in (
        ("batch_count", len(EXPECTED_BATCH_IDS)),
        ("rows_per_batch", ROWS_PER_BATCH),
        ("total_rows", EXPECTED_ENTRY_COUNT),
    ):
        _require_manifest_integer(manifest_path, key, manifest[key], expected_value)

    batches = manifest["batches"]
    expected_batches = expected["batches"]
    if not isinstance(batches, dict) or set(batches) != set(EXPECTED_BATCH_IDS):
        raise AuthoredCatalogError(
            f"{manifest_path}: batches must contain exactly b001 through b100"
        )
    if not isinstance(expected_batches, dict):  # Defensive: internal contract.
        raise AuthoredCatalogError("internal authored manifest expectation is malformed")

    parsed_digests: dict[str, AuthoredBatchDigest] = {}
    for batch_id in EXPECTED_BATCH_IDS:
        actual_digest = batches[batch_id]
        expected_digest = expected_batches[batch_id]
        if not isinstance(actual_digest, dict) or set(actual_digest) != _BATCH_MANIFEST_KEYS:
            raise AuthoredCatalogError(
                f"{manifest_path}: batch {batch_id} must contain exactly "
                f"{sorted(_BATCH_MANIFEST_KEYS)!r}"
            )
        if not isinstance(expected_digest, dict):  # Defensive: internal contract.
            raise AuthoredCatalogError("internal batch digest expectation is malformed")
        for field_name in ("text_sha256", "metadata_sha256"):
            value = actual_digest[field_name]
            if not isinstance(value, str) or _SHA256_PATTERN.fullmatch(value) is None:
                raise AuthoredCatalogError(
                    f"{manifest_path}: batch {batch_id} {field_name} must be a lowercase SHA-256"
                )
        expected_row_count = expected_digest["row_count"]
        if type(expected_row_count) is not int:  # Defensive: internal contract.
            raise AuthoredCatalogError("internal batch row count expectation is malformed")
        _require_manifest_integer(
            manifest_path,
            f"batch {batch_id} row_count",
            actual_digest["row_count"],
            expected_row_count,
        )
        for field_name in ("text_sha256", "metadata_sha256"):
            if actual_digest[field_name] != expected_digest[field_name]:
                raise AuthoredCatalogError(
                    f"{manifest_path}: batch {batch_id} {field_name} mismatch"
                )
        parsed_digests[batch_id] = AuthoredBatchDigest(
            row_count=actual_digest["row_count"],
            text_sha256=actual_digest["text_sha256"],
            metadata_sha256=actual_digest["metadata_sha256"],
        )

    root_sha256 = manifest["root_sha256"]
    expected_root = expected["root_sha256"]
    if not isinstance(root_sha256, str) or _SHA256_PATTERN.fullmatch(root_sha256) is None:
        raise AuthoredCatalogError(f"{manifest_path}: root_sha256 must be a lowercase SHA-256")
    if root_sha256 != expected_root:
        raise AuthoredCatalogError(f"{manifest_path}: root_sha256 mismatch")
    return MappingProxyType(parsed_digests), root_sha256


def load_authored_catalog(authored_dir: Path, manifest_path: Path) -> AuthoredCatalog:
    """Load the strict authored source only after its manifest is verified."""

    entries = parse_authored_batches(Path(authored_dir))
    expected_manifest = build_authorship_manifest_payload(entries)
    manifest = _read_manifest(Path(manifest_path))
    batch_digests, root_sha256 = _validate_manifest(
        Path(manifest_path), manifest, expected_manifest
    )
    catalog = AuthoredCatalog(
        entries=entries,
        batch_digests=batch_digests,
        root_sha256=root_sha256,
    )
    ledger = tuple(catalog.ledger_rows())
    if len(ledger) != len(entries) or len({row.variant_id for row in ledger}) != len(entries):
        raise AuthoredCatalogError("authorship ledger is not one-to-one with source rows")
    return catalog


__all__ = [
    "APPROVED_REVIEW_STATUS",
    "AUTHORED_HEADER",
    "AUTHORED_LEDGER_HEADER",
    "AUTHORED_MANIFEST_FORMAT",
    "AuthoredBatchDigest",
    "AuthoredCatalog",
    "AuthoredCatalogError",
    "AuthoredEntry",
    "AuthorshipLedgerRow",
    "EXPECTED_BATCH_IDS",
    "EXPECTED_ENTRY_COUNT",
    "RELATIONSHIP_PROFILES",
    "ROWS_PER_BATCH",
    "build_authorship_manifest_payload",
    "canonical_manifest_json",
    "load_authored_catalog",
    "parse_authored_batches",
]
