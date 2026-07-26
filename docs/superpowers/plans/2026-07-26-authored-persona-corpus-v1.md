# Authored Persona Corpus v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Ship a 30,000-line, static, non-Cartesian, adult-safe authored persona corpus that replaces all selectable legacy surface variants.

**Architecture:** Authored source is split into 100 literal 300-row UTF-8 TSV batches. A strict Python loader verifies a versioned manifest and produces the runtime TSV and ledger deterministically; C# reads the extended contract and enforces relationship-profile quotas before scene selection. Legacy source remains audit-only and cannot materialize runtime rows.

**Tech Stack:** Python 3 standard library, JSON/TSV/SHA-256, .NET 9 WPF/C#, xUnit, PowerShell GitHub Actions.

## Global Constraints

- Exactly 100 source batches, b001 through b100, each with exactly 300 literal rows; exactly 30,000 enabled runtime rows.
- Source rows must be written as static TSV text using apply_patch; no template, Cartesian, or programmatic sentence generation is allowed.
- All runtime authored rows have requires_reply=false, source_kind=curated_authored, and no enabled legacy_surface_variant rows exist.
- Valid relationship profiles are neutral, warm_friend, playful_friend, and nickname_easter_egg; warm_friend is non-explicit/non-exclusive and nickname rows are exact allowlisted content.
- warm_friend may occur at most twice in the most recent 20 plays; nickname_easter_egg may occur at most once in the most recent 100 plays.
- ide_foreground, active_90m, and idle_return are zero-row authored contexts until end-to-end runtime collection and simulation exists.
- Build, validation, simulation, Python tests, C# tests, CI evidence parsing, generated C# contract, embedded asset, and release hashes must agree without warning allowlists.
- Preserve current uncommitted README and release/audit documentation until their final audited update; do not stage them with intermediate code commits.

---

### Task 1: Define the authored source and manifest loader

**Files:**
- Create: src/persona_corpus/authored_catalog.py
- Create: tools/build_authorship_manifest.py
- Create: tests/test_authored_catalog.py
- Create: data/authored/v1/.gitkeep
- Modify: src/persona_corpus/schema.py

**Interfaces:**
- Produces AUTHORED_HEADER, AuthoredEntry, AuthoredCatalog, parse_authored_batches(authored_dir: Path) -> tuple[AuthoredEntry, ...], and load_authored_catalog(authored_dir: Path, manifest_path: Path) -> AuthoredCatalog.
- AuthoredCatalog.entries is sorted by (batch_id, variant_id) and has exactly one entry per source row.
- AuthoredCatalog exposes batch_digests, root_sha256, and ledger_rows().
- tools/build_authorship_manifest.py parses the complete literal directory with parse_authored_batches and writes a canonical JSON manifest to --output.

- [ ] **Step 1: Write failing loader/manifest tests**

~~~python
def test_load_authored_catalog_requires_all_100_300_row_batches(tmp_path: Path) -> None:
    with self.assertRaisesRegex(ValueError, "expected 100 batches"):
        load_authored_catalog(tmp_path / "authored", tmp_path / "manifest.json")

def test_load_authored_catalog_rejects_manifest_text_hash_drift(tmp_path: Path) -> None:
    authored_dir, manifest = write_valid_authored_fixture(tmp_path)
    mutate_first_text(authored_dir / "b001.tsv")
    with self.assertRaisesRegex(ValueError, "text_sha256"):
        load_authored_catalog(authored_dir, manifest)
~~~

- [ ] **Step 2: Run the new tests and record the failing loader error**

Run: python -m unittest tests.test_authored_catalog -v

Expected: FAIL because authored_catalog does not exist.

- [ ] **Step 3: Implement strict source parsing**

Define the source header with variant_id, batch_id, playback metadata,
relationship_profile, text, and review_status. Reject UTF-8 decoding errors,
non-TSV physical rows, unknown/missing columns, non-literal batch names,
non-300 row batches, duplicate IDs/text, invalid profile, non-approved review
state, or a manifest whose per-batch/root SHA-256 does not match the sorted
source records. Build digest input with NUL-separated UTF-8 field values so a
row boundary cannot be ambiguous. Implement build_authorship_manifest.py with
required --authored-dir and --output arguments; it must serialize sorted keys,
the 100 exact batch IDs, every batch digest, and the root digest without
reading or writing any runtime corpus file.

