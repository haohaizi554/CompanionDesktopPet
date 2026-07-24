# Persona Corpus v2 Implementation Plan

> **Historical plan; numerical targets superseded.** This file records the 2026-07-22 implementation path. Its roughly 800–1,200-row target, Easter egg `<=2%` limit, recent-50 quota, and curated-only runtime assumptions are not current release acceptance. The 2026-07-24 persona contract and expanded-runtime work require 806 curated-core rows + 51,326 approved safe legacy surfaces = 52,132 runtime rows grouped into exactly 533 semantic scenes, scene-first selection, and playback acceptance of Easter egg 8%–12%, seasoning 3%–6%, and dry-sharp 2%–4%. Counts and final hashes remain release gates on the integrated commit; see `README-persona-corpus.md` and `docs/release/2026-07-25-expanded-runtime-release-checklist.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 75,375-line Cartesian-product runtime corpus with an immutable, traceable, offline v2 corpus of roughly 800–1,200 independently written lines, plus auditing, extraction, scheduling, validation, simulation, reports, and WPF integration.

**Architecture:** Treat `src/CompanionDesktopPet/Assets/persona-corpus.tsv` as immutable source evidence. Standard-library Python tools produce intermediate structure, v2/archive/review datasets and reports; a strict selector enforces context, cooldown, budget and quota rules. The WPF app embeds the generated v2 resource and consumes only enabled safe lines while retaining its existing state, story and animation layers.

**Tech Stack:** Python 3.11 standard library (`argparse`, `csv`, `dataclasses`, `datetime`, `hashlib`, `json`, `random`, `re`, `statistics`, `unittest`); C#/.NET 9 WPF and xUnit; UTF-8 TSV/JSON/Markdown.

## Global Constraints

- Never modify or delete `src/CompanionDesktopPet/Assets/persona-corpus.tsv`; copy it byte-for-byte to `data/source/persona-corpus.original.tsv` and verify equal SHA-256 before and after all work.
- Do not call a network, external API or model at build time or runtime; do not add pandas, numpy or other third-party Python dependencies.
- Final enabled corpus has the exact 20-column schema from the approved spec, `requires_reply=false`, no `?` or `？`, no duplicate/normalized duplicate text and no runtime or build-time prefix/core/suffix Cartesian expansion.
- All disabled source material remains traceable in archive or review; all text files are UTF-8 and TSV text fields contain no tabs or real newlines.
- Scheduler group weights sum to `1.0`; technical is `0.10–0.20`, character_life is highest and easter_egg is at most `0.02`.
- Simulation covers 30 days and at least 10 deterministic seeds and treats every hard constraint violation as failure.
- Keep click hearts, drag tilt and landing spring; do not reintroduce blink or greeting actions.

---

### Task 1: Immutable source, strict models, loader and baseline audit

**Files:**
- Create: `data/source/persona-corpus.original.tsv`
- Create: `src/persona_corpus/__init__.py`
- Create: `src/persona_corpus/models.py`
- Create: `src/persona_corpus/loader.py`
- Create: `src/persona_corpus/normalization.py`
- Create: `tools/audit_corpus.py`
- Create: `tests/test_audit.py`
- Create: `reports/corpus-audit-before.md`

**Interfaces:**
- Produces: `load_legacy(path: Path) -> list[LegacyLine]`, `load_v2(path: Path, enabled_only: bool = False) -> list[CorpusLine]`, `sha256_file(path: Path) -> str`, `audit_legacy(lines: Sequence[LegacyLine]) -> AuditResult`.
- `LegacyLine` contains `source_line`, `category`, `text`; malformed rows raise `CorpusFormatError` containing path and one-based line number.

- [ ] **Step 1: Write failing loader and audit tests**

```python
class LoaderTests(unittest.TestCase):
    def test_bad_row_reports_line_number(self):
        with self.assertRaisesRegex(CorpusFormatError, r"line 2"):
            load_legacy(write_fixture("Debugging\tok\nmissing-tab\n"))

    def test_audit_detects_normalized_and_question_risks(self):
        rows = [LegacyLine(1, "ProactiveChat", "你现在做什么？"),
                LegacyLine(2, "ProactiveChat", "你现在做什么 ?")]
        result = audit_legacy(rows)
        self.assertEqual(2, result.question_count)
        self.assertEqual(2, result.high_risk_patterns["你现在"])
        self.assertEqual(1, result.normalized_duplicate_count)
