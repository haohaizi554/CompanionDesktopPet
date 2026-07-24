from __future__ import annotations

import unicodedata
from collections import Counter, defaultdict
from difflib import SequenceMatcher
from typing import Sequence

from .models import AuditPair, AuditResult, LegacyLine
from .lexical import SEASONING_MARKERS as CATCHPHRASES, match_seasoning_markers
from .privacy import LEGACY_AUDIT_POLICY, contains_pii


PREFIX_LENGTHS = range(2, 7)
SUFFIX_LENGTHS = (4, 6, 8, 10)
HIGH_RISK_PATTERNS = (
    "你现在",
    "你今天",
    "你是不是",
    "你有没有",
    "你觉得",
    "告诉我",
    "回复我",
    "你的工资",
    "你住在",
    "你工作",
)
MAX_EXAMPLES = 20
MAX_RARE_GRAM_FREQUENCY = 256
MAX_CANDIDATES_PER_LINE = 16


def normalize_text(text: str) -> str:
    normalized = unicodedata.normalize("NFKC", text).casefold()
    return "".join(
        character
        for character in normalized
        if not unicodedata.category(character).startswith(("P", "Z", "C"))
        and not character.isspace()
    )


def character_ngrams(text: str, size: int = 3) -> frozenset[str]:
    if size <= 0:
        raise ValueError("ngram size must be positive")
    if not text:
        return frozenset()
    if len(text) <= size:
        return frozenset((text,))
    return frozenset(
        text[index : index + size] for index in range(len(text) - size + 1)
    )


def _duplicate_count(counter: Counter[str]) -> int:
    return sum(count - 1 for count in counter.values() if count > 1)


def _append_example(examples: dict[str, list[int]], key: str, line: int) -> None:
    bucket = examples[key]
    if len(bucket) < MAX_EXAMPLES:
        bucket.append(line)


def _looks_like_pii(text: str) -> bool:
    return contains_pii(text, LEGACY_AUDIT_POLICY)


def _prior_candidates(posting: list[int], index: int) -> list[int]:
    """Return bounded prior members; postings are ordered by source position."""
    try:
        position = posting.index(index)
    except ValueError:
        return []
    return posting[max(0, position - MAX_CANDIDATES_PER_LINE) : position]


def _similar_pairs(
    lines: Sequence[LegacyLine], normalized_texts: Sequence[str]
) -> tuple[int, list[AuditPair]]:
    gram_frequencies: Counter[str] = Counter()
    for text in normalized_texts:
        gram_frequencies.update(character_ngrams(text))

    postings: dict[str, list[int]] = defaultdict(list)
    for index, text in enumerate(normalized_texts):
        for gram in character_ngrams(text):
            frequency = gram_frequencies[gram]
            if 2 <= frequency <= MAX_RARE_GRAM_FREQUENCY:
                postings[gram].append(index)

    similar_count = 0
    examples: list[AuditPair] = []
    signature_cache: dict[int, frozenset[str]] = {}
    for index, text in enumerate(normalized_texts):
        grams = character_ngrams(text)
        rare_grams = sorted(
            (gram for gram in grams if gram in postings),
            key=lambda gram: (gram_frequencies[gram], gram),
        )[:4]
        candidates: set[int] = set()
        for gram in rare_grams:
            candidates.update(_prior_candidates(postings[gram], index))
            if len(candidates) >= MAX_CANDIDATES_PER_LINE:
                break
        for candidate in sorted(candidates, reverse=True)[:MAX_CANDIDATES_PER_LINE]:
            other = normalized_texts[candidate]
            if text == other or not text or not other:
                continue
            other_grams = signature_cache.get(candidate)
            if other_grams is None:
                other_grams = character_ngrams(other)
                signature_cache[candidate] = other_grams
            union_size = len(grams | other_grams)
            if not union_size or len(grams & other_grams) / union_size < 0.45:
                continue
            similarity = SequenceMatcher(None, text, other, autojunk=False).ratio()
            if similarity < 0.80:
                continue
            similar_count += 1
            if len(examples) < MAX_EXAMPLES:
                examples.append(
                    AuditPair(
                        left_source_line=lines[candidate].source_line,
                        right_source_line=lines[index].source_line,
                        similarity=similarity,
                        left_text=lines[candidate].text,
                        right_text=lines[index].text,
                    )
                )
        signature_cache[index] = grams
    return similar_count, examples


