# Fullscreen-Aware Visible Dialogue Cadence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the user-visible 5–15 / 10–20 / 30–60 / fullscreen 60–120 minute automatic bubble cadence, backed by a tri-state Windows foreground-fullscreen detector and a validated v2 safe-feedback path.

**Architecture:** Keep time-window math, monotonic timer state, fullscreen observation, native Win32 calls, and dialogue selection as separate testable units. Carry raw fullscreen observation and effective quiet mode together through `MainWindow -> DialogueService -> OfflineCompanionAgent`, while `Automatic` and six direct-feedback events bypass only the proactive interruption budget. Integrate through existing WPF timers without hooks, then verify the complete corpus, privacy, single-file package, and release assets.

**Tech Stack:** C# 13 / .NET 9 WPF (`net9.0-windows`, win-x64, SystemAware DPI), Win32 user32+dwmapi P/Invoke, xUnit 2.9, Python 3.11 standard-library corpus tooling, PowerShell release verifiers, GitHub Actions.

## Global Constraints

- Preserve the exact local-time windows: 06:00–17:59 = 5–15 minutes; 18:00–22:59 = 10–20; 23:00–05:59 = 30–60; effective fullscreen = 60–120 and overrides every time period.
- Reuse `TemporalDialogueService.GetTimePeriod`; do not create a second set of hour boundaries.
- `Observed=false` alone may create `not_fullscreen`; `Observed=null` must never be rewritten as false.
- `EffectiveQuietMode` retains the last explicit fullscreen state; before the first explicit state it is false.
- Each display decision performs at most one detector observation and propagates that same snapshot through the reply chain.
- `Automatic` and exactly `Click`, `DragReleased`, `AnimationPaused`, `AnimationResumed`, `SizeChanged`, and `PositionRestored` bypass the proactive interruption budget. Clock/day/idle/story events remain budgeted.
- Safe fallback content must come from enabled v2 runtime lines and exclude story nodes, EasterEgg, `dry_sharp`, `user_direct`, seasoning markers, questions, reply-required lines, and immediate repeated text.
- Preserve daily maximums and content-safety gates. The deepest fallback may relax semantic/line cooldown and ordinary adjacency only.
- Do not read window titles, process names, file names, keyboard input, clipboard data, screen pixels, or network data.
- Use `DWMWA_EXTENDED_FRAME_BOUNDS` and `MONITORINFO.rcMonitor`; never mix WPF DIP with native pixel rectangles and never fall back to `GetWindowRect`.
- Hide/close stops automatic and event timing. Restore samples again before arming. A tick over one minute late silently rearms a full interval instead of bursting after resume.
- Preserve SystemAware DPI, offline operation, unsigned-release policy, SHA-256 verification, and the one-EXE/no-sidecar delivery contract.
- Follow RED -> GREEN -> focused regression -> commit -> proxy push for every implementation task.

---

## File Map

**New production files**

- `src/CompanionDesktopPet/Services/FullscreenSnapshot.cs` — raw/effective state value plus last-known tracker.
- `src/CompanionDesktopPet/Services/DialogueEventPolicy.cs` — authoritative automatic/direct-feedback budget policy.
- `src/CompanionDesktopPet/Services/SceneScheduler.SafeFeedback.cs` — v2 safe fallback selection and coverage validation.
- `src/CompanionDesktopPet/Services/IForegroundFullscreenDetector.cs` — three-state observation interface.
- `src/CompanionDesktopPet/Services/ForegroundFullscreenNative.cs` — native rectangle, Win32 adapter interface, and P/Invoke implementation.
- `src/CompanionDesktopPet/Services/WindowsForegroundFullscreenDetector.cs` — pure two-attempt classification algorithm.
- `src/CompanionDesktopPet/UI/AutomaticDialogueCadenceController.cs` — monotonic arm/evaluate/rearm state machine.
- `src/CompanionDesktopPet/UI/AutomaticDialogueRuntimeSnapshot.cs` — non-reflection WPF test snapshot.

**Modified production files**

- `src/CompanionDesktopPet/Services/DialogueScheduler.cs`
- `src/CompanionDesktopPet/Services/SceneEngine.cs`
- `src/CompanionDesktopPet/Services/SceneCatalog.cs`
- `src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs`
- `src/CompanionDesktopPet/Services/DialogueService.cs`
- `src/CompanionDesktopPet/UI/MainWindowDependencies.cs`
- `src/CompanionDesktopPet/MainWindow.xaml.cs`

**New/modified tests**

- `tests/CompanionDesktopPet.Tests/DialogueSchedulerTests.cs`
- `tests/CompanionDesktopPet.Tests/FullscreenStateTrackerTests.cs`
- `tests/CompanionDesktopPet.Tests/AutomaticDialogueCadenceControllerTests.cs`
- `tests/CompanionDesktopPet.Tests/WindowsForegroundFullscreenDetectorTests.cs`
- `tests/CompanionDesktopPet.Tests/DialogueServiceTests.cs`
- `tests/CompanionDesktopPet.Tests/OfflineCompanionAgentTests.cs`
- `tests/CompanionDesktopPet.Tests/SceneEngineTests.cs`
- `tests/CompanionDesktopPet.Tests/SceneCatalogSafetyTests.cs`
- `tests/CompanionDesktopPet.Tests/DialogueWarmupTests.cs`
- `tests/CompanionDesktopPet.Tests/DialogueWarmupCoordinatorTests.cs`
- `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`

**Documentation/evidence**

- `README.md`
- `README-persona-corpus.md`
- `docs/audits/2026-07-25-review-remediation.md`
- `docs/release/2026-07-25-expanded-runtime-release-checklist.md`

---

### Task 1: Exact Automatic Cadence Windows

**Files:**

- Modify: `src/CompanionDesktopPet/Services/DialogueScheduler.cs`
- Modify: `tests/CompanionDesktopPet.Tests/DialogueSchedulerTests.cs`

**Interfaces:**

- Produces: `AutomaticCadenceMode`, `DialogueScheduler.GetMode(DateTime, bool)`, and `DialogueScheduler.NextDelay(DateTime, bool)`.
- Consumed later by: `AutomaticDialogueCadenceController` and `MainWindow`.

