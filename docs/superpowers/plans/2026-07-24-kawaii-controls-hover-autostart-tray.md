# Kawaii Controls, Hover Countdown, Auto-start, and Tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the approved kawaii context menu, exact 30-DIP bubble gap, alternating click tilt, hover-paused bubble lifetime, current-user auto-start, and native tray management without changing the one-EXE delivery contract.

**Architecture:** Keep visual concerns in WPF resources/layout, move bubble lifetime and Windows auto-start into small deterministic services, and expose one shared MainWindow command surface to both WPF and tray menus. `App` owns the native tray lifetime, while smoke tests receive disabled system integration so automated verification never touches the real tray or registry.

**Tech Stack:** .NET 9, WPF, `TimeProvider`, `DispatcherTimer`, `Microsoft.Win32.Registry`, framework-provided `System.Windows.Forms.NotifyIcon`, xUnit, PowerShell release verifier, Python standard-library corpus gates.

## Global Constraints

- Target Windows x64 and keep a self-contained single EXE with zero DLL/JSON/PDB runtime sidecars.
- Add no NuGet tray package, helper process, network call, administrator requirement, HKLM entry, scheduled task, or startup-folder file.
- Store auto-start only in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; do not add a field to `PetSettings` or `settings.json`.
- Preserve the existing public MainWindow constructor and the exact existing eight-parameter internal constructor overload.
- `--smoke-test` must create neither a tray icon nor a real-registry reader/writer.
- Preserve click hearts, drag-direction lean, landing spring, dialogue provenance, memory, privacy gates, and all existing user-owned changes outside this clean worktree.
- Use RED → GREEN → REFACTOR for every production change and commit only after the focused tests pass.

---

## File Map

### New production files

- `src/CompanionDesktopPet/UI/BubbleCountdownController.cs` — pure three-state bubble lifetime and two-target hover flags.
- `src/CompanionDesktopPet/Services/AutoStartService.cs` — current-user Run-value contract, Windows store, and disabled smoke implementation.
- `src/CompanionDesktopPet/Services/TrayIconService.cs` — native NotifyIcon/menu adapter and deterministic state refresh.
- `src/CompanionDesktopPet/Properties/AssemblyInfo.cs` — exposes internal test seams only to `CompanionDesktopPet.Tests`.

### New test files

- `tests/CompanionDesktopPet.Tests/BubbleCountdownControllerTests.cs`
- `tests/CompanionDesktopPet.Tests/AutoStartServiceTests.cs`
- `tests/CompanionDesktopPet.Tests/TrayIconServiceTests.cs`

### Existing files to modify

- `src/CompanionDesktopPet/MainWindow.xaml` — 30-DIP stack layout, menu names/items/tags, kawaii style attachment.
- `src/CompanionDesktopPet/MainWindow.xaml.cs` — countdown rendering, shared commands, auto-start integration, tray command surface.
- `src/CompanionDesktopPet/Themes/PetTheme.xaml` — semantic menu tokens and complete ContextMenu/MenuItem/Separator templates.
- `src/CompanionDesktopPet/UI/AnimationController.cs` — alternating click tilt sign.
- `src/CompanionDesktopPet/App.xaml.cs` — production/no-op service composition and tray lifetime.
- `tests/CompanionDesktopPet.Tests/AnimationControllerTests.cs` — sampled negative/positive click reactions.
- `tests/CompanionDesktopPet.Tests/WindowShellTests.cs` — geometry, hover wiring, menu surface, auto-start command, hide/show integration.
- `README.md` — user operation, startup-path caveat, tray recovery, and unchanged single-file claim.
- `outputs/CompanionDesktopPet/佳怡桌宠.exe` — rebuilt verified deliverable.

---

### Task 1: Pure bubble countdown state machine

**Files:**
- Create: `src/CompanionDesktopPet/UI/BubbleCountdownController.cs`
- Create: `tests/CompanionDesktopPet.Tests/BubbleCountdownControllerTests.cs`

**Interfaces:**
- Consumes: `System.TimeProvider`.
- Produces: `BubbleCountdownController`, `BubbleCountdownState`, and `[Flags] BubbleHoverTarget` for MainWindow integration in Task 2.

- [ ] **Step 1: Write the failing state-machine tests**

Create tests with a manual monotonic provider. The test body must cover initial show, elapsed-time freeze, dual hover ownership, message reset while paused, stale tick rejection, explicit hide, and permanent close:

```csharp
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Tests;

public sealed class BubbleCountdownControllerTests
{
    [Fact]
    public void HoverPausesRemainingTimeUntilTheLastTargetLeaves()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);

        countdown.Show();
        time.Advance(TimeSpan.FromSeconds(2));
        countdown.Enter(BubbleHoverTarget.Character);
        Assert.Equal(BubbleCountdownState.HoverPaused, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Remaining);

        countdown.Enter(BubbleHoverTarget.Bubble);
        time.Advance(TimeSpan.FromSeconds(100));
        countdown.Leave(BubbleHoverTarget.Character);
        Assert.Equal(BubbleCountdownState.HoverPaused, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Remaining);

        countdown.Leave(BubbleHoverTarget.Bubble);
        Assert.Equal(BubbleCountdownState.CountingDown, countdown.State);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.True(countdown.TryExpire());
        Assert.Equal(BubbleCountdownState.Hidden, countdown.State);
    }

    [Fact]
    public void NewMessageWhileHoveredResetsToPausedFiveSeconds()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);
        countdown.Enter(BubbleHoverTarget.Character);
        countdown.Show();
        time.Advance(TimeSpan.FromMinutes(1));
        countdown.Show();

        Assert.Equal(BubbleCountdownState.HoverPaused, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(5), countdown.Remaining);
        Assert.False(countdown.TryExpire());
    }

    [Fact]
    public void HideAndCloseCannotBeRevivedByLeaveOrShow()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);
        countdown.Enter(BubbleHoverTarget.Character);
        countdown.Show();
        countdown.Hide();
        countdown.Leave(BubbleHoverTarget.Character);
        Assert.Equal(BubbleCountdownState.Hidden, countdown.State);

        countdown.Close();
        countdown.Show();
        Assert.Equal(BubbleCountdownState.Hidden, countdown.State);
        Assert.False(countdown.TryExpire());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~BubbleCountdownControllerTests"
```

