"""Corpus-level surface diversity and distribution rules."""

from __future__ import annotations

from collections import Counter, defaultdict
from typing import Sequence

from ..contract import PERSONA_CONTRACT
from ..lexical import contains_seasoning_marker
from ..models import CorpusLine
from .core import _Issues


SURFACE_CATCHPHRASE_HARD_MAX = 0.25
SURFACE_OPENING_HARD_MAX = 0.065
SURFACE_ENDING_HARD_MAX = 0.065
SURFACE_CARTESIAN_TOPIC_HARD_MAX = 0.05
SURFACE_TOPIC_FACE_HARD_MAX = 0.08

def _has_cartesian_grid(texts: Sequence[str]) -> bool:
    if len(texts) < 8:
        return False
    for prefix_width in range(2, 7):
        for suffix_width in (4, 6, 8, 10):
            eligible = [
                text
                for text in texts
                if len(text) >= prefix_width + suffix_width + 1
            ]
            if len(eligible) < 8:
                continue
            pairs = {(text[:prefix_width], text[-suffix_width:]) for text in eligible}
            prefixes = {prefix for prefix, _ in pairs}
            suffixes = {suffix for _, suffix in pairs}
            pair_product = (
                len(prefixes) >= 3
                and len(suffixes) >= 3
                and len(pairs) == len(prefixes) * len(suffixes)
                and len(pairs) == len(eligible)
            )
            triples = {
                (text[:prefix_width], text[prefix_width:-suffix_width], text[-suffix_width:])
                for text in eligible
            }
            cores = {core for _, core, _ in triples}
            cube_size = len(prefixes) * len(cores) * len(suffixes)
            cube_product = (
                len(prefixes) >= 2
                and len(cores) >= 2
                and len(suffixes) >= 2
                and cube_size >= 8
                and len(triples) / cube_size >= 0.90
            )
            if pair_product or cube_product:
                return True
    return False


def _cartesian_grid_issues(rows: Sequence[CorpusLine], issues: _Issues) -> None:
    by_topic: dict[tuple[str, str], list[CorpusLine]] = defaultdict(list)
    for row in rows:
        if (
            row.enabled is True
            and row.source_kind != "legacy_surface_variant"
            and isinstance(row.text, str)
        ):
            by_topic[(str(row.category), str(row.topic_id))].append(row)
    for (category, topic_id), topic_rows in sorted(by_topic.items()):
        texts = [row.text for row in topic_rows]
        if _has_cartesian_grid(texts):
            issues.error(
                "cartesian_signature",
                f"topic {category}/{topic_id} forms a complete repeated opening-ending grid",
            )


def _surface_inventory_issues(rows: Sequence[CorpusLine], issues: _Issues) -> None:
    surface_rows = [
        row
        for row in rows
        if row.enabled is True
        and row.source_kind == "legacy_surface_variant"
        and isinstance(row.text, str)
    ]
    count = len(surface_rows)
    if count < 8:
        return

    texts = [row.text for row in surface_rows]
    expanded_runtime = (
        sum(row.enabled is True for row in rows)
        >= int(PERSONA_CONTRACT.inventory["expanded_runtime"][0])
    )
    catchphrase_count = sum(contains_seasoning_marker(text) for text in texts)
    catchphrase_share = catchphrase_count / count
    if catchphrase_share > SURFACE_CATCHPHRASE_HARD_MAX + 1e-12:
        if not expanded_runtime:
            issues.error(
                "surface_catchphrase_inventory",
                f"legacy surface catchphrases appear in {catchphrase_count}/{count} rows "
                f"({catchphrase_share:.2%}), above {SURFACE_CATCHPHRASE_HARD_MAX:.1%}",
            )

    opening_peaks: list[tuple[float, int, str, int]] = []
    for width in range(2, 7):
        frequencies = Counter(text[:width] for text in texts if len(text) >= width)
        if not frequencies:
            continue
        phrase, frequency = frequencies.most_common(1)[0]
        share = frequency / count
        opening_peaks.append((share, width, phrase, frequency))
        if share > SURFACE_OPENING_HARD_MAX + 1e-12:
            issues.error(
                "surface_opening_inventory",
                f"legacy surface {width}-character opening {phrase!r} appears "
                f"{frequency}/{count} times ({share:.2%}), above "
                f"{SURFACE_OPENING_HARD_MAX:.1%}",
            )

    ending_peaks: list[tuple[float, int, str, int]] = []
    for width in (4, 6, 8, 10):
        frequencies = Counter(text[-width:] for text in texts if len(text) >= width)
        if not frequencies:
            continue
        phrase, frequency = frequencies.most_common(1)[0]
        share = frequency / count
        ending_peaks.append((share, width, phrase, frequency))
        if share > SURFACE_ENDING_HARD_MAX + 1e-12:
            issues.error(
                "surface_ending_inventory",
                f"legacy surface {width}-character ending {phrase!r} appears "
                f"{frequency}/{count} times ({share:.2%}), above "
                f"{SURFACE_ENDING_HARD_MAX:.1%}",
            )

    by_topic: dict[tuple[str, str], list[str]] = defaultdict(list)
    for row in surface_rows:
        by_topic[(str(row.category), str(row.topic_id))].append(row.text)
    cartesian_topics = sum(_has_cartesian_grid(texts) for texts in by_topic.values())
    cartesian_share = cartesian_topics / len(by_topic) if by_topic else 0.0
    if cartesian_share > SURFACE_CARTESIAN_TOPIC_HARD_MAX + 1e-12:
        if not expanded_runtime:
            issues.error(
                "surface_cartesian_topics",
                f"legacy surface cartesian signatures occur in {cartesian_topics}/"
                f"{len(by_topic)} topics ({cartesian_share:.2%}), above "
                f"{SURFACE_CARTESIAN_TOPIC_HARD_MAX:.1%}",
            )

    worst_face: tuple[float, tuple[str, str], int, int, int, int] | None = None
    for topic, topic_texts in by_topic.items():
        if len(topic_texts) < 8:
            continue
        for prefix_width in range(2, 7):
            for suffix_width in (4, 6, 8, 10):
                eligible = [
                    text
                    for text in topic_texts
                    if len(text) >= prefix_width + suffix_width + 1
                ]
                if len(eligible) < 8:
                    continue
                frequency = Counter(
                    (text[:prefix_width], text[-suffix_width:]) for text in eligible
                ).most_common(1)[0][1]
                candidate = (
                    frequency / len(eligible),
                    topic,
                    prefix_width,
                    suffix_width,
                    frequency,
                    len(eligible),
                )
                if worst_face is None or candidate[0] > worst_face[0]:
                    worst_face = candidate
    if worst_face is not None and worst_face[0] > SURFACE_TOPIC_FACE_HARD_MAX + 1e-12:
        share, (category, topic_id), prefix_width, suffix_width, frequency, eligible = worst_face
        issues.error(
            "surface_topic_face_frequency",
            f"legacy surface topic {category}/{topic_id} repeats a "
            f"{prefix_width}+{suffix_width} opening-ending face {frequency}/{eligible} "
            f"times ({share:.2%}), above {SURFACE_TOPIC_FACE_HARD_MAX:.1%}",
        )

    if count >= 1_000:
        opening = max(opening_peaks, default=(0.0, 0, "", 0))
        ending = max(ending_peaks, default=(0.0, 0, "", 0))
        issues.warning(
            "surface_inventory_observation",
            f"legacy surface inventory observation: rows={count}; seasoning-marker rows="
            f"{catchphrase_count} ({catchphrase_share:.2%}); peak identical raw opening "
            f"share={opening[0]:.2%}; peak identical raw ending share={ending[0]:.2%}; "
            f"topics matching the opening/core/ending product heuristic="
            f"{cartesian_topics}/{len(by_topic)} ({cartesian_share:.2%}). These are raw "
            "inventory descriptors; hash-bound playback exposure gates are separate.",
        )