- [ ] **Step 1: Replace the old range tests with exact boundary and endpoint tests**

Add the complete boundary theory and scripted random helper:

```csharp
[Theory]
[InlineData(3, 59, 59, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
[InlineData(4, 0, 0, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
[InlineData(5, 59, 59, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
[InlineData(6, 0, 0, AutomaticCadenceMode.Daytime, 5, 15)]
[InlineData(10, 59, 59, AutomaticCadenceMode.Daytime, 5, 15)]
[InlineData(11, 0, 0, AutomaticCadenceMode.Daytime, 5, 15)]
[InlineData(13, 59, 59, AutomaticCadenceMode.Daytime, 5, 15)]
[InlineData(14, 0, 0, AutomaticCadenceMode.Daytime, 5, 15)]
[InlineData(17, 59, 59, AutomaticCadenceMode.Daytime, 5, 15)]
[InlineData(18, 0, 0, AutomaticCadenceMode.Evening, 10, 20)]
[InlineData(22, 59, 59, AutomaticCadenceMode.Evening, 10, 20)]
[InlineData(23, 0, 0, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
public void NextDelay_UsesCanonicalInclusiveBoundaries(
    int hour, int minute, int second,
    AutomaticCadenceMode expectedMode,
    int minimumMinutes, int maximumMinutes)
{
    var at = new DateTime(2026, 7, 26, hour, minute, second);
    Assert.Equal(expectedMode, DialogueScheduler.GetMode(at, false));
    Assert.Equal(TimeSpan.FromMinutes(minimumMinutes),
        new DialogueScheduler(new EndpointRandom(false)).NextDelay(at));
    Assert.Equal(TimeSpan.FromMinutes(maximumMinutes),
        new DialogueScheduler(new EndpointRandom(true)).NextDelay(at));
}

private sealed class EndpointRandom(bool maximum) : Random
{
    public override int Next(int minValue, int maxValue) =>
        maximum ? maxValue - 1 : minValue;
}
```

Add a second theory that passes `effectiveQuietMode: true` for Dawn, Daytime, Evening, and LateNight and asserts mode `Fullscreen` plus both 60/120 endpoints.

- [ ] **Step 2: Run the scheduler tests and capture RED**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter FullyQualifiedName~DialogueSchedulerTests
```

Expected: failure because `AutomaticCadenceMode`/`GetMode` do not exist and the old ranges are 20–50, 45–90, and 90–150.

- [ ] **Step 3: Implement the canonical mode and inclusive ranges**

Use this shape in `DialogueScheduler.cs`:

```csharp
internal enum AutomaticCadenceMode
{
    Daytime,
    Evening,
    LateNightOrDawn,
    Fullscreen
}

internal static AutomaticCadenceMode GetMode(DateTime localTime, bool effectiveQuietMode)
{
    if (effectiveQuietMode) return AutomaticCadenceMode.Fullscreen;
    return TemporalDialogueService.GetTimePeriod(localTime) switch
    {
        TimePeriod.Evening => AutomaticCadenceMode.Evening,
        TimePeriod.LateNight or TimePeriod.Dawn => AutomaticCadenceMode.LateNightOrDawn,
        _ => AutomaticCadenceMode.Daytime
    };
}

public TimeSpan NextDelay(DateTime localTime, bool effectiveQuietMode = false)
{
    var (minimum, maximum) = GetMode(localTime, effectiveQuietMode) switch
    {
        AutomaticCadenceMode.Daytime => (5, 15),
        AutomaticCadenceMode.Evening => (10, 20),
        AutomaticCadenceMode.LateNightOrDawn => (30, 60),
        AutomaticCadenceMode.Fullscreen => (60, 120),
        _ => throw new ArgumentOutOfRangeException()
    };
    return TimeSpan.FromSeconds(_random.Next(minimum * 60, maximum * 60 + 1));
}
```

- [ ] **Step 4: Run focused GREEN and diff checks**

Run the Task 1 test command again, then:

```powershell
git diff --check
```

Expected: all `DialogueSchedulerTests` pass and diff check exits 0.

- [ ] **Step 5: Commit and stage-push Task 1**

```powershell
git add src/CompanionDesktopPet/Services/DialogueScheduler.cs `
  tests/CompanionDesktopPet.Tests/DialogueSchedulerTests.cs
git commit -m "feat: set visible automatic dialogue cadence"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
```

---

### Task 2: Raw/Effective Fullscreen Propagation and Budget Scope

**Files:**

- Create: `src/CompanionDesktopPet/Services/FullscreenSnapshot.cs`
- Create: `src/CompanionDesktopPet/Services/DialogueEventPolicy.cs`
- Create: `tests/CompanionDesktopPet.Tests/FullscreenStateTrackerTests.cs`
- Modify: `src/CompanionDesktopPet/Services/SceneEngine.cs`
- Modify: `src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs`
- Modify: `src/CompanionDesktopPet/Services/DialogueService.cs`
- Modify: `tests/CompanionDesktopPet.Tests/DialogueServiceTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneEngineTests.cs`
- Modify: four `ICompanionDialogueAgent` fakes in warmup/window tests

**Interfaces:**

- Produces: `FullscreenSnapshot`, `FullscreenStateTracker.Update(bool?)`, and `DialogueEventPolicy`.
- Produces: internal four-argument `DialogueService.GetReply` and `OfflineCompanionAgent.Respond` overloads.
- Consumed later by: fullscreen detector/WPF tasks.

- [ ] **Step 1: Write RED tests for the state tracker and independent propagation**

Create `FullscreenStateTrackerTests.cs`:

```csharp
[Fact]
public void Update_PreservesLastExplicitQuietModeWithoutInventingObservedFalse()
{
    var tracker = new FullscreenStateTracker();
    Assert.Equal(new FullscreenSnapshot(null, false), tracker.Update(null));
    Assert.Equal(new FullscreenSnapshot(true, true), tracker.Update(true));
    Assert.Equal(new FullscreenSnapshot(null, true), tracker.Update(null));
    Assert.Equal(new FullscreenSnapshot(false, false), tracker.Update(false));
    Assert.Equal(new FullscreenSnapshot(null, false), tracker.Update(null));
}
```