- [ ] **Step 4: Add ledger serialization tests**

~~~python
def test_ledger_rows_are_one_to_one_and_hash_bound(self) -> None:
    catalog = load_authored_catalog(self.authored_dir, self.manifest_path)
    ledger = list(catalog.ledger_rows())
    self.assertEqual(30000, len(ledger))
    self.assertEqual(30000, len({row.variant_id for row in ledger}))
    self.assertEqual(catalog.root_sha256, ledger[0].root_sha256)
~~~

- [ ] **Step 5: Run focused tests and commit**

Run: python -m unittest tests.test_authored_catalog -v

Commit:

~~~bash
git add src/persona_corpus/authored_catalog.py src/persona_corpus/schema.py tools/build_authorship_manifest.py tests/test_authored_catalog.py data/authored/v1/.gitkeep
git commit -m "feat: add hash-bound authored corpus loader"
~~~

### Task 2: Version the persona contract and generated C# contract

**Files:**
- Modify: config/persona-contract.json
- Modify: config/schemas/persona-contract.schema.json
- Modify: src/persona_corpus/contract.py
- Modify: tools/generate_persona_contract_cs.py
- Modify: src/CompanionDesktopPet/Services/PersonaContract.g.cs
- Modify: tests/test_contract.py

**Interfaces:**
- PersonaContract.relationship_profiles: frozenset[str] contains exactly the four global profiles.
- release_inventory contains authored_runtime_rows, semantic_scene_count, and legacy_surface_rows where the last value is exactly 0.
- Generated C# exposes ControlledRelationshipProfiles, ExpectedAuthoredRuntimeRows, and ExpectedLegacySurfaceRows.

- [ ] **Step 1: Add failing contract-schema tests**

~~~python
def test_contract_requires_exact_authored_release_inventory(self) -> None:
    raw = load_json(CONTRACT_PATH)
    raw["release_inventory"]["legacy_surface_rows"] = 1
    with self.assertRaisesRegex(PersonaContractError, "legacy_surface_rows"):
        load_persona_contract(write_json(self.temp, raw))
~~~

- [ ] **Step 2: Implement contract/schema fields**

Set authored_runtime_rows=30000, legacy_surface_rows=0, and add
relationship_profiles under controlled values. Require the exact profile set,
positive authored count, zero legacy count, and C# generated source matching
--check output.

- [ ] **Step 3: Regenerate and verify**

~~~bash
python tools/generate_persona_contract_cs.py
python tools/generate_persona_contract_cs.py --check
python -m unittest tests.test_contract -v
~~~

- [ ] **Step 4: Commit**

~~~bash
git add config/persona-contract.json config/schemas/persona-contract.schema.json src/persona_corpus/contract.py tools/generate_persona_contract_cs.py src/CompanionDesktopPet/Services/PersonaContract.g.cs tests/test_contract.py
git commit -m "feat: version authored corpus relationship contract"
~~~

### Task 3: Build authored rows only and replace legacy runtime lineage

**Files:**
- Modify: src/persona_corpus/builder.py
- Modify: src/persona_corpus/validation_rules/lineage_rules.py
- Modify: src/persona_corpus/validation_rules/surface_rules.py
- Modify: src/persona_corpus/validation_rules/safety_rules.py
- Modify: tools/build_corpus_v2.py
- Modify: tests/test_build.py
- Modify: tests/test_validation.py
- Modify: tests/test_surface_variants.py

**Interfaces:**
- build_v2(..., authored: AuthoredCatalog) emits exactly authored.entries as enabled rows and never calls legacy surface materialization.
- Runtime source_reference is catalog:authored-v1:<batch_id>;variant:<variant_id>.
- build_repository_registry() derives expected runtime lineage from the authored catalog and ledger.

- [ ] **Step 1: Write red build/lineage tests**

