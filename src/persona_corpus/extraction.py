from __future__ import annotations

import hashlib
import re
import unicodedata
from collections import Counter, defaultdict
from dataclasses import dataclass
from typing import Sequence

from .models import LegacyLine
from .normalization import normalize_text


MIN_COMPONENT_FREQUENCY = 2
MIN_CARTESIAN_DIMENSION = 2
MIN_EXTRACTION_CONFIDENCE = 0.75


@dataclass(frozen=True, slots=True)
class ExtractedComponent:
    id: str
    category: str
    text: str
    normalized_text: str
    source_count: int
    extraction_confidence: float
    standalone: bool = False


@dataclass(frozen=True, slots=True)
class SourceMapping:
    source_line: int
    category: str
    original_text: str
    prefix_id: str
    topic_id: str
    suffix_id: str
    extraction_confidence: float


@dataclass(frozen=True, slots=True)
class ExtractionResult:
    prefixes: tuple[ExtractedComponent, ...]
    topics: tuple[ExtractedComponent, ...]
    suffixes: tuple[ExtractedComponent, ...]
    mappings: tuple[SourceMapping, ...]


class _TrieNode:
    __slots__ = ("children", "count")

    def __init__(self) -> None:
        self.children: dict[str, _TrieNode] = {}
        self.count = 0


@dataclass(frozen=True, slots=True)
class _Split:
    line: LegacyLine
    prefix_text: str
    prefix_normalized: str
    topic_text: str
    topic_normalized: str
    suffix_text: str
    suffix_normalized: str


@dataclass(frozen=True, slots=True)
class _Candidate:
    split: _Split
    frequency_support: int
    component_length: int


def _build_trie(texts: Sequence[str], reverse: bool = False) -> _TrieNode:
    root = _TrieNode()
    for text in texts:
        root.count += 1
        node = root
        characters = reversed(text) if reverse else iter(text)
        for character in characters:
            node = node.children.setdefault(character, _TrieNode())
            node.count += 1
    return root


def _boundary_candidates(root: _TrieNode, text: str, reverse: bool = False) -> list[tuple[int, int]]:
    """Return (component length, frequency) at category-local trie branches."""
    node = root
    result: list[tuple[int, int]] = []
    characters = list(reversed(text)) if reverse else list(text)
    for index, character in enumerate(characters, start=1):
        child = node.children.get(character)
        if child is None:
            break
        node = child
        if index >= len(text):
            continue
        next_character = characters[index]
        next_node = node.children.get(next_character)
        next_count = next_node.count if next_node is not None else 0
        if node.count >= MIN_COMPONENT_FREQUENCY and next_count < node.count:
            result.append((index, node.count))
    return result


def _normalized_offsets(text: str) -> tuple[str, list[int]]:
    characters: list[str] = []
    offsets: list[int] = []
    for original_index, character in enumerate(text):
        for normalized_character in unicodedata.normalize("NFKC", character):
            if unicodedata.category(normalized_character).startswith(("P", "Z")):
                continue
            if normalized_character.isspace():
                continue
            characters.append(normalized_character)
            offsets.append(original_index)
    return "".join(characters), offsets


def _original_boundaries(text: str, prefix_length: int, suffix_length: int) -> tuple[int, int]:
    normalized, offsets = _normalized_offsets(text)
    if prefix_length <= 0 or suffix_length <= 0:
        raise ValueError("prefix and suffix lengths must be positive")
    if prefix_length + suffix_length >= len(normalized):
        raise ValueError("prefix and suffix must leave a non-empty topic")

    prefix_end = offsets[prefix_length - 1] + 1
    while prefix_end < len(text):
        if normalize_text(text[prefix_end]):
            break
        prefix_end += 1
    suffix_start = offsets[len(normalized) - suffix_length]
    return prefix_end, suffix_start