In `DialogueServiceTests`, use a recording fake to assert that `new FullscreenSnapshot(null, true)` reaches the agent unchanged. In `SceneEngineTests`, assert that raw false adds `not_fullscreen`, raw null does not, and effective true still activates the fullscreen event budget.

- [ ] **Step 2: Write RED tests for exact bypass policy**

Add:

```csharp
[Theory]
[InlineData(CompanionEvent.Click)]
[InlineData(CompanionEvent.DragReleased)]
[InlineData(CompanionEvent.AnimationPaused)]
[InlineData(CompanionEvent.AnimationResumed)]
[InlineData(CompanionEvent.SizeChanged)]
[InlineData(CompanionEvent.PositionRestored)]
public void DirectFeedbackEventsBypassInterruptionBudget(CompanionEvent trigger)
{
    var now = new DateTime(2026, 7, 26, 15, 0, 0);
    var history = CreateRecentBudgetHistory(now);
    var context = new SceneContext(trigger, now, CharacterState.Create(now));

    var selected = new SceneScheduler().Select(
        context,
        history,
        new Random(2607),
        DialogueEventPolicy.BypassesInterruptionBudget(trigger));

    Assert.NotNull(selected);
}

[Theory]
[InlineData(CompanionEvent.ClockTick)]
[InlineData(CompanionEvent.DayChanged)]
[InlineData(CompanionEvent.IdleReturned)]
[InlineData(CompanionEvent.StoryTimerDue)]
public void EventOutputsRemainBudgeted(CompanionEvent trigger)
{
    var now = new DateTime(2026, 7, 26, 15, 0, 0);
    var history = CreateRecentBudgetHistory(now);
    var context = new SceneContext(trigger, now, CharacterState.Create(now));

    Assert.Null(new SceneScheduler().Select(context, history, new Random(2608)));
}

private static SceneHistory CreateRecentBudgetHistory(DateTime now)
{
    var history = new SceneHistory();
    var scenes = SceneCatalog.PersonaScenes
        .Where(scene => scene.Lines.Count > 0)
        .Take(InterruptionBudget.MaximumOutputsPerHour)
        .ToArray();
    for (var index = 0; index < scenes.Length; index++)
    {
        history.Record(scenes[index], now.AddMinutes(-2 - index), scenes[index].Lines[0]);
    }
    return history;
}
```

Add `AutomaticBypassesLegacyHourlyLateNightAndFullscreenBudgets` with two recent entries and effective fullscreen true; it must still select an `Automatic` scene.

- [ ] **Step 3: Run Task 2 tests and capture RED**

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter "FullyQualifiedName~FullscreenStateTrackerTests|FullyQualifiedName~DialogueServiceTests|FullyQualifiedName~SceneEngineTests"
```

Expected: missing types/signatures and existing non-click/non-automatic budget rejection.

- [ ] **Step 4: Implement snapshot, tracker, and policy**

```csharp
internal readonly record struct FullscreenSnapshot(bool? Observed, bool EffectiveQuietMode);

internal sealed class FullscreenStateTracker
{
    private bool? _lastExplicit;

    internal FullscreenSnapshot Update(bool? observed)
    {
        if (observed.HasValue) _lastExplicit = observed.Value;
        return new FullscreenSnapshot(observed, _lastExplicit is true);
    }
}

internal static class DialogueEventPolicy
{
    internal static bool IsDirectFeedback(CompanionEvent trigger) => trigger is
        CompanionEvent.Click or CompanionEvent.DragReleased or
        CompanionEvent.AnimationPaused or CompanionEvent.AnimationResumed or
        CompanionEvent.SizeChanged or CompanionEvent.PositionRestored;

    internal static bool BypassesInterruptionBudget(CompanionEvent trigger) =>
        trigger == CompanionEvent.Automatic || IsDirectFeedback(trigger);
}
```

Add `bool EffectiveFullscreen = false` to `SceneContext`; continue using `IsFullscreen` only for tokens. Pass `context.EffectiveFullscreen` to `InterruptionBudget.CanPlay`.

- [ ] **Step 5: Preserve public APIs and add the internal snapshot path**

Keep the three-argument public methods as wrappers:

```csharp
public AgentReply GetReply(CompanionEvent trigger, DateTime localTime, Random random) =>
    GetReply(trigger, localTime, random, default);

internal AgentReply GetReply(
    CompanionEvent trigger, DateTime localTime, Random random,
    FullscreenSnapshot fullscreen)
{
    ArgumentNullException.ThrowIfNull(random);
    lock (_sync)
    {
        return _agent is { } agent
            ? agent.Respond(trigger, localTime, random, fullscreen)
            : GetFallbackReply(trigger);
    }
}
```

Keep `OfflineCompanionAgent.Respond(trigger, time, random)` as the public compatibility method. Add an internal `RespondWithContext(trigger, time, random, FullscreenSnapshot)` for direct tests, route both to one private `RespondCore`, and explicitly implement `ICompanionDialogueAgent.Respond(CompanionEvent, DateTime, Random, FullscreenSnapshot)` so the public class never exposes an internal parameter type:

```csharp
public AgentReply Respond(CompanionEvent trigger, DateTime localTime, Random random) =>
    RespondCore(trigger, localTime, random, default);

internal AgentReply RespondWithContext(
    CompanionEvent trigger, DateTime localTime, Random random,
    FullscreenSnapshot fullscreen) =>
    RespondCore(trigger, localTime, random, fullscreen);

AgentReply ICompanionDialogueAgent.Respond(
    CompanionEvent trigger, DateTime localTime, Random random,
    FullscreenSnapshot fullscreen) =>
    RespondCore(trigger, localTime, random, fullscreen);
```

Change the internal interface signature and update exactly these fakes: `DialogueWarmupCoordinatorTests.FixedAgent`, `DialogueWarmupTests.FixedAgent`, `DialogueWarmupTests.ConcurrentCallDetectingAgent`, and `WindowShellTests.FixedDialogueAgent`.

- [ ] **Step 6: Run focused GREEN plus concurrency regression**

Run the Task 2 filter, then:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter "FullyQualifiedName~GetReply_SerializesConcurrentCallsIntoTheMutableAgent|FullyQualifiedName~DialogueWarmup"
```

Expected: both commands pass; the concurrency test still reports a maximum of one mutable-agent call.