~~~python
def test_authored_build_has_exactly_one_runtime_row_per_authored_entry(self) -> None:
    result = build_v2([], [], 20260726, authored=self.catalog)
    self.assertEqual(30000, len(result.enabled))
    self.assertEqual(0, sum(row.source_kind == "legacy_surface_variant" for row in result.enabled))

def test_enabled_legacy_surface_is_a_hard_validation_error(self) -> None:
    report = validate([legacy_surface_fixture()])
    self.assertIn("enabled_legacy_surface", issue_codes(report))
~~~

- [ ] **Step 2: Run the red tests**

Run: python -m unittest tests.test_build tests.test_validation tests.test_surface_variants -v

Expected: FAIL because the existing builder materializes legacy surfaces.

- [ ] **Step 3: Replace runtime materialization**

Route --authored-dir data/authored/v1 --authorship-manifest
config/persona-authorship-manifest.json through the build tool. Preserve
legacy source/archive/surface files as read-only audit outputs, but remove
their enabled-row extension from build_v2. Replace catalog registry lineage
with authored variant/manifest lineage. Serialize a one-row-per-authored-line
ledger as a tracked output.

- [ ] **Step 4: Enforce anti-mechanical and relationship hard rules**

Make Cartesian grid detection source-kind independent. Add indexed similarity
candidate detection and require a tracked exact adjudication for permitted
high-similarity pairs. Reject sexual/minor/coercive/exclusive/dependency,
unsupported-state, nickname-not-allowlisted, and reply-seeking content.

- [ ] **Step 5: Run focused verification and commit**

~~~bash
python -m unittest tests.test_build tests.test_validation tests.test_surface_variants -v
python tools/build_corpus_v2.py --authored-dir data/authored/v1 --authorship-manifest config/persona-authorship-manifest.json --output data/optimized/persona-corpus-v2.tsv
~~~

~~~bash
git add src/persona_corpus/builder.py src/persona_corpus/validation_rules tools/build_corpus_v2.py tests/test_build.py tests/test_validation.py tests/test_surface_variants.py
git commit -m "feat: build runtime corpus from authored rows only"
~~~

### Task 4: Parse relationship profiles in C# and enforce profile budgets

**Files:**
- Modify: src/CompanionDesktopPet/Services/PersonaCorpus.cs
- Modify: src/CompanionDesktopPet/Services/SceneCatalog.cs
- Modify: src/CompanionDesktopPet/Services/SceneEngine.cs
- Modify: tests/CompanionDesktopPet.Tests/PersonaCorpusTests.cs
- Modify: tests/CompanionDesktopPet.Tests/SceneEngineTests.cs

**Interfaces:**
- PersonaLine.RelationshipProfile is a controlled, required field.
- SceneDefinition.RelationshipProfile participates in semantic-scene metadata equality.
- SceneHistory.MeetsRelationshipProfileQuota(SceneDefinition scene) implements recent-20 and recent-100 rules.

- [ ] **Step 1: Add failing parser and quota tests**

~~~csharp
[Fact]
public void PersonaCorpus_RejectsUnknownRelationshipProfile() =>
    Assert.Throws<InvalidDataException>(() => LoadCorpus("relationship_profile=exclusive"));

[Fact]
public void SceneHistory_BlocksThirdWarmFriendInRecentTwenty()
{
    var history = HistoryWithProfiles("warm_friend", "warm_friend");
    Assert.False(history.MeetsRelationshipProfileQuota(WarmFriendScene));
}
~~~

- [ ] **Step 2: Implement field parsing and scene consistency**

Extend the TSV header, required-field parser, C# controlled values, scene
signature, scene history entry, snapshot migration, and corruption checks.
Use real profile values instead of a tone heuristic. Preserve backward
snapshot compatibility by migrating absent historical profile entries to
neutral only when the snapshot schema formally permits it.

- [ ] **Step 3: Implement selector gates before score selection**

Filter profile-quota-ineligible scenes in normal, click fallback, safe
feedback, and automatic selection paths before Score. Do not change random
seed order for candidates that remain eligible.

- [ ] **Step 4: Run C# tests and commit**

~~~bash
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj -c Release --filter "FullyQualifiedName~PersonaCorpusTests|FullyQualifiedName~SceneEngineTests" --logger "console;verbosity=normal"
~~~