Expected: compilation fails because `BubbleCountdownController`, `BubbleCountdownState`, and `BubbleHoverTarget` do not exist.

- [ ] **Step 3: Implement the pure controller**

Create the following public surface and preserve hover flags while hidden so a message appearing under a stationary character pointer starts paused:

```csharp
namespace CompanionDesktopPet.UI;

[Flags]
public enum BubbleHoverTarget
{
    None = 0,
    Character = 1,
    Bubble = 2
}

public enum BubbleCountdownState
{
    Hidden,
    CountingDown,
    HoverPaused
}

public sealed class BubbleCountdownController
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _timeProvider;
    private long _startedAt;
    private TimeSpan _remaining;
    private bool _closed;

    public BubbleCountdownController(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public BubbleCountdownState State { get; private set; }
    public BubbleHoverTarget HoverTargets { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (State != BubbleCountdownState.CountingDown)
            {
                return _remaining;
            }

            var elapsed = _timeProvider.GetElapsedTime(
                _startedAt,
                _timeProvider.GetTimestamp());
            return elapsed >= _remaining ? TimeSpan.Zero : _remaining - elapsed;
        }
    }

    public void Show()
    {
        if (_closed) return;
        _remaining = DisplayDuration;
        if (HoverTargets != BubbleHoverTarget.None)
        {
            State = BubbleCountdownState.HoverPaused;
            return;
        }

        StartCounting();
    }

    public void Enter(BubbleHoverTarget target)
    {
        if (_closed || target == BubbleHoverTarget.None) return;
        var wasClear = HoverTargets == BubbleHoverTarget.None;
        HoverTargets |= target;
        if (wasClear && State == BubbleCountdownState.CountingDown)
        {
            _remaining = Remaining;
            State = BubbleCountdownState.HoverPaused;
        }
    }

    public void Leave(BubbleHoverTarget target)
    {
        if (_closed || target == BubbleHoverTarget.None) return;
        HoverTargets &= ~target;
        if (HoverTargets == BubbleHoverTarget.None
            && State == BubbleCountdownState.HoverPaused)
        {
            if (_remaining <= TimeSpan.Zero)
            {
                Hide();
            }
            else
            {
                StartCounting();
            }
        }
    }

    public bool TryExpire()
    {
        if (_closed
            || State != BubbleCountdownState.CountingDown
            || Remaining > TimeSpan.Zero)
        {
            return false;
        }

        Hide();
        return true;
    }

    public void Hide()
    {
        State = BubbleCountdownState.Hidden;
        _remaining = TimeSpan.Zero;
    }

    public void Close()
    {
        _closed = true;
        HoverTargets = BubbleHoverTarget.None;
        Hide();
    }

    private void StartCounting()
    {
        _startedAt = _timeProvider.GetTimestamp();
        State = BubbleCountdownState.CountingDown;
    }
}
```

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all `BubbleCountdownControllerTests` pass.

- [ ] **Step 5: Commit the state machine**

```powershell
git add src/CompanionDesktopPet/UI/BubbleCountdownController.cs `
  tests/CompanionDesktopPet.Tests/BubbleCountdownControllerTests.cs
git commit -m "feat: add pauseable bubble countdown state"
```

---

### Task 2: Wire hover pausing and the exact 30-DIP layout

**Files:**
- Modify: `src/CompanionDesktopPet/MainWindow.xaml`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml.cs`
- Modify: `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`

**Interfaces:**
- Consumes: `BubbleCountdownController.Show/Enter/Leave/TryExpire/Hide/Close`, `Remaining`, and `State` from Task 1.
- Produces: hover-aware WPF timer rendering and a three-scale layout whose bubble-tail-to-stage gap is 30 DIP.

- [ ] **Step 1: Add failing integration and geometry tests**

Add one STA test that loads a window, verifies timer stop/resume across Character and Bubble hover sources, invokes a stale Tick while paused, and confirms the bubble remains visible. Add a theory over `Small`, `Normal`, and `Large` that displays a bubble, runs layout, and measures:

```csharp
var bubbleBottom = bubble.TranslatePoint(
    new Point(0, bubble.ActualHeight), window).Y;
var characterTop = stage.TranslatePoint(new Point(0, 0), window).Y;
Assert.InRange(characterTop - bubbleBottom, 29.5, 30.5);
```

The hover test must use the existing reflection helpers:

```csharp
InvokePrivate(window, "BubbleHover_MouseEnter", stage, null);
Assert.False(GetPrivateField<DispatcherTimer>(window, "_bubbleTimer").IsEnabled);
InvokePrivate(window, "BubbleHover_MouseEnter", bubble, null);
InvokePrivate(window, "BubbleHover_MouseLeave", stage, null);
Assert.False(GetPrivateField<DispatcherTimer>(window, "_bubbleTimer").IsEnabled);
InvokePrivate(window, "BubbleTimer_Tick", null, EventArgs.Empty);
Assert.Equal(Visibility.Visible, bubble.Visibility);
InvokePrivate(window, "BubbleHover_MouseLeave", bubble, null);
Assert.True(GetPrivateField<DispatcherTimer>(window, "_bubbleTimer").IsEnabled);
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~MainWindow_BubbleHover|FullyQualifiedName~MainWindow_BubbleGap"
```

Expected: reflection cannot find the hover handlers and the current geometry is far outside 30 DIP.

- [ ] **Step 3: Replace sibling layout with a bottom-aligned vertical stack**

Set `Window.Height="520"`. Insert `<StackPanel x:Name="PetStack" HorizontalAlignment="Center" VerticalAlignment="Bottom">` immediately before the existing `SpeechBubble` opening tag, and insert its closing `</StackPanel>` immediately after the existing `CharacterStage` closing tag. Do not reorder either child: SpeechBubble remains first and CharacterStage remains second.

Change the bubble attributes to:

```xml
<StackPanel x:Name="SpeechBubble"
            Width="276"
            Margin="12,0,12,30"
            HorizontalAlignment="Center"
            Visibility="Collapsed"
            Panel.ZIndex="2">
```

Remove `Margin="0,72,0,0"` and `VerticalAlignment="Bottom"` from `CharacterStage`; retain its center alignment, transparent background, render origin, contents, and ContextMenu unchanged.

- [ ] **Step 4: Wire the pure countdown to the DispatcherTimer**

