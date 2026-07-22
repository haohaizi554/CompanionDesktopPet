# Task 3 Report: Curated v2 corpus, archive, review and PII outputs

## Status

Implemented, generated with seed `20260722`, independently audited, and ready for the explicit-path Task 3 commit.

## Implementation

- Added the exact 20-column v2 schema and typed archive/review/PII review records.
- Added a materialized catalog of exactly 800 complete, playback-ready Chinese sentences. The catalog contains explicit `CatalogEntry(...)` calls rather than opener/core/closer arrays, Cartesian products, runtime assembly, or mechanical synonym expansion.
- Added deterministic `build_v2(source, mappings, seed, pii_policy="review") -> BuildResult` migration with stable snake-case IDs, exact source references, full source dispositions, strict source/mapping consistency checks, and UTF-8/LF TSV serialization.
- Added public-contract normalization at the build boundary:
  - `Career -> career`
  - `Study` and `EnglishPractice -> growth`
  - deterministic `SystemAmbient -> system_observe`
  - editorial source types map to `rewritten_topic`, `curated_standalone`, `preserved_easter_egg`, or `new_ambient`.
- Archived every legacy row, including every `ProactiveChat` row as `requires_user_reply`, while routing PII, false-context, and uncertain-intimacy rows to explicit review outputs.
- Added a standard-library CLI that emits all four deterministic outputs and reports counts plus the v2 SHA-256.
- Tuned 63 individually reviewed complete sentences without changing catalog size: 25 now contain 11-15 characters and 38 contain 18-24 characters. This moved all hard length buckets into range without introducing questions, PII, false user context, duplicated text, or repeated template edges.

## Files

- `src/persona_corpus/schema.py`
- `src/persona_corpus/content_catalog.py`
- `src/persona_corpus/builder.py`
- `tools/build_corpus_v2.py`
- `tests/test_build.py`
- `data/optimized/persona-corpus-v2.tsv`
- `data/optimized/persona-corpus-archive.tsv`
- `data/optimized/persona-corpus-review.tsv`
- `reports/pii-review.tsv`
- `.superpowers/sdd/task-3-report.md`

The pre-staged authoritative source `src/CompanionDesktopPet/Assets/persona-corpus.tsv` and unrelated C#/WPF, documentation, output, and packaging changes are intentionally excluded from this Task 3 commit.

## TDD evidence

### Original RED

Command:

```text
python -B -m unittest tests.test_build -v
```

Before implementation, import failed as expected because `src.persona_corpus.builder` did not exist (`ImportError`/`ModuleNotFoundError`). The original complete terminal traceback was not persisted, so no exception line or test count is reconstructed here.

### Initial GREEN

Before the final contract audit, the focused suite completed:

```text
Ran 16 tests in 74.049s
OK
```

### Public taxonomy regression RED/GREEN

An audit against the user's exact field contract found that editorial `source_kind` values leaked into the runtime output and that `Career`, `Study`, and `EnglishPractice` were incorrectly counted as technical. A regression test was added first:

```text
python -B -m unittest tests.test_build.BuildContractTests.test_public_taxonomy_matches_the_v2_contract -v
AssertionError: False is not true
Ran 1 test in 0.063s
FAILED (failures=1)
```

After adding only build-boundary mappings, the same test passed:

```text
Ran 1 test in 0.221s
OK
```

### Final focused GREEN

After the 63 sentence-level length refinements:

```text
python -B -m unittest tests.test_build -v
Ran 18 tests in 134.220s
OK
```

The focused suite covers explicit materialization/no Cartesian product, exact schema, standalone no-reply safety, valid metadata, public taxonomy, deterministic IDs/serialization, complete dispositions, exact rewrite traceability, ProactiveChat archival, PII/context review routing, output headers, real-corpus uniqueness, length/voice limits, opening-template dominance, and independent CLI reproducibility.

## Generated outputs

All files are UTF-8, contain LF-only physical rows, end in LF, have the exact declared header, and every physical data row has the exact expected column count.

| Output | Rows | Physical lines | Bytes | SHA-256 |
|---|---:|---:|---:|---|
| `persona-corpus-v2.tsv` | 800 | 801 | 304,374 | `1183bd03c08e2b5a634b4aecf31509b5755230b18393725739f72701d4f2ecf7` |
| `persona-corpus-archive.tsv` | 75,375 | 75,376 | 16,773,321 | `9b2bd234feaaec34175d2fd5d5044af91cac19a8f691cfe6639025e5707ec753` |
| `persona-corpus-review.tsv` | 3,212 | 3,213 | 1,122,025 | `cc588ec5d4e563c13b841ed2dcc4f6471435a64eb2ebfdca88ea8c82d82dc6e5` |
| `pii-review.tsv` | 1,248 | 1,249 | 421,300 | `2d2c9940a5e6e10c1221523efef055bc239fa51f2a987b3a2f4f6abecd86fc41` |

Archive reasons:

