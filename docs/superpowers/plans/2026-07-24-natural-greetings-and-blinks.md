# Natural Greetings and Blinks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a natural closed-eye blink and an explicit cute greeting action to the offline WPF desktop pet without disturbing click hearts, drag/landing motion, persona dialogue, privacy, or single-file delivery.

**Architecture:** A pure `PetActionCoordinator` arbitrates mutually exclusive ambient/drag states, while `AmbientActionScheduler` supplies deterministic blink timing. `AnimationController` owns WPF layer animations and completion/reset behavior; `MainWindow` only wires lifecycle, timer, menu, pause, and drag events. A transparent eye-only PNG overlays the unchanged base character, and a WPF badge provides the visible greeting cue.

**Tech Stack:** C# 13, .NET 9, WPF, xUnit, PNG resources, PowerShell publish verifier.

## Global Constraints

- Keep click hearts, drag tilt, and landing spring.
- Do not restore `DialogueService.GetGreeting`, corpus-driven `AnimationCue`, or `PlayAmbientGesture`.
- Blink must modify only an aligned eye overlay; never squash or replace the whole face.
- Greeting is a body lean/nod/lift plus a short `嗨♡` badge; no fabricated hand layer.
- Ambient actions stop and reset on pause, drag, close, or higher-priority motion.
- The app stays fully offline and reads no input content, clipboard, file name, window title, or network state.
- Final delivery remains one self-contained Windows EXE plus instructions, with zero adjacent DLLs.
- Preserve user-local dirty documentation and untracked legacy experiments; stage only task-owned files.

---

## File map

- Create `src/CompanionDesktopPet/Models/PetActionState.cs`: action-state and ambient-action enums.
- Create `src/CompanionDesktopPet/Services/PetActionCoordinator.cs`: pure transition arbitration.
- Create `src/CompanionDesktopPet/Services/AmbientActionScheduler.cs`: deterministic blink interval and double-blink decisions.
- Create `src/CompanionDesktopPet/Assets/character-blink-closed.png`: aligned eye-only overlay.
- Modify `src/CompanionDesktopPet/CompanionDesktopPet.csproj`: embed the overlay resource.
- Modify `src/CompanionDesktopPet/MainWindow.xaml`: blink layer, greeting badge, and menu command.
- Modify `src/CompanionDesktopPet/UI/AnimationController.cs`: blink/greeting/cancel/completion animations.
- Modify `src/CompanionDesktopPet/MainWindow.xaml.cs`: lifecycle, timer, pause/drag arbitration, and menu wiring.
- Modify `src/CompanionDesktopPet/App.xaml.cs`: make isolated smoke exercise and reset both new actions.
- Create `tests/CompanionDesktopPet.Tests/PetActionCoordinatorTests.cs`: transition coverage.
- Create `tests/CompanionDesktopPet.Tests/AmbientActionSchedulerTests.cs`: deterministic timing coverage.
- Modify `tests/CompanionDesktopPet.Tests/AnimationControllerTests.cs`: WPF animation/reset coverage.
- Modify `tests/CompanionDesktopPet.Tests/CharacterAssetTests.cs`: overlay dimensions and bounded-alpha checks.
- Modify `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`: real window wiring and lifecycle coverage.
- Modify `README.md` and `README-persona-corpus.md`: describe the new local actions accurately.
- Modify `outputs/CompanionDesktopPet/佳怡桌宠.exe`: refreshed verified single-file delivery.

---

### Task 1: Pure action arbitration and timing

**Files:**
- Create: `src/CompanionDesktopPet/Models/PetActionState.cs`
- Create: `src/CompanionDesktopPet/Services/PetActionCoordinator.cs`
- Create: `src/CompanionDesktopPet/Services/AmbientActionScheduler.cs`
- Create: `tests/CompanionDesktopPet.Tests/PetActionCoordinatorTests.cs`
- Create: `tests/CompanionDesktopPet.Tests/AmbientActionSchedulerTests.cs`

**Interfaces:**
- Produces: `PetActionState`, `PetAmbientAction`, `PetActionCoordinator.State`, `TryBeginAmbient`, `BeginDrag`, `BeginLanding`, `Complete`, `Pause`, `Resume`.
- Produces: `AmbientActionScheduler.NextBlinkDelay()` and `ShouldDoubleBlink()`.