Add `private readonly BubbleCountdownController _bubbleCountdown = new();`, attach `MouseEnter/MouseLeave` to both `CharacterStage` and `SpeechBubble`, and replace the old timer methods with:

```csharp
private void ShowBubble(string text)
{
    SpeechText.Text = text;
    SpeechBubble.Visibility = Visibility.Visible;
    _bubbleCountdown.Show();
    SynchronizeBubbleTimer();
}

private void BubbleHover_MouseEnter(object sender, MouseEventArgs? e)
{
    _bubbleCountdown.Enter(sender == SpeechBubble
        ? BubbleHoverTarget.Bubble
        : BubbleHoverTarget.Character);
    SynchronizeBubbleTimer();
}

private void BubbleHover_MouseLeave(object sender, MouseEventArgs? e)
{
    _bubbleCountdown.Leave(sender == SpeechBubble
        ? BubbleHoverTarget.Bubble
        : BubbleHoverTarget.Character);
    SynchronizeBubbleTimer();
}

private void BubbleTimer_Tick(object? sender, EventArgs e)
{
    if (_bubbleCountdown.TryExpire())
    {
        CollapseBubble();
        return;
    }

    SynchronizeBubbleTimer();
}

private void HideBubble()
{
    _bubbleCountdown.Hide();
    CollapseBubble();
}

private void CollapseBubble()
{
    _bubbleTimer.Stop();
    SpeechText.Text = string.Empty;
    SpeechBubble.Visibility = Visibility.Collapsed;
}

private void SynchronizeBubbleTimer()
{
    _bubbleTimer.Stop();
    if (_bubbleCountdown.State != BubbleCountdownState.CountingDown)
    {
        return;
    }

    var remaining = _bubbleCountdown.Remaining;
    _bubbleTimer.Interval = remaining > TimeSpan.FromMilliseconds(1)
        ? remaining
        : TimeSpan.FromMilliseconds(1);
    _bubbleTimer.Start();
}
```

Call `_bubbleCountdown.Close()` before stopping `_bubbleTimer` in `Window_Closed`.

- [ ] **Step 5: Run focused and existing window tests**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~WindowShellTests|FullyQualifiedName~BubbleCountdownControllerTests"
```

Expected: the new hover/geometry tests and all existing WindowShell tests pass.

- [ ] **Step 6: Commit layout and hover integration**

```powershell
git add src/CompanionDesktopPet/MainWindow.xaml `
  src/CompanionDesktopPet/MainWindow.xaml.cs `
  tests/CompanionDesktopPet.Tests/WindowShellTests.cs
git commit -m "feat: pause bubble lifetime while hovered"
```

---

### Task 3: Build the A+B kawaii ContextMenu surface

**Files:**
- Modify: `src/CompanionDesktopPet/Themes/PetTheme.xaml`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml`
- Modify: `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`

**Interfaces:**
- Consumes: native WPF `ContextMenu`, `MenuItem`, `Separator`, and the existing menu command names.
- Produces: `KawaiiContextMenuStyle`, semantic brushes, `MenuShell`, `MenuItemChrome`, and intact submenu/check behavior.

- [ ] **Step 1: Add failing template/resource tests**

Extend `MainWindow_UsesTransparentDesktopPetChrome` or add a focused test that applies templates and asserts:

```csharp
var menu = Assert.IsType<ContextMenu>(stage.ContextMenu);
menu.ApplyTemplate();
var shell = Assert.IsType<Border>(menu.Template.FindName("MenuShell", menu));
Assert.Equal(new CornerRadius(24), shell.CornerRadius);
Assert.Equal(new Thickness(2), shell.BorderThickness);
Assert.IsType<DropShadowEffect>(shell.Effect);

var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
say.ApplyTemplate();
var chrome = Assert.IsType<Border>(say.Template.FindName("MenuItemChrome", say));
Assert.Equal(new CornerRadius(14), chrome.CornerRadius);
Assert.Equal(35, say.MinHeight);

var surface = Assert.IsType<LinearGradientBrush>(window.FindResource("MenuSurfaceBrush"));
Assert.Equal(2, surface.GradientStops.Count);
```

Open the parent menu before setting the Size item `IsSubmenuOpen=true`; assert the template `PART_Popup` is open, then close both menus in `finally`.

- [ ] **Step 2: Run the menu-focused test and verify RED**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~MainWindow_KawaiiContextMenu"
```

Expected: resources and named template parts are absent.

- [ ] **Step 3: Add semantic brushes and ContextMenu shell**

In `PetTheme.xaml`, keep the existing Cream/Blush/Rose/Cocoa resources and add:

```xml
<LinearGradientBrush x:Key="MenuSurfaceBrush" StartPoint="0,0" EndPoint="1,1">
  <GradientStop Color="#FAFFFDF7" Offset="0" />
  <GradientStop Color="#E8FFE0EA" Offset="1" />
</LinearGradientBrush>
<SolidColorBrush x:Key="MenuBorderBrush" Color="#B8E56F91" />
<SolidColorBrush x:Key="MenuInnerHighlightBrush" Color="#D9FFFFFF" />
<SolidColorBrush x:Key="MenuItemHoverBrush" Color="#CCFFFFFF" />
<LinearGradientBrush x:Key="MenuSeparatorBrush" StartPoint="0,0" EndPoint="1,0">
  <GradientStop Color="#00E98FA4" Offset="0" />
  <GradientStop Color="#99E98FA4" Offset="0.5" />
  <GradientStop Color="#00E98FA4" Offset="1" />
</LinearGradientBrush>
```

Replace the implicit ContextMenu style with an explicit keyed style and keep an implicit BasedOn alias:

```xml
<Style x:Key="KawaiiContextMenuStyle" TargetType="ContextMenu">
  <Setter Property="Background" Value="Transparent" />
  <Setter Property="Foreground" Value="{StaticResource CocoaBrush}" />
  <Setter Property="FontFamily" Value="Microsoft YaHei UI" />
  <Setter Property="FontSize" Value="14" />
  <Setter Property="MinWidth" Value="270" />
  <Setter Property="Padding" Value="0" />
  <Setter Property="HasDropShadow" Value="False" />
  <Setter Property="PopupAnimation" Value="Fade" />
  <Setter Property="OverridesDefaultStyle" Value="True" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="ContextMenu">
        <Grid Margin="12">
          <Border x:Name="MenuShell"
                  Padding="11"
                  CornerRadius="24"
                  BorderThickness="2"
                  BorderBrush="{StaticResource MenuBorderBrush}"
                  Background="{StaticResource MenuSurfaceBrush}">
            <Border.Effect>
              <DropShadowEffect Color="#784F2938"
                                BlurRadius="24"
                                ShadowDepth="5"
                                Opacity="0.28" />
            </Border.Effect>
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle" />
          </Border>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
