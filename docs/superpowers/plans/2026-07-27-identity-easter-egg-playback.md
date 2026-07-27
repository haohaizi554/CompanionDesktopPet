# Identity Easter Egg Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the confirmed authored-identity policy: exactly 3,000 Easter Egg source rows in `b083`–`b092`, four authorized identity markers, and session-scoped direct-marker exposure controls that preserve natural playback without wall-clock lockouts.

**Architecture:** `config/persona-contract.json` becomes the sole policy source. Python parses and validates literal authored TSV rows against it, then the C# generator exposes the same immutable policy. `IdentitySessionExposure` is a non-persisted in-memory gate owned by `OfflineCompanionAgent`; it filters candidate lines before scoring and surface choice while `SceneHistory` retains only durable playback history. The legacy editorial manifest remains an exact-provenance validator for legacy rows only, never an authorization mechanism for authored rows.

**Tech Stack:** Python 3 standard library, JSON Schema 2020-12, strict UTF-8 TSV/SHA-256, .NET 9/C#, xUnit, existing Python unittest suite, PowerShell/GitHub Actions.

## Confirmed policy contract

- `privacy.pii_markers` is exactly `雷琳玥`, `小玥`, `玥仔`, and `玥玥`.
- New top-level `authored_identity` has policy version `authored-identity-v1`, the same ordered marker set, direct batch mapping `{b083: 雷琳玥, b084: 小玥, b085: 玥仔, b086: 玥玥}`, Easter Egg range `b083`–`b092`, all-category placement enabled, and the four controlled relationship profiles.
- The session limits are `minimum_intervening_bubbles_same_semantic_group=3`, `recent_bubbles_per_semantic_group=8`, `direct_marker_max_per_identity_class=3`, and `persist_across_restarts=false`.
- `b083`–`b086` each contain exactly 300 rows with exactly their assigned direct marker; `b087`–`b092` supply the remaining 1,800 related lore rows. All 3,000 rows are `EasterEgg/easter_egg/self_talk`.
- Marker-bearing rows may occur in any configured category when they are otherwise safe; unknown markers, unsafe PII, questions, unsupported observation claims, dependency/exclusivity/coercion, sexual content, and false real-person biography fail closed.
- Session gates apply to every direct-marker line in every selection path. A restart creates a new exposure state; it does not deserialize one from `AgentMemorySnapshot`.

## Global constraints

- Preserve the four unrelated dirty documentation files and never stage them in intermediate commits: `README.md`, `README-persona-corpus.md`, `docs/audits/2026-07-25-review-remediation.md`, and `docs/release/2026-07-25-expanded-runtime-release-checklist.md`.
- Create literal authored TSV text only. Do not use a text generator, Cartesian expansion, concatenation script, or a handwritten manifest digest.
- Keep all identity policy lists derived from the contract. Do not re-author marker names or session limits in `persona-editorial-manifest.json`, C#, test fixtures, or CI.
- Preserve legacy exact-match validation for `legacy_surface_variant` rows until the runtime corpus migration removes legacy enabled rows; authored identity rows must not be checked against `IdentityEasterEggRules`.
- Do not weaken the existing PII classifier for ordinary content. Add a narrowly scoped authorized-marker disposition only after contract/batch/profile checks succeed.
- Make all new selection gates hard gates: normal selection, startup, automatic ticks, story path when relevant, click fallback, reusable click fallback, and line-level fallback must never bypass them.
- Every commit is narrow, runs its focused red/green test set, and is pushed to `main` after review.

---

### Task 1: Version the single-source authored identity contract

**Files:**

- Modify: `config/persona-contract.json`
- Modify: `config/schemas/persona-contract.schema.json`
- Modify: `src/persona_corpus/contract.py`
- Modify: `tests/test_contract.py`
- Modify: `tests/test_config_provenance.py`

**Interfaces:**