- [ ] **Step 1: Write failing coordinator tests**

```csharp
public sealed class PetActionCoordinatorTests
{
    [Fact]
    public void AmbientActions_AreExclusiveAndCompleteBackToIdle()
    {
        var coordinator = new PetActionCoordinator();
        Assert.True(coordinator.TryBeginAmbient(PetAmbientAction.Blink));
        Assert.Equal(PetActionState.Blinking, coordinator.State);
        Assert.False(coordinator.TryBeginAmbient(PetAmbientAction.Greeting));
        coordinator.Complete(PetActionState.Blinking);
        Assert.Equal(PetActionState.Idle, coordinator.State);
    }

    [Fact]
    public void DragPauseAndLanding_HavePriorityAndRecoverExplicitly()
    {
        var coordinator = new PetActionCoordinator();
        Assert.True(coordinator.TryBeginAmbient(PetAmbientAction.Greeting));
        coordinator.BeginDrag();
        Assert.Equal(PetActionState.Dragging, coordinator.State);
        coordinator.BeginLanding();
        Assert.Equal(PetActionState.Landing, coordinator.State);
        coordinator.Complete(PetActionState.Landing);
        Assert.Equal(PetActionState.Idle, coordinator.State);
        coordinator.Pause();
        Assert.Equal(PetActionState.Paused, coordinator.State);
        Assert.False(coordinator.TryBeginAmbient(PetAmbientAction.Blink));
        coordinator.BeginDrag();
        Assert.Equal(PetActionState.Dragging, coordinator.State);
        coordinator.BeginLanding();
        coordinator.Complete(PetActionState.Landing);
        Assert.Equal(PetActionState.Paused, coordinator.State);
        coordinator.Resume();
        Assert.Equal(PetActionState.Idle, coordinator.State);
    }
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter FullyQualifiedName~PetActionCoordinatorTests
```

Expected: compilation fails because `PetActionCoordinator`, `PetActionState`, and `PetAmbientAction` do not exist.

- [ ] **Step 3: Implement the minimal state model**

```csharp
namespace CompanionDesktopPet.Models;

public enum PetActionState { Idle, Blinking, Greeting, Dragging, Landing, Paused }
public enum PetAmbientAction { Blink, Greeting }
```

```csharp
using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public sealed class PetActionCoordinator
{
    private bool _returnToPaused;

    public PetActionState State { get; private set; } = PetActionState.Idle;

    public bool TryBeginAmbient(PetAmbientAction action)
    {
        if (State != PetActionState.Idle) return false;
        State = action == PetAmbientAction.Blink
            ? PetActionState.Blinking
            : PetActionState.Greeting;
        return true;
    }

    public void BeginDrag()
    {
        _returnToPaused = State == PetActionState.Paused;
        State = PetActionState.Dragging;
    }

    public void BeginLanding()
    {
        if (State == PetActionState.Dragging) State = PetActionState.Landing;
    }

    public void Pause() => State = PetActionState.Paused;
    public void Resume() { if (State == PetActionState.Paused) State = PetActionState.Idle; }
    public void Complete(PetActionState completed)
    {
        if (State != completed) return;
        State = completed == PetActionState.Landing && _returnToPaused
            ? PetActionState.Paused
            : PetActionState.Idle;
        _returnToPaused = false;
    }
}
```

- [ ] **Step 4: Run coordinator tests and verify GREEN**

Run the Step 2 command. Expected: all `PetActionCoordinatorTests` pass.

- [ ] **Step 5: Write failing deterministic scheduler tests**

```csharp
public sealed class AmbientActionSchedulerTests
{
    [Theory]
    [InlineData(0.0, 3.2)]
    [InlineData(0.5, 5.0)]
    [InlineData(1.0, 6.8)]
    public void NextBlinkDelay_MapsSamplesIntoNaturalBounds(double sample, double seconds)
    {
        var scheduler = new AmbientActionScheduler(() => sample);
        Assert.Equal(TimeSpan.FromSeconds(seconds), scheduler.NextBlinkDelay());
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.124, true)]
    [InlineData(0.125, false)]
    [InlineData(1.0, false)]
    public void ShouldDoubleBlink_UsesOneInEightThreshold(double sample, bool expected)
    {
        Assert.Equal(expected, new AmbientActionScheduler(() => sample).ShouldDoubleBlink());
    }
}
```