- [ ] **Step 7: Commit and stage-push Task 2**

```powershell
git add src/CompanionDesktopPet/Services tests/CompanionDesktopPet.Tests
git commit -m "feat: propagate fullscreen dialogue context"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
```

Before commit, inspect `git diff --cached --stat` and confirm only Task 2 files are staged.

---

### Task 3: Validated v2 Safe Feedback

**Files:**

- Create: `src/CompanionDesktopPet/Services/SceneScheduler.SafeFeedback.cs`
- Modify: `src/CompanionDesktopPet/Services/SceneEngine.cs`
- Modify: `src/CompanionDesktopPet/Services/SceneCatalog.cs`
- Modify: `src/CompanionDesktopPet/Services/OfflineCompanionAgent.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneEngineTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/SceneCatalogSafetyTests.cs`
- Modify: `tests/CompanionDesktopPet.Tests/OfflineCompanionAgentTests.cs`

**Interfaces:**

- Produces: `SafeFeedbackSelection`, `SceneScheduler.SelectSafeFeedback`, and `SceneScheduler.ValidateSafeFeedbackCoverage`.
- Consumes: `DialogueEventPolicy` and `FullscreenSnapshot` from Task 2.

- [ ] **Step 1: Write RED selector safety tests**

Build small in-memory scenes and assert the selector rejects disabled lines, story nodes, EasterEgg, `dry_sharp`, `UserDirect`, seasoning-marked text, reply-required content, and the immediately previous text. Add separate tests proving it keeps `MaxPerDay`, first prefers unused lines, then selects the least-recent safe line, and remains scene-first before line choice.

Use this exact production predicate in test expectations:

```csharp
line.Enabled
&& !line.RequiresReply
&& !line.HasSeasoningMarker
&& scene.StoryArcId is null
&& scene.CategoryGroup != DialogueCategoryGroup.EasterEgg
&& scene.Tone != "dry_sharp"
&& scene.OutputMode != DialogueOutputMode.UserDirect
```

- [ ] **Step 2: Write RED runtime coverage tests**

Add a theory covering Daytime 10:00, Evening 20:00, LateNight 02:00, and Dawn 05:00 on a weekday, weekend, and holiday, crossed with `Observed` null/false/true. Add the six direct-feedback events. Every scenario must have at least two safe lines. Aggregate daily capacity must be at least 144 for Daytime (12 hours / 5 minutes), 30 for Evening (5 hours / 10 minutes), 14 for LateNight+Dawn (7 hours / 30 minutes), and 24 for a full day in Fullscreen mode. Add a negative fixture with one generic scene and assert `InvalidDataException`.

- [ ] **Step 3: Write RED end-to-end agent tests**

For a fresh agent, create budget pressure with a startup reply and recent history. Assert:

- `Automatic` remains visible one second later and has enabled `PersonaCorpus` provenance;
- each of the six direct events remains visible under the same pressure;
- `ClockTick` remains silent when the budget is blocked;
- safe fallback never emits EasterEgg, `dry_sharp`, `UserDirect`, seasoning, or the previous text.

- [ ] **Step 4: Run Task 3 tests and capture RED**

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter "FullyQualifiedName~SceneEngineTests|FullyQualifiedName~SceneCatalogSafetyTests|FullyQualifiedName~OfflineCompanionAgentTests"
```

Expected: direct actions/Automatic can still become intentional silence and safe coverage API is missing.

- [ ] **Step 5: Implement the partial safe-feedback selector**

Change `SceneScheduler` to `public sealed partial class SceneScheduler`. In the new partial file implement:

```csharp
internal sealed record SafeFeedbackSelection(SceneDefinition Scene, DialogueLine Line);

internal SafeFeedbackSelection? SelectSafeFeedback(
    IReadOnlyList<SceneDefinition> scenes,
    SceneContext context,
    SceneHistory history,
    Random random);

internal static void ValidateSafeFeedbackCoverage(IReadOnlyList<SceneDefinition> scenes);
```

Layer one retains normal semantic/line cooldown, daily caps, trigger/context, and content safety. Layer two retains daily caps and all safety predicates but relaxes semantic/line cooldown and ordinary adjacency. In both layers choose a scene using existing score bands, then choose an unused or least-recent safe line inside that scene. Never fall back to builtin text after warmup.

- [ ] **Step 6: Wire warmup validation and agent fallback**

Call `SceneScheduler.ValidateSafeFeedbackCoverage(scenes)` from `SceneCatalog.ValidatePublishedScenes`. Do not apply the v2 coverage count to `FallbackDialogueCatalog`; primary validation failure must remain a recorded degraded fallback.

In `OfflineCompanionAgent` use:

```csharp
var bypassBudget = DialogueEventPolicy.BypassesInterruptionBudget(trigger);
var scene = _scheduler.Select(context, _history, random, bypassBudget);
SafeFeedbackSelection? safe = null;
if (scene is null && bypassBudget)
{
    safe = _scheduler.SelectSafeFeedback(SceneCatalog.PersonaScenes, context, _history, random);
    scene = safe?.Scene;
}
var line = safe?.Line ?? SelectEligibleLine(scene!, localTime, random);
```

Make every safe selection return the concrete line it selected; do not send it back through normal cooldown filtering. Keep the existing intentional-silence path if even the validated safe pool is unavailable, trace one nonfatal contract failure for `Automatic`, and never enter a rapid retry loop. Update `OfflineCompanionAgent.WarmUp` to throw `InvalidDataException` with `SceneCatalog.PersonaLoadFailure` as its inner exception when primary v2 validation fell back, so degraded builtin scenes cannot be reported as a ready full corpus.

- [ ] **Step 7: Run focused GREEN and published-corpus simulation tests**

Run the Task 3 filter, then:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter "FullyQualifiedName~Respond_MultiSeedPublishedOutputMeetsEasterEggPlaybackContract|FullyQualifiedName~Respond_NeverRepeatsBlockedGroupsAdjacentlyOrInsideSemanticCooldown"
```

Expected: safe fallback tests pass and normal long-run exposure/cooldown behavior remains unchanged.

- [ ] **Step 8: Commit and stage-push Task 3**

