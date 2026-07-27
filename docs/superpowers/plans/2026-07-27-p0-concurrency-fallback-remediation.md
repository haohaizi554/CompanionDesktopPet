# P0 concurrency and fallback remediation

**Goal:** close the remaining concurrency, persistence, and fallback gaps without changing the desktop pet's user-visible cadence or bypassing the existing readiness model.

**Scope:** this plan is isolated from the authored identity work. Every task must leave the four user-edited documentation files in the main worktree untouched.

**Verification rule:** run the focused test command after each task. Before integration, run the combined service suite and the solution-level `dotnet test` with hang diagnostics.

### Task 1: Split service-state locking from agent operations

**Files:**

- Modify: `src/CompanionDesktopPet/Services/DialogueService.cs`
- Modify: `tests/CompanionDesktopPet.Tests/DialogueServiceTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/DialogueWarmupTests.cs`

**Steps:**

1. Keep `_sync` exclusively for service state (`_agent`, warmup task, readiness and failure state).
2. Capture a ready agent under `_sync`, then call `Respond`, `CreateSnapshot`, and `NextStoryDueAt` under a separate private agent-operation gate, never while `_sync` is held.
3. Preserve warmup single-flight and the current safe fallback when a request races a not-yet-published agent.
4. Add a blocking test double proving that a blocked `Respond` does not block `IsReady` or `LastWarmupException`, and that stateful agent operations cannot overlap.
5. Run:

   ```powershell
   dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --no-restore --filter "FullyQualifiedName~DialogueWarmupTests|FullyQualifiedName~DialogueServiceTests"
   ```

### Task 2: Freeze the catalog fallback and warmup boundary

**Files:**

- Modify: `src/CompanionDesktopPet/Services/SceneCatalog.cs`
- Modify: `src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneCatalogSafetyTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/OfflineCompanionAgentTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/DialogueWarmupTests.cs`

**Steps:**

1. Make the intent explicit: fallback scenes keep immediate speech safe, but a recorded persona-catalog load failure must prevent readiness from reporting success.
2. Ensure `WarmUp` materializes catalog data before it checks `PersonaLoadFailure`, then throws a deterministic `InvalidDataException` that retains the original failure as its inner exception.
3. Add test seams only where needed; do not reset static `Lazy` state through reflection.
4. Cover fallback result plus original error, failed warmup plus fallback reply, retryable deferred warmup, and normal ready behavior.
5. Run:

   ```powershell
   dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --no-restore --filter "FullyQualifiedName~SceneCatalogSafetyTests|FullyQualifiedName~DialogueWarmupTests|FullyQualifiedName~OfflineCompanionAgentTests"
   ```

### Task 3: Add true concurrent event-pump regression coverage

**Files:**

- Modify: `tests/CompanionDesktopPet.Tests/ServiceConcurrencyTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/CompanionEventPumpTests.cs` only if a small reusable test helper is genuinely necessary

**Steps:**

1. Do not change `CompanionEventPump` production behavior unless a regression test proves a real defect.
2. Use a barrier and long-running workers to race 32 simultaneous `Poll` calls through day-change, idle-return, story-due and clock-tick state transitions.
3. Repeat the race enough times to expose unlocked state, assert each event is emitted exactly once, then assert a same-time second poll emits nothing.
4. Use bounded task waits, not sleeps.
5. Run:

   ```powershell
   dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --no-restore --filter "FullyQualifiedName~ServiceConcurrencyTests|FullyQualifiedName~CompanionEventPumpTests"
   ```

### Task 4: Prove atomic same-destination success under contention

**Files:**

- Modify: `src/CompanionDesktopPet/Services/AtomicJsonFile.cs` only for a concise bounded-destination ownership comment if useful
- Modify: `tests/CompanionDesktopPet.Tests/AtomicJsonFileTests.cs`

**Steps:**

1. Retain the per-destination gate; do not introduce unsafe eager gate removal.
2. Add a deterministic two-writer test using a controllable serializer/converter: writer A blocks in serialization, writer B targets the same path, and B must remain unfinished until A releases.
3. After both succeed, assert the final payload is B's valid JSON and no temporary files remain.
4. Keep the existing primary-exception cleanup behavior covered.
5. Run:

   ```powershell
   dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --no-restore --filter "FullyQualifiedName~AtomicJsonFileTests"
   ```

### Task 5: Make state ownership explicit and finish integration verification

**Files:**

- Modify: `src/CompanionDesktopPet/Services/CharacterState.cs`
- Modify: `src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs` only if an ownership guard or documentation requires it
- Modify: `tests/CompanionDesktopPet.Tests/OfflineCompanionAgentTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/ServiceConcurrencyTests.cs`

**Steps:**

1. Declare `CharacterState` as owner-confined mutable agent state rather than adding partial locks that cannot provide coherent snapshots.
2. Preserve external snapshot isolation; no UI or persistence caller may receive a live mutable state instance.
3. Add a concurrent `Respond`/snapshot regression that validates snapshot structure and monotonic state without exposing live collections.
4. Run the combined suite:

   ```powershell
   dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --no-restore --filter "FullyQualifiedName~ServiceConcurrencyTests|FullyQualifiedName~DialogueWarmupTests|FullyQualifiedName~DialogueServiceTests|FullyQualifiedName~AtomicJsonFileTests|FullyQualifiedName~SceneCatalogSafetyTests|FullyQualifiedName~OfflineCompanionAgentTests"
   dotnet test CompanionDesktopPet.sln --no-restore --verbosity minimal --blame-hang --blame-hang-timeout 60s --blame-hang-dump-type none
   ```

**Integration notes:** merge this branch only after a fresh review of each task. Do not modify user-owned markdown files when resolving conflicts.