- [ ] **Step 6: Run scheduler tests and verify RED**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter FullyQualifiedName~AmbientActionSchedulerTests
```

Expected: compilation fails because `AmbientActionScheduler` does not exist.

- [ ] **Step 7: Implement the scheduler with sample validation**

```csharp
namespace CompanionDesktopPet.Services;

public sealed class AmbientActionScheduler(Func<double>? sample = null)
{
    private readonly Func<double> _sample = sample ?? Random.Shared.NextDouble;

    public TimeSpan NextBlinkDelay() =>
        TimeSpan.FromSeconds(3.2 + (3.6 * NextSample()));

    public bool ShouldDoubleBlink() => NextSample() < 0.125;

    private double NextSample()
    {
        var value = _sample();
        if (!double.IsFinite(value) || value < 0 || value > 1)
            throw new InvalidOperationException("Random samples must be finite values from zero through one.");
        return value;
    }
}
```

- [ ] **Step 8: Run focused and full .NET tests**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter "FullyQualifiedName~PetActionCoordinatorTests|FullyQualifiedName~AmbientActionSchedulerTests"
dotnet test CompanionDesktopPet.sln
```

Expected: focused tests and the existing suite pass.

- [ ] **Step 9: Commit Task 1**

```powershell
git add src/CompanionDesktopPet/Models/PetActionState.cs src/CompanionDesktopPet/Services/PetActionCoordinator.cs src/CompanionDesktopPet/Services/AmbientActionScheduler.cs tests/CompanionDesktopPet.Tests/PetActionCoordinatorTests.cs tests/CompanionDesktopPet.Tests/AmbientActionSchedulerTests.cs
git commit -m "feat: add desktop pet action coordinator"
```

---

### Task 2: Closed-eye overlay asset and resource gate

**Files:**
- Create: `src/CompanionDesktopPet/Assets/character-blink-closed.png`
- Modify: `src/CompanionDesktopPet/CompanionDesktopPet.csproj`
- Modify: `tests/CompanionDesktopPet.Tests/CharacterAssetTests.cs`

**Interfaces:**
- Consumes: `Assets/character.png` at 1024×1024.
- Produces: a WPF pack resource named `Assets/character-blink-closed.png` at exactly 1024×1024.

- [ ] **Step 1: Write a failing asset contract test**

Add a test that decodes both PNGs with `BitmapDecoder`, asserts equal pixel dimensions, scans BGRA pixels, and requires:

```csharp
Assert.Equal(baseFrame.PixelWidth, overlay.PixelWidth);
Assert.Equal(baseFrame.PixelHeight, overlay.PixelHeight);
Assert.InRange(visiblePixelCount, 200, 45_000);
Assert.Equal(0, visiblePixelsOutsideEyeBounds);
```

Use normalized eye bounds covering `x=0.34..0.64` and `y=0.28..0.39` of the 1024×1024 canvas. Count a pixel as visible when alpha is at least 8.

