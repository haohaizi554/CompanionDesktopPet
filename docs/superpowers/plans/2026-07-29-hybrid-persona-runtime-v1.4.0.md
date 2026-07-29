# Persona Runtime v1.4.0 Hybrid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship v1.4.0 with one deterministic 82,132-line runtime that keeps all 30,000 authored lines and safely restores all 52,132 v1.2.1 legacy runtime lines, with legacy playback centered at 30%.

**Architecture:** Introduce an explicit `hybrid` build profile and build authored/legacy partitions independently before a deterministic merge. Derive a two-value source tier from existing `source_kind`, normalize only legacy metadata needed by the current contract, and implement the same rolling-window source-tier policy in Python simulation and C# runtime.

**Tech Stack:** Python 3.12, pytest, C#/.NET 8, xUnit, PowerShell, PyInstaller, GitHub Actions, GitHub CLI.

## Global Constraints

- Release version and GitHub Release title are exactly `v1.4.0`.
- Runtime inventory is exactly 82,132 rows: 30,000 authored plus 52,132 legacy, with 1,723 scenes.
- Normalized text and line IDs are globally unique; authored text is unchanged and legacy stable ID/source/text hashes match v1.2.1.
- Legacy playback uses a recent-100 target of 0.30, aggregate acceptance 0.25–0.35, and per-seed acceptance 0.20–0.40.
- Missing source tier in v1.3 history migrates to `authored`; relationship and identity quotas remain unchanged.
- Dry-sharp inventory is exactly 12 scenes: all 8 authored scenes plus 4 deterministic legacy scenes.
- Safety gates cannot be relaxed to satisfy source-tier balance.
- Release notes are concrete Chinese Markdown; the GitHub Release body must not be empty.
- GitHub traffic uses `http://127.0.0.1:7890`; stale worktrees are removed only after published assets are downloaded and hash-verified.

---

### Task 1: Version the hybrid corpus contract

**Files:**
- Modify: `config/persona-contract.json`
- Modify: `config/schemas/persona-contract.schema.json`
- Modify: `src/persona_corpus/contract.py`
- Modify: `tools/generate_persona_contract_cs.py`
- Regenerate: `src/CompanionDesktopPet/Services/PersonaContract.g.cs`
- Test: `tests/test_contract.py`
- Test: `tests/test_config_provenance.py`

**Interfaces:**
- Consumes: `load_persona_contract(path: Path) -> PersonaContract`.
- Produces: `PersonaContract.runtime_profile: str`, `PersonaContract.source_tier: Mapping[str, object]`, and generated C# constants `RuntimeProfile`, `ExpectedRuntimeRows`, `ExpectedAuthoredRuntimeRows`, `ExpectedLegacyRuntimeRows`, `ExpectedSceneCount`, `SourceTierRecentWindow`, `SourceTierTarget`, `SourceTierLowerBound`, `SourceTierUpperBound`, `SourceTierPerSeedLowerBound`, `SourceTierPerSeedUpperBound`.

- [ ] **Step 1: Write failing contract tests.**

```python
def test_release_contract_declares_exact_hybrid_inventory():
    contract = load_persona_contract()
    assert contract.runtime_profile == "hybrid"
    assert contract.expanded_runtime_rows == (82_132, 82_132)
    assert contract.expected_authored_runtime_rows == 30_000
    assert contract.expected_legacy_runtime_rows == 52_132
    assert contract.expected_scene_count == 1_723

def test_release_contract_declares_source_tier_policy():
    policy = load_persona_contract().source_tier
    assert policy == {
        "recent_window": 100,
        "warmup_observations": 20,
        "target": 0.30,
        "acceptance": [0.25, 0.35],
        "per_seed_acceptance": [0.20, 0.40],
        "missing_history_default": "authored",
    }
```

- [ ] **Step 2: Run the tests and verify RED.**

Run: `python -m pytest tests/test_contract.py tests/test_config_provenance.py -q`