def audit_legacy(lines: Sequence[LegacyLine]) -> AuditResult:
    category_counts: Counter[str] = Counter()
    exact_counts: Counter[str] = Counter()
    normalized_counts: Counter[str] = Counter()
    prefix_counts = {length: Counter() for length in PREFIX_LENGTHS}
    suffix_counts = {length: Counter() for length in SUFFIX_LENGTHS}
    text_length_counts: Counter[int] = Counter()
    high_risk_patterns: Counter[str] = Counter({pattern: 0 for pattern in HIGH_RISK_PATTERNS})
    high_risk_examples: dict[str, list[int]] = defaultdict(list)
    catchphrase_counts: Counter[str] = Counter({phrase: 0 for phrase in CATCHPHRASES})
    catchphrase_examples: dict[str, list[int]] = defaultdict(list)
    question_examples: list[int] = []
    likely_pii_examples: list[int] = []
    normalized_first_line: dict[str, int] = {}
    normalized_duplicate_examples: list[tuple[int, int]] = []
    normalized_texts: list[str] = []
    question_count = 0
    likely_pii_count = 0

    for line in lines:
        normalized = normalize_text(line.text)
        normalized_texts.append(normalized)
        category_counts[line.category] += 1
        exact_counts[line.text] += 1
        normalized_counts[normalized] += 1
        text_length_counts[len(line.text)] += 1
        for length, counts in prefix_counts.items():
            if len(normalized) >= length:
                counts[normalized[:length]] += 1
        for length, counts in suffix_counts.items():
            if len(normalized) >= length:
                counts[normalized[-length:]] += 1

        if "?" in line.text or "？" in line.text:
            question_count += 1
            if len(question_examples) < MAX_EXAMPLES:
                question_examples.append(line.source_line)
        for pattern in HIGH_RISK_PATTERNS:
            if pattern in line.text:
                high_risk_patterns[pattern] += 1
                _append_example(high_risk_examples, pattern, line.source_line)
        for phrase in match_seasoning_markers(line.text):
            catchphrase_counts[phrase] += 1
            _append_example(catchphrase_examples, phrase, line.source_line)
        if _looks_like_pii(line.text):
            likely_pii_count += 1
            if len(likely_pii_examples) < MAX_EXAMPLES:
                likely_pii_examples.append(line.source_line)

        first_line = normalized_first_line.setdefault(normalized, line.source_line)
        if (
            first_line != line.source_line
            and len(normalized_duplicate_examples) < MAX_EXAMPLES
        ):
            normalized_duplicate_examples.append((first_line, line.source_line))

    similar_pair_count, similar_pair_examples = _similar_pairs(lines, normalized_texts)
    return AuditResult(
        total_lines=len(lines),
        category_counts=dict(category_counts),
        exact_duplicate_count=_duplicate_count(exact_counts),
        normalized_duplicate_count=_duplicate_count(normalized_counts),
        question_count=question_count,
        question_examples=question_examples,
        high_risk_patterns=dict(high_risk_patterns),
        high_risk_examples=dict(high_risk_examples),
        catchphrase_counts=dict(catchphrase_counts),
        catchphrase_examples=dict(catchphrase_examples),
        likely_pii_count=likely_pii_count,
        likely_pii_examples=likely_pii_examples,
        prefix_counts={length: dict(counts) for length, counts in prefix_counts.items()},
        suffix_counts={length: dict(counts) for length, counts in suffix_counts.items()},
        text_length_counts=dict(text_length_counts),
        normalized_duplicate_examples=normalized_duplicate_examples,
        similar_pair_count=similar_pair_count,
        similar_pair_examples=similar_pair_examples,
    )