- [ ] **Step 2: Run the asset test and verify RED**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter FullyQualifiedName~CharacterAssetTests
```

Expected: FAIL because `character-blink-closed.png` is absent.

- [ ] **Step 3: Generate the aligned overlay with the image editing tool**

Use `Assets/character.png` as the reference and this exact intent:

```text
Create a 1024x1024 transparent PNG overlay aligned pixel-for-pixel with the supplied character portrait. Keep every pixel fully transparent except two small patches over the existing eyes. Each patch must contain matching surrounding skin, eyelashes, and naturally closed eyelids so it completely covers the open eye beneath it. Preserve the exact eye positions, perspective, lighting, skin tone, lash style, and face geometry. Do not draw or change hair, eyebrows, nose, mouth, clothing, face outline, background, text, or any other region. No wink: both eyes are gently closed. Output only the sparse transparent overlay, not the full portrait.
```

Inspect the result at original resolution. Reject and regenerate if the overlay contains a rectangle edge, changes eyebrow/hair pixels, covers less than both open eyes, or has visible pixels outside the specified eye bounds.

- [ ] **Step 4: Embed the overlay resource**

Change the resource item to:

```xml
<None Remove="Assets\character.png;Assets\character-blink-closed.png;Assets\pet.ico" />
<Resource Include="Assets\character.png;Assets\character-blink-closed.png;Assets\pet.ico" />
```

- [ ] **Step 5: Run the asset contract and inspect the PNG**

Run the Step 2 command. Expected: all `CharacterAssetTests` pass. Open the PNG at original resolution and confirm transparency outside both eye patches.

- [ ] **Step 6: Commit Task 2**

```powershell
git add src/CompanionDesktopPet/Assets/character-blink-closed.png src/CompanionDesktopPet/CompanionDesktopPet.csproj tests/CompanionDesktopPet.Tests/CharacterAssetTests.cs
git commit -m "feat: add natural closed-eye overlay"
```

---

### Task 3: Test-first WPF blink and greeting animations

**Files:**
- Modify: `src/CompanionDesktopPet/UI/AnimationController.cs`
- Modify: `tests/CompanionDesktopPet.Tests/AnimationControllerTests.cs`

**Interfaces:**
- Consumes: `FrameworkElement blinkOverlay`, `FrameworkElement greetingBadge`, `TranslateTransform greetingBadgeOffset`.
- Produces: `PlayBlink(bool doubleBlink, Action completed)`, `PlayGreeting(Action completed)`, `CancelAmbientAction()`, and `PlayLanding(Action? completed = null)`.

- [ ] **Step 1: Write failing WPF animation tests**

Construct the controller on an STA thread with two extra `Border` elements and a badge offset. Assert:

```csharp
controller.PlayBlink(doubleBlink: false, completed: () => blinkCompleted = true);
Assert.True(blinkOverlay.HasAnimatedProperties);

controller.CancelAmbientAction();
Assert.Equal(0, blinkOverlay.Opacity);
Assert.Equal(0, greetingBadge.Opacity);
Assert.Equal(0, actionRotation.Angle);
Assert.Equal(0, actionOffset.Y);

controller.PlayGreeting(() => greetingCompleted = true);
Assert.True(actionRotation.HasAnimatedProperties);
Assert.True(actionOffset.HasAnimatedProperties);
Assert.True(greetingBadge.HasAnimatedProperties);
```

Add a dispatcher wait helper that pumps until callbacks fire, then assert each completion fires exactly once and every base value is neutral.

- [ ] **Step 2: Run animation tests and verify RED**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter FullyQualifiedName~AnimationControllerTests
```

Expected: compilation fails because the constructor and action APIs do not exist.

- [ ] **Step 3: Extend the controller constructor and cancellation path**

Store the new visual elements and implement `CancelAmbientAction()` by removing opacity/transform animations, restoring blink and badge opacity to zero, restoring badge offset, and calling `ResetActionBase()`.

- [ ] **Step 4: Implement `PlayBlink`**

Use `DoubleAnimationUsingKeyFrames` on `blinkOverlay.Opacity`. A single blink uses frames at 0/95/150/300 ms with values 0/1/1/0. A double blink adds 420/515/570/720 ms with values 0/1/1/0. Attach the callback to the opacity animation `Completed` event, reset the overlay, then invoke once.

- [ ] **Step 5: Implement `PlayGreeting` and landing completion**

Reset ambient/action visuals, then animate:

```text
ActionRotation.Angle: 0 -> -3.0 -> -1.0 -> 0 over 1100 ms
ActionOffset.Y:       0 -> -4.0 -> -2.0 -> 0 over 1100 ms
ActionScale.ScaleY:  1 -> 0.988 -> 1.006 -> 1 over 1100 ms
GreetingBadge.Opacity: 0 -> 1 -> 1 -> 0 over 900 ms
GreetingBadgeOffset.Y: 8 -> 0 -> -20 over 900 ms
```

Use the action-offset animation as the single completion source. Extend `PlayLanding` similarly so `MainWindow` can leave `Landing` only after the real animation finishes.