Expected: FAIL because `PersonaContract` has no `runtime_profile` or `source_tier`, and current legacy count is zero.

- [ ] **Step 3: Add the typed contract fields and exact JSON/schema rules.**

```python
@dataclass(frozen=True, slots=True)
class PersonaContract:
    runtime_profile: str
    source_tier: Mapping[str, object]
    # existing fields remain unchanged

def _validate_source_tier(value: object) -> Mapping[str, object]:
    policy = _mapping(value, "source_tier")
    if policy["recent_window"] != 100 or policy["warmup_observations"] != 20:
        raise PersonaContractError("source_tier windows must be 100/20")
    if policy["target"] != 0.30 or policy["acceptance"] != [0.25, 0.35]:
        raise PersonaContractError("source_tier target/acceptance must be 0.30/[0.25, 0.35]")
    if policy["per_seed_acceptance"] != [0.20, 0.40]:
        raise PersonaContractError("source_tier per-seed acceptance must be [0.20, 0.40]")
    if policy["missing_history_default"] != "authored":
        raise PersonaContractError("source_tier missing history default must be authored")
    return _freeze(policy)
```

Set `expanded_runtime_rows` to `[82132, 82132]`, authored/legacy counts to `30000/52132`, scene count to `1723`, and dry-sharp release target to `12` with accepted scene count `[11, 13]`. Validate partition sum, ascending ratios, and target containment.

- [ ] **Step 4: Regenerate C# constants and verify GREEN.**

Run: `python tools/generate_persona_contract_cs.py`

Run: `python -m pytest tests/test_contract.py tests/test_config_provenance.py -q`

Expected: PASS.

- [ ] **Step 5: Commit.**

```powershell
git add config/persona-contract.json config/schemas/persona-contract.schema.json src/persona_corpus/contract.py tools/generate_persona_contract_cs.py src/CompanionDesktopPet/Services/PersonaContract.g.cs tests/test_contract.py tests/test_config_provenance.py
git commit -m "feat: define v1.4.0 hybrid persona contract"
```

### Task 2: Version legacy identity evidence

**Files:**
- Create: `config/persona-legacy-identity-manifest-v1.2.1.json`
- Create: `config/schemas/persona-legacy-identity-manifest.schema.json`
- Modify: `src/persona_corpus/editorial.py`
- Modify: `src/persona_corpus/authored_identity.py`
- Test: `tests/test_editorial_manifest.py`
- Test: `tests/test_privacy_policy.py`

**Interfaces:**
- Consumes: exact identity evidence read from Git tag `v1.2.1`.
- Produces: `load_legacy_identity_manifest(path: Path = ...) -> LegacyIdentityManifest` and `validate_identity_line(row: CorpusLine, authored_manifest: EditorialManifest, legacy_manifest: LegacyIdentityManifest) -> None`.

- [ ] **Step 1: Write failing exact-evidence tests.**

```python
def test_legacy_identity_requires_exact_scene_and_text_evidence():
    manifest = load_legacy_identity_manifest()
    allowed = next(iter(manifest.lines))
    validate_identity_line(legacy_row(allowed.semantic_group, allowed.text), authored_manifest(), manifest)
    with pytest.raises(ValueError, match="legacy identity evidence"):
        validate_identity_line(legacy_row(allowed.semantic_group, allowed.text + "呀"), authored_manifest(), manifest)

def test_authored_and_legacy_identity_namespaces_do_not_cross_authorize():
    legacy = load_legacy_identity_manifest()
    row = authored_row(text=next(iter(legacy.lines)).text)
    with pytest.raises(ValueError, match="authored identity evidence"):
        validate_identity_line(row, authored_manifest(), legacy)
```

- [ ] **Step 2: Run and verify RED.**

Run: `python -m pytest tests/test_editorial_manifest.py tests/test_privacy_policy.py -q`

Expected: FAIL because the legacy loader and dual-namespace validator do not exist.

- [ ] **Step 3: Export and validate the v1.2.1 exact allowlist.**