~~~bash
git add src/CompanionDesktopPet/Services/PersonaCorpus.cs src/CompanionDesktopPet/Services/SceneCatalog.cs src/CompanionDesktopPet/Services/SceneEngine.cs tests/CompanionDesktopPet.Tests/PersonaCorpusTests.cs tests/CompanionDesktopPet.Tests/SceneEngineTests.cs
git commit -m "feat: enforce relationship profiles in runtime selection"
~~~

### Task 5: Write the 30,000 literal authored batches

**Files:**
- Create: data/authored/v1/b001.tsv through data/authored/v1/b100.tsv
- Create: config/persona-authorship-manifest.json
- Test: tests/test_authored_catalog.py

**Interfaces:**
- Every batch has the exact AUTHORED_HEADER, 300 UTF-8 literal rows, matching batch ID, and review_status=approved.
- Batch range assignment is fixed:
  - b001-b018: technical (5,400)
  - b019-b028: growth (3,000)
  - b029-b035: career (2,100)
  - b036-b045: daily_care (3,000)
  - b046-b055: emotional_reflection (3,000)
  - b056-b082: character_life (8,100)
  - b083-b092: easter_egg (3,000)
  - b093-b100: system_ambient (2,400)

- [ ] **Step 1: Create each batch as literal lines**

Use apply_patch to create each TSV. Every row must contain a unique descriptive
variant_id, a unique standalone text, audited metadata, and one relationship
profile. Do not create a generator, CSV expander, Python list comprehension,
or concatenation utility for source sentences. Write no ide_foreground,
active_90m, or idle_return row.

- [ ] **Step 2: Create manifest from reviewed source, not a hand-guessed digest**

After every literal source batch exists, run the exact manifest command below
to calculate sorted batch text/metadata hashes and root hash. Commit the
generated JSON; do not manually type SHA-256 values.

~~~bash
python tools/build_authorship_manifest.py --authored-dir data/authored/v1 --output config/persona-authorship-manifest.json
~~~

- [ ] **Step 3: Run source-quality gates**

~~~bash
python -m unittest tests.test_authored_catalog tests.test_validation tests.test_surface_variants -v
python tools/build_corpus_v2.py --authored-dir data/authored/v1 --authorship-manifest config/persona-authorship-manifest.json --output data/optimized/persona-corpus-v2.tsv
python tools/validate_corpus_v2.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json --allowlist config/persona-review-allowlist.json --simulation reports/simulation-events.json
~~~

Expected: exactly 30,000 rows, zero enabled legacy surfaces, zero warnings,
and no unresolved similarity/relationship issue.

- [ ] **Step 4: Commit content in category-sized stages**

Commit technical/growth/career, then daily/emotional/character, then
easter/ambient plus manifest/output. Each stage must run its current batch
loader test and must not claim a final inventory until all 100 batches exist.

### Task 6: Regenerate runtime assets and expand simulation coverage

**Files:**
- Modify: src/persona_corpus/selector.py
- Modify: src/persona_corpus/simulation.py
- Modify: src/persona_corpus/simulation_core/scenarios.py
- Modify: tests/test_selector.py
- Modify: tests/test_simulation.py
- Modify: reports/simulation-events.json
- Modify: data/optimized/persona-corpus-v2.tsv
- Create: data/optimized/persona-authorship-ledger.tsv

**Interfaces:**
- Python selector applies the same profile quotas as C#.
- Scenario generation covers all actual dayparts, four seasons, holiday and non-holiday calendars, all direct events, and every implemented nullable signal state.

- [ ] **Step 1: Add red budget and profile simulation tests**

~~~python
def test_warm_friend_quota_blocks_third_line_in_recent_twenty():
    assert not select_line(rows, history=two_recent_warm_friend()).is_warm_friend

def test_simulation_can_trigger_night_hour_interval_and_adjacent_category_violations():
    report = simulate_adversarial_budget_cases(rows)
    assert report.detected_rule_families == {"night", "hour", "interval", "adjacent"}
~~~

- [ ] **Step 2: Implement shared profile/budget scenarios**