- [ ] **Step 6: Run animation tests and verify GREEN**

Run the Step 2 command. Expected: all animation tests pass without dispatcher timeouts.

- [ ] **Step 7: Run full .NET suite and commit Task 3**

```powershell
dotnet test CompanionDesktopPet.sln
git add src/CompanionDesktopPet/UI/AnimationController.cs tests/CompanionDesktopPet.Tests/AnimationControllerTests.cs
git commit -m "feat: animate natural blinks and greetings"
```

---

### Task 4: Window layers, action lifecycle, and real controls

**Files:**
- Modify: `src/CompanionDesktopPet/MainWindow.xaml`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml.cs`
- Modify: `src/CompanionDesktopPet/App.xaml.cs`
- Modify: `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`

**Interfaces:**
- Consumes: Task 1 coordinator/scheduler and Task 3 animation APIs.
- Produces: named elements `BlinkOverlay`, `GreetingBadge`, `GreetingBadgeOffset`, `GreetingMenuItem`; one `_ambientTimer`; real startup/menu/pause/drag behavior; `RunSmokeActionProbeAsync()`.

- [ ] **Step 1: Write failing real-window tests**

Add STA tests that show the window and assert:

```csharp
Assert.NotNull(window.FindName("BlinkOverlay"));
Assert.NotNull(window.FindName("GreetingBadge"));
Assert.NotNull(window.FindName("GreetingMenuItem"));
Assert.Equal(TimeSpan.FromMilliseconds(650), ambientTimer.Interval); // initial greeting
```

Invoke the real `GreetingMenuItem.Click` event and assert greeting animation properties are present. Click `PauseMenuItem`, assert the ambient timer is stopped and overlays are reset. Resume and assert a new blink interval is scheduled within 3.2–6.8 seconds. Close and assert the timer remains stopped.

Add an async smoke-probe test that calls `RunSmokeActionProbeAsync()`, requires a true result within three seconds, and verifies both overlay opacity and action transforms are neutral afterward.

- [ ] **Step 2: Run window tests and verify RED**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter FullyQualifiedName~WindowShellTests
```

Expected: FAIL because the named elements and `_ambientTimer` do not exist.

- [ ] **Step 3: Add the XAML layers and menu command**

Inside the existing 320×320 character grid, place `BlinkOverlay` immediately after `PetImage` with the same stretch/source alignment. Add `GreetingBadge` above `HeartLayer`, set `Opacity="0"`, `IsHitTestVisible="False"`, and provide a named `TranslateTransform`. Add `GreetingMenuItem Header="打个招呼♡"` and update tests to find named menu items rather than relying on fragile indices.

- [ ] **Step 4: Wire timer and startup greeting**

Create:

```csharp
private readonly DispatcherTimer _ambientTimer = new();
private readonly PetActionCoordinator _actionCoordinator = new();
private readonly AmbientActionScheduler _ambientScheduler;
private PetAmbientAction _pendingAmbientAction;
```

Allow optional scheduler injection in the constructor for tests. On first `ContentRendered`, schedule one greeting at 650 ms. After any greeting/blink completion, return the coordinator to idle and schedule a fresh blink. Timer ticks call `TryBeginAmbient`; a rejected action schedules a fresh blink rather than replaying accumulated work.

Implement `RunSmokeActionProbeAsync()` to stop normal scheduling, play a single blink and greeting sequentially through their real completion callbacks, time out each after two seconds, verify neutral final values, and return `true` only when both actions entered and exited successfully. Change `App.HandleSmokeContentRendered` to `async void`, await this probe after `TryVerifySmokeReadiness`, and close only on success; any failure exits with code 1.

- [ ] **Step 5: Wire pause, drag, landing, close, and menu arbitration**

- Pause: stop the ambient timer, cancel ambient visuals, and call `Pause()`.
- Resume: call `Resume()` and schedule a fresh blink.
- Drag threshold crossed: stop timer, cancel ambient visuals, and call `BeginDrag()` before `DragMove()`.
- Drag released: call `BeginLanding()`, then `PlayLanding` with a callback that completes landing and schedules a fresh blink.
- Greeting menu: attempt a local greeting without requesting or replacing dialogue.
- Close: stop ambient timer and cancel all ambient visuals.