Run: `git show v1.2.1:config/persona-editorial-manifest.json > $env:TEMP\persona-editorial-v1.2.1.json`

Create a manifest with `policy_version`, exact `semantic_group`, normalized `text`, `source_kind`, and `source_reference` for each authorized identity line. The schema sets `additionalProperties: false`; validation uses tuple equality, never substring matching.

```python
LegacyIdentityKey = tuple[str, str, str, str]

def legacy_identity_key(row: CorpusLine) -> LegacyIdentityKey:
    return (row.semantic_group, normalize_text(row.text), row.source_kind, row.source_reference)
```

- [ ] **Step 4: Run and verify GREEN.**

Run: `python -m pytest tests/test_editorial_manifest.py tests/test_privacy_policy.py -q`

Expected: PASS.

- [ ] **Step 5: Commit.**

```powershell
git add config/persona-legacy-identity-manifest-v1.2.1.json config/schemas/persona-legacy-identity-manifest.schema.json src/persona_corpus/editorial.py src/persona_corpus/authored_identity.py tests/test_editorial_manifest.py tests/test_privacy_policy.py
git commit -m "feat: version legacy identity evidence"
```

### Task 3: Build and normalize the deterministic hybrid corpus

**Files:**
- Modify: `src/persona_corpus/models.py`
- Modify: `src/persona_corpus/builder.py`
- Modify: `src/persona_corpus/surface_variants.py`
- Modify: `tools/build_corpus_v2.py`
- Modify: `tests/test_build.py`
- Modify: `tests/test_surface_variants.py`
- Modify: `tests/test_content_catalog_integrity.py`

**Interfaces:**
- Consumes: existing `build_v2(...) -> BuildResult`, authored catalog v1, frozen source/mappings, and v1.2.1 surface baseline.
- Produces: `source_tier_for(source_kind: str) -> Literal["authored", "legacy"]`, `build_hybrid(...) -> BuildResult`, and `BuildResult.partition_manifest: Mapping[str, object]` containing counts, hashes, and selected legacy dry-sharp scene IDs.

- [ ] **Step 1: Write failing hybrid inventory and determinism tests.**

```python
def test_hybrid_build_has_exact_partition_inventory(hybrid_result):
    rows = hybrid_result.enabled
    assert len(rows) == 82_132
    assert sum(row.source_kind == "curated_authored" for row in rows) == 30_000
    assert sum(row.source_kind != "curated_authored" for row in rows) == 52_132
    assert len({row.id for row in rows}) == 82_132
    assert len({normalize_text(row.text) for row in rows}) == 82_132
    assert len({row.semantic_group for row in rows}) == 1_723

def test_hybrid_build_is_byte_deterministic(build_hybrid_bytes):
    assert build_hybrid_bytes(seed=20260729) == build_hybrid_bytes(seed=20260729)
```

- [ ] **Step 2: Write failing lineage and dry-sharp tests.**

```python
def test_hybrid_preserves_partitions_and_allocates_twelve_dry_sharp(hybrid_result):
    assert hybrid_result.partition_manifest["authored_sha256"] == authored_v130_sha256()
    assert hybrid_result.partition_manifest["legacy_identity_sha256"] == legacy_v121_identity_sha256()
    sharp = {row.semantic_group for row in hybrid_result.enabled if row.tone == "dry_sharp"}
    assert len(sharp) == 12
    assert authored_dry_sharp_scene_ids() <= sharp
    assert len(sharp - authored_dry_sharp_scene_ids()) == 4
```

- [ ] **Step 3: Run and verify RED.**

Run: `python -m pytest tests/test_build.py tests/test_surface_variants.py tests/test_content_catalog_integrity.py -q`

Expected: FAIL because authored/catalog inputs are mutually exclusive and the naive 30-scene dry-sharp ratio violates the current contract.

- [ ] **Step 4: Add explicit hybrid build interfaces.**