```

- [ ] **Step 2: Run the tests and verify missing modules fail**

Run: `python -m unittest tests.test_audit -v`  
Expected: failure importing `src.persona_corpus.loader`.

- [ ] **Step 3: Implement strict TSV loading, normalization, SHA-256 and sub-quadratic audit metrics**

Use NFKC text normalization, punctuation/whitespace stripping and character 3-gram signatures. Count prefix lengths 2–6, suffix lengths 4/6/8/10, risk patterns, catchphrases, likely PII and bucket similar text by shared rare n-grams before SequenceMatcher comparison. Never perform all-pairs comparison over 75,000 rows.

- [ ] **Step 4: Copy the source bytes and generate baseline report**

Run:

```powershell
Copy-Item src/CompanionDesktopPet/Assets/persona-corpus.tsv data/source/persona-corpus.original.tsv
python tools/audit_corpus.py --input src/CompanionDesktopPet/Assets/persona-corpus.tsv --output reports/corpus-audit-before.md
```

Expected: source and copy SHA-256 are identical; report contains totals, distributions, examples and source line numbers.

- [ ] **Step 5: Run tests and commit**

Run: `python -m unittest tests.test_audit -v`  
Expected: all tests pass.

Commit: `feat: audit and preserve persona source corpus`

---

### Task 2: Recover prefix, topic and suffix structure

**Files:**
- Create: `src/persona_corpus/extraction.py`
- Create: `tools/extract_corpus_structure.py`
- Create: `tests/test_extraction.py`
- Create: `data/intermediate/extracted-prefixes.tsv`
- Create: `data/intermediate/extracted-topics.tsv`
- Create: `data/intermediate/extracted-suffixes.tsv`
- Create: `data/intermediate/source-line-map.tsv`

**Interfaces:**
- Consumes: `LegacyLine`, `normalize_text`.
- Produces: `extract_structure(lines: Sequence[LegacyLine]) -> ExtractionResult`; `SourceMapping` fields exactly match `source_line,category,original_text,prefix_id,topic_id,suffix_id,extraction_confidence`.

- [ ] **Step 1: Write failing extraction tests**

```python
def test_cartesian_rows_recover_shared_parts():
    result = extract_structure(make_two_by_two_by_two_rows())
    assert len(result.prefixes) == 2
    assert len(result.topics) == 2
    assert len(result.suffixes) == 2
    assert all(row.extraction_confidence >= 0.9 for row in result.mappings)

def test_easter_egg_is_standalone():
    result = extract_structure([LegacyLine(1, "EasterEgg", "玥玥把秘密藏进了书页。")])
    assert result.mappings[0].prefix_id == ""
    assert result.mappings[0].topic_id.startswith("egg_standalone_")
    assert result.mappings[0].suffix_id == ""
