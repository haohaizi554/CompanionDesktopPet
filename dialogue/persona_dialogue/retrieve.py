"""用字对检索 authored 台词。桌宠这一侧不把八万行塞进提示词。"""

from __future__ import annotations

import csv
import math
from collections import Counter, defaultdict
from pathlib import Path

from persona_dialogue.distill import PRIVACY_MARKERS


class LineIndex:
    def __init__(self, lines: list[str]) -> None:
        self._lines = lines
        self._postings: dict[str, list[int]] = defaultdict(list)
        self._document_frequency: Counter[str] = Counter()
        for index, text in enumerate(lines):
            grams = set(_bigrams(text))
            for gram in grams:
                self._postings[gram].append(index)
            self._document_frequency.update(grams)
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

    def search(self, query: str, limit: int = 4) -> list[str]:
        grams = _bigrams(query)
        if not grams or self._count == 0:
            return []
        scores: Counter[int] = Counter()
        query_counts = Counter(grams)
        for gram, count in query_counts.items():
            matches = self._postings.get(gram)
            if not matches:
                continue
            idf = math.log((self._count + 1) / (self._document_frequency[gram] + 1)) + 1
            for index in matches:
                scores[index] += idf * (count / (count + 1))
        ranked = [
            self._lines[index]
            for index, _score in scores.most_common(limit * 3)
            if self._lines[index] != query.strip()
        ]
        unique: list[str] = []
        for text in ranked:
            if text not in unique:
                unique.append(text)
            if len(unique) == limit:
                break
        return unique


def _bigrams(text: str) -> list[str]:
    chars = [char for char in text if "\u4e00" <= char <= "\u9fff"]
    return [chars[index] + chars[index + 1] for index in range(len(chars) - 1)]