<Style TargetType="ContextMenu" BasedOn="{StaticResource KawaiiContextMenuStyle}" />
```

- [ ] **Step 4: Add complete MenuItem and Separator templates**

Add an implicit MenuItem style whose root Border is named `MenuItemChrome`, whose Popup is named `PART_Popup`, and whose triggers keep highlighting, checks, submenus, and disabled state:

```xml
<Style TargetType="MenuItem">
  <Setter Property="Foreground" Value="{StaticResource CocoaBrush}" />
  <Setter Property="Background" Value="Transparent" />
  <Setter Property="MinHeight" Value="35" />
  <Setter Property="Margin" Value="2" />
  <Setter Property="Padding" Value="0" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="MenuItem">
        <Grid>
          <Border x:Name="MenuItemChrome"
                  MinHeight="35"
                  CornerRadius="14"
                  BorderThickness="1"
                  BorderBrush="Transparent"
                  Background="{TemplateBinding Background}">
            <Grid Margin="10,0">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="24" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="24" />
              </Grid.ColumnDefinitions>
              <TextBlock x:Name="IconGlyph"
                         VerticalAlignment="Center"
                         HorizontalAlignment="Center"
                         FontFamily="Segoe UI Symbol"
                         Text="{TemplateBinding Tag}" />
              <ContentPresenter Grid.Column="1"
                                Margin="7,0,8,0"
                                VerticalAlignment="Center"
                                ContentSource="Header"
                                RecognizesAccessKey="True" />
              <TextBlock x:Name="SubmenuArrow"
                         Grid.Column="2"
                         VerticalAlignment="Center"
                         HorizontalAlignment="Center"
                         Text="›"
                         FontSize="18"
                         Visibility="Collapsed" />
            </Grid>
          </Border>
          <Popup x:Name="PART_Popup"
                 AllowsTransparency="True"
                 Focusable="False"
                 HorizontalOffset="-4"
                 IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}"
                 Placement="Right"
                 PopupAnimation="Fade">
            <Grid Margin="12">
              <Border Padding="11"
                      MinWidth="190"
                      CornerRadius="20"
                      BorderThickness="2"
                      BorderBrush="{StaticResource MenuBorderBrush}"
                      Background="{StaticResource MenuSurfaceBrush}">
                <Border.Effect>
                  <DropShadowEffect Color="#784F2938"
                                    BlurRadius="22"
                                    ShadowDepth="4"
                                    Opacity="0.26" />
                </Border.Effect>
                <StackPanel IsItemsHost="True"
                            KeyboardNavigation.DirectionalNavigation="Cycle" />
              </Border>
            </Grid>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property="IsHighlighted" Value="True">
            <Setter TargetName="MenuItemChrome" Property="Background" Value="{StaticResource MenuItemHoverBrush}" />
            <Setter TargetName="MenuItemChrome" Property="BorderBrush" Value="{StaticResource MenuInnerHighlightBrush}" />
          </Trigger>
          <Trigger Property="IsChecked" Value="True">
            <Setter TargetName="IconGlyph" Property="Text" Value="✓" />
            <Setter TargetName="IconGlyph" Property="Foreground" Value="#FFBE4B70" />
          </Trigger>
          <Trigger Property="Role" Value="SubmenuHeader">
            <Setter TargetName="SubmenuArrow" Property="Visibility" Value="Visible" />
          </Trigger>
          <Trigger Property="IsEnabled" Value="False">
            <Setter TargetName="MenuItemChrome" Property="Opacity" Value="0.46" />
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>

<Style TargetType="Separator">
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="Separator">
        <Border Height="1"
                Margin="14,6"
                Background="{StaticResource MenuSeparatorBrush}" />
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
```

Attach `Style="{StaticResource KawaiiContextMenuStyle}"` to the character ContextMenu and add stable symbol tags to existing items (`✦`, `♡`, `☾`, `◌`, `⌁`, `⌂`, `☁`) without changing their command names or headers.

- [ ] **Step 5: Run focused menu and full WindowShell tests**

Run the commands from Tasks 2 and 3. Expected: templates instantiate, the Size submenu opens, and all WindowShell tests pass.

- [ ] **Step 6: Commit the menu surface**

```powershell
git add src/CompanionDesktopPet/Themes/PetTheme.xaml `
  src/CompanionDesktopPet/MainWindow.xaml `
  tests/CompanionDesktopPet.Tests/WindowShellTests.cs
git commit -m "feat: style the pet control menu"
```

---

### Task 4: Alternate click tilt left and right

**Files:**
- Modify: `src/CompanionDesktopPet/UI/AnimationController.cs`
- Modify: `tests/CompanionDesktopPet.Tests/AnimationControllerTests.cs`

**Interfaces:**
- Consumes: existing `PlayClickReaction()` and `ApplyReaction`.
- Produces: deterministic `-2.2°`, `+2.2°` alternating click reactions without changing the public method signature.

- [ ] **Step 1: Write a failing sampled-direction test**

Add this test beside the other AnimationController STA tests:

```csharp
[Fact]
public void ClickReaction_AlternatesTiltDirection()
{
    RunOnStaThread(() =>
    {
        var breathing = new ScaleTransform();
        var sway = new RotateTransform();
        var floating = new TranslateTransform();
        var reactionScale = new ScaleTransform();
        var reactionRotation = new RotateTransform();
        var actionScale = new ScaleTransform();
        var actionRotation = new RotateTransform();
        var actionOffset = new TranslateTransform();
        var root = new Grid
        {
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    breathing,
                    sway,
                    floating,
                    reactionScale,
                    reactionRotation,
                    actionScale,
                    actionRotation,
                    actionOffset
                }
            }
        };
        var host = new Window { Content = root };
        var controller = new AnimationController(
            breathing,
            sway,
            floating,
            reactionScale,
            reactionRotation,
            actionScale,
            actionRotation,
            actionOffset,
            []);
        host.Show();
        host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        try
        {
            controller.PlayClickReaction();
            var first = SampleFor(
                () => reactionRotation.Angle,
                TimeSpan.FromMilliseconds(150));
            controller.PlayClickReaction();
            var second = SampleFor(
                () => reactionRotation.Angle,
                TimeSpan.FromMilliseconds(150));

            Assert.True(first.Min() < -0.3);
            Assert.True(first.Max() <= 0.001);
            Assert.True(second.Max() > 0.3);
            Assert.True(second.Min() >= -0.001);
            Assert.Equal(0, reactionRotation.Angle);
        }
        finally
        {
            host.Close();
        }
    });
}
```

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~ClickReaction_AlternatesTiltDirection"
```