- `PersonaContract.authored_identity: Mapping[str, object]` is a frozen, validated view of the new top-level object.
- `PersonaContract.pii_markers` preserves the contract order and contains all four marker values.
- `authored_identity` requires exactly `policy_version`, `markers`, `direct_marker_batches`, `easter_egg_batches`, `category`, `category_group`, `output_mode`, `allowed_relationship_profiles`, `allow_markers_in_any_category`, and `session_exposure`.

- [ ] **Step 1: Add contract-schema tests before implementation**

~~~python
def test_contract_rejects_missing_or_misordered_authored_identity_marker() -> None:
    raw = load_contract_json()
    raw["authored_identity"]["markers"] = ["雷琳玥", "小玥", "玥玥"]
    with self.assertRaisesRegex(PersonaContractError, "authored_identity.*markers"):
        load_persona_contract(write_json(self.temp, raw))

def test_contract_rejects_non_session_identity_exposure_policy() -> None:
    raw = load_contract_json()
    raw["authored_identity"]["session_exposure"]["persist_across_restarts"] = True
    with self.assertRaisesRegex(PersonaContractError, "persist_across_restarts"):
        load_persona_contract(write_json(self.temp, raw))
~~~

- [ ] **Step 2: Run the focused tests and record their expected red state**

Run: `python -m unittest tests.test_contract tests.test_config_provenance -v`

Expected: failure because the schema/parser does not yet recognize `authored_identity` and `玥仔` is not a privacy marker.

- [ ] **Step 3: Implement exact schema and Python parser validation**

Extend `_TOP_LEVEL_KEYS`, `PersonaContract`, and `load_persona_contract()` to reject extra/missing keys, incorrect batch mapping, duplicate markers, marker/privacy divergence, an invalid category/group/output tuple, a non-exact profile set, invalid boolean placement permission, or non-positive/bad session bounds. Require direct mapping exactly for `b083`–`b086`, range exactly `b083`–`b092`, and a direct marker batch assignment that is one-to-one with the four markers. Freeze the validated mapping in the returned dataclass.

- [ ] **Step 4: Test the stable policy surface**

~~~python
contract = load_persona_contract(CONTRACT_PATH)
self.assertEqual(("雷琳玥", "小玥", "玥仔", "玥玥"), contract.pii_markers)
self.assertEqual("玥仔", contract.authored_identity["direct_marker_batches"]["b085"])
self.assertFalse(contract.authored_identity["session_exposure"]["persist_across_restarts"])
~~~

- [ ] **Step 5: Commit the contract only**

~~~bash
git add config/persona-contract.json config/schemas/persona-contract.schema.json src/persona_corpus/contract.py tests/test_contract.py tests/test_config_provenance.py
git commit -m "feat: define authored identity playback contract"
~~~

### Task 2: Make Python authored-source validation marker-aware and fail closed

**Files:**

- Create: `src/persona_corpus/authored_identity.py`
- Modify: `src/persona_corpus/authored_catalog.py`
- Modify: `src/persona_corpus/privacy.py`
- Modify: `tests/test_authored_catalog.py`
- Modify: `tests/test_privacy.py`

**Interfaces:**

- `authored_identity.marker_hits(text) -> tuple[str, ...]` returns ordered, deduplicated contract marker hits.
- `authored_identity.validate_authored_identity_entries(entries)` reports batch, variant ID, marker, and invariant for every identity-contract failure.
- `parse_authored_batches()` performs the identity validation after per-row field parsing and semantic-group metadata validation, before inventory/manifest generation.

- [ ] **Step 1: Update the 100-batch fixture to be valid under the new policy**

Make fixture rows for `b083`–`b086` include their assigned marker exactly once, use `EasterEgg/easter_egg/self_talk`, and use an allowed profile. Keep b087–b092 as safe Easter Egg lore rows. This ensures every existing manifest/ledger test exercises the real identity layout rather than a now-invalid synthetic layout.

- [ ] **Step 2: Add red malformed-policy tests**