def _distribution_issues(rows: Sequence[CorpusLine], issues: _Issues) -> None:
    non_surface_rows = [
        row
        for row in rows
        if row.enabled is True and row.source_kind != "legacy_surface_variant"
    ]
    authored_rows = [
        row for row in non_surface_rows if row.source_kind == "curated_authored"
    ]
    authored_only = len(authored_rows) >= int(
        PERSONA_CONTRACT.inventory["expanded_runtime"][0]
    )
    distribution_rows = authored_rows if authored_only else non_surface_rows
    texts = [row.text for row in distribution_rows if isinstance(row.text, str)]
    count = len(texts)
    if count >= 20:
        catchphrase_lines = sum(contains_seasoning_marker(text) for text in texts)
        seasoning = PERSONA_CONTRACT.lexical_exposure["seasoning"]
        profiles = seasoning["inventory_profiles"]
        expanded_floor = PERSONA_CONTRACT.inventory["expanded_runtime"][0]
        core_policy = profiles["curated_core"]
        maximum = float(core_policy["maximum"])
        if count < expanded_floor and catchphrase_lines / count > maximum + 1e-12:
            issues.error(
                "catchphrase_frequency",
                "seasoning markers appear in "
                f"{catchphrase_lines}/{count} enabled texts, above {maximum:.0%}",
            )

        average = sum(map(len, texts)) / count
        shares = {
            "8-16": sum(8 <= len(text) <= 16 for text in texts) / count,
            "17-24": sum(17 <= len(text) <= 24 for text in texts) / count,
            "25-36": sum(25 <= len(text) <= 36 for text in texts) / count,
            ">36": sum(len(text) > 36 for text in texts) / count,
        }
        distribution_matches = (
            (
                22 <= average <= 26
                and 0.05 <= shares["8-16"] <= 0.10
                and 0.55 <= shares["17-24"] <= 0.60
                and 0.29 <= shares["25-36"] <= 0.32
                and shares[">36"] <= 0.05
            )
            if authored_only
            else (
                18 <= average <= 26
                and 0.25 <= shares["8-16"] <= 0.35
                and 0.35 <= shares["17-24"] <= 0.45
                and 0.20 <= shares["25-36"] <= 0.30
                and shares[">36"] <= 0.08
            )
        )
        if not distribution_matches:
            issues.error(
                "length_distribution",
                "enabled length distribution must meet its source-profile average and bucket targets; "
                f"observed average={average:.3f}, shares={shares}",
            )

    if count >= 100:
        for width in range(2, 7):
            frequencies = Counter(text[:width] for text in texts if len(text) >= width)
            if frequencies:
                phrase, frequency = frequencies.most_common(1)[0]
                if frequency / count > 0.02 + 1e-12:
                    issues.error(
                        "opening_frequency",
                        f"{width}-character opening {phrase!r} appears {frequency}/{count} times, above 2%",
                    )
        for width in (4, 6, 8, 10):
            frequencies = Counter(text[-width:] for text in texts if len(text) >= width)
            if frequencies:
                phrase, frequency = frequencies.most_common(1)[0]
                if frequency / count > 0.02 + 1e-12:
                    issues.error(
                        "ending_frequency",
                        f"{width}-character ending {phrase!r} appears {frequency}/{count} times, above 2%",
                    )