Expected: the first animation only enters positive angles.

- [ ] **Step 3: Implement the alternating sign**

Add `private double _nextClickTiltDirection = -1;` and replace the fixed rotation target in `PlayClickReaction()`:

```csharp
var targetAngle = 2.2 * _nextClickTiltDirection;
_nextClickTiltDirection *= -1;
ApplyReaction(reactionRotation, RotateTransform.AngleProperty, 0.0, targetAngle);
```

Do not change drag lean, idle sway, action coordination, pause behavior, or public signatures.

- [ ] **Step 4: Run focused plus full AnimationController tests**

Expected: all animation tests pass and the sampled signs alternate.

- [ ] **Step 5: Commit the click direction**

```powershell
git add src/CompanionDesktopPet/UI/AnimationController.cs `
  tests/CompanionDesktopPet.Tests/AnimationControllerTests.cs
git commit -m "fix: alternate click tilt direction"
```

---

### Task 5: Current-user auto-start service

**Files:**
- Create: `src/CompanionDesktopPet/Services/AutoStartService.cs`
- Create: `src/CompanionDesktopPet/Properties/AssemblyInfo.cs`
- Create: `tests/CompanionDesktopPet.Tests/AutoStartServiceTests.cs`

**Interfaces:**
- Consumes: `Environment.ProcessPath`, `Microsoft.Win32.Registry`.
- Produces: `IAutoStartService.TryGetEnabled(out bool)` and `TrySetEnabled(bool)`, plus `DisabledAutoStartService.Instance` for smoke tests.

- [ ] **Step 1: Write failing fake-store tests**

Cover missing value, exact/case-insensitive match, old/malformed path, quoted Unicode/space path written as REG_SZ, overwrite, idempotent delete, and read/write exceptions. Use an in-memory `IAutoStartRegistryStore`; never open HKCU in a test.

The core assertions are:

```csharp
var store = new FakeStore();
var service = new WindowsAutoStartService(store, () => @"D:\可爱 桌宠\佳怡桌宠.exe");
Assert.True(service.TrySetEnabled(true));
Assert.Equal("\"D:\\可爱 桌宠\\佳怡桌宠.exe\"", store.Value);
Assert.Equal(RegistryValueKind.String, store.Kind);
Assert.True(service.TryGetEnabled(out var enabled));
Assert.True(enabled);

store.Value = "\"D:\\旧目录\\佳怡桌宠.exe\"";
Assert.True(service.TryGetEnabled(out enabled));
Assert.False(enabled);
Assert.True(service.TrySetEnabled(false));
Assert.True(store.DeleteCalled);
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~AutoStartServiceTests"
```

Expected: service interfaces and test seam do not exist.

- [ ] **Step 3: Add test visibility and service implementation**

Create `AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("CompanionDesktopPet.Tests")]
```

Create these exact contracts:

```csharp
public interface IAutoStartService
{
    bool TryGetEnabled(out bool enabled);
    bool TrySetEnabled(bool enabled);
}

internal interface IAutoStartRegistryStore
{
    object? Read(string valueName);
    void Write(string valueName, string value, RegistryValueKind kind);
    void Delete(string valueName);
}
```

Complete the file with this implementation:

```csharp
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CompanionDesktopPet.Services;

public sealed class WindowsAutoStartService : IAutoStartService
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "CompanionDesktopPet";
    private readonly IAutoStartRegistryStore _store;
    private readonly Func<string?> _processPath;

    public WindowsAutoStartService()
        : this(new CurrentUserRunRegistryStore(), () => Environment.ProcessPath)
    {
    }

    internal WindowsAutoStartService(
        IAutoStartRegistryStore store,
        Func<string?> processPath)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _processPath = processPath ?? throw new ArgumentNullException(nameof(processPath));
    }

    public bool TryGetEnabled(out bool enabled)
    {
        enabled = false;
        var expected = QuoteExecutablePath(_processPath());
        if (expected is null) return false;

        try
        {
            enabled = _store.Read(ValueName) is string actual
                && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var command = QuoteExecutablePath(_processPath());
                if (command is null) return false;
                _store.Write(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                _store.Delete(ValueName);
            }

            return true;
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return false;
        }
    }

    internal static string? QuoteExecutablePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
        || !Path.IsPathFullyQualified(path)
        || path.Contains('"')
            ? null
            : $"\"{path}\"";

    private static bool IsRegistryFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;
}

internal sealed class CurrentUserRunRegistryStore : IAutoStartRegistryStore
{
    public object? Read(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            WindowsAutoStartService.RunKeyPath,
            writable: false);
        return key?.GetValue(
            valueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public void Write(string valueName, string value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            WindowsAutoStartService.RunKeyPath,
            writable: true)
            ?? throw new IOException("The current-user Run key is unavailable.");
        key.SetValue(valueName, value, kind);
    }