```powershell
git add src/CompanionDesktopPet/Services tests/CompanionDesktopPet.Tests
git commit -m "feat: guarantee safe automatic and direct feedback"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
```

---

### Task 4: Tri-State Win32 Foreground Fullscreen Detector

**Files:**

- Create: `src/CompanionDesktopPet/Services/IForegroundFullscreenDetector.cs`
- Create: `src/CompanionDesktopPet/Services/ForegroundFullscreenNative.cs`
- Create: `src/CompanionDesktopPet/Services/WindowsForegroundFullscreenDetector.cs`
- Create: `tests/CompanionDesktopPet.Tests/WindowsForegroundFullscreenDetectorTests.cs`

**Interfaces:**

- Produces: `IForegroundFullscreenDetector.Observe(nint excludedWindow)`.
- Consumed later by: `MainWindowDependencies` and `MainWindow`.

- [ ] **Step 1: Write the queue-driven fake and RED classification matrix**

The fake must expose `Queue<nint> ForegroundWindows`, `ForegroundReadCount`, native status flags, frame rectangle, monitor handle, and monitor rectangle. Cover these exact outcomes:

- zero or excluded HWND -> null with one foreground read;
- invalid/style failure twice -> null;
- first attempt changes HWND and second is stable -> second classification;
- two unstable attempts -> null after four reads;
- desktop/shell/invisible/minimized/child/cloaked -> false;
- DWM cloak/frame/monitor-info query failure -> null;
- monitor handle zero -> false;
- exact cover and every edge within one pixel -> true;
- any edge off by two pixels -> false;
- work-area-only/maximized -> false;
- full monitor with auto-hidden taskbar semantics -> true;
- negative-coordinate secondary and portrait -> true;
- spanning/offscreen/nonmatching -> false;
- changed monitor rectangle on a later call is re-read, never cached.

- [ ] **Step 2: Run detector tests and capture RED**

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter FullyQualifiedName~WindowsForegroundFullscreenDetectorTests
```

Expected: detector/native types are missing.

- [ ] **Step 3: Implement the detector-facing native contract**

```csharp
internal readonly record struct NativePixelRect(int Left, int Top, int Right, int Bottom);

internal interface IForegroundFullscreenNative
{
    nint GetForegroundWindow();
    nint GetDesktopWindow();
    nint GetShellWindow();
    bool IsWindow(nint window);
    bool IsWindowVisible(nint window);
    bool IsWindowMinimized(nint window);
    bool TryGetWindowStyle(nint window, out uint style);
    bool TryGetCloaked(nint window, out bool cloaked);
    bool TryGetExtendedFrameBounds(nint window, out NativePixelRect bounds);
    nint GetIntersectingMonitor(nint window);
    bool TryGetMonitorBounds(nint monitor, out NativePixelRect bounds);
}
```

Use private blittable `RECT`/`MONITORINFO` P/Invoke structs and map them to `NativePixelRect`. Declare user32 calls for foreground/desktop/shell, visibility, iconic, `GetWindowLongPtrW(GWL_STYLE=-16)`, `MonitorFromWindow(flags=0)`, `GetMonitorInfoW`; declare dwmapi `DwmGetWindowAttribute` for attributes 9 and 14. Set `MONITORINFO.cbSize`, clear/read last-error around `GetWindowLongPtrW`, and use `HRESULT == 0` only.

- [ ] **Step 4: Implement two-attempt stable classification**

```csharp
internal interface IForegroundFullscreenDetector
{
    bool? Observe(nint excludedWindow);
}

internal sealed class WindowsForegroundFullscreenDetector : IForegroundFullscreenDetector
{
    private const int MaximumAttempts = 2;
    private const int EdgeTolerancePixels = 1;
    private const uint WsChild = 0x40000000u;
    private readonly IForegroundFullscreenNative _native;

    internal WindowsForegroundFullscreenDetector()
        : this(ForegroundFullscreenNative.Instance) { }

    internal WindowsForegroundFullscreenDetector(IForegroundFullscreenNative native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    public bool? Observe(nint excludedWindow)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var start = _native.GetForegroundWindow();
            if (start == 0 || start == excludedWindow) return null;

            var result = Classify(start);
            var end = _native.GetForegroundWindow();
            if (end != start || result == ProbeResult.Retry) continue;
            return result == ProbeResult.Fullscreen;
        }
        return null;
    }

    private ProbeResult Classify(nint window)
    {
        if (window == _native.GetDesktopWindow() || window == _native.GetShellWindow())
            return ProbeResult.NotFullscreen;
        if (!_native.IsWindow(window)) return ProbeResult.Retry;
        if (!_native.IsWindowVisible(window) || _native.IsWindowMinimized(window))
            return ProbeResult.NotFullscreen;
        if (!_native.TryGetWindowStyle(window, out var style)) return ProbeResult.Retry;
        if ((style & WsChild) != 0) return ProbeResult.NotFullscreen;
        if (!_native.TryGetCloaked(window, out var cloaked)) return ProbeResult.Retry;
        if (cloaked) return ProbeResult.NotFullscreen;
        if (!_native.TryGetExtendedFrameBounds(window, out var frame) || !IsPositive(frame))
            return ProbeResult.Retry;
        var monitor = _native.GetIntersectingMonitor(window);
        if (monitor == 0) return ProbeResult.NotFullscreen;
        if (!_native.TryGetMonitorBounds(monitor, out var bounds) || !IsPositive(bounds))
            return ProbeResult.Retry;
        return EdgesMatch(frame, bounds) ? ProbeResult.Fullscreen : ProbeResult.NotFullscreen;
    }

    private static bool IsPositive(NativePixelRect value) =>
        value.Right > value.Left && value.Bottom > value.Top;

    private static bool EdgesMatch(NativePixelRect left, NativePixelRect right) =>
        Math.Abs((long)left.Left - right.Left) <= EdgeTolerancePixels
        && Math.Abs((long)left.Top - right.Top) <= EdgeTolerancePixels
        && Math.Abs((long)left.Right - right.Right) <= EdgeTolerancePixels
        && Math.Abs((long)left.Bottom - right.Bottom) <= EdgeTolerancePixels;

    private enum ProbeResult { Retry, NotFullscreen, Fullscreen }
}
```

Treat HWND invalidation/query failures as unknown after retry. Reject nonpositive rectangles as unknown. Compare the four edges independently with `Math.Abs(left - monitor.Left) <= 1`; do not use area percentages.

- [ ] **Step 5: Run GREEN plus non-deterministic native smoke**

Run the Task 4 filter. Add one native smoke test that only asserts `new WindowsForegroundFullscreenDetector().Observe(0)` does not throw and returns a legal nullable bool; never assert the CI runner desktop state.

- [ ] **Step 6: Commit and stage-push Task 4**

```powershell
git add src/CompanionDesktopPet/Services/IForegroundFullscreenDetector.cs `
  src/CompanionDesktopPet/Services/ForegroundFullscreenNative.cs `
  src/CompanionDesktopPet/Services/WindowsForegroundFullscreenDetector.cs `
  tests/CompanionDesktopPet.Tests/WindowsForegroundFullscreenDetectorTests.cs
git commit -m "feat: detect foreground fullscreen windows"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
```