~~~python
def test_parse_authored_batches_rejects_wrong_direct_marker_count(self) -> None:
    replace_text(self.authored_dir / "b085.tsv", "玥仔", "小玥")
    with self.assertRaisesRegex(ValueError, r"b085.*玥仔.*300"):
        parse_authored_batches(self.authored_dir)

def test_parse_authored_batches_rejects_marker_profile_or_batch_mismatch(self) -> None:
    set_field(self.authored_dir / "b083.tsv", "relationship_profile", "forbidden_profile")
    with self.assertRaisesRegex(ValueError, r"b083.*relationship_profile"):
        parse_authored_batches(self.authored_dir)

def test_parse_authored_batches_rejects_unregistered_identity_marker(self) -> None:
    replace_text(self.authored_dir / "b084.tsv", "小玥", "小月")
    with self.assertRaisesRegex(ValueError, r"b084.*direct marker"):
        parse_authored_batches(self.authored_dir)
~~~

- [ ] **Step 3: Implement strict batch and marker rules**

Validate exactly 3,000 rows in `b083`–`b092`, the configured Easter Egg category/group/output tuple for all 3,000 rows, exactly 300 direct hits in each mapped batch, no other direct marker in a mapped direct batch, and the configured allowed profiles for every marker-bearing row. Allow a known marker outside the ten batches only when `allow_markers_in_any_category` is true and its existing category/output contract remains valid. Preserve normal `classify_pii()` findings: the authorization can exempt only `known_identity` evidence equal to a configured marker, never phone, ID, e-mail, unknown person-name, location, income, employment, or unsafe narrative evidence.

- [ ] **Step 4: Add source safety and provenance regression tests**

Test a marker-bearing technical row accepted under the all-category flag, a marker-bearing row with a question mark rejected, a marker-bearing row with an unsupported observation claim rejected, a row with a direct marker plus personal location rejected, and a non-identity row containing an arbitrary PII marker rejected. Assert diagnostics contain the TSV filename, line/variant ID, and marker.

- [ ] **Step 5: Run focused Python tests and commit**

Run: `python -m unittest tests.test_authored_catalog tests.test_privacy -v`

~~~bash
git add src/persona_corpus/authored_identity.py src/persona_corpus/authored_catalog.py src/persona_corpus/privacy.py tests/test_authored_catalog.py tests/test_privacy.py
git commit -m "feat: validate authored identity corpus policy"
~~~

### Task 3: Generate one C# policy facade and retire authored dependence on the legacy manifest

**Files:**

- Modify: `tools/generate_persona_contract_cs.py`
- Modify: `src/CompanionDesktopPet/Services/PersonaContract.g.cs`
- Modify: `src/CompanionDesktopPet/Services/PersonaCorpus.cs`
- Modify: `tests/CompanionDesktopPet.Tests/PersonaCorpusTests.cs`
- Modify: `.github/workflows/ci-cd.yml` only if its generated-contract check needs the new policy evidence

**Interfaces:**

- Generated `AuthoredIdentityPolicy` exposes `Markers`, `DirectMarkerBatchById`, `AllowedRelationshipProfiles`, `AllowMarkersInAnyCategory`, and the three session numeric limits.
- `PersonaContractGenerated` may continue exposing `IdentityEasterEggRules` exclusively for legacy `legacy_surface_variant` provenance; authored marker authorization is derived from `AuthoredIdentityPolicy`, not from the editorial manifest.
- `DialogueLine` has a cached ordered `IdentityMarkerClasses` property derived only from generated contract markers and its `Text`.

- [ ] **Step 1: Add red generator and corpus-parser tests**

~~~csharp
[Fact]
public void GeneratedContract_ExposesAllFourAuthoredIdentityMarkers()
{
    Assert.Equal(["雷琳玥", "小玥", "玥仔", "玥玥"], PersonaContractGenerated.AuthoredIdentity.Markers);
    Assert.Equal("玥仔", PersonaContractGenerated.AuthoredIdentity.DirectMarkerBatchById["b085"]);
}