- [ ] **Step 6: Run window tests and full suite**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter FullyQualifiedName~WindowShellTests
dotnet test CompanionDesktopPet.sln
```

Expected: focused and full tests pass. Existing startup reply provenance remains v2, click hearts remain available, and corpus cues remain `none`.

- [ ] **Step 7: Commit Task 4**

```powershell
git add src/CompanionDesktopPet/MainWindow.xaml src/CompanionDesktopPet/MainWindow.xaml.cs src/CompanionDesktopPet/App.xaml.cs tests/CompanionDesktopPet.Tests/WindowShellTests.cs
git commit -m "feat: schedule local greeting and blink actions"
```

---

### Task 5: Documentation, visual acceptance, and single-file release

**Files:**
- Modify: `README.md`
- Modify: `README-persona-corpus.md`
- Modify: `outputs/CompanionDesktopPet/佳怡桌宠.exe`
- Verify: `outputs/CompanionDesktopPet/使用说明.txt`

**Interfaces:**
- Consumes: completed Tasks 1–4.
- Produces: accurate documentation and a verified refreshed delivery.

- [ ] **Step 1: Replace obsolete exclusions in both READMEs**

Document that the pet now includes a natural eye-overlay blink, one startup greeting, and a `打个招呼♡` menu action. Keep the statement that there is no legacy `GetGreeting`, no corpus-driven gesture, no fake hand wave, and no network/input-content dependency.

- [ ] **Step 2: Run all static and automated gates**

Run:

```powershell
dotnet restore CompanionDesktopPet.sln -r win-x64
dotnet test CompanionDesktopPet.sln --no-restore
python -m unittest discover -s tests
python tools/validate_corpus_v2.py --corpus data/optimized/persona-corpus-v2.tsv --config config/persona-scheduler.json --allowlist config/persona-review-allowlist.json --simulation reports/simulation-events.json
git diff --check
```

Expected: all .NET and Python tests pass; corpus validator reports 0 hard errors and 0 warnings; diff check is clean.

- [ ] **Step 3: Publish a fresh single-file executable**

Resolve the repository root first and confirm both `publish` and `outputs/CompanionDesktopPet` are inside it. Then run:

```powershell
Remove-Item -LiteralPath publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore src/CompanionDesktopPet/CompanionDesktopPet.csproj -r win-x64
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj -c Release -r win-x64 --self-contained true --no-restore -o publish
Copy-Item -LiteralPath publish/CompanionDesktopPet.exe -Destination outputs/CompanionDesktopPet/佳怡桌宠.exe -Force
```

Ensure the delivery directory contains exactly the EXE and `使用说明.txt`.

- [ ] **Step 4: Run the real publish verifier**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-Publish.ps1 -ExePath outputs/CompanionDesktopPet/佳怡桌宠.exe -PublishExePath publish/CompanionDesktopPet.exe
```

Expected: one EXE, zero DLL/runtime sidecars, privacy scan PASS, isolated `--smoke-test` exits 0, and no residual process.

- [ ] **Step 5: Perform visual acceptance**

Launch the delivery EXE in an isolated test profile. Record screenshots or a short capture proving:

- both eyelids close without a rectangular patch or whole-face squash;
- startup greeting and menu greeting are readable and restore neutral transforms;
- pause freezes ambient actions; resume restarts them;
- dragging during an action cancels it cleanly and landing still springs;
- click hearts still render.

If visual quality fails, replace the overlay and repeat asset tests, runtime acceptance, publish, and verifier; do not weaken the acceptance test.

- [ ] **Step 6: Commit release artifacts and docs**

```powershell
git add README.md README-persona-corpus.md outputs/CompanionDesktopPet/佳怡桌宠.exe
git commit -m "build: release desktop pet greeting and blink actions"
```

- [ ] **Step 7: Independent review and cloud update**

Request an independent whole-branch review against `5c55bc8`, fix all Critical/Important findings, rerun affected gates, fast-forward `feat/cute-companion-desktop-pet`, push without force, and verify draft PR #1 points to the final commit and exposes the refreshed README and EXE blob.