---

### Task 5: Monotonic Automatic Cadence Controller

**Files:**

- Create: `src/CompanionDesktopPet/UI/AutomaticDialogueCadenceController.cs`
- Create: `tests/CompanionDesktopPet.Tests/AutomaticDialogueCadenceControllerTests.cs`

**Interfaces:**

- Consumes: `DialogueScheduler.GetMode/NextDelay`.
- Produces: `Arm`, `Evaluate`, `RequiresModeRearm`, `Reset`, and an inspectable runtime state.
- Consumed later by: `MainWindow`.

- [ ] **Step 1: Write RED pure state-machine tests**

Cover: initial not armed, inclusive due tick speaks, early tick returns remaining delay, mode change returns silent rearm, 60-second lateness still speaks, over-60-second lateness rearms, wall-clock rollback cannot change monotonic evaluation, Reset clears state, and fullscreen-to-fullscreen across a wall-clock band boundary is not a mode change.

- [ ] **Step 2: Run controller tests and capture RED**

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter FullyQualifiedName~AutomaticDialogueCadenceControllerTests
```

Expected: controller/evaluation types are missing.

- [ ] **Step 3: Implement the pure controller**

```csharp
internal enum AutomaticCadenceDecision
{
    NotArmed,
    Wait,
    Speak,
    RearmModeChanged,
    RearmLate
}

internal readonly record struct AutomaticCadenceEvaluation(
    AutomaticCadenceDecision Decision,
    TimeSpan Remaining);

internal readonly record struct AutomaticCadenceState(
    bool IsArmed,
    AutomaticCadenceMode? Mode,
    TimeSpan Delay,
    long ArmedAtTimestamp);

internal sealed class AutomaticDialogueCadenceController
{
    private static readonly TimeSpan LateTolerance = TimeSpan.FromMinutes(1);
    private readonly DialogueScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private AutomaticCadenceMode? _mode;
    private TimeSpan _delay;
    private long _armedAt;

    internal AutomaticDialogueCadenceController(
        DialogueScheduler scheduler,
        TimeProvider timeProvider)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal TimeSpan Arm(DateTime localTime, bool effectiveQuietMode)
    {
        _mode = DialogueScheduler.GetMode(localTime, effectiveQuietMode);
        _delay = _scheduler.NextDelay(localTime, effectiveQuietMode);
        _armedAt = _timeProvider.GetTimestamp();
        return _delay;
    }

    internal bool RequiresModeRearm(DateTime localTime, bool effectiveQuietMode) =>
        _mode.HasValue
        && _mode.Value != DialogueScheduler.GetMode(localTime, effectiveQuietMode);

    internal AutomaticCadenceEvaluation Evaluate(DateTime localTime, bool effectiveQuietMode)
    {
        if (!_mode.HasValue)
            return new(AutomaticCadenceDecision.NotArmed, TimeSpan.Zero);
        if (RequiresModeRearm(localTime, effectiveQuietMode))
            return new(AutomaticCadenceDecision.RearmModeChanged, TimeSpan.Zero);

        var elapsed = _timeProvider.GetElapsedTime(_armedAt, _timeProvider.GetTimestamp());
        if (elapsed < _delay)
            return new(AutomaticCadenceDecision.Wait, _delay - elapsed);
        if (elapsed - _delay > LateTolerance)
            return new(AutomaticCadenceDecision.RearmLate, TimeSpan.Zero);
        return new(AutomaticCadenceDecision.Speak, TimeSpan.Zero);
    }

    internal AutomaticCadenceState Capture() =>
        new(_mode.HasValue, _mode, _delay, _armedAt);

    internal void Reset()
    {
        _mode = null;
        _delay = TimeSpan.Zero;
        _armedAt = 0;
    }
}
```

Store the arm timestamp and delay. Use `TimeProvider.GetTimestamp()` and `GetElapsedTime`; never compare `DateTime.Now` to a due wall-clock value. Return `RearmLate` only when elapsed exceeds delay by more than one minute.

- [ ] **Step 4: Run focused GREEN and scheduler regressions**

Run the Task 5 filter plus `DialogueSchedulerTests`. Confirm all pass and `git diff --check` exits 0.

- [ ] **Step 5: Commit and stage-push Task 5**

```powershell
git add src/CompanionDesktopPet/UI/AutomaticDialogueCadenceController.cs `
  tests/CompanionDesktopPet.Tests/AutomaticDialogueCadenceControllerTests.cs
git commit -m "feat: track automatic cadence monotonically"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
```

---

### Task 6: WPF Fullscreen Signal and Lifecycle Integration

**Files:**