Add explicit adversarial scenarios rather than relying on natural schedule
spacing. Keep the subseed namespace versioned and include corpus/config/
subseed-function SHA-256 values in reproducibility output.

- [ ] **Step 3: Regenerate canonical assets and verify deterministic rebuild**

Run the build twice into separate temporary output roots and compare the
runtime TSV, ledger, and manifest byte for byte. Then regenerate the tracked
runtime asset and committed simulation events from the same source.

- [ ] **Step 4: Commit**

~~~bash
git add src/persona_corpus/selector.py src/persona_corpus/simulation.py src/persona_corpus/simulation_core tests/test_selector.py tests/test_simulation.py reports/simulation-events.json data/optimized/persona-corpus-v2.tsv data/optimized/persona-authorship-ledger.tsv
git commit -m "test: simulate authored corpus relationship and budget gates"
~~~

### Task 7: Harden CI and release checks for authored assets

**Files:**
- Modify: .github/workflows/ci-cd.yml
- Modify: tests/Ci-TestEvidence.Contract.ps1
- Modify: README-persona-corpus.md
- Modify: docs/release/2026-07-25-expanded-runtime-release-checklist.md

**Interfaces:**
- CI rebuilds authored runtime data in a temporary output root and fails on a byte/hash mismatch.
- CI records authored row count, zero legacy row count, manifest root hash, and generated embedded resource hash.

- [ ] **Step 1: Add CI contract test cases for wrong authored inventory and stale manifest**

~~~powershell
Assert-Rejected -Case 'authored inventory mismatch' -ExpectedMessage '30000' -Action {
  Assert-AuthoredReleaseEvidence -ExpectedRows 30000 -ActualRows 29999
}
~~~

- [ ] **Step 2: Implement temporary deterministic rebuild gate**

Use a dedicated temporary output directory, invoke the authored build with the
tracked source/manifest, byte-compare all derived assets, then delete only
that verified temporary directory. Do not silently accept validation warnings.

- [ ] **Step 3: Update user-facing corpus documentation**

Document the literal authored inventory, non-Cartesian rule, relationship
profiles, source/batch audit process, known content boundary, and full rebuild
commands. Remove every claim that legacy surface rows are runtime content.

- [ ] **Step 4: Run CI-equivalent commands and commit**

~~~bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Ci-TestEvidence.Contract.ps1
python -m unittest discover -s tests -v
dotnet test CompanionDesktopPet.sln -c Release --no-restore --logger "trx;LogFileName=authored-final.trx"
~~~

~~~bash
git add .github/workflows/ci-cd.yml tests/Ci-TestEvidence.Contract.ps1 README-persona-corpus.md docs/release/2026-07-25-expanded-runtime-release-checklist.md
git commit -m "ci: verify authored corpus release evidence"
~~~

### Task 8: Final audit, build, and staged release

**Files:**
- Modify: README.md
- Modify: docs/audits/2026-07-25-review-remediation.md
- Modify: docs/release/2026-07-25-expanded-runtime-release-checklist.md

- [ ] **Step 1: Run all final gates fresh**

~~~bash
git diff --check
python -m unittest discover -s tests -v
python tools/generate_persona_contract_cs.py --check
python tools/validate_corpus_v2.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json --allowlist config/persona-review-allowlist.json --simulation reports/simulation-events.json
dotnet test CompanionDesktopPet.sln -c Release --no-restore --logger "console;verbosity=normal"
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
~~~

- [ ] **Step 2: Verify release asset evidence**

Verify the published executable is the intended self-contained file, capture
SHA-256, validate its embedded corpus inventory/hash using the project
verification tool, and run a startup/selection smoke test from a fresh
settings directory.

- [ ] **Step 3: Update audit/release documentation from fresh output only**

Record the exact commit, commands, test counts, source/runtime row counts,
zero legacy count, root hash, release asset hash, CI run URL, and any
non-blocking deferred item. Remove stale counts or unverifiable claims.

- [ ] **Step 4: Review, staged push, and release**

Request a whole-branch review, resolve every Critical/Important finding,
push verified staged commits to main, wait for GitHub Actions, create a
Chinese release description, attach the verified executable and checksums,
then proxy-download the release asset to verify it can be consumed.