```python
SourceTier = Literal["authored", "legacy"]

def source_tier_for(source_kind: str) -> SourceTier:
    return "authored" if source_kind == "curated_authored" else "legacy"

def build_hybrid(*, source: Sequence[LegacyLine], mappings: Sequence[SourceMapping], authored: AuthoredCatalog, catalog: Sequence[CatalogEntry], seed: int) -> BuildResult:
    authored_rows = tuple(_authored_to_corpus(entry) for entry in authored.entries)
    legacy_result = build_v2(source=source, mappings=mappings, catalog=catalog, seed=seed)
    legacy_rows = tuple(replace(row, relationship_profile="neutral", tone="dry") for row in legacy_result.enabled)
    promoted = select_legacy_dry_sharp_scenes(legacy_rows, count=4)
    enabled = normalize_hybrid_dry_sharp((*authored_rows, *legacy_rows), promoted)
    return finalize_hybrid_result(enabled, legacy_result, authored)
```

Use `sha256("persona-hybrid-dry-sharp-v1\0" + semantic_group)` ordering to select four eligible legacy scenes. Change only `tone` and `relationship_profile`; preserve line ID, text, source kind, and source reference. Sort final rows by stable ID before serialization.

- [ ] **Step 5: Expose `--profile authored|legacy|hybrid` and partition evidence.**

```python
parser.add_argument("--profile", choices=("authored", "legacy", "hybrid"), default="hybrid")
```

The hybrid manifest records `runtime_rows`, `authored_rows`, `legacy_rows`, `scene_count`, `authored_sha256`, `legacy_identity_sha256`, `runtime_sha256`, and `legacy_dry_sharp_scene_ids`.

- [ ] **Step 6: Run and verify GREEN.**

Run: `python -m pytest tests/test_build.py tests/test_surface_variants.py tests/test_content_catalog_integrity.py -q`

Expected: PASS.

- [ ] **Step 7: Commit.**

```powershell
git add src/persona_corpus/models.py src/persona_corpus/builder.py src/persona_corpus/surface_variants.py tools/build_corpus_v2.py tests/test_build.py tests/test_surface_variants.py tests/test_content_catalog_integrity.py
git commit -m "feat: build deterministic hybrid persona corpus"
```

### Task 4: Enforce source-tier playback in Python

**Files:**
- Modify: `src/persona_corpus/history.py`
- Modify: `src/persona_corpus/selector.py`
- Modify: `src/persona_corpus/simulation.py`
- Modify: `src/persona_corpus/simulation_core/constraints.py`
- Modify: `src/persona_corpus/simulation_core/metrics.py`
- Modify: `src/persona_corpus/simulation_core/scenarios.py`
- Modify: `src/persona_corpus/simulation_core/report.py`
- Modify: `tests/test_selector.py`
- Modify: `tests/test_prepared_selector.py`
- Modify: `tests/test_simulation.py`
- Modify: `tests/test_simulation_coverage_rules.py`

**Interfaces:**
- Consumes: `source_tier_for(source_kind)` and contract `source_tier` policy.
- Produces: `HistoryRecord.source_tier: Literal["authored", "legacy"] = "authored"`, `PreparedScene.source_tier`, and `source_tier_decision(records, candidate_tier, authored_available, legacy_available) -> tuple[bool, float, str]`.

- [ ] **Step 1: Write failing migration and gate tests.**

```python
def test_history_without_source_tier_migrates_to_authored(tmp_path):
    path = write_history_json(tmp_path, source_tier_missing=True)
    assert SelectionHistory.load(path).records[0].source_tier == "authored"

@pytest.mark.parametrize(
    ("legacy_count", "candidate", "allowed"),
    [(24, "legacy", True), (36, "legacy", False), (36, "authored", True)],
)
def test_recent_hundred_source_tier_gate(legacy_count, candidate, allowed):
    history = tier_history(total=100, legacy=legacy_count)
    result = source_tier_decision(history, candidate, authored_available=True, legacy_available=True)
    assert result[0] is allowed
```