- Create: `src/CompanionDesktopPet/UI/AutomaticDialogueRuntimeSnapshot.cs`
- Modify: `src/CompanionDesktopPet/UI/MainWindowDependencies.cs`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml.cs`
- Modify: `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`

**Interfaces:**

- Consumes: Tasks 1–5.
- Produces: `ProcessAutomaticTimerTick`, `ProcessEventTimerTick`, and `CaptureAutomaticDialogueRuntime` internal test seams.

- [ ] **Step 1: Add RED WPF lifecycle/propagation tests without new reflection**

Add `SequenceFullscreenDetector` and inject it through `MainWindowDependencies`. Add these tests:

- loaded observes once and arms from the same snapshot;
- entering/exiting fullscreen silently rearms without changing `LastReply`;
- nonfullscreen 17:59 -> 18:00 silently rearms;
- fullscreen crossing 18:00 keeps the existing Fullscreen mode;
- null after known true preserves effective quiet but reaches the agent as raw null;
- on-time automatic tick observes once, displays, and rearms;
- over-one-minute-late tick and mode-changed tick rearm without speaking;
- wall-clock rollback does not defeat monotonic lateness;
- visible event/direct reply resets the automatic countdown;
- silent budgeted event preserves the existing deadline;
- hidden/closed queued ticks do not observe or rearm;
- tray restore resamples before arming.

- [ ] **Step 2: Run WPF tests and capture RED**

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter FullyQualifiedName~WindowShellTests
```

Expected: dependency properties/internal processing methods/runtime snapshot are missing.

- [ ] **Step 3: Add dependency injection and runtime snapshot**

Add to `MainWindowDependencies`:

```csharp
internal IForegroundFullscreenDetector? ForegroundFullscreenDetector { get; init; }
internal DialogueScheduler? DialogueScheduler { get; init; }
```

Create:

```csharp
internal readonly record struct AutomaticDialogueRuntimeSnapshot(
    bool IsScheduled,
    TimeSpan ScheduledDelay,
    AutomaticCadenceMode? ArmedMode,
    long ArmedAtTimestamp,
    FullscreenSnapshot Fullscreen);
```

Construct the production detector only when not injected and construct `AutomaticDialogueCadenceController` from the injected scheduler plus existing `TimeProvider`.

- [ ] **Step 4: Implement one-observation decision methods**

Use `WindowInteropHelper(this).Handle`, catch only nonfatal detector exceptions, trace them, and update the tracker with null. Add:

```csharp
private FullscreenSnapshot ObserveFullscreen();
private void ArmAutomaticTimer(DateTime localTime, FullscreenSnapshot fullscreen);
private void DisarmAutomaticTimer();
internal void ProcessAutomaticTimerTick();
internal void ProcessEventTimerTick();
internal AutomaticDialogueRuntimeSnapshot CaptureAutomaticDialogueRuntime();
```

Make the DispatcherTimer handlers one-line delegates to the internal processing methods.

- [ ] **Step 5: Rewire reply presentation and timer reset semantics**

Change `ShowEventBubble` to accept one `DateTime` and one `FullscreenSnapshot`, return whether text was displayed, and call the internal four-argument `DialogueService.GetReply`. Change `PresentReply` to return `reply.ShouldDisplayText` after presenting.

When a non-Automatic reply displays, rearm from the same time/snapshot. For Automatic, the tick method always rearms a full interval after attempting the validated reply. Silent budgeted events preserve the existing automatic arm.

In `Window_Loaded`, observe once, show Startup with the same values, and arm only once. In the event poll, sample once; if mode changed, silently rearm and return before emitting an event. In hide/exit/close call `DisarmAutomaticTimer`; on restore observe after showing/position correction and before activation, then arm.

- [ ] **Step 6: Remove duplicate legacy reschedules and update existing tests**

Remove the standalone `ScheduleNextPhrase()` calls after click and drag because visible `ShowEventBubble` now owns rearming. Update tray/event tests to use `CaptureAutomaticDialogueRuntime`, `ProcessAutomaticTimerTick`, and `ProcessEventTimerTick` for new logic instead of reflection. Preserve unrelated legacy reflection tests until their own refactor stage.

- [ ] **Step 7: Run WPF GREEN and focused lifecycle regressions**

Run the Task 6 filter, then:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -c Release --filter "FullyQualifiedName~AppLifecycleTests|FullyQualifiedName~DialogueWarmupTests|FullyQualifiedName~CompanionEventPumpTests|FullyQualifiedName~BubbleCountdownControllerTests"
```

Expected: all selected tests pass with no hidden-window detector calls and no reply changes on mode-only transitions.

- [ ] **Step 8: Commit and stage-push Task 6**

```powershell
git add src/CompanionDesktopPet/MainWindow.xaml.cs `
  src/CompanionDesktopPet/UI/MainWindowDependencies.cs `
  src/CompanionDesktopPet/UI/AutomaticDialogueRuntimeSnapshot.cs `
  tests/CompanionDesktopPet.Tests/WindowShellTests.cs
git commit -m "feat: connect fullscreen-aware dialogue timing"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
```

---

### Task 7: Privacy Documentation, Original Review Audit, and Full Gates

**Files:**

- Modify: `README.md`
- Modify: `README-persona-corpus.md`
- Modify: `docs/audits/2026-07-25-review-remediation.md`
- Modify: `docs/release/2026-07-25-expanded-runtime-release-checklist.md`

**Interfaces:**

- Consumes: final implemented behavior and fresh command output.
- Produces: auditable v1.1.0 evidence and updated user-facing privacy/frequency claims.

- [ ] **Step 1: Update user-facing cadence and privacy text**

Replace the statement that fullscreen is a future unknown signal. State the four exact cadence windows and that detection reads only foreground HWND validity/visibility/style, DWM frame geometry, and monitor bounds. Explicitly state it does not read title, process content/name, input, clipboard, user files, pixels, or network data, and failures remain unknown.

- [ ] **Step 2: Reconcile every original P0/P1/P2 review row**

In the audit document record current file/test evidence for concurrency locking, lazy fallback, random atomic temp files, exact generated counts, hash state, trigger/privacy single sources, MainWindow dependencies, accessibility, runtime checks, reflection seams, performance traits, Dawn boundary, and config derivation. Mark a row fixed only when current source and a covering test both prove it; preserve technically incorrect review claims as rejected with reasons.

- [ ] **Step 3: Run generators, simulation, validator, and Python tests**

```powershell
python tools/simulate_persona.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --days 30 --seeds 10 `
  --report reports/simulation-report.md `
  --events-json reports/simulation-events.json
python tools/validate_corpus_v2.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --allowlist config/persona-review-allowlist.json `
  --simulation reports/simulation-events.json