[Fact]
public void Load_AllowsAuthorizedAuthoredMarkerOutsideEasterEggCategory()
{
    var line = LoadSingleAuthoredLine(category: "Python", text: "小玥把报错栈折成一小段线索。");
    Assert.Equal(["小玥"], line.IdentityMarkerClasses);
}
~~~

- [ ] **Step 2: Regenerate from the contract, never by hand**

Refactor `generate_persona_contract_cs.py` to read `PERSONA_CONTRACT.authored_identity`; do not import `EDITORIAL_MANIFEST` for authored policy. Preserve a separately named legacy provenance section only while the legacy runtime asset exists. Generate `PersonaContract.g.cs` with LF bytes and validate it with `--check`.

- [ ] **Step 3: Split legacy and authored parser paths**

In `PersonaCorpus.Parse`, retain exact legacy manifest checks only for legacy source kinds. For `curated_standalone` authored rows, accept a marker only when every hit is in generated `AuthoredIdentity.Markers`, the text has no forbidden/unsafe PII, and the row's category/group/output remain contract-valid. Remove the existing unconditional `EasterEgg/easter_egg` requirement for marker text. Do not use a source-line hash allowlist for authored rows.

- [ ] **Step 4: Verify generated source and parser tests**

Run:

~~~bash
python tools/generate_persona_contract_cs.py
python tools/generate_persona_contract_cs.py --check
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj -c Release --filter "FullyQualifiedName~PersonaCorpusTests" --logger "console;verbosity=normal"
~~~

- [ ] **Step 5: Commit generated and source changes together**

~~~bash
git add tools/generate_persona_contract_cs.py src/CompanionDesktopPet/Services/PersonaContract.g.cs src/CompanionDesktopPet/Services/PersonaCorpus.cs tests/CompanionDesktopPet.Tests/PersonaCorpusTests.cs
git commit -m "feat: generate authored identity runtime policy"
~~~

### Task 4: Add session-only direct-marker exposure gating in Python and C#

**Files:**

- Create: `src/persona_corpus/identity_session.py`
- Modify: `src/persona_corpus/selector.py`
- Modify: `tests/test_selector.py`
- Create: `src/CompanionDesktopPet/Services/IdentitySessionExposure.cs`
- Modify: `src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs`
- Modify: `src/CompanionDesktopPet/Services/SceneEngine.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneEngineTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/OfflineCompanionAgentTests.cs`

**Interfaces:**

- `IdentitySessionExposure` is constructed fresh by every `OfflineCompanionAgent`, is absent from `AgentMemorySnapshot`, and records only successfully displayed lines.
- `IsEligible(DialogueLine line)` returns false for a direct-marker line if any marker class has already appeared three times in the current session, if the relevant semantic group violates the explicit three-intervening rule, or if that group appears in the latest eight emitted bubbles.
- `SceneScheduler.Select`, `SelectClickFallback`, `SelectReusableClickFallback`, `ChooseBestWithEligibleLine`, and `OfflineCompanionAgent.SelectEligibleLine` receive/use the same line predicate. A scene with no predicate-eligible line is removed before score/weighted selection.

- [ ] **Step 1: Write boundary tests before code**

~~~csharp
[Fact]
public void IdentityExposure_AllowsSameGroupAfterExactlyThreeInterveningBubbles()
{
    var exposure = ExposureWith("identity.group", "filler.1", "filler.2", "filler.3");
    Assert.True(exposure.MeetsMinimumInterveningBubbles("identity.group"));
}

[Fact]
public void IdentityExposure_RejectsAGroupStillInsideRecentEight()
{
    var exposure = ExposureWith("identity.group", "filler.1", "filler.2", "filler.3", "filler.4", "filler.5", "filler.6", "filler.7");
    Assert.False(exposure.IsEligible(IdentityLine("identity.group", "玥仔")));
}