- [ ] **Step 2: Write failing simulation acceptance tests.**

```python
def test_hybrid_simulation_stays_in_source_tier_acceptance(simulation_report):
    assert 0.25 <= simulation_report["source_tier"]["legacy_ratio"] <= 0.35
    assert all(0.20 <= seed["legacy_ratio"] <= 0.40 for seed in simulation_report["seeds"])
```

- [ ] **Step 3: Run and verify RED.**

Run: `python -m pytest tests/test_selector.py tests/test_prepared_selector.py tests/test_simulation.py tests/test_simulation_coverage_rules.py -q`

Expected: FAIL because history, prepared scenes, and reports do not expose source tier.

- [ ] **Step 4: Implement the rolling-100 policy after safety filtering.**

```python
def source_tier_decision(records, candidate_tier, authored_available, legacy_available):
    recent = tuple(records[-100:])
    legacy = sum(record.source_tier == "legacy" for record in recent)
    if len(recent) < 20:
        deficit = 0.30 - (legacy / len(recent) if recent else 0.0)
        bonus = deficit * 200.0 if candidate_tier == "legacy" else -deficit * 200.0
        return True, bonus, "source_tier_warmup"
    projected = (legacy + int(candidate_tier == "legacy")) / min(100, len(recent) + 1)
    if candidate_tier == "legacy" and projected > 0.35 and authored_available:
        return False, 0.0, "source_tier_upper_bound"
    if candidate_tier == "authored" and legacy / len(recent) < 0.25 and legacy_available:
        return False, 0.0, "source_tier_lower_bound"
    return True, (0.30 - projected) * (200.0 if candidate_tier == "legacy" else -200.0), "source_tier_target"
```

Apply it only to already-safe scenes. If a preferred tier has no safe candidates, retry the other tier without bypassing context, cooldown, identity, relationship, seasoning, or dry-sharp gates.

- [ ] **Step 5: Add source-tier metrics and hybrid reports.**

Report total/authored/legacy selections, aggregate legacy ratio, each seed's legacy ratio, acceptance bounds, and pass/fail. Remove the existing `curated_authored`-only report rejection.

- [ ] **Step 6: Run and verify GREEN.**

Run: `python -m pytest tests/test_selector.py tests/test_prepared_selector.py tests/test_simulation.py tests/test_simulation_coverage_rules.py -q`

Expected: PASS.

- [ ] **Step 7: Commit.**

```powershell
git add src/persona_corpus/history.py src/persona_corpus/selector.py src/persona_corpus/simulation.py src/persona_corpus/simulation_core tests/test_selector.py tests/test_prepared_selector.py tests/test_simulation.py tests/test_simulation_coverage_rules.py
git commit -m "feat: schedule legacy playback at safe ratio"
```

### Task 5: Mirror hybrid loading and scheduling in C#

**Files:**
- Modify: `src/CompanionDesktopPet/Services/PersonaCorpus.cs`
- Modify: `src/CompanionDesktopPet/Services/SceneCatalog.cs`
- Modify: `src/CompanionDesktopPet/Services/SceneEngine.cs`
- Modify: `src/CompanionDesktopPet/Services/AgentMemoryService.cs`
- Modify: `tests/CompanionDesktopPet.Tests/PersonaCorpusTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneCatalogSafetyTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneEngineTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneHistoryTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/AgentMemoryServiceTests.cs`

**Interfaces:**
- Consumes: generated source-tier constants and `DialogueLine.SourceKind`.
- Produces: `PersonaSourceTier` enum, `DialogueLine.SourceTier`, `SceneDefinition.SourceTier`, `SceneHistoryEntry.SourceTier = PersonaSourceTier.Authored`, and `SceneHistory.GetSourceTierDecision(...)` equivalent to Python.

- [ ] **Step 1: Write failing loader/history tests.**

