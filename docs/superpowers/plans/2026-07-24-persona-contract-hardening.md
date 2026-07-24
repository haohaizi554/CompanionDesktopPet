# Persona Contract Hardening Implementation Plan

> **Scope note:** “current 800-row corpus” below is the pre-expansion wording used when this plan was drafted. The completed curated core is 806 rows. The later expanded-runtime release contract is 806 core + 51,326 approved safe legacy surfaces = 52,132 runtime rows and exactly 533 semantic scenes, with scene-first selection and playback acceptance of Easter egg 8%–12%, seasoning 3%–6%, and dry-sharp 2%–4%. This plan establishes the contract/manifest foundation; it does not by itself prove the expanded artifact or its final hashes.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace duplicated corpus taxonomy assumptions with one machine-readable contract and make the builder, validator, selector, editorial catalog, and C# runtime reject drift.

**Architecture:** `config/persona-contract.json` is the semantic source of truth. A small immutable Python loader exposes typed constants; focused validation-rule modules consume that contract while `validation.py` remains the compatibility facade. A generated C# contract file and parity tests keep the embedded runtime parser aligned without shipping external files.

**Tech Stack:** Python 3 standard library, `unittest`, JSON/TSV, .NET 9/xUnit.

## Global Constraints

- Work only on `feat/corpus-contract-hardening` in the isolated worktree.
- Do not modify `simulation.py`, UI code, publish/output executables, or unrelated untracked files.
- Keep the current 800-row corpus valid; do not perform the later 50k-row expansion here.
- Every production behavior begins with an observed failing test.
- Preserve the public `src.persona_corpus.validation` facade and CLI behavior.

---

### Task 1: Shared taxonomy and catalog correction

**Files:**
- Create: `config/persona-contract.json`
- Create: `src/persona_corpus/contract.py`
- Modify: `src/persona_corpus/content_catalog.py`
- Modify: `src/persona_corpus/builder.py`
- Test: `tests/test_contract.py`
- Test: `tests/test_build.py`

**Interfaces:**
- Produces `PERSONA_CONTRACT`, `CATEGORY_GROUP_BY_CATEGORY`, `category_group_for(category)`, controlled enums, and scheduler acceptance values.
- Builder must derive every row group from `category_group_for`; no override table is allowed.

- [ ] Write tests proving the contract maps every category exactly once and proving raw Career/Study/EnglishPractice catalog entries already carry career/growth.
- [ ] Run the focused tests and record failure from the missing contract and bad source metadata.
- [ ] Add the strict JSON loader and canonical taxonomy; mechanically correct the 75 catalog entries and delete builder overrides.
- [ ] Rebuild the canonical corpus and prove the output is deterministic and still has eight real groups.
- [ ] Commit the complete taxonomy slice.

### Task 2: Config, selector, and C# parity

**Files:**
- Modify: `config/persona-scheduler.json`
- Modify: `src/persona_corpus/validation.py`
- Modify: `src/persona_corpus/selector.py`
- Create: `tools/generate_persona_contract_cs.py`
- Create: `src/CompanionDesktopPet/Services/PersonaContract.g.cs`
- Modify: `src/CompanionDesktopPet/Services/PersonaCorpus.cs`
- Test: `tests/test_contract.py`
- Test: `tests/test_selector.py`
- Test: `tests/CompanionDesktopPet.Tests/PersonaCorpusTests.cs`

**Interfaces:**
- Scheduler targets are exactly technical .18, growth .10, career .07, daily_care .10, emotional_reflection .10, character_life .27, easter_egg .10, system_ambient .08.
- Easter eggs use rolling 10/max 1 and are included in blocked adjacent groups.

- [ ] Write RED tests for 10% EasterEgg target/quota/adjacency and byte-stable C# generation.
- [ ] Move config acceptance constants to the contract and make config validation/selector consume them.
- [ ] Generate C# categories, category groups, tones, and category-group lookup; make the runtime reject cross-field mismatch.
- [ ] Run focused Python and .NET parity tests and commit.

### Task 3: Schema, safety, semantic, and lineage rules

**Files:**
- Create: `src/persona_corpus/validation_rules/schema_rules.py`
- Create: `src/persona_corpus/validation_rules/safety_rules.py`
- Create: `src/persona_corpus/validation_rules/lineage_rules.py`
- Create: `src/persona_corpus/validation_rules/distribution_rules.py`
- Modify: `src/persona_corpus/validation.py`
- Test: `tests/test_validation.py`

**Interfaces:**
- Each rule module accepts corpus rows plus an issue sink; `validation.py` keeps `validate_config`, `validate_corpus`, `validate_file`, and report types stable.
- Canonical lineage resolves `variant`, legacy line, source category, mapping topic, and row topic as one tuple.

- [ ] Add one negative fixture per missing category/group, semantic consistency, disabled safety, normalized uniqueness, and lineage condition; observe the expected RED codes.
- [ ] Enforce category/group on every row and semantic-group equality for group/mode/trigger/context/both cooldowns/max-per-day/interrupt-cost.
- [ ] Pre-audit disabled rows for PII, questions/reply requirements, fake context, and unsafe emotional claims; normalize uniqueness across all rows.
- [ ] Enforce exact source-reference grammar, variant existence, legacy bounds/existence, mapping category/topic equality, and dangling-reference rejection.
- [ ] Split rule families while retaining the facade, run the full Python suite, and commit.

### Task 4: Editorial manifest and dry-sharp bootstrap

**Files:**
- Create: `config/persona-editorial-manifest.json`
- Create: `src/persona_corpus/editorial.py`
- Modify: `src/persona_corpus/content_catalog.py`
- Modify: `config/persona-contract.json`
- Modify: `src/CompanionDesktopPet/Services/PersonaContract.g.cs`
- Test: `tests/test_contract.py`
- Test: `tests/test_build.py`
- Test: `tests/test_validation.py`

**Interfaces:**
- Compatibility tuples are derived from a single manifest and remain importable by their old names.
- `dry_sharp` target inventory is 5% with 4-6% expansion acceptance, playback target 3%, rolling 20/max 1, and forbidden care/emotional/EasterEgg/late-night/holiday/anniversary usage.

- [ ] Add RED tests for manifest disjointness/completeness and dry-sharp safety/rolling constraints.
- [ ] Load both compatibility tuples from the manifest and verify adjudicated variants exist in catalog while retired variants remain explicit manifest records.
- [ ] Add a small hand-written dry-sharp sample only; stage the 5% inventory gate for the later expanded corpus.
- [ ] Validate tone placement and rolling playback limits, then commit.

### Task 5: Documentation and final verification

**Files:**
- Modify: `README-persona-corpus.md`
- Modify: `docs/superpowers/specs/2026-07-22-persona-corpus-v2-design.md`

- [ ] Document taxonomy ownership, staged dry-sharp expansion, 10% EasterEgg target, lineage grammar, and disabled-row preflight behavior.
- [ ] Run all Python tests, canonical validator, affected .NET tests, build determinism, and `git diff --check` fresh.
- [ ] Review the diff for UI/publish/output leakage and commit documentation only after evidence is clean.