[Fact]
public void AgentRestart_ResetsOnlyIdentitySessionCap()
{
    var first = DriveThreeDirectMarkerReplies(new OfflineCompanionAgent());
    Assert.False(first.CanEmit("玥玥"));
    Assert.True(new OfflineCompanionAgent(first.CreateSnapshot()).CanEmit("玥玥"));
}
~~~

- [ ] **Step 2: Implement a non-persisted exposure ledger**

Store a bounded in-memory sequence of `(semanticGroup, markerClasses)` and per-marker counts. Record after `ShouldDisplayText=true`, never during a rejected candidate or intentional silence. Do not add fields to `SceneHistoryEntry`, restore logic, JSON snapshot DTOs, or disk persistence. Keep explicit methods for the three-intervening and recent-eight predicates even though the latter is stricter, so both contract boundaries remain testable and auditable.

- [ ] **Step 3: Apply the hard predicate before score and at final line choice**

Thread a `Func<DialogueLine, bool>` through scene selection and all fallback paths. A normal or click path may relax ordinary cooldown reuse only where it already does today, but it must never relax the identity session predicate. Preserve deterministic candidate ordering and random calls for candidates that remain eligible. The Python selector receives an equivalent optional session state and its simulation creates one fresh state per run.

- [ ] **Step 4: Add deterministic C# and Python replay tests**

Cover: first, second, and third direct marker allowed; fourth denied; each of the 0/1/2/3-intervening boundaries; a group in positions 1 through 8 of the recent window; release after the ninth emitted bubble; multiple marker classes with independent caps; marker-free lore unaffected; an ordinary festival/memorial semantic group governed only by standard recent rules; snapshot restore does not carry session cap; normal/click/deep fallback never leaks a blocked line. Use fixed RNG seeds and assert exact selected line IDs where possible.

- [ ] **Step 5: Run focused tests and commit**

Run:

~~~bash
python -m unittest tests.test_selector -v
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj -c Release --filter "FullyQualifiedName~SceneEngineTests|FullyQualifiedName~OfflineCompanionAgentTests" --logger "console;verbosity=normal"
~~~

~~~bash
git add src/persona_corpus/identity_session.py src/persona_corpus/selector.py tests/test_selector.py src/CompanionDesktopPet/Services/IdentitySessionExposure.cs src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs src/CompanionDesktopPet/Services/SceneEngine.cs tests/CompanionDesktopPet.Tests/SceneEngineTests.cs tests/CompanionDesktopPet.Tests/OfflineCompanionAgentTests.cs
git commit -m "feat: enforce session identity exposure limits"
~~~

### Task 5: Author and audit the 3,000 literal Easter Egg rows

**Files:**

- Create: `data/authored/v1/b083.tsv` through `data/authored/v1/b092.tsv`
- Modify: `tests/test_authored_catalog.py` only for additional source-level assertions

**Interfaces:**

- Each file has exactly `AUTHORED_HEADER` and 300 UTF-8 LF rows; each semantic group has 25 rows with invariant selector metadata.
- `b083` is direct `雷琳玥`, `b084` direct `小玥`, `b085` direct `玥仔`, `b086` direct `玥玥`; every direct batch has exactly 300 occurrences of its single assigned marker.
- b087–b092 are literal related lore and may include markers only when natural and policy-valid.

- [ ] **Step 1: Reserve non-overlapping authoring ownership**

Assign one agent/file and never allow concurrent writers to touch the same TSV. Every agent must self-check 301 physical lines, 12 groups × 25, controlled values, no question marks, no unsafe PII, no marker spelling drift, duplicate IDs/text, and local similarity before replying `停止编辑`.

- [ ] **Step 2: Run root audits on a stable file snapshot**

For each stopped batch, parse with `_parse_entry`, run `_validate_semantic_group_metadata`, assert exact category/group/output, exact marker allocation where required, global unique variant ID/text checks, and an indexed normalized n-gram similarity gate against all existing authored batches. Do not audit a file while its owner is still writing.

- [ ] **Step 3: Stage and push short content commits**