```csharp
[Fact]
public void LegacySourceKindMapsToLegacyTier()
{
    Assert.Equal(PersonaSourceTier.Legacy, PersonaCorpus.SourceTierFor("legacy_surface_variant"));
    Assert.Equal(PersonaSourceTier.Authored, PersonaCorpus.SourceTierFor("curated_authored"));
}

[Fact]
public void MissingSerializedSourceTierMigratesToAuthored()
{
    var entry = DeserializeHistoryEntryWithoutSourceTier();
    Assert.Equal(PersonaSourceTier.Authored, entry.SourceTier);
}
```

- [ ] **Step 2: Write failing scheduler parity tests.**

```csharp
[Theory]
[InlineData(24, PersonaSourceTier.Legacy, true)]
[InlineData(36, PersonaSourceTier.Legacy, false)]
[InlineData(36, PersonaSourceTier.Authored, true)]
public void SourceTierGateMatchesContract(int legacyCount, PersonaSourceTier candidate, bool allowed)
{
    var history = BuildTierHistory(100, legacyCount);
    Assert.Equal(allowed, history.GetSourceTierDecision(candidate, true, true).Allowed);
}
```

- [ ] **Step 3: Run and verify RED.**

Run: `dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter "FullyQualifiedName~PersonaCorpusTests|FullyQualifiedName~SceneCatalogSafetyTests|FullyQualifiedName~SceneEngineTests|FullyQualifiedName~SceneHistoryTests|FullyQualifiedName~AgentMemoryServiceTests"`

Expected: FAIL because `PersonaSourceTier`, properties, and decision API do not exist.

- [ ] **Step 4: Implement loader, history migration, and selection parity.**

```csharp
public enum PersonaSourceTier { Authored, Legacy }

public static PersonaSourceTier SourceTierFor(string sourceKind) =>
    sourceKind == "curated_authored" ? PersonaSourceTier.Authored : PersonaSourceTier.Legacy;

public sealed record SourceTierDecision(bool Allowed, double ScoreBonus, string Reason);
```

Add `SourceTier` to the end of records to preserve call-site compatibility. Validate every scene contains one tier, calculate projected ratios over the latest 100 entries, use warm-up scoring before 20, and apply tier preference only after all safety eligibility checks.

- [ ] **Step 5: Run and verify GREEN.**

Run: `dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter "FullyQualifiedName~PersonaCorpusTests|FullyQualifiedName~SceneCatalogSafetyTests|FullyQualifiedName~SceneEngineTests|FullyQualifiedName~SceneHistoryTests|FullyQualifiedName~AgentMemoryServiceTests"`

Expected: PASS.

- [ ] **Step 6: Commit.**

```powershell
git add src/CompanionDesktopPet/Services/PersonaCorpus.cs src/CompanionDesktopPet/Services/SceneCatalog.cs src/CompanionDesktopPet/Services/SceneEngine.cs src/CompanionDesktopPet/Services/AgentMemoryService.cs tests/CompanionDesktopPet.Tests
git commit -m "feat: enforce hybrid playback in desktop runtime"
```

### Task 6: Generate canonical artifacts and release evidence

**Files:**
- Modify: `tools/validate_corpus_v2.py`
- Modify: `tools/simulate_persona.py`
- Modify: `tests/test_validation.py`
- Modify: `tests/test_validation_facade.py`
- Modify: `tests/test_simulation.py`
- Regenerate: `assets/persona-corpus-v2.tsv`
- Regenerate: established corpus manifests/reports discovered by `git ls-files reports config | rg "persona|simulation|manifest"`.

**Interfaces:**
- Consumes: hybrid build, dual identity manifests, and source-tier simulation metrics.
- Produces: canonical TSV plus JSON reports with exact inventory, partition hashes, dry-sharp allocation, identity results, and source-tier acceptance.

- [ ] **Step 1: Write failing validator/report assertions.**

```python
def test_validation_report_contains_hybrid_evidence(validation_report):
    assert validation_report["inventory"] == {"total": 82_132, "authored": 30_000, "legacy": 52_132, "scenes": 1_723}
    assert validation_report["dry_sharp"]["scene_count"] == 12
    assert validation_report["identity"]["authored_policy"] == "authored-identity-v1"
    assert validation_report["identity"]["legacy_policy"] == "legacy-identity-v1.2.1"
```

