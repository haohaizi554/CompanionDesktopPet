# Task 3 — Persona corpus byte fidelity and editorial diversity

Date: 2026-07-23
Status: implementation complete; final verification evidence is recorded below

## Immutable source and newline policy

The canonical source is `data/source/persona-corpus.original.tsv`. Its exact byte contract is:

- 7,961,787 bytes
- 75,375 CRLF sequences and no bare LF
- SHA-256 `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534`

The root `.gitattributes` now applies `-text diff` to both the canonical source and `src/CompanionDesktopPet/Assets/persona-corpus.tsv`. This stores the original CRLF bytes without normalization while retaining textual diffs. It applies `text eol=lf` to `data/intermediate/*.tsv`, `data/optimized/*.tsv`, and `reports/*.tsv`, so generated hashes survive clean checkouts.

Before the fix, the worktree source had the canonical hash above but both Git index blobs were normalized to 7,886,412 LF bytes with SHA-256 `9adcba0025d75f416cfd618d0ff1c075e442cafc086d14f71192c0f9af7b00d5`. After `git add --renormalize`, source and asset worktree/index blobs are all 7,961,787 bytes with SHA-256 `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534`.

The legacy asset remains staged as a separate 75,375-line user change and is intentionally excluded from this task's commit. The canonical source and `.gitattributes` are included.

## Editorial retirement and replacement

The second editorial pass retired 29 generic `.practice` variants while preserving all 63 previously human-adjudicated variants:

| Runtime group | Retired practice variants |
| --- | ---: |
| technical | 20 |
| growth | 6 |
| career | 3 |
| Total | 29 |

The seven reviewer-identified pairs above the 0.55 similarity ceiling are included in that retirement set. The retained catalog now has 188 observation/practice pairs; a full-catalog scan reports zero above 0.55 and a maximum ratio of 0.4103. The 63-entry adjudication manifest remains intact and disjoint from the 29-entry retirement manifest.

Exactly 29 independently authored replacements keep the enabled catalog at 800 without padding:

| Category | Authored replacements | New runtime topics |
| --- | ---: | ---: |
| CharacterLife | 15 | 5 |
| DailyCare | 9 | 3 |
| EmotionalSupport | 5 | 2 |
| Total | 29 | 10 |

Every replacement is a standalone context-safe sentence, 12-30 Chinese characters long, with a unique `variant_id`, text, runtime topic role, and human-authored rationale. Their semantic cooldown is 168 hours, not lower than the 144-hour ID cooldown.

Retained stable IDs are unchanged because IDs remain derived solely from immutable `variant_id`. Retired IDs are deliberately absent, and newly authored entries receive new IDs.

## Runtime topic cardinalities

| Category group | Topic-size distribution | Minimum | Maximum |
| --- | --- | ---: | ---: |
| technical | 20 topics × 1; 155 topics × 2 | 1 | 2 |
| growth | 6 topics × 1; 22 topics × 2 | 1 | 2 |
| career | 3 topics × 1; 11 topics × 2 | 1 | 2 |
| character_life | 13 topics × 3; 15 topics × 5 | 3 | 5 |
| daily_care | 6 topics × 2; 21 topics × 3 | 2 | 3 |
| emotional_reflection | 3 topics × 2; 10 topics × 3 | 2 | 3 |
| easter_egg | 30 topics × 1 | 1 | 1 |
| system_ambient | 28 topics × 5 | 5 | 5 |

Technical, growth, and career now each contain a meaningful mixture of one- and two-variant topics; singleton shares are 11.43%, 21.43%, and 21.43% respectively.

## Current generated outputs

| File | Data rows | SHA-256 |
| --- | ---: | --- |
| `persona-corpus-v2.tsv` | 800 | `d2e7e655c1a4aeb3464ccdc9403498378b3cdf098bbc526c25a4159329ede4b3` |
| `persona-corpus-archive.tsv` | 75,375 | `e9eb0a03db310bfef81fc2912b045941bdb78137f7d5871fb2e61e93735d15d4` |
| `persona-corpus-review.tsv` | 3,265 | `a251b1e01003a078d7912f71099e57c5c6830a75195558ea61428105990b866a` |
| `pii-review.tsv` | 1,248 | `702037759f730759be83fb1c643a8f61382fa1c3f8f2a25e2c0351a177eec6e7` |

All four generated files contain LF only. The v2 output has 800 unique stable IDs and 800 unique normalized texts. Its length buckets are 216 short (8–16 characters), 352 medium (17–24), and 232 long (25–36), with no line over 36 characters; mean length is 20.8000. These shares are 27%, 44%, and 29%, leaving 2, 1, and 1 percentage points of margin against the configured boundaries.

## Verification evidence

- Newline root-cause reproduction: worktree source `3fd735…` versus index/HEAD source `9adcba…` before attributes.
- Second-wave focused RED: 3 tests produced 5 expected failures (`Ran 3 tests in 3.843s`) for the seven duplicate pairs, missing attributes, and all-three-group absence of singleton topics.
- Cooldown RED: the new catalog-level test failed on `authored.small_errands.01` because 96 was below 144.
- Focused GREEN: 6/6 selected regressions passed (`Ran 6 tests in 9.744s`, `OK`).
- Formal fixed-seed build: `enabled=800`, `archive=75375`, `review=3265`, `pii_review=1248`.
- Immutable-source audit: `audit_corpus.py` completed over 75,375 lines with canonical SHA-256 `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534`.
- Full Python suite: 57/57 tests passed (`Ran 57 tests in 7.161s`, `OK`) with bytecode writes disabled.
- Python bytecode compilation: 15/15 scoped persona modules, tools, and tests compiled successfully.
- Two independent temporary-directory rebuilds exited 0; all four hashes matched each other, the formal tracked outputs, and the values above byte-for-byte.
- Clean Git archive reproduction: temporary index tree `b2c55f5683adfea91269db3f3cb2df41a3d1a030` excluded the separately staged legacy asset and passed 57/57 tests (`Ran 57 tests in 18.073s`). The archived source remained 7,961,787 bytes, 75,375 CRLF records, and SHA-256 `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534`; all four generated-output hashes matched the formal files.
- Final scoped checks: `git diff --check` passed with the source-only `whitespace=cr-at-eol` contract; source and legacy asset worktree/index blobs were each 7,961,787 bytes with SHA-256 `3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534`, while the 75,375-line legacy asset remained staged and excluded from this task commit.

## Schema authority

The v2 header remains the fixed 20-column runtime interface. `runtime_topic_id` is serialized into the existing `topic_id` column; `editorial_role` remains immutable catalog metadata and a build-time quality contract. The design specification continues to state the exact Archive and Review header order.