Commit b083–b086 only after their exact direct-marker audit, then b087–b092 after their lore/safety audit. Use `git add -- <exact paths>` and `git diff --cached --check`; preserve unrelated documentation changes.

- [ ] **Step 4: Confirm all authored inventories**

After b001–b100 exist, run `parse_authored_batches(data/authored/v1)` and assert 30,000 total rows; 3,000 `easter_egg`; 300 direct rows for each marker; no duplicate normalized text; no semantic metadata drift; no question; no unauthorized PII; and no similarity violation.

### Task 6: Bind identity evidence into the authored manifest, runtime build, simulation, and release

**Files:**

- Create/modify: `config/persona-authorship-manifest.json`
- Create/modify: `data/optimized/persona-authorship-ledger.tsv`
- Modify: `src/persona_corpus/builder.py`
- Modify: `tools/build_corpus_v2.py`
- Modify: `src/persona_corpus/simulation.py`
- Modify: `src/persona_corpus/simulation_core/scenarios.py`
- Modify: `tests/test_build.py`, `tests/test_simulation.py`, and CI evidence checks as required
- Regenerate: `data/optimized/persona-corpus-v2.tsv`, `reports/simulation-events.json`, and `src/CompanionDesktopPet/Assets/persona-corpus-v2.tsv`

- [ ] **Step 1: Generate provenance from the final literal source**

Run:

~~~bash
python tools/build_authorship_manifest.py --authored-dir data/authored/v1 --output config/persona-authorship-manifest.json
python tools/generate_persona_contract_cs.py
python tools/generate_persona_contract_cs.py --check
~~~

Do not type or patch SHA-256 values manually. Require generated manifest/root hash, generated C# contract, runtime TSV, ledger, and release evidence to bind the exact contract hash and authored root hash.

- [ ] **Step 2: Wire authored source to runtime-only content**

Use `load_authored_catalog()` to build one runtime row per authored entry with a batch-preserving source reference. Include relationship profile provenance needed by the runtime migration. Retire enabled legacy surface materialization only in the dedicated migration commit; leave legacy extraction/audit data read-only.

- [ ] **Step 3: Add adversarial simulation and replay evidence**

Run explicit session scenarios that force every identity boundary rather than relying on natural cadence: fourth class hit, recent-eight release, three-intervening boundary, restart reset, four markers, all four seasons, holiday/non-holiday, and all relevant dayparts. Include contract SHA-256, authored manifest SHA-256, corpus SHA-256, and selector subseed namespace/version in reproducibility output.

- [ ] **Step 4: Run final verification and publish the final staged release**

Run:

~~~bash
python -m unittest discover -s tests -v
python tools/build_corpus_v2.py --authored-dir data/authored/v1 --authorship-manifest config/persona-authorship-manifest.json --output data/optimized/persona-corpus-v2.tsv
python tools/validate_corpus_v2.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json --allowlist config/persona-review-allowlist.json --simulation reports/simulation-events.json
dotnet test --no-restore --verbosity minimal
~~~

Require zero warnings/errors, a deterministic second build byte-for-byte equal to the first, and C# loading of the embedded asset. Commit generated assets separately from implementation code, push each approved stage to `main`, then build/sign/package the next release only after these evidence gates pass.

## Final self-review checklist

- [ ] `rg -n "EDITORIAL_MANIFEST.*authored|720\.0|nickname_easter_egg.*100" src tools tests` shows no accidental legacy authorization for authored rows.
- [ ] `python tools/generate_persona_contract_cs.py --check` succeeds with no generated diff.
- [ ] Contract/schema/parser tests reject unknown marker, count drift, category/output mismatch, unsafe PII, and restart persistence.
- [ ] Source audit reports exactly 30,000 rows, 3,000 Easter Egg rows, 300 direct rows per marker, and all four marker strings including `玥仔`.
- [ ] Selection tests prove direct-marker caps are session-only and all fallback paths honor the hard predicate.
- [ ] `git diff --check` and `git status --short` show only intentionally staged paths; unrelated docs remain untouched.