- [ ] **Step 2: Run and verify RED.**

Run: `python -m pytest tests/test_validation.py tests/test_validation_facade.py tests/test_simulation.py -q`

Expected: FAIL because validation/reporting assumes authored-only runtime and omits partition evidence.

- [ ] **Step 3: Implement hybrid validation and generate twice.**

Run:

```powershell
python tools/build_corpus_v2.py --profile hybrid --output assets/persona-corpus-v2.tsv --report-output reports/pii-review.tsv
$first = (Get-FileHash assets/persona-corpus-v2.tsv -Algorithm SHA256).Hash
python tools/build_corpus_v2.py --profile hybrid --output assets/persona-corpus-v2.tsv --report-output reports/pii-review.tsv
$second = (Get-FileHash assets/persona-corpus-v2.tsv -Algorithm SHA256).Hash
if ($first -ne $second) { throw "hybrid corpus is not deterministic" }
python tools/simulate_persona.py --corpus assets/persona-corpus-v2.tsv --config config/persona-scheduler.json --days 30 --seeds 100 --report reports/persona-simulation.json
python tools/validate_corpus_v2.py --corpus assets/persona-corpus-v2.tsv --config config/persona-scheduler.json --simulation reports/persona-simulation.json
```

- [ ] **Step 4: Run and verify GREEN.**

Run: `python -m pytest tests/test_validation.py tests/test_validation_facade.py tests/test_simulation.py -q`

Expected: PASS, and report source-tier ratios satisfy global constraints.

- [ ] **Step 5: Commit.**

```powershell
git add tools/validate_corpus_v2.py tools/simulate_persona.py tests/test_validation.py tests/test_validation_facade.py tests/test_simulation.py assets/persona-corpus-v2.tsv reports
git commit -m "build: generate v1.4.0 hybrid persona artifacts"
```

### Task 7: Update CI, documentation, package, publish, and clean

**Files:**
- Modify: `.github/workflows/ci-cd.yml`
- Modify: version/package files returned by `rg -n "1\.3\.0|v1\.3\.0" . --glob "!assets/persona-corpus-v2.tsv"`.
- Modify: `README.md`
- Modify: `README-persona-corpus.md`
- Modify: `docs/release/2026-07-25-expanded-runtime-release-checklist.md`
- Create: `docs/release/v1.4.0.md`
- Modify: `tests/Ci-TestEvidence.Contract.ps1`
- Modify: `tests/Release-Packaging.Contract.ps1`
- Modify: `tests/Verify-Publish.Contract.ps1`

**Interfaces:**
- Consumes: canonical corpus/reports and repository release packaging workflow.
- Produces: v1.4.0 executable bundle, eight release assets, Chinese release body, published GitHub Release, final task-board evidence, and one intended worktree.

- [ ] **Step 1: Write failing CI/package contract assertions.**

```powershell
$workflow = Get-Content .github/workflows/ci-cd.yml -Raw
Assert-Contains $workflow '82132'
Assert-Contains $workflow '--profile hybrid'
Assert-Contains $workflow 'v1.4.0'
```

Run: `pwsh -NoProfile -File tests/Ci-TestEvidence.Contract.ps1; pwsh -NoProfile -File tests/Release-Packaging.Contract.ps1; pwsh -NoProfile -File tests/Verify-Publish.Contract.ps1`

Expected: FAIL on v1.3.0/authored-only assumptions.

- [ ] **Step 2: Update version, CI, Chinese docs, release notes, and task board.**

The release Markdown starts with these concrete facts and contains no generic filler:

```markdown
# v1.4.0

本版本把 30,000 条人工编写语料与 v1.2.1 的 52,132 条可运行旧语料合并为 82,132 条统一运行库，共 1,723 个场景。运行时以最近 100 次播放为窗口，将旧语料占比稳定在约 30%，同时继续优先执行上下文、冷却、隐私、身份彩蛋和关系等级安全约束。
```

