# Task 3 — Persona corpus lineage and editorial hardening

Date: 2026-07-23
Status: implementation complete; final verification evidence recorded below

## Scope and source integrity

- The immutable source is `data/source/persona-corpus.original.tsv`.
- Its SHA-256 is `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534` and its size is 7,961,787 bytes.
- The staged legacy asset has the same byte count and SHA-256. It was not edited by this task.
- Stable v2 line IDs remain derived solely from immutable `variant_id`; copy edits, runtime topics, and editorial roles cannot change them.

## Editorial adjudication

Exactly 63 former `.practice` paraphrases were individually rewritten as distinct complete sentences with a declared editorial angle:

| Category | Rewritten variants |
| --- | ---: |
| Algorithms | 13 |
| Architecture | 10 |
| Backend | 9 |
| Career | 9 |
| Cpp | 12 |
| Database | 10 |
| Total | 63 |

Every adjudicated entry carries a literal `editorial_role` and a `human-editorial-angle:` rationale. Against its paired `.observation`, the maximum normalized `SequenceMatcher` ratio is 0.4000; zero entries exceed the 0.55 regression ceiling. All 63 IDs and rewritten texts are unique.

## Runtime topic and role contract

`CatalogEntry` now separates three identities:

- `variant_id`: immutable stable-ID input.
- `runtime_topic_id`: literal runtime grouping key.
- `editorial_role`: immutable angle or purpose within the runtime topic.

Legacy variants retain the exact source-mapping topic as `runtime_topic_id`, and every multi-variant legacy topic has distinct roles. Authored topic families were split semantically rather than by serial number alone: CharacterLife into 3-entry topics, daily care and emotional reflection into 2–3-entry topics, and SystemAmbient into 5-entry topics.

Observed real-corpus cardinalities:

| Category group | Topic-size distribution | Minimum | Maximum |
| --- | --- | ---: | ---: |
| technical | 175 topics × 2 | 2 | 2 |
| growth | 28 topics × 2 | 2 | 2 |
| career | 14 topics × 2 | 2 | 2 |
| character_life | 8 topics × 3; 15 topics × 5 | 3 | 5 |
| daily_care | 6 topics × 2; 18 topics × 3 | 2 | 3 |
| emotional_reflection | 2 topics × 2; 9 topics × 3 | 2 | 3 |
| easter_egg | 30 topics × 1 | 1 | 1 |
| system_ambient | 28 topics × 5 | 5 | 5 |

## Current generated outputs

| File | Data rows | SHA-256 |
| --- | ---: | --- |
| `persona-corpus-v2.tsv` | 800 | `f4f6c1594bb79be9a983093ad9995ee6f0e6132b08f8c7098c55965882052d97` |
| `persona-corpus-archive.tsv` | 75,375 | `8b0de182e39d8a367518390fdd1fc67afb84b2ab97169f43eff9654f5d39dbb6` |
| `persona-corpus-review.tsv` | 3,265 | `a251b1e01003a078d7912f71099e57c5c6830a75195558ea61428105990b866a` |
| `pii-review.tsv` | 1,248 | `702037759f730759be83fb1c643a8f61382fa1c3f8f2a25e2c0351a177eec6e7` |

The v2 output contains 800 unique stable IDs and 800 unique normalized texts. Its length buckets are 200 short (8–16 characters), 360 medium (17–24), and 240 long (25–36), with no line over 36 characters; mean length is 21.015.

## Verification evidence

- Focused RED before implementation: 7 selected regressions ran and all failed (`failures=4, errors=3`, 4.270 s), covering absent runtime fields and roles, missing 63-entry adjudication manifest, stale topic cardinalities, absent exact spec headers, and stale report truth.
- Formal fixed-seed build: `enabled=800`, `archive=75375`, `review=3265`, `pii_review=1248`.
- Immutable-source audit: `audit_corpus.py` completed over 75,375 lines and reported SHA-256 `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534`.
- Two independent temporary-directory rebuilds returned 0. Each produced the same four hashes shown above, and both matched the formal outputs byte-for-byte.
- Focused GREEN: 7/7 selected regressions passed (`Ran 7 tests in 2.420s`, `OK`) with no warnings.
- Full Python suite: 53/53 tests passed (`Ran 53 tests in 7.883s`, `OK`).
- Python bytecode compilation: all persona modules, corpus tools, and `tests/test_build.py` compiled successfully with `py_compile`.
- Final scoped diff: `git diff --check` passed for every Task 3 path; unrelated staged and working-tree changes were excluded from this task.

## Design-schema authority

The design specification now states the exact Archive and Review column orders. Runtime output remains TSV, while the comma-separated strings in the specification are the reviewable schema authority required by the contract tests.