```

- [ ] **Step 2: Run targeted tests and confirm failure**

Run: `python -m unittest tests.test_extraction -v`  
Expected: import/function failure.

- [ ] **Step 3: Implement frequency-guided decomposition**

Build category-local prefix/suffix tries, rank candidates by cross-product support, select the non-overlapping split with maximum support and store confidence. Unsplit and low-confidence rows remain standalone and are later routed to review; EasterEgg always bypasses decomposition.

- [ ] **Step 4: Generate all four intermediate TSVs twice and compare hashes**

Run: `python tools/extract_corpus_structure.py --input src/CompanionDesktopPet/Assets/persona-corpus.tsv --output-dir data/intermediate`  
Expected: four non-empty TSVs with stable IDs and identical second-run SHA-256.

- [ ] **Step 5: Run tests and commit**

Commit: `feat: recover persona corpus combination structure`

---

### Task 3: Build curated v2, archive, review and PII outputs

**Files:**
- Create: `src/persona_corpus/schema.py`
- Create: `src/persona_corpus/content_catalog.py`
- Create: `src/persona_corpus/builder.py`
- Create: `tools/build_corpus_v2.py`
- Create: `tests/test_build.py`
- Create: `data/optimized/persona-corpus-v2.tsv`
- Create: `data/optimized/persona-corpus-archive.tsv`
- Create: `data/optimized/persona-corpus-review.tsv`
- Create: `reports/pii-review.tsv`

**Interfaces:**
- Produces: `build_v2(source, mappings, seed, pii_policy="review") -> BuildResult` and stable `CorpusLine.id`.
- Full v2 header is `id,category,category_group,topic_id,semantic_group,output_mode,trigger,required_context,tone,interrupt_cost,cooldown_hours,semantic_cooldown_hours,max_per_day,weight,requires_reply,enabled,text,source_kind,source_reference,rewrite_reason`.

- [ ] **Step 1: Write failing schema/build tests**

```python
def test_enabled_lines_are_standalone_and_need_no_reply():
    result = build_fixture_v2(seed=20260722)
    assert all(not row.requires_reply for row in result.enabled)
    assert all("?" not in row.text and "？" not in row.text for row in result.enabled)
    assert all(row.semantic_group and row.source_reference for row in result.enabled)

def test_ids_and_output_are_reproducible():
    assert serialize(build_fixture_v2(20260722)) == serialize(build_fixture_v2(20260722))
```

- [ ] **Step 2: Run tests and confirm they fail before the builder exists**

Run: `python -m unittest tests.test_build -v`.

- [ ] **Step 3: Implement explicit complete-sentence content catalog and deterministic migration**

Store complete sentences grouped by semantic topic; do not store combinable opener/core/closer arrays. Target 800–1,200 enabled rows: each non-ProactiveChat technical topic gets 1–2 observations, character-life topics get 3–5 distinct situations, care/reflection topics get 1–3 cautious lines, and system ambient covers available time/date events. Archive every original ProactiveChat row with `requires_user_reply`; route PII and uncertain intimacy/context to review.

- [ ] **Step 4: Generate outputs and assert traceability counts**

Run: `python tools/build_corpus_v2.py --input src/CompanionDesktopPet/Assets/persona-corpus.tsv --output data/optimized/persona-corpus-v2.tsv --seed 20260722`  
Expected: v2 has 800–1,200 enabled lines; archive/review are non-empty; every source line appears in a source mapping and at least one migration disposition.

- [ ] **Step 5: Run build tests and commit**

Commit: `feat: build curated offline persona corpus v2`

---

### Task 4: Implement strict validator and quality gates

**Files:**
- Create: `src/persona_corpus/validation.py`
- Create: `config/persona-scheduler.json`
- Create: `config/persona-review-allowlist.json`
- Create: `tools/validate_corpus_v2.py`
- Create: `tests/test_validation.py`

**Interfaces:**
- Produces: `validate_corpus(lines, scheduler_config, allowlist) -> ValidationReport`; CLI returns `0` only with zero hard errors.

- [ ] **Step 1: Write failing boundary tests**

```python
def test_validator_rejects_question_fake_context_and_duplicate():
    rows = [valid_line(id="a", text="你现在累不累？"),
            valid_line(id="b", text="你现在累不累？")]
    codes = {issue.code for issue in validate_corpus(rows, config(), allowlist()).errors}
    assert {"question", "fake_context", "duplicate_text"} <= codes

def test_weights_and_enums_are_strict():
    bad = config(technical=0.30, easter_egg=0.03)
    assert validate_config(bad).errors