def _candidate_splits(
    line: LegacyLine, prefix_trie: _TrieNode, suffix_trie: _TrieNode
) -> list[_Candidate]:
    normalized = normalize_text(line.text)
    if len(normalized) < 3:
        return []
    prefix_candidates = _boundary_candidates(prefix_trie, normalized)
    suffix_candidates = _boundary_candidates(suffix_trie, normalized, reverse=True)
    result: list[_Candidate] = []
    for prefix_length, prefix_frequency in prefix_candidates:
        for suffix_length, suffix_frequency in suffix_candidates:
            if prefix_length + suffix_length >= len(normalized):
                continue
            prefix_end, suffix_start = _original_boundaries(
                line.text, prefix_length, suffix_length
            )
            topic_text = line.text[prefix_end:suffix_start]
            if not topic_text or not normalize_text(topic_text):
                continue
            result.append(
                _Candidate(
                    split=_Split(
                        line=line,
                        prefix_text=line.text[:prefix_end],
                        prefix_normalized=normalized[:prefix_length],
                        topic_text=topic_text,
                        topic_normalized=normalized[
                            prefix_length : len(normalized) - suffix_length
                        ],
                        suffix_text=line.text[suffix_start:],
                        suffix_normalized=normalized[
                            len(normalized) - suffix_length :
                        ],
                    ),
                    frequency_support=prefix_frequency * suffix_frequency,
                    component_length=prefix_length + suffix_length,
                )
            )
    return result


def _slug(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "_", value.casefold()).strip("_")
    return slug or "category"


def _stable_id(kind: str, category: str, normalized_text: str, source_line: int | None = None) -> str:
    identity = f"{kind}\0{category}\0{normalized_text}"
    if source_line is not None:
        identity += f"\0{source_line}"
    digest = hashlib.sha256(identity.encode("utf-8")).hexdigest()[:12]
    return f"{kind}_{_slug(category)}_{digest}"


def _standalone_id(line: LegacyLine) -> str:
    kind = "egg_standalone" if line.category.casefold() == "easteregg" else "standalone"
    identity = f"{kind}\0{line.category}\0{normalize_text(line.text)}\0{line.source_line}"
    digest = hashlib.sha256(identity.encode("utf-8")).hexdigest()[:12]
    return f"{kind}_{digest}"


def _component(
    component_id: str,
    category: str,
    text: str,
    normalized_text: str,
    count: int,
    confidence: float,
    standalone: bool = False,
) -> ExtractedComponent:
    return ExtractedComponent(
        id=component_id,
        category=category,
        text=text,
        normalized_text=normalized_text,
        source_count=count,
        extraction_confidence=confidence,
        standalone=standalone,
    )


