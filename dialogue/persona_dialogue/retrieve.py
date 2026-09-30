"""用字对和英文词检索 authored 台词。桌宠这一侧不把八万行塞进提示词。"""

from __future__ import annotations

import csv
import math
import re
from collections import Counter, defaultdict
from collections.abc import Iterable
from pathlib import Path

from persona_dialogue.distill import PRIVACY_MARKERS

_WORD = re.compile(r"[A-Za-z][A-Za-z0-9_+#]{1,23}")
_QUESTION_CHARS = set("怎什哪这那")


class LineIndex:
    def __init__(self, lines: list[str]) -> None:
        self._lines = lines
        self._postings: dict[str, list[int]] = defaultdict(list)
        self._document_frequency: Counter[str] = Counter()
        for index, text in enumerate(lines):
            tokens = set(_bigrams(text))
            tokens.update(_words(text))
            for token in tokens:
                self._postings[token].append(index)
            self._document_frequency.update(tokens)
        self._count = len(lines)

    @property
    def count(self) -> int:
        return self._count

    @classmethod
    def from_corpus(cls, corpus_path: Path) -> "LineIndex":
        lines: list[str] = []
        with corpus_path.open(encoding="utf-8", newline="") as handle:
            reader = csv.DictReader(handle, delimiter="\t")
            for row in reader:
                if row.get("enabled") != "true" or row.get("source_kind") != "curated_authored":
                    continue
                text = (row.get("text") or "").strip()
                if not 8 <= len(text) <= 80:
                    continue
                if any(marker in text for marker in PRIVACY_MARKERS):
                    continue
                lines.append(text)
        return cls(lines)

    def search(
        self,
        query: str,
        limit: int = 4,
        exclude: Iterable[str] | None = None,
    ) -> list[str]:
        terms = _query_terms(query)
        if not terms or self._count == 0:
            return []
        banned = {item.strip() for item in (exclude or ()) if item and item.strip()}
        banned.add(query.strip())
        scores: Counter[int] = Counter()
        for term, weight in terms.items():
            matches = self._postings.get(term)
            if not matches:
                continue
            idf = math.log((self._count + 1) / (self._document_frequency[term] + 1)) + 1
            boost = idf * weight
            for index in matches:
                scores[index] += boost
        ranked: list[tuple[tuple[int, int, int], float, int, str]] = []
        for index, score in scores.most_common(max(limit * 12, limit)):
            text = self._lines[index]
            if text in banned:
                continue
            ranked.append((_best_span(query, text), score, -len(text), text))
        ranked.sort(reverse=True)
        unique: list[str] = []
        for _span, _score, _length, text in ranked:
            if text not in unique:
                unique.append(text)
            if len(unique) == limit:
                break
        return unique


def _cjk_chars(text: str) -> list[str]:
    return [char for char in text if "\u4e00" <= char <= "\u9fff"]


def _bigrams(text: str) -> list[str]:
    chars = _cjk_chars(text)
    return [chars[index] + chars[index + 1] for index in range(len(chars) - 1)]


def _words(text: str) -> list[str]:
    return [match.group(0).lower() for match in _WORD.finditer(text)]


def _query_terms(query: str) -> dict[str, float]:
    chars = _cjk_chars(query)
    terms: dict[str, float] = {}
    span = max(len(chars) - 1, 1)
    for index in range(len(chars) - 1):
        gram = chars[index] + chars[index + 1]
        weight = 0.35 + 0.65 * (index / max(span - 1, 1))
        terms[gram] = max(terms.get(gram, 0.0), weight)
    for word in _words(query):
        terms[word] = max(terms.get(word, 0.0), 1.0)
    return terms


def _best_span(query: str, line: str) -> tuple[int, int, int, int]:
    chars = _cjk_chars(query)
    text = "".join(chars)
    best = (0, 0, 0, 0)
    if text:
        for length in range(min(len(text), 16), 1, -1):
            found = False
            for start in range(len(text) - length + 1):
                piece = text[start : start + length]
                placed = _placement(line, piece, "" if start == 0 else text[start - 1])
                if placed is None:
                    continue
                found = True
                left_rank, right_clean = placed
                candidate = (length, left_rank, start + length, right_clean)
                if candidate > best:
                    best = candidate
            if found:
                break
    query_length = len(text)
    for word in _words(query):
        if _word_hit(line, word):
            best = max(best, (len(word), 1, query_length + len(word), 1))
    return best


def _placement(line: str, piece: str, query_before: str) -> tuple[int, int] | None:
    start = 0
    best: tuple[int, int] | None = None
    while True:
        at = line.find(piece, start)
        if at < 0:
            return best
        prev = line[at - 1] if at else ""
        after = at + len(piece)
        nxt = line[after] if after < len(line) else ""
        prev_cjk = bool(prev) and "\u4e00" <= prev <= "\u9fff"
        mismatched = prev_cjk and bool(query_before) and prev != query_before
        questioned = mismatched and (prev in _QUESTION_CHARS or query_before in _QUESTION_CHARS)
        left_rank = 0 if questioned else 1
        right_clean = 0 if nxt and "\u4e00" <= nxt <= "\u9fff" else 1
        candidate = (left_rank, right_clean)
        if best is None or candidate > best:
            best = candidate
        if best == (1, 1):
            return best
        start = at + 1


def _word_hit(line: str, word: str) -> bool:
    haystack = line.lower()
    start = 0
    while True:
        at = haystack.find(word, start)
        if at < 0:
            return False
        before = haystack[at - 1] if at > 0 else ""
        after_at = at + len(word)
        after = haystack[after_at] if after_at < len(haystack) else ""
        if not before.isalnum() and not after.isalnum() and before not in "_+#" and after not in "_+#":
            return True
        start = at + 1