    public void Delete(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            WindowsAutoStartService.RunKeyPath,
            writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

internal sealed class DisabledAutoStartService : IAutoStartService
{
    public static DisabledAutoStartService Instance { get; } = new();
    public bool TryGetEnabled(out bool enabled) { enabled = false; return true; }
    public bool TrySetEnabled(bool enabled) => false;
}
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Expected: every test passes without creating, reading, or deleting a real registry value.

- [ ] **Step 5: Commit the service**

```powershell
git add src/CompanionDesktopPet/Services/AutoStartService.cs `
  src/CompanionDesktopPet/Properties/AssemblyInfo.cs `
  tests/CompanionDesktopPet.Tests/AutoStartServiceTests.cs
git commit -m "feat: add current-user auto-start service"
```

---

### Task 6: Shared window commands and new control-panel items

**Files:**
- Modify: `src/CompanionDesktopPet/MainWindow.xaml`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml.cs`
- Modify: `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`

**Interfaces:**
- Consumes: `IAutoStartService` and `DisabledAutoStartService` from Task 5.
- Produces: internal tray-callable commands and WPF menu items that share the same state transitions.

- [ ] **Step 1: Add failing menu, service, hide/show, and idempotent-exit tests**

Use a fake `IAutoStartService` and a new complete internal MainWindow overload. Assert:

- `AutoStartMenuItem` and `HideToTrayMenuItem` exist;
- opening `ControlMenu` refreshes from the fake;
- clicking AutoStart calls `TrySetEnabled` with the requested value;
- a failed write restores the prior check and leaves the window open;
- `HideToTray()` calls `Hide()` without firing Closed or shutdown;
- `ToggleVisibilityFromTray()` shows it again;
- WPF Say/Pause/Exit and internal tray methods share the same underlying command outcomes.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~MainWindow_SystemCommands"
```

Expected: new menu fields, overload, and shared methods are absent.

- [ ] **Step 3: Add control-menu items and event wiring**

Name the ContextMenu `ControlMenu`. Add near the existing Topmost/Restore items:

```xml
<MenuItem x:Name="AutoStartMenuItem"
          Header="开机自启动"
          Tag="⌁"
          IsCheckable="True" />
<MenuItem x:Name="HideToTrayMenuItem"
          Header="藏到托盘里 ♡"
          Tag="☁" />
```

Wire `ControlMenu.Opened`, `AutoStartMenuItem.Click`, and `HideToTrayMenuItem.Click` in the constructor.

- [ ] **Step 4: Preserve constructors while injecting auto-start**

Keep the existing public constructor and eight-parameter internal constructor byte-for-byte callable. Add a complete internal overload with an `IAutoStartService` final parameter; have old overloads delegate using:

```csharp
autoStartService: suppressApplicationShutdownOnClose
    ? DisabledAutoStartService.Instance
    : new WindowsAutoStartService()
```

Add a shorter internal App composition overload receiving settings, settings service, memory service, memory snapshot, and `IAutoStartService`. Store a non-null `_autoStartService`.

- [ ] **Step 5: Extract and expose one shared command surface**

Refactor existing handlers to call these methods:

```csharp
internal void SaySomething() => ReactAndSpeak();

internal async Task ToggleAnimationAsync()
{
    _paused = !_paused;
    if (_paused)
    {
        PreserveScheduledStartupGreeting();
        InvalidateAmbientSchedule();
        CancelActiveAmbientAction();
        _actionCoordinator.Pause();
        _animation.PauseIdle();
    }
    else
    {
        _animation.ResumeIdle();
        _actionCoordinator.Resume();
        ScheduleNextAmbientAction();
    }

    UpdatePauseLabel();
    ShowEventBubble(_paused
        ? CompanionEvent.AnimationPaused
        : CompanionEvent.AnimationResumed);
    await SaveSettingsAsync();
}

internal void HideToTray() => Hide();

internal void ToggleVisibilityFromTray()
{
    if (IsVisible)
    {
        HideToTray();
        return;
    }

    Show();
    WindowState = WindowState.Normal;
    Activate();
}

internal bool TryReadAutoStart(out bool enabled) =>
    _autoStartService.TryGetEnabled(out enabled);

internal void ToggleAutoStartFromTray()
{
    var enabled = _autoStartService.TryGetEnabled(out var current) && current;
    ApplyAutoStart(!enabled);
}

internal async Task RequestExitAsync()
{
    if (_exitCommandRunning || _isClosed) return;
    _exitCommandRunning = true;
    await SaveSettingsAsync();
    await SaveAgentMemoryAsync();
    if (!_isClosed) Close();
}

private void RefreshAutoStartState()
{
    if (_autoStartService.TryGetEnabled(out var enabled))
    {
        _lastKnownAutoStart = enabled;
        AutoStartMenuItem.IsChecked = enabled;
        AutoStartMenuItem.IsEnabled = true;
        AutoStartMenuItem.ToolTip = null;
        return;
    }

    AutoStartMenuItem.IsChecked = _lastKnownAutoStart;
    AutoStartMenuItem.IsEnabled = false;
    AutoStartMenuItem.ToolTip = "Windows 暂时不允许读取开机启动设置。";
}

private void ApplyAutoStart(bool requested)
{
    var previous = _lastKnownAutoStart;
    if (_autoStartService.TrySetEnabled(requested))
    {
        RefreshAutoStartState();
        return;
    }

    _lastKnownAutoStart = previous;
    AutoStartMenuItem.IsChecked = previous;
    ShowBubble("开机启动没设置上，Windows 不让改。");
}
```

Add `_lastKnownAutoStart` and `_exitCommandRunning` fields. `ControlMenu.Opened` calls `RefreshAutoStartState`; the WPF AutoStart handler passes its toggled check to `ApplyAutoStart`; `Exit_Click` awaits `RequestExitAsync`. The fixed functional error bubble does not change `LastReply` or memory.

- [ ] **Step 6: Run focused and full WindowShell tests**

Expected: menu/service/hide/show tests pass and every existing WindowShell test stays green.

- [ ] **Step 7: Commit shared system commands**

```powershell
git add src/CompanionDesktopPet/MainWindow.xaml `
  src/CompanionDesktopPet/MainWindow.xaml.cs `
  tests/CompanionDesktopPet.Tests/WindowShellTests.cs
git commit -m "feat: add auto-start and tray window commands"
```

---

### Task 7: Native tray service and App-owned lifecycle

**Files:**
- Create: `src/CompanionDesktopPet/Services/TrayIconService.cs`
- Create: `tests/CompanionDesktopPet.Tests/TrayIconServiceTests.cs`
- Modify: `src/CompanionDesktopPet/App.xaml.cs`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml.cs`
- Modify: `tests/CompanionDesktopPet.Tests/WindowShellTests.cs`

**Interfaces:**
- Consumes: MainWindow shared commands from Task 6 and embedded `Assets/pet.ico`.
- Produces: `TrayMenuState` and disposable `TrayIconService`; App composes it only outside smoke mode.

- [ ] **Step 1: Write failing tray state/command tests without publishing an icon**

Construct the service on an STA dispatcher with `publishIcon:false`, a cloned test icon, state provider, and counting delegates. Call internal `RefreshMenu()` and `PerformClick()` on each exposed internal menu item. Assert dynamic text/checks, Dispatcher-routed commands, double-toggle method, and idempotent Dispose. No test may set `NotifyIcon.Visible=true`.

Core state assertions:

```csharp
state = new TrayMenuState(false, true, true);
service.RefreshMenu();
Assert.Equal("显示佳怡", service.ShowHideMenuItem.Text);
Assert.Equal("继续动画", service.PauseMenuItem.Text);
Assert.True(service.AutoStartMenuItem.Checked);

state = new TrayMenuState(true, false, false);
service.RefreshMenu();
Assert.Equal("藏起佳怡", service.ShowHideMenuItem.Text);
Assert.Equal("暂停动画", service.PauseMenuItem.Text);
Assert.False(service.AutoStartMenuItem.Checked);
```

- [ ] **Step 2: Run focused tray tests and verify RED**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~TrayIconServiceTests"
```

Expected: `TrayIconService` and `TrayMenuState` do not exist.

- [ ] **Step 3: Implement the native adapter**

Create this native adapter (use explicit WinForms/Drawing aliases because project implicit usings remove both namespaces):

```csharp
using System.Windows.Threading;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace CompanionDesktopPet.Services;

public readonly record struct TrayMenuState(
    bool IsWindowVisible,
    bool IsPaused,
    bool IsAutoStartEnabled);

public sealed class TrayIconService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<TrayMenuState> _getState;
    private readonly Action _toggleVisibility;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly DrawingIcon _ownedIcon;
    private bool _disposed;

    internal Forms.ToolStripMenuItem ShowHideMenuItem { get; }
    internal Forms.ToolStripMenuItem SayMenuItem { get; }
    internal Forms.ToolStripMenuItem PauseMenuItem { get; }
    internal Forms.ToolStripMenuItem AutoStartMenuItem { get; }
    internal Forms.ToolStripMenuItem ExitMenuItem { get; }

    public TrayIconService(
        Dispatcher dispatcher,
        DrawingIcon icon,
        Func<TrayMenuState> getState,
        Action toggleVisibility,
        Action say,
        Action togglePause,
        Action toggleAutoStart,
        Action exit)
        : this(
            dispatcher,
            icon,
            getState,
            toggleVisibility,
            say,
            togglePause,
            toggleAutoStart,
            exit,
            publishIcon: true)
    {
    }

    internal TrayIconService(
        Dispatcher dispatcher,
        DrawingIcon icon,
        Func<TrayMenuState> getState,
        Action toggleVisibility,
        Action say,
        Action togglePause,
        Action toggleAutoStart,
        Action exit,
        bool publishIcon)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        ArgumentNullException.ThrowIfNull(icon);
        _getState = getState ?? throw new ArgumentNullException(nameof(getState));
        _toggleVisibility = toggleVisibility ?? throw new ArgumentNullException(nameof(toggleVisibility));
        ArgumentNullException.ThrowIfNull(say);
        ArgumentNullException.ThrowIfNull(togglePause);
        ArgumentNullException.ThrowIfNull(toggleAutoStart);
        ArgumentNullException.ThrowIfNull(exit);

        _ownedIcon = (DrawingIcon)icon.Clone();
        ShowHideMenuItem = new Forms.ToolStripMenuItem();
        SayMenuItem = new Forms.ToolStripMenuItem("说句话 ♡");
        PauseMenuItem = new Forms.ToolStripMenuItem();
        AutoStartMenuItem = new Forms.ToolStripMenuItem("开机自启动")
        {
            CheckOnClick = false
        };
        ExitMenuItem = new Forms.ToolStripMenuItem("先休息啦（退出）");
        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.AddRange(
        [
            ShowHideMenuItem,
            SayMenuItem,
            PauseMenuItem,
            AutoStartMenuItem,
            new Forms.ToolStripSeparator(),
            ExitMenuItem
        ]);

        ShowHideMenuItem.Click += (_, _) => Dispatch(_toggleVisibility);
        SayMenuItem.Click += (_, _) => Dispatch(say);
        PauseMenuItem.Click += (_, _) => Dispatch(togglePause);
        AutoStartMenuItem.Click += (_, _) => Dispatch(toggleAutoStart);
        ExitMenuItem.Click += (_, _) => Dispatch(exit);
        _contextMenu.Opening += (_, _) => RefreshMenu();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _ownedIcon,
            Text = "佳怡桌宠",
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(_toggleVisibility);
        RefreshMenu();
        _notifyIcon.Visible = publishIcon;
    }

    internal void RefreshMenu()
    {
        if (_disposed) return;
        var state = _getState();
        ShowHideMenuItem.Text = state.IsWindowVisible ? "藏起佳怡" : "显示佳怡";
        PauseMenuItem.Text = state.IsPaused ? "继续动画" : "暂停动画";
        AutoStartMenuItem.Checked = state.IsAutoStartEnabled;
    }

    internal void SimulateDoubleClick() => Dispatch(_toggleVisibility);

    private void Dispatch(Action action)
    {
        if (!_disposed) _dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _contextMenu.Dispose();
        _ownedIcon.Dispose();
        _notifyIcon.Dispose();
    }
}
```

- [ ] **Step 4: Add MainWindow tray state and App composition**

Add to MainWindow:

```csharp
internal TrayMenuState GetTrayMenuState()
{
    var autoStart = _autoStartService.TryGetEnabled(out var enabled) && enabled;
    return new TrayMenuState(IsVisible, _paused, autoStart);
}
```

Add `_trayIconService` and `_autoStartService` fields to App. In `OnStartup`, choose:

```csharp
_autoStartService = _smokeTest
    ? DisabledAutoStartService.Instance
    : new WindowsAutoStartService();
```

Construct MainWindow with the new internal composition overload. Show the window first. Only when `!_smokeTest`, load `pet.ico` with `Application.GetResourceStream`, clone it, and create `TrayIconService` with delegates to the shared MainWindow methods. Catch recoverable icon/resource/native-menu exceptions; dispose any partial tray and keep the visible window running.

In `OnExit`, dispose and null the tray before disposing `_instanceGuard`. Smoke mode must never call the icon loader or Windows auto-start store.

- [ ] **Step 5: Run focused tray, App, and WindowShell tests**

```powershell
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~TrayIconServiceTests|FullyQualifiedName~WindowShellTests|FullyQualifiedName~App"
```

Expected: all focused tests pass; no real tray icon or registry value is created by tests.

- [ ] **Step 6: Commit tray lifecycle**

```powershell
git add src/CompanionDesktopPet/Services/TrayIconService.cs `
  src/CompanionDesktopPet/App.xaml.cs `
  src/CompanionDesktopPet/MainWindow.xaml.cs `
  tests/CompanionDesktopPet.Tests/TrayIconServiceTests.cs `
  tests/CompanionDesktopPet.Tests/WindowShellTests.cs
git commit -m "feat: add native tray management"
```

---

### Task 8: Documentation, visual acceptance, full gates, and one-EXE release

**Files:**
- Modify: `README.md`
- Modify: `outputs/CompanionDesktopPet/佳怡桌宠.exe`
- Verify only: all source, tests, corpus/config/report inputs, publish directory, original user worktree.

**Interfaces:**
- Consumes: all Tasks 1–7.
- Produces: documented, visually inspected, fully verified, republished one-EXE delivery and updated remote branch/PR.

- [ ] **Step 1: Update README before the final gates**

Update the operation section to document:

- right-click kawaii menu and `藏到托盘里 ♡`;
- tray double-click plus tray commands;
- auto-start is default-off and writes only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CompanionDesktopPet`;
- moving/renaming the EXE requires unchecking/rechecking auto-start;
- hover over character or bubble pauses only the bubble disappearance countdown;
- click tilt alternates left/right;
- delivery remains one EXE and zero DLL.

Add these exact operation bullets, retaining the existing size/position/privacy details around them:

```markdown
- 左键单击：显示爱心并说一句话；每次点击倾斜会在左、右之间轮换。
- 鼠标停在人物或气泡上：暂停当前气泡的消失倒计时；移开后只继续剩余时间。
- 右键人物：打开奶油樱花糖控制面板，可说话、暂停/继续、调整大小、切换置顶、切换开机自启动、恢复位置、藏到托盘或退出。
- 托盘图标：双击可显示/隐藏佳怡；右键可说话、暂停/继续、切换开机自启动或退出。
- `开机自启动` 默认关闭，只管理 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下名为 `CompanionDesktopPet` 的当前用户启动值。移动或重命名 EXE 后，请取消再重新勾选以更新路径。
```

- [ ] **Step 2: Run formatting and focused regression gates**

```powershell
git diff --check
dotnet test CompanionDesktopPet.sln -c Release --no-restore `
  --filter "FullyQualifiedName~BubbleCountdownControllerTests|FullyQualifiedName~AutoStartServiceTests|FullyQualifiedName~TrayIconServiceTests|FullyQualifiedName~AnimationControllerTests|FullyQualifiedName~WindowShellTests"
```

Expected: zero whitespace errors and every focused test passes.

- [ ] **Step 3: Run all repository gates**

```powershell
python -m unittest discover -s tests -v
python tools/validate_corpus_v2.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --allowlist config/persona-review-allowlist.json `
  --simulation reports/simulation-events.json
python tools/simulate_persona.py `
  --corpus data/optimized/persona-corpus-v2.tsv `
  --config config/persona-scheduler.json `
  --days 30 `
  --seeds 10 `
  --report reports/simulation-report.md
dotnet test CompanionDesktopPet.sln -c Release --no-restore
```

Expected: Python 182 tests (or higher) pass, validator reports `0 hard errors, 0 warnings`, simulation has zero hard violations, and .NET 197 tests (or higher) pass.

- [ ] **Step 4: Perform actual-window visual and interaction acceptance**

Launch a non-smoke Debug/Release build from the clean worktree and record checks for:

- no clipping of menu shell, shadow, items, separators, and Size submenu at 100% and the active system DPI;
- Small/Normal/Large bubble gap looks like the approved 30-DIP preview;
- consecutive clicks visibly lean left then right;
- hover over Character and Bubble separately holds the current bubble past five seconds, then resumes only the remainder;
- hide/show from pet menu and tray; say/pause/autostart labels synchronize;
- enable auto-start, verify only the expected current-user value, then disable it and verify the value is absent;
- exit removes the tray icon and leaves no process.

Do not leave auto-start enabled after acceptance.

- [ ] **Step 5: Commit source and README before rebuilding the binary**

```powershell
git add README.md
git commit -m "docs: explain tray and auto-start controls"
```

- [ ] **Step 6: Publish in a clean output directory and run the verifier**

Use the repository's established commands:

```powershell
Remove-Item publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore src/CompanionDesktopPet/CompanionDesktopPet.csproj -r win-x64
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj `
  -c Release -r win-x64 --self-contained true --no-restore -o publish
Copy-Item publish/CompanionDesktopPet.exe `
  outputs/CompanionDesktopPet/佳怡桌宠.exe -Force
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/Verify-Publish.ps1 `
  -ExePath outputs/CompanionDesktopPet/佳怡桌宠.exe `
  -PublishExePath publish/CompanionDesktopPet.exe
```

Expected: one delivery EXE, zero DLL/JSON/PDB sidecars, matching delivery/publish SHA-256, successful isolated smoke exit, no residual process, no tray icon, and no auto-start registry access.

- [ ] **Step 7: Commit the verified release artifact**

```powershell
git add outputs/CompanionDesktopPet/佳怡桌宠.exe
git commit -m "build: release kawaii tray desktop pet"
```

- [ ] **Step 8: Review, preserve the dirty user worktree, and update GitHub**

Run two review gates: task-spec compliance and code quality. Confirm the clean worktree has only the known ignored scratch/cache paths. Push the feature branch, update the existing PR, then fast-forward the original user worktree only if its twelve known dirty/untracked paths remain unchanged and no tracked overlap exists.

Final handoff must report commit range, test counts, validator/simulation results, EXE absolute path, byte size, SHA-256, tray/registry cleanup status, GitHub branch/PR, and preserved user-owned paths.