Document exact counts, deterministic four-scene legacy dry-sharp selection, old-history migration, test commands, asset names/hashes, and known compatibility behavior. Set the Release title to only `v1.4.0`.

- [ ] **Step 3: Run contract tests and commit documentation.**

Run: `pwsh -NoProfile -File tests/Ci-TestEvidence.Contract.ps1; pwsh -NoProfile -File tests/Release-Packaging.Contract.ps1; pwsh -NoProfile -File tests/Verify-Publish.Contract.ps1`

Expected: PASS.

```powershell
git add .github README.md README-persona-corpus.md docs tests/Ci-TestEvidence.Contract.ps1 tests/Release-Packaging.Contract.ps1 tests/Verify-Publish.Contract.ps1
git commit -m "docs: prepare v1.4.0 release"
```

- [ ] **Step 4: Run fresh full verification.**

```powershell
python -m pytest -q
dotnet test
python tools/build_corpus_v2.py --profile hybrid --output assets/persona-corpus-v2.tsv --report-output reports/pii-review.tsv
python tools/simulate_persona.py --corpus assets/persona-corpus-v2.tsv --config config/persona-scheduler.json --days 30 --seeds 100 --report reports/persona-simulation.json
python tools/validate_corpus_v2.py --corpus assets/persona-corpus-v2.tsv --config config/persona-scheduler.json --simulation reports/persona-simulation.json
```

Expected: every command exits 0; inventory is 82,132/30,000/52,132/1,723, dry-sharp is 12, and source-tier ratios are inside both acceptance ranges.

- [ ] **Step 5: Package and smoke-test the EXE.**

Run the repository's versioned packaging command identified by `tests/Release-Packaging.Contract.ps1`. Start the produced EXE with a bounded timeout, confirm the process reaches corpus load without an error event, stop it cleanly, calculate SHA-256, and assert the established eight asset names exist and are non-empty.

- [ ] **Step 6: Publish through port 7890 and verify downloads.**

```powershell
$env:HTTPS_PROXY = 'http://127.0.0.1:7890'
$env:HTTP_PROXY = 'http://127.0.0.1:7890'
git push origin agent/v1.4.0-hybrid
git tag -a v1.4.0 -m 'v1.4.0'
git push origin v1.4.0
gh release create v1.4.0 --title 'v1.4.0' --notes-file docs/release/v1.4.0.md release-work/distribution/ASSET_AND_PERSONA_RIGHTS.md release-work/distribution/Jiayi-Desktop-Pet-README-zh-CN.txt release-work/distribution/Jiayi-Desktop-Pet-win-x64.zip release-work/distribution/Jiayi-Desktop-Pet.exe release-work/distribution/LICENSE release-work/distribution/LICENSE-SCOPE.md release-work/distribution/NOTICE release-work/distribution/SHA256SUMS.txt
gh release view v1.4.0 --json tagName,name,body,assets
```

Create the new directory `outputs/release-download-v1.4.0`, run `gh release download v1.4.0 --dir outputs/release-download-v1.4.0`, and compare each downloaded SHA-256 with its local artifact. Confirm `name == "v1.4.0"` and the Chinese body is non-empty.

- [ ] **Step 7: Record final evidence and clean extra worktrees last.**

Update the Codex task panel with actual test totals, simulation ratios, artifact hashes, release URL, and download-verification result. Then inspect each registered path before removal. If a stale registered worktree is found, resolve its absolute path from the `worktree` line, confirm it begins with `D:/desktop/CompanionDesktopPet-`, and pass that exact resolved path to `git worktree remove`:

```powershell
git worktree list --porcelain
git worktree prune
git worktree list --porcelain
git status --short
```

Expected: only the intended primary worktree remains, the current worktree is clean, and `gh release view v1.4.0` still reports exactly eight verified assets.