- `cartesian_duplicate`: 58,690
- `requires_user_reply`: 8,580
- `overly_commanding`: 4,160
- `fake_context`: 1,574
- `privacy_risk`: 1,248
- `low_information`: 551
- `manual_review`: 338
- `unsafe_emotional_claim`: 234

Review risks:

- `future_context_signal`: 1,574
- `privacy_risk`: 1,248
- `uncertain_intimacy`: 390

PII review types:

- `location_or_history`: 793
- `income_or_employment`: 394
- `person_name`: 61

## Traceability and source protection

- Source rows: 75,375.
- Source mappings: 75,375.
- Non-empty migration dispositions: 75,375 unique source lines.
- Archive coverage: 75,375 rows and 75,375 unique source lines, exactly equal to the source line set.
- Legacy `ProactiveChat`: 2,925 source rows; all 2,925 are archived with `requires_user_reply`; zero are directly enabled.
- Formal outputs are byte-identical to serialization of an in-memory build from the authoritative source and mapping files.

Immutable source verification:

```text
SOURCE_SHA256=3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534
COPY_SHA256=3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534
HASH_EQUAL=True
SOURCE_BYTES=7961787
COPY_BYTES=7961787
```

## Enabled-corpus quality audit

- Enabled rows: 800, all with stable unique snake-case IDs.
- Exact duplicate texts: 0.
- NFKC/punctuation/whitespace-normalized duplicate texts: 0.
- Question marks: 0.
- `requires_reply=true`: 0.
- Missing semantic groups/source references: 0.
- Invalid cooldowns or interrupt costs: 0.
- Enabled PII-risk markers: 0.
- Enabled false-context markers: 0.
- Enabled uncertain-intimacy markers: 0.
- Catchphrase-bearing rows: 0 (0%, below the 10% ceiling).
- Minimum/mean/median/maximum length: 11 / 21.92625 / 22 / 36.

Exact length distribution:

| Length | Count | Share | Required |
|---|---:|---:|---:|
| 8-16 | 200 | 25% | 25-35% |
| 17-24 | 360 | 45% | 35-45% |
| 25-36 | 240 | 30% | 20-30% |
| >36 | 0 | 0% | <=8% |

Maximum fixed opening shares:

- 2 characters: `今天`, 16/800 = 2.000%.
- 3-6 characters: 5/800 = 0.625% at each width.

Maximum fixed ending shares:

- 4 characters: 5/800 = 0.625%.
- 6 characters: 4/800 = 0.500%.
- 8 characters: 4/800 = 0.500%.
- 10 characters: 3/800 = 0.375%.

Public category-group inventory:

- `technical`: 350
- `system_ambient`: 140
- `character_life`: 99
- `daily_care`: 66
- `growth`: 56
- `emotional_reflection`: 31
- `easter_egg`: 30
- `career`: 28

Output-mode inventory:

- `self_talk`: 594
- `system_observe`: 140
- `ambient`: 66

Source-kind inventory:

- `rewritten_topic`: 566
- `new_ambient`: 140
- `curated_standalone`: 64
- `preserved_easter_egg`: 30

## Reproducibility

Two fresh builds were run into independent temporary roots using seed `20260722`. For every one of the four files, Build A SHA-256 equalled Build B SHA-256 and the checked-in formal output SHA-256. The temporary roots were path-checked under `C:\tmp\task3-rebuild-*` and then removed.

## Final verification

Full Python suite:

```text
python -B -m unittest discover -v
Ran 35 tests in 92.208s
OK
```

Task 3 syntax compile:

```text
python -B -m py_compile src/persona_corpus/schema.py src/persona_corpus/content_catalog.py src/persona_corpus/builder.py tools/build_corpus_v2.py tests/test_build.py
Exit code: 0
```

All generated `src/persona_corpus/__pycache__`, `tests/__pycache__`, and `tools/__pycache__` directories were removed and the remaining count was verified as zero.

## Self-review

- Verified the four exact headers and physical row widths independently of the loader.
- Verified that every formal output byte matches a fresh in-memory build.
- Verified all 75,375 source rows have non-empty dispositions and exact archive coverage.
- Verified all enabled safety, uniqueness, length, edge-frequency, metadata, and public-enum constraints.
- Verified no runtime or build-time network/model dependency and no non-standard Python package is introduced.
- Verified source/copy bytes after generation and all tests.
- Scoped the intended commit to Task 3 files only; unrelated pre-staged and dirty files remain untouched.

## Concerns

- The enabled inventory intentionally contains all curated technical topics, so technical rows are 350/800 in the library. The user's 10-20% technical requirement applies to simulated playback and remains a selector/simulator acceptance gate in later tasks, not a reason to delete traceable technical coverage here.
- `pii-review.tsv` contains conservative heuristic candidates. They remain disabled and require human review; the counts do not establish that the text contains real personal data.
- Verification used the installed Python 3.13 runtime. The implementation uses the Python standard library and syntax compatible with Python 3.11.
- The full 800-entry materialized catalog is deliberately large because storing complete sentences is an explicit product and audit requirement.

No remaining correctness issue was found in the Task 3 scope.