python -m unittest discover -s tests -v
```

Expected: simulation has zero hard violations; validator prints `Validation: 0 hard errors` and exactly the allowed `surface_inventory_observation` warning; Python reports zero failures/errors.

- [ ] **Step 4: Run fresh complete .NET Release tests**

```powershell
dotnet restore CompanionDesktopPet.sln -r win-x64
$isTestProject = dotnet msbuild `
  tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj `
  -nologo -getProperty:IsTestProject
if (($isTestProject | Out-String).Trim() -ne 'true') { throw 'IsTestProject gate failed.' }
dotnet test CompanionDesktopPet.sln -c Release --no-restore
```

Expected: nonzero discovery, zero failed, zero skipped. Record the actual final count instead of copying a historical number.

- [ ] **Step 5: Request independent code review and address findings**

Review the complete range from `d69b081` to current HEAD for correctness, privacy, WPF lifecycle, native interop, and regression coverage. Re-run the narrowest reproducer for each accepted finding, then the complete .NET suite after all fixes.

- [ ] **Step 6: Commit and stage-push documentation/evidence**

```powershell
git add README.md README-persona-corpus.md docs/audits `
  docs/release reports/simulation-report.md reports/simulation-events.json
git commit -m "docs: verify fullscreen-aware dialogue release"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
```

Wait for the final `main` GitHub Actions run and require conclusion `success` before tagging.

---

### Task 8: v1.1.0 Build, Release, Proxy Re-download, and Cleanup

**Files:**

- Modify only if evidence changes are required: `docs/release/2026-07-25-expanded-runtime-release-checklist.md`
- Generated local delivery: `outputs/CompanionDesktopPet/佳怡桌宠.exe`

**Interfaces:**

- Consumes: clean final `origin/main` and successful final CI.
- Produces: immutable annotated tag, Chinese GitHub Release, eight assets, and verified local delivery.

- [ ] **Step 1: Verify clean final main before packaging**

```powershell
git status --short --branch
git rev-parse HEAD
git rev-parse origin/main
git worktree list --porcelain
git tag --list v1.1.0
$env:HTTP_PROXY='http://127.0.0.1:7890'
$env:HTTPS_PROXY='http://127.0.0.1:7890'
gh release view v1.1.0 --repo haohaizi554/CompanionDesktopPet
```

Require a clean tree, `HEAD == origin/main`, exactly the intended single working tree, no local/remote `v1.1.0` tag, and no existing `v1.1.0` Release before tagging. `gh release view` must fail specifically with release-not-found; authentication/network failures are not absence evidence.

- [ ] **Step 2: Publish and run the real package verifier**

Use the guarded cleanup from the release checklist, then:

```powershell
dotnet restore src/CompanionDesktopPet/CompanionDesktopPet.csproj -r win-x64
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj `
  -c Release -r win-x64 --self-contained true --no-restore -o publish
Copy-Item -LiteralPath publish/CompanionDesktopPet.exe `
  -Destination outputs/CompanionDesktopPet/佳怡桌宠.exe -Force
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Publish.ps1 `
  -ExePath outputs/CompanionDesktopPet/佳怡桌宠.exe `
  -PublishExePath publish/CompanionDesktopPet.exe
```

Require one publish EXE, delivery EXE plus permitted Chinese instructions only, zero DLL/PDB/JSON sidecars, identical hashes, isolated smoke exit 0, and Authenticode `NotSigned` with null signer/timestamper.

- [ ] **Step 3: Create and push immutable v1.1.0 tag**

```powershell
git tag -a v1.1.0 -m "佳怡桌宠 v1.1.0"
git -c http.proxy=http://127.0.0.1:7890 push origin v1.1.0
```

Require the tag target to equal final `origin/main`. Do not move, delete, or force-push an existing tag.

- [ ] **Step 4: Wait for tag CI and inspect the Chinese Release**

Require tag quality gates, packaging, unsigned verification, asset manifest, and release creation to succeed. Confirm the release title/body are Chinese and describe offline single-EXE operation, SHA-256 verification, unsigned status, build source, and license/image/persona scope.

- [ ] **Step 5: Proxy-download and verify all eight immutable assets**

Download through `http://127.0.0.1:7890` into a new versioned verification directory. Require exactly:

1. `ASSET_AND_PERSONA_RIGHTS.md`
2. `LICENSE`
3. `LICENSE-SCOPE.md`
4. `NOTICE`
5. `SHA256SUMS.txt`
6. `Jiayi-Desktop-Pet-README-zh-CN.txt`
7. `Jiayi-Desktop-Pet-win-x64.zip`
8. `Jiayi-Desktop-Pet.exe`

Verify every SHA-256 entry, flat ZIP contents, `ProductVersion=1.1.0+<40-char tag SHA>`, Authenticode `NotSigned`, and an isolated smoke run of the downloaded EXE.

- [ ] **Step 6: Copy the verified direct EXE to delivery and record final evidence**

Copy only the re-downloaded direct EXE to `outputs/CompanionDesktopPet/佳怡桌宠.exe`, copy the Chinese instructions to `使用说明.txt`, rerun `Verify-Publish.ps1`, and update the release checklist with release URL, tag SHA, CI run URL, asset hashes, signature state, ZIP inventory, smoke PID/exit, and final test counts.

- [ ] **Step 7: Final evidence commit and clean-main proof**

```powershell
git add docs/release README.md
git commit -m "docs: record v1.1.0 release verification"
git -c http.proxy=http://127.0.0.1:7890 `
  -c https.proxy=http://127.0.0.1:7890 push origin main
git status --short --branch
git rev-parse HEAD
git rev-parse origin/main
```

Require final status clean and final `HEAD == origin/main`. If the evidence commit intentionally occurs after the immutable release tag, document that the tag points to the verified release source commit while `main` contains only post-release evidence.

---

## Plan Self-Review Checklist

- [x] Every section of the approved design maps to Tasks 1–8.
- [x] Every new type has one authoritative file and the same exact name/signature everywhere.
- [x] No code task changes corpus source/archive data or bypasses privacy/content safety.
- [x] Every code task contains a RED command, GREEN command, commit, and proxy push; documentation and release tasks use their authoritative verification gates.
- [x] Final verification uses fresh full outputs rather than historical test counts.
- [x] Release verification covers all eight assets, hashes, ProductVersion, unsigned signature, ZIP layout, and real smoke.