```

- [ ] **Step 2: Verify failing tests, then implement all 27 validation groups**

Validation includes exact header/order, row width, IDs, normalized duplicates, enums/ranges, questions, context gates, PII, EasterEgg cooldown, high-cost weights, technical fake-context phrases, Cartesian structure signatures, catchphrase and length targets, config weights and simulation result ingestion. Centralize all documented exceptions with reasons in the allowlist JSON.

- [ ] **Step 3: Run validator**

Run: `python tools/validate_corpus_v2.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json`  
Expected: exit `0`, `0 hard errors`; warnings are included in after report with reasons.

- [ ] **Step 4: Run tests and commit**

Commit: `test: enforce persona corpus v2 quality gates`

---

### Task 5: Implement context, history and deterministic selector

**Files:**
- Create: `src/persona_corpus/context.py`
- Create: `src/persona_corpus/history.py`
- Create: `src/persona_corpus/selector.py`
- Create: `tests/test_selector.py`

**Interfaces:**
- Produces: `select_line(corpus: Sequence[CorpusLine], context: PersonaContext, history: SelectionHistory, now: datetime, seed: int | None = None) -> SelectedLine | None`.
- `SelectionHistory.to_json()` and `SelectionHistory.from_json(text)` preserve `selected_id,played_at,category,category_group,semantic_group,output_mode,trigger,interrupt_cost`.

- [ ] **Step 1: Write failing behavior tests**

Tests separately assert trigger match, required context, ID cooldown, semantic cooldown, max-per-day, 8-minute minimum, hourly budget, late-night budget, no adjacent technical/care/reflection, recent group quotas, EasterEgg rarity, deterministic seed, `None` when no candidate and JSON round-trip equality.

- [ ] **Step 2: Run selector tests and confirm failure**

Run: `python -m unittest tests.test_selector -v`.

- [ ] **Step 3: Implement the required 12-stage filter/score/select pipeline**

Apply filters in the approved order. Score by group deficit against rolling history, configured group weight, output-mode target deficit, row weight and interrupt penalty. Restrict weighted random choice to the highest score band and seed a local `random.Random`; never mutate global random state.

- [ ] **Step 4: Run tests and commit**

Commit: `feat: add deterministic offline persona selector`

---

### Task 6: Simulate 30 days and generate comparison reports

**Files:**
- Create: `src/persona_corpus/simulation.py`
- Create: `tools/simulate_persona.py`
- Create: `tests/test_simulation.py`
- Create: `reports/simulation-report.md`
- Create: `reports/corpus-audit-after.md`
- Create: `reports/corpus-rewrite-summary.md`
- Create: `reports/corpus-manual-review.md`

**Interfaces:**
- Produces: `simulate(corpus, config, days: int, seeds: Sequence[int]) -> SimulationReport`; report exposes every metric listed in the approved specification and a `hard_violations` collection.

- [ ] **Step 1: Write failing 30-day invariant tests**

```python
def test_thirty_days_ten_seeds_have_no_hard_violations():
    report = simulate(load_enabled(), load_config(), days=30, seeds=range(10))
    assert report.hard_violations == []
    assert 0.10 <= report.group_ratio["technical"] <= 0.20
    assert report.mode_ratio["self_talk"] + report.mode_ratio["ambient"] >= 0.65
    assert report.mode_ratio["user_direct"] <= 0.15
    assert report.group_ratio["easter_egg"] <= 0.02