def extract_structure(lines: Sequence[LegacyLine]) -> ExtractionResult:
    """Recover repeated category-local prefix/topic/suffix combinations."""
    by_category: dict[str, list[LegacyLine]] = defaultdict(list)
    for line in lines:
        by_category[line.category].append(line)

    accepted: dict[int, tuple[_Split, float]] = {}
    for category in sorted(by_category):
        category_lines = by_category[category]
        if category.casefold() == "easteregg":
            continue
        normalized_texts = [normalize_text(line.text) for line in category_lines]
        prefix_trie = _build_trie(normalized_texts)
        suffix_trie = _build_trie(normalized_texts, reverse=True)

        candidates_by_line: dict[int, list[_Candidate]] = {}
        topic_pairs: dict[str, set[tuple[str, str]]] = defaultdict(set)
        topic_prefixes: dict[str, set[str]] = defaultdict(set)
        topic_suffixes: dict[str, set[str]] = defaultdict(set)
        for line in category_lines:
            candidates = _candidate_splits(line, prefix_trie, suffix_trie)
            candidates_by_line[line.source_line] = candidates
            for candidate in candidates:
                split = candidate.split
                key = split.topic_normalized
                topic_pairs[key].add(
                    (split.prefix_normalized, split.suffix_normalized)
                )
                topic_prefixes[key].add(split.prefix_normalized)
                topic_suffixes[key].add(split.suffix_normalized)

        confidence_by_topic: dict[str, float] = {}
        for key, observed_pairs in topic_pairs.items():
            prefix_count = len(topic_prefixes[key])
            suffix_count = len(topic_suffixes[key])
            possible_pairs = prefix_count * suffix_count
            if (
                prefix_count < MIN_CARTESIAN_DIMENSION
                or suffix_count < MIN_CARTESIAN_DIMENSION
                or possible_pairs == 0
            ):
                confidence_by_topic[key] = 0.0
                continue
            completeness = len(observed_pairs) / possible_pairs
            support = min(1.0, len(observed_pairs) / 4.0)
            confidence_by_topic[key] = completeness * support

        for source_line, candidates in candidates_by_line.items():
            ranked = [
                candidate
                for candidate in candidates
                if confidence_by_topic[candidate.split.topic_normalized]
                >= MIN_EXTRACTION_CONFIDENCE
            ]
            if not ranked:
                continue
            best = max(
                ranked,
                key=lambda candidate: (
                    confidence_by_topic[candidate.split.topic_normalized],
                    candidate.frequency_support,
                    -candidate.component_length,
                ),
            )
            accepted[source_line] = (
                best.split,
                confidence_by_topic[best.split.topic_normalized],
            )

    prefix_usage: Counter[tuple[str, str]] = Counter()
    topic_usage: Counter[tuple[str, str]] = Counter()
    suffix_usage: Counter[tuple[str, str]] = Counter()
    first_prefix: dict[tuple[str, str], str] = {}
    first_topic: dict[tuple[str, str], str] = {}
    first_suffix: dict[tuple[str, str], str] = {}
    topic_confidence: dict[tuple[str, str], float] = {}
    for split, confidence in accepted.values():
        prefix_key = (split.line.category, split.prefix_normalized)
        topic_key = (split.line.category, split.topic_normalized)
        suffix_key = (split.line.category, split.suffix_normalized)
        prefix_usage[prefix_key] += 1
        topic_usage[topic_key] += 1
        suffix_usage[suffix_key] += 1
        first_prefix.setdefault(prefix_key, split.prefix_text)
        first_topic.setdefault(topic_key, split.topic_text)
        first_suffix.setdefault(suffix_key, split.suffix_text)
        topic_confidence[topic_key] = confidence

    prefixes = tuple(
        sorted(
            (
                _component(
                    _stable_id("prefix", category, normalized),
                    category,
                    first_prefix[(category, normalized)],
                    normalized,
                    count,
                    1.0,
                )
                for (category, normalized), count in prefix_usage.items()
            ),
            key=lambda component: component.id,
        )
    )
    decomposed_topics = [
        _component(
            _stable_id("topic", category, normalized),
            category,
            first_topic[(category, normalized)],
            normalized,
            count,
            topic_confidence[(category, normalized)],
        )
        for (category, normalized), count in topic_usage.items()
    ]
    suffixes = tuple(
        sorted(
            (
                _component(
                    _stable_id("suffix", category, normalized),
                    category,
                    first_suffix[(category, normalized)],
                    normalized,
                    count,
                    1.0,
                )
                for (category, normalized), count in suffix_usage.items()
            ),
            key=lambda component: component.id,
        )
    )

    standalone_topics: list[ExtractedComponent] = []
    mappings: list[SourceMapping] = []
    for line in lines:
        accepted_split = accepted.get(line.source_line)
        if accepted_split is None:
            topic_id = _standalone_id(line)
            standalone_topics.append(
                _component(
                    topic_id,
                    line.category,
                    line.text,
                    normalize_text(line.text),
                    1,
                    0.0,
                    standalone=True,
                )
            )
            mappings.append(
                SourceMapping(
                    source_line=line.source_line,
                    category=line.category,
                    original_text=line.text,
                    prefix_id="",
                    topic_id=topic_id,
                    suffix_id="",
                    extraction_confidence=0.0,
                )
            )
            continue

        split, confidence = accepted_split
        mappings.append(
            SourceMapping(
                source_line=line.source_line,
                category=line.category,
                original_text=line.text,
                prefix_id=_stable_id(
                    "prefix", line.category, split.prefix_normalized
                ),
                topic_id=_stable_id("topic", line.category, split.topic_normalized),
                suffix_id=_stable_id(
                    "suffix", line.category, split.suffix_normalized
                ),
                extraction_confidence=confidence,
            )
        )

    topics = tuple(
        sorted(decomposed_topics + standalone_topics, key=lambda component: component.id)
    )
    return ExtractionResult(
        prefixes=prefixes,
        topics=topics,
        suffixes=suffixes,
        mappings=tuple(mappings),
    )