```

- [ ] **Step 2: Implement deterministic trigger generation, simulation and report writers**

Generate app start, day parts, weekend, holiday, anniversary and long-silence contexts; future signals are deterministic nullable values. Reports include per-seed anomalies, before/after table, at least 50 rewrites, 20 archive reasons, 20 tone fixes and 20 fake-context fixes with source lines.

- [ ] **Step 3: Run simulation CLI twice and compare output hashes**

Run: `python tools/simulate_persona.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json --days 30 --seeds 10 --report reports/simulation-report.md`  
Expected: no hard violations and identical report hash on rerun.

- [ ] **Step 4: Run tests and commit**

Commit: `feat: simulate and report persona playback quality`

---

### Task 7: Integrate v2 into the .NET desktop pet

**Files:**
- Modify: `src/CompanionDesktopPet/CompanionDesktopPet.csproj`
- Modify: `src/CompanionDesktopPet/Services/PersonaCorpus.cs`
- Modify: `src/CompanionDesktopPet/Services/DialogueForest.cs`
- Modify: `src/CompanionDesktopPet/Services/SceneCatalog.cs`
- Modify: `src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs`
- Modify: `tests/CompanionDesktopPet.Tests/PersonaCorpusTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/OfflineCompanionAgentTests.cs`

**Interfaces:**
- `DialogueLine` expands to include v2 ID, category group, semantic group, output mode, trigger/context, cooldown, cost, daily max, weight and source metadata.
- `PersonaCorpus.All` contains only enabled v2 rows; `PersonaCorpus.EasterEggs` remains a filtered view.

- [ ] **Step 1: Replace legacy-count tests with v2 safety and metadata tests**

Assert 800–1,200 lines, zero questions, zero duplicate normalized text, no reply-required row, complete semantic/cooldown/source metadata, technical share not treated as a runtime weight, PII review names absent from enabled corpus, and embedded resource name resolves.

- [ ] **Step 2: Run .NET tests and verify they fail against the legacy parser**

Run: `dotnet test CompanionDesktopPet.sln -c Release --no-restore --filter PersonaCorpusTests`.

- [ ] **Step 3: Embed v2 and adapt scene construction**

Point the csproj embedded resource at `../../data/optimized/persona-corpus-v2.tsv` with logical name `CompanionDesktopPet.Assets.persona-corpus-v2.tsv`. Parse the exact header by column name, reject disabled/unsafe rows, and build scenes from semantic groups rather than fixed `Skip(index * 8)` slices. Map v2 output modes and cooldown/cost directly into `SceneDefinition`.

- [ ] **Step 4: Enforce runtime recent-group constraints**

Use `SceneHistory` to prevent adjacent technical/care/reflection and respect semantic cooldown/daily max. Remove the legacy 46% technical `WeightedTree` bias; align group choice with `config/persona-scheduler.json` targets represented as constants covered by tests.

- [ ] **Step 5: Run all .NET tests and commit**

Commit: `feat: run desktop pet on curated persona corpus v2`

---

### Task 8: Documentation, final verification and single-EXE delivery

**Files:**
- Create: `README-persona-corpus.md`
- Modify: `README.md`
- Modify: `scripts/Verify-Publish.ps1`
- Regenerate: `outputs/CompanionDesktopPet/佳怡桌宠.exe`
- Regenerate: `outputs/CompanionDesktopPet/使用说明.txt`

**Interfaces:**
- Documentation contains the 20 required subjects and exact commands using the actual source path.

- [ ] **Step 1: Write the complete corpus README and final reports**

Document why 75,375 rows are not independent content, the 20 v2 fields, build/audit/extract/validate/simulate/selector commands, extension rules, PII policy and future context signals.

- [ ] **Step 2: Run every Python gate freshly**

```powershell
python -m unittest discover -s tests -v
python tools/validate_corpus_v2.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json
python tools/simulate_persona.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json --days 30 --seeds 10 --report reports/simulation-report.md
```

Expected: all tests pass, validator exits 0, simulation has zero hard violations.

- [ ] **Step 3: Re-run source hash and reproducibility gates**

Compare source/copy SHA-256, rebuild with seed `20260722`, compare v2 SHA-256 before/after, and fail if either differs.

- [ ] **Step 4: Run .NET tests, publish and smoke-test**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj -c Release -r win-x64 --self-contained true --no-restore -o publish
powershell -ExecutionPolicy Bypass -File scripts/Verify-Publish.ps1 -ExePath outputs/CompanionDesktopPet/佳怡桌宠.exe
```

Expected: zero test failures; output folder contains one EXE and zero DLLs; process launch smoke test passes.

- [ ] **Step 5: Request code review, fix all Critical/Important findings and commit**

Commit: `docs: finish persona corpus v2 delivery`

