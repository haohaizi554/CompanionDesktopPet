# Companion Desktop Pet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a cute, offline Windows desktop pet from the supplied character image and deliver it as a self-contained single-file `角色桌宠.exe`.

**Architecture:** Use one WPF `WinExe` with a transparent topmost window, focused services for dialogue, persistence, placement, scheduling, and single-instance lifetime, plus a small xUnit project for deterministic domain tests. Keep animation in a dedicated controller and use the window code-behind only to connect mouse/menu events to those services.

**Tech Stack:** C# 13, .NET 9, WPF, System.Text.Json, xUnit, PowerShell, `dotnet publish` for `win-x64`

## Global Constraints

- Target Windows 10/11 x64.
- Publish a self-contained, single-file Windows executable with no console window.
- Work offline with no API, account, telemetry, microphone, camera, or cloud dependency.
- Preserve the supplied person's face, hairstyle, clothing, pose, photographic appearance, and apparent age.
- Use a transparent cutout with a cute warm cream/blush visual treatment; do not imitate or claim the photographed person's identity.
- Embed the character PNG and application icon so the executable requires no adjacent assets.
- Save settings under `%LOCALAPPDATA%\CompanionDesktopPet\settings.json`.
- Verify every acceptance item in the approved design specification.

---

## File Map

```text
CompanionDesktopPet.sln
src/CompanionDesktopPet/
  CompanionDesktopPet.csproj        WPF build and publish settings
  App.xaml                           application resources and startup
  App.xaml.cs                        exception handling and singleton lifetime
  MainWindow.xaml                    transparent pet, bubble, and context menu
  MainWindow.xaml.cs                 mouse/menu integration and persistence hooks
  Assets/character.png               edited transparent character resource
  Assets/pet.ico                     application icon
  Models/PetSettings.cs              persisted settings contract and scale enum
  Models/ScreenGeometry.cs            testable screen rectangle/point types
  Services/DialogueService.cs         greetings and non-repeating phrases
  Services/DialogueScheduler.cs       randomized 5–10 minute scheduling
  Services/ScreenPlacementService.cs  visible-work-area clamping
  Services/WorkAreaService.cs         DPI-aware monitor work areas
  Services/SettingsService.cs         defensive JSON load and atomic save
  Services/SingleInstanceGuard.cs     process-wide named mutex
  UI/AnimationController.cs           idle and click WPF animations
  Themes/PetTheme.xaml                cream/blush colors and menu styles
tests/CompanionDesktopPet.Tests/
  CompanionDesktopPet.Tests.csproj
  DialogueServiceTests.cs
  DialogueSchedulerTests.cs
  SettingsServiceTests.cs
  ScreenPlacementServiceTests.cs
  CharacterAssetTests.cs
scripts/New-PetIcon.ps1               deterministic PNG-to-ICO conversion
scripts/Verify-Publish.ps1             artifact and process smoke checks
README.md                              build, controls, and verification guide
outputs/CompanionDesktopPet/
  角色桌宠.exe                          final deliverable
  使用说明.txt                           concise end-user controls
```

### Task 1: Create the solution and deterministic dialogue core

**Files:**
- Create: `CompanionDesktopPet.sln`
- Create: `src/CompanionDesktopPet/CompanionDesktopPet.csproj`
- Create: `tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj`
- Create: `src/CompanionDesktopPet/Services/DialogueService.cs`
- Create: `tests/CompanionDesktopPet.Tests/DialogueServiceTests.cs`

**Interfaces:**
- Consumes: no project code.
- Produces: `DialogueService.GetGreeting(DateTime) : string` and `DialogueService.GetNextPhrase(Random) : string`.

- [ ] **Step 1: Scaffold the solution and projects**

Run:

```powershell
dotnet new sln -n CompanionDesktopPet
dotnet new wpf -n CompanionDesktopPet -o src/CompanionDesktopPet -f net9.0
dotnet new xunit -n CompanionDesktopPet.Tests -o tests/CompanionDesktopPet.Tests -f net9.0
dotnet sln add src/CompanionDesktopPet/CompanionDesktopPet.csproj tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj
dotnet add tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj reference src/CompanionDesktopPet/CompanionDesktopPet.csproj
```

Expected: both projects are listed by `dotnet sln list`.

- [ ] **Step 2: Normalize the application project**

Set `src/CompanionDesktopPet/CompanionDesktopPet.csproj` to:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>CompanionDesktopPet</AssemblyName>
    <RootNamespace>CompanionDesktopPet</RootNamespace>
  </PropertyGroup>
</Project>
```

Set the test project's `<TargetFramework>` to `net9.0-windows`, add `<UseWPF>true</UseWPF>`, and add the application `ProjectReference` before compiling tests. A plain `net9.0` test project cannot reference a `net9.0-windows` WPF project.

- [ ] **Step 3: Write failing dialogue tests**

Create `tests/CompanionDesktopPet.Tests/DialogueServiceTests.cs`:

```csharp
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueServiceTests
{
    [Theory]
    [InlineData(7, "早上好呀，今天也一起加油 ♡")]
    [InlineData(13, "下午好，要记得喝水哦 ♡")]
    [InlineData(19, "晚上好，辛苦一天啦 ♡")]
    [InlineData(1, "这么晚还没睡呀？要照顾好自己哦")]
    public void GetGreeting_UsesLocalHour(int hour, string expected)
    {
        var service = new DialogueService();
        Assert.Equal(expected, service.GetGreeting(new DateTime(2026, 7, 22, hour, 0, 0)));
    }

    [Fact]
    public void GetNextPhrase_DoesNotImmediatelyRepeat()
    {
        var service = new DialogueService();
        var random = new Random(1234);
        var previous = service.GetNextPhrase(random);

        for (var index = 0; index < 30; index++)
        {
            var next = service.GetNextPhrase(random);
            Assert.NotEqual(previous, next);
            previous = next;
        }
    }
}
```

- [ ] **Step 4: Run the tests and confirm the red state**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter DialogueServiceTests
```

Expected: FAIL because `CompanionDesktopPet.Services.DialogueService` does not exist.

- [ ] **Step 5: Implement the dialogue service**

Create `src/CompanionDesktopPet/Services/DialogueService.cs`:

```csharp
namespace CompanionDesktopPet.Services;

public sealed class DialogueService
{
    private static readonly string[] Phrases =
    [
        "今天也很棒呀 ♡",
        "伸个懒腰吧，我陪着你",
        "喝一小口水，好不好？",
        "忙完这一点，就休息一下吧",
        "嘿嘿，被你发现我在发呆啦",
        "保持好心情，幸运会靠近你的",
        "别皱眉啦，慢慢来就好",
        "给你一颗小爱心 ♡"
    ];

    private int _lastPhraseIndex = -1;

    public string GetGreeting(DateTime localTime) => localTime.Hour switch
    {
        >= 5 and < 12 => "早上好呀，今天也一起加油 ♡",
        >= 12 and < 18 => "下午好，要记得喝水哦 ♡",
        >= 18 and < 24 => "晚上好，辛苦一天啦 ♡",
        _ => "这么晚还没睡呀？要照顾好自己哦"
    };

    public string GetNextPhrase(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        int index;
        do
        {
            index = random.Next(Phrases.Length);
        }
        while (index == _lastPhraseIndex);

        _lastPhraseIndex = index;
        return Phrases[index];
    }
}
```

- [ ] **Step 6: Verify and commit**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter DialogueServiceTests
git add CompanionDesktopPet.sln src tests
git commit -m "feat: add desktop pet dialogue core"
```

Expected: 5 tests pass and the commit succeeds.

### Task 2: Add settings persistence and screen placement

**Files:**
- Create: `src/CompanionDesktopPet/Models/PetSettings.cs`
- Create: `src/CompanionDesktopPet/Models/ScreenGeometry.cs`
- Create: `src/CompanionDesktopPet/Services/SettingsService.cs`
- Create: `src/CompanionDesktopPet/Services/ScreenPlacementService.cs`
- Create: `src/CompanionDesktopPet/Services/WorkAreaService.cs`
- Create: `tests/CompanionDesktopPet.Tests/SettingsServiceTests.cs`
- Create: `tests/CompanionDesktopPet.Tests/ScreenPlacementServiceTests.cs`

**Interfaces:**
- Consumes: no Task 1 services.
- Produces: `PetSettings`, `SettingsService.LoadAsync()`, `SettingsService.SaveAsync(PetSettings)`, `ScreenPlacementService.Clamp(...)`, and `WorkAreaService.GetWorkAreas(Window)`.

- [ ] **Step 1: Write failing settings and placement tests**

Create `tests/CompanionDesktopPet.Tests/SettingsServiceTests.cs`:

```csharp
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsEveryField()
    {
        var service = new SettingsService(_directory);
        var expected = new PetSettings(120, 240, PetScale.Large, true, false);
        await service.SaveAsync(expected);
        Assert.Equal(expected, await service.LoadAsync());
    }

    [Fact]
    public async Task Load_MalformedJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{broken");
        Assert.Equal(PetSettings.Default, await new SettingsService(_directory).LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
```

Create `tests/CompanionDesktopPet.Tests/ScreenPlacementServiceTests.cs`:

```csharp
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class ScreenPlacementServiceTests
{
    [Fact]
    public void Clamp_OffScreenPosition_ReturnsVisiblePoint()
    {
        var screens = new[] { new ScreenRect(0, 0, 1920, 1040) };
        var actual = ScreenPlacementService.Clamp(new ScreenPoint(5000, -800), 420, 500, screens);
        Assert.Equal(new ScreenPoint(1500, 0), actual);
    }

    [Fact]
    public void Clamp_ValidPosition_IsUnchanged()
    {
        var screens = new[] { new ScreenRect(0, 0, 1920, 1040) };
        var actual = ScreenPlacementService.Clamp(new ScreenPoint(900, 400), 420, 500, screens);
        Assert.Equal(new ScreenPoint(900, 400), actual);
    }
}
```

- [ ] **Step 2: Run the tests and confirm the red state**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter "SettingsServiceTests|ScreenPlacementServiceTests"
```

Expected: FAIL because the models and services do not exist.

- [ ] **Step 3: Add the models**

Create `src/CompanionDesktopPet/Models/PetSettings.cs`:

```csharp
namespace CompanionDesktopPet.Models;

public enum PetScale { Small, Normal, Large }

public sealed record PetSettings(
    double Left,
    double Top,
    PetScale Scale,
    bool AnimationPaused,
    bool AlwaysOnTop)
{
    public static PetSettings Default { get; } = new(double.NaN, double.NaN, PetScale.Normal, false, true);
}
```

Create `src/CompanionDesktopPet/Models/ScreenGeometry.cs`:

```csharp
namespace CompanionDesktopPet.Models;

public readonly record struct ScreenPoint(double X, double Y);
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool Contains(ScreenPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}
```

- [ ] **Step 4: Implement atomic settings persistence**

Create `src/CompanionDesktopPet/Services/SettingsService.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsService(string? directory = null) =>
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet");

    public async Task<PetSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return PetSettings.Default;
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<PetSettings>(stream, JsonOptions)
                ?? PetSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return PetSettings.Default;
        }
    }

    public async Task SaveAsync(PetSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
            await stream.FlushAsync();
        }
        File.Move(temporaryPath, SettingsPath, true);
    }
}
```

- [ ] **Step 5: Implement visible-area clamping**

Create `src/CompanionDesktopPet/Services/ScreenPlacementService.cs`:

```csharp
using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public static class ScreenPlacementService
{
    public static ScreenPoint Clamp(
        ScreenPoint requested,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<ScreenRect> workAreas)
    {
        if (workAreas.Count == 0) return requested;
        var area = workAreas.FirstOrDefault(screen => screen.Contains(requested));
        if (area.Width <= 0)
        {
            area = workAreas.MinBy(screen => DistanceSquared(requested, screen));
        }

        var maxX = Math.Max(area.Left, area.Right - windowWidth);
        var maxY = Math.Max(area.Top, area.Bottom - windowHeight);
        return new ScreenPoint(
            Math.Clamp(requested.X, area.Left, maxX),
            Math.Clamp(requested.Y, area.Top, maxY));
    }

    private static double DistanceSquared(ScreenPoint point, ScreenRect screen)
    {
        var x = Math.Clamp(point.X, screen.Left, screen.Right);
        var y = Math.Clamp(point.Y, screen.Top, screen.Bottom);
        return Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2);
    }
}
```

Create `src/CompanionDesktopPet/Services/WorkAreaService.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using CompanionDesktopPet.Models;
using Forms = System.Windows.Forms;

namespace CompanionDesktopPet.Services;

public static class WorkAreaService
{
    public static IReadOnlyList<ScreenRect> GetWorkAreas(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        return Forms.Screen.AllScreens
            .Select(screen => screen.WorkingArea)
            .Select(area => new ScreenRect(
                area.Left / dpi.DpiScaleX,
                area.Top / dpi.DpiScaleY,
                area.Width / dpi.DpiScaleX,
                area.Height / dpi.DpiScaleY))
            .ToArray();
    }
}
```

- [ ] **Step 6: Verify and commit**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter "SettingsServiceTests|ScreenPlacementServiceTests"
git add src/CompanionDesktopPet/Models src/CompanionDesktopPet/Services tests/CompanionDesktopPet.Tests
git commit -m "feat: persist pet settings and recover screen position"
```

Expected: all settings and placement tests pass.

### Task 3: Produce and verify the embedded transparent character asset

**Files:**
- Create: `src/CompanionDesktopPet/Assets/character.png`
- Create: `src/CompanionDesktopPet/Assets/pet.ico`
- Create: `scripts/New-PetIcon.ps1`
- Create: `tests/CompanionDesktopPet.Tests/CharacterAssetTests.cs`
- Modify: `src/CompanionDesktopPet/CompanionDesktopPet.csproj`
- Modify: `tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj`

**Interfaces:**
- Consumes: supplied image `C:\Users\MemoryLeak\.codex\attachments\87e5a20b-c4e2-4d5c-a084-fb07fa1923d1\image-1.png`.
- Produces: WPF resource URI `pack://application:,,,/Assets/character.png` and executable icon `Assets/pet.ico`.

- [ ] **Step 1: Edit the supplied image with the image editing tool**

Use the image editing tool with the supplied image as the sole referenced image and this exact request:

```text
Remove the entire scene background and return a transparent PNG cutout of the same photographed person. Preserve identity, face, apparent age, hairstyle, braids, sailor-style clothing, body proportions, pose, lighting, and photographic texture exactly. Keep fine hair strands with a soft clean alpha edge. Do not add objects, text, scenery, effects, makeup, body changes, age changes, sexualization, or illustration styling. Crop transparent margins closely while leaving 12 pixels of padding around the visible subject.
```

Save the returned PNG as `src/CompanionDesktopPet/Assets/character.png`.

- [ ] **Step 2: Write the failing alpha-channel test**

Add this linked asset item to the test project:

```xml
<ItemGroup>
  <None Include="..\..\src\CompanionDesktopPet\Assets\character.png"
        Link="Assets\character.png"
        CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Create `tests/CompanionDesktopPet.Tests/CharacterAssetTests.cs`:

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CompanionDesktopPet.Tests;

public sealed class CharacterAssetTests
{
    [Fact]
    public void CharacterPng_ContainsVisibleAndTransparentPixels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "character.png");
        var bitmap = new BitmapImage(new Uri(path));
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var alpha = Enumerable.Range(0, pixels.Length / 4).Select(index => pixels[index * 4 + 3]).ToArray();

        Assert.Contains(alpha, value => value == 0);
        Assert.Contains(alpha, value => value >= 250);
        Assert.True(bitmap.PixelHeight >= 500);
    }
}
```

Set `<UseWPF>true</UseWPF>` and `<TargetFramework>net9.0-windows</TargetFramework>` in the test project.

- [ ] **Step 3: Run the asset test**

Run:

```powershell
dotnet test tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj --filter CharacterAssetTests
```

Expected: PASS with both fully transparent and visible pixels detected.

- [ ] **Step 4: Create the icon conversion script**

Create `scripts/New-PetIcon.ps1`:

```powershell
param(
    [Parameter(Mandatory = $true)][string]$InputPng,
    [Parameter(Mandatory = $true)][string]$OutputIco
)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeIconMethods {
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
}
'@

$source = [System.Drawing.Image]::FromFile($InputPng)
$bitmap = New-Object System.Drawing.Bitmap 256, 256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$ratio = [Math]::Min(232.0 / $source.Width, 232.0 / $source.Height)
$width = [int]($source.Width * $ratio)
$height = [int]($source.Height * $ratio)
$graphics.DrawImage($source, [int]((256 - $width) / 2), [int]((256 - $height) / 2), $width, $height)
$handle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($handle)
$stream = [System.IO.File]::Create($OutputIco)
try { $icon.Save($stream) }
finally {
    $stream.Dispose(); $icon.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $source.Dispose()
    [NativeIconMethods]::DestroyIcon($handle) | Out-Null
}
```

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/New-PetIcon.ps1 -InputPng src/CompanionDesktopPet/Assets/character.png -OutputIco src/CompanionDesktopPet/Assets/pet.ico
```

Expected: `pet.ico` exists and has a nonzero length.

- [ ] **Step 5: Embed both assets**

Add to the application project:

```xml
<PropertyGroup>
  <ApplicationIcon>Assets\pet.ico</ApplicationIcon>
</PropertyGroup>
<ItemGroup>
  <Resource Include="Assets\character.png" />
  <Resource Include="Assets\pet.ico" />
</ItemGroup>
```

- [ ] **Step 6: Build, test, and commit**

Run:

```powershell
dotnet test CompanionDesktopPet.sln
dotnet build CompanionDesktopPet.sln -c Release
git add src/CompanionDesktopPet/Assets src/CompanionDesktopPet/CompanionDesktopPet.csproj tests scripts
git commit -m "feat: embed transparent character artwork"
```

Expected: tests and Release build pass; no loose runtime asset path is used.

### Task 4: Build the cute transparent window and animations

**Files:**
- Create: `src/CompanionDesktopPet/Themes/PetTheme.xaml`
- Create: `src/CompanionDesktopPet/UI/AnimationController.cs`
- Modify: `src/CompanionDesktopPet/App.xaml`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml`

**Interfaces:**
- Consumes: `/Assets/character.png` and the named WPF transforms/elements from `MainWindow.xaml`.
- Produces: `AnimationController.StartIdle()`, `PauseIdle()`, `ResumeIdle()`, and `PlayClickReaction()`.

- [ ] **Step 1: Add the cute palette**

Create `src/CompanionDesktopPet/Themes/PetTheme.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Color x:Key="CreamColor">#FFFFF8EE</Color>
  <Color x:Key="BlushColor">#FFFFD6DF</Color>
  <Color x:Key="RoseColor">#FFE98FA4</Color>
  <Color x:Key="CocoaColor">#FF543A3F</Color>
  <SolidColorBrush x:Key="CreamBrush" Color="{StaticResource CreamColor}" />
  <SolidColorBrush x:Key="BlushBrush" Color="{StaticResource BlushColor}" />
  <SolidColorBrush x:Key="RoseBrush" Color="{StaticResource RoseColor}" />
  <SolidColorBrush x:Key="CocoaBrush" Color="{StaticResource CocoaColor}" />
</ResourceDictionary>
```

Merge it in `App.xaml`:

```xml
<Application x:Class="CompanionDesktopPet.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="Themes/PetTheme.xaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

- [ ] **Step 2: Replace the generated window XAML**

Set `MainWindow.xaml` to a `420 x 500` transparent, frameless, taskbar-hidden window containing:

```xml
<Window x:Class="CompanionDesktopPet.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="角色桌宠" Width="420" Height="500"
        WindowStyle="None" AllowsTransparency="True" Background="{x:Null}"
        ShowInTaskbar="False" ResizeMode="NoResize" Topmost="True">
  <Grid x:Name="InteractionSurface">
    <StackPanel x:Name="SpeechBubble" Width="270" Margin="12,8,12,0"
                HorizontalAlignment="Center" VerticalAlignment="Top"
                Visibility="Collapsed" Panel.ZIndex="2">
      <Border Padding="18,12" CornerRadius="24"
              Background="{StaticResource CreamBrush}"
              BorderBrush="{StaticResource RoseBrush}" BorderThickness="2">
        <Border.Effect><DropShadowEffect Color="#663A2026" BlurRadius="14" ShadowDepth="3" Opacity="0.32" /></Border.Effect>
        <TextBlock x:Name="SpeechText" Foreground="{StaticResource CocoaBrush}"
                   FontFamily="Microsoft YaHei UI" FontSize="15" FontWeight="SemiBold"
                   TextWrapping="Wrap" TextAlignment="Center" />
      </Border>
      <Path Width="20" Height="12" HorizontalAlignment="Center"
            Fill="{StaticResource BlushBrush}" Stroke="{StaticResource RoseBrush}"
            StrokeThickness="2" Data="M 0,0 L 20,0 L 10,12 Z" />
    </StackPanel>
    <Image x:Name="PetImage" Width="320" VerticalAlignment="Bottom" HorizontalAlignment="Center"
           Source="/Assets/character.png" Stretch="Uniform" RenderTransformOrigin="0.5,0.92">
      <Image.RenderTransform>
        <TransformGroup>
          <ScaleTransform x:Name="BreathingScale" ScaleX="1" ScaleY="1" />
          <ScaleTransform x:Name="ReactionScale" ScaleX="1" ScaleY="1" />
          <RotateTransform x:Name="SwayRotation" Angle="0" />
          <RotateTransform x:Name="ReactionRotation" Angle="0" />
          <TranslateTransform x:Name="FloatingOffset" Y="0" />
        </TransformGroup>
      </Image.RenderTransform>
      <Image.ContextMenu>
        <ContextMenu FontFamily="Microsoft YaHei UI">
          <MenuItem Header="说句话 ♡" Click="SaySomething_Click" />
          <MenuItem x:Name="PauseMenuItem" Header="暂停动画" Click="ToggleAnimation_Click" />
          <Separator />
          <MenuItem Header="大小">
            <MenuItem Header="小巧" Tag="Small" Click="SetSize_Click" />
            <MenuItem Header="刚刚好" Tag="Normal" Click="SetSize_Click" />
            <MenuItem Header="大一点" Tag="Large" Click="SetSize_Click" />
          </MenuItem>
          <MenuItem x:Name="TopmostMenuItem" Header="保持置顶" IsCheckable="True" IsChecked="True" Click="ToggleTopmost_Click" />
          <MenuItem Header="回到右下角" Click="RestorePosition_Click" />
          <Separator />
          <MenuItem Header="先休息啦（退出）" Click="Exit_Click" />
        </ContextMenu>
      </Image.ContextMenu>
    </Image>
  </Grid>
</Window>
```

- [ ] **Step 3: Implement focused animation control**

Create `src/CompanionDesktopPet/UI/AnimationController.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CompanionDesktopPet.UI;

public sealed class AnimationController(
    ScaleTransform breathingScale,
    RotateTransform swayRotation,
    TranslateTransform floatingOffset,
    ScaleTransform reactionScale,
    RotateTransform reactionRotation)
{
    private readonly List<AnimationClock> _idleClocks = [];
    private bool _started;

    public void StartIdle()
    {
        foreach (var clock in _idleClocks) clock.Controller?.Remove();
        _idleClocks.Clear();
        ApplyIdle(breathingScale, ScaleTransform.ScaleXProperty, 1.0, 1.015, 2.0);
        ApplyIdle(breathingScale, ScaleTransform.ScaleYProperty, 0.985, 1.015, 2.0);
        ApplyIdle(swayRotation, RotateTransform.AngleProperty, -1.2, 1.2, 3.0);
        ApplyIdle(floatingOffset, TranslateTransform.YProperty, 3.0, -3.0, 2.5);
        _started = true;
    }

    public void PauseIdle()
    {
        foreach (var clock in _idleClocks) clock.Controller?.Pause();
    }

    public void ResumeIdle()
    {
        if (!_started) StartIdle();
        else foreach (var clock in _idleClocks) clock.Controller?.Resume();
    }

    public void PlayClickReaction()
    {
        ApplyReaction(reactionScale, ScaleTransform.ScaleXProperty, 1.0, 1.06);
        ApplyReaction(reactionScale, ScaleTransform.ScaleYProperty, 1.0, 0.94);
        ApplyReaction(reactionRotation, RotateTransform.AngleProperty, 0.0, 2.2);
    }

    private void ApplyIdle(Animatable target, DependencyProperty property, double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        var clock = animation.CreateClock();
        target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        _idleClocks.Add(clock);
    }

    private static void ApplyReaction(Animatable target, DependencyProperty property, double from, double to)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(110))
        {
            AutoReverse = true,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
```

- [ ] **Step 4: Build and run the window for visual inspection**

Run:

```powershell
dotnet build src/CompanionDesktopPet/CompanionDesktopPet.csproj -c Debug
dotnet run --project src/CompanionDesktopPet/CompanionDesktopPet.csproj
```

Expected: a transparent, topmost, taskbar-hidden character appears without a console window; cream/blush bubble styling and smooth idle motion are visible.

- [ ] **Step 5: Commit the visual shell**

Run:

```powershell
git add src/CompanionDesktopPet/App.xaml src/CompanionDesktopPet/MainWindow.xaml src/CompanionDesktopPet/Themes src/CompanionDesktopPet/UI
git commit -m "feat: add cute transparent pet window and animation"
```

### Task 5: Wire companion scheduling, interaction, persistence, and lifecycle

**Files:**
- Create: `src/CompanionDesktopPet/Services/DialogueScheduler.cs`
- Create: `src/CompanionDesktopPet/Services/SingleInstanceGuard.cs`
- Create: `tests/CompanionDesktopPet.Tests/DialogueSchedulerTests.cs`
- Modify: `src/CompanionDesktopPet/CompanionDesktopPet.csproj`
- Modify: `src/CompanionDesktopPet/MainWindow.xaml.cs`
- Modify: `src/CompanionDesktopPet/App.xaml.cs`

**Interfaces:**
- Consumes: Tasks 1–4 services, models, named UI elements, and animations.
- Produces: full click/drag/menu behavior, randomized 5–10 minute bubbles, safe exit, settings restore/save, and duplicate-instance prevention.

- [ ] **Step 1: Write failing scheduler tests**

Create `tests/CompanionDesktopPet.Tests/DialogueSchedulerTests.cs`:

```csharp
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueSchedulerTests
{
    [Fact]
    public void NextDelay_IsAlwaysBetweenFiveAndTenMinutes()
    {
        var scheduler = new DialogueScheduler(new Random(42));
        for (var index = 0; index < 100; index++)
        {
            var delay = scheduler.NextDelay();
            Assert.InRange(delay, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        }
    }
}
```

- [ ] **Step 2: Implement scheduler and singleton guard**

Create `src/CompanionDesktopPet/Services/DialogueScheduler.cs`:

```csharp
namespace CompanionDesktopPet.Services;

public sealed class DialogueScheduler(Random random)
{
    public TimeSpan NextDelay() => TimeSpan.FromSeconds(random.Next(300, 601));
}
```

Create `src/CompanionDesktopPet/Services/SingleInstanceGuard.cs`:

```csharp
namespace CompanionDesktopPet.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsPrimaryInstance { get; }

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(true, name, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    public void Dispose()
    {
        if (IsPrimaryInstance) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
```

- [ ] **Step 3: Wire startup and fatal-error handling**

Set `src/CompanionDesktopPet/App.xaml.cs` to:

```csharp
using System.Windows;
using System.Windows.Threading;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceGuard = new SingleInstanceGuard("Local\\CompanionDesktopPet-7E5D78F4");
        if (!_instanceGuard.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += HandleDispatcherException;
        var settingsService = new SettingsService();
        var settings = await settingsService.LoadAsync();
        var window = new MainWindow(settings, settingsService);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= HandleDispatcherException;
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "桌宠遇到问题，需要先休息一下。\n\n" + e.Exception.Message,
            "角色桌宠",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
```

- [ ] **Step 4: Wire the window behaviors**

Set `src/CompanionDesktopPet/MainWindow.xaml.cs` to:

```csharp
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet;

public partial class MainWindow : Window
{
    private readonly DialogueService _dialogue = new();
    private readonly DialogueScheduler _scheduler = new(new Random());
    private readonly SettingsService _settingsService;
    private readonly AnimationController _animation;
    private readonly DispatcherTimer _automaticTimer = new();
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private PetSettings _settings;
    private PetScale _scale;
    private bool _paused;
    private bool _dragged;
    private Point _mouseDown;

    public MainWindow(PetSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        _animation = new AnimationController(
            BreathingScale, SwayRotation, FloatingOffset, ReactionScale, ReactionRotation);

        Loaded += Window_Loaded;
        Closed += Window_Closed;
        PetImage.PreviewMouseLeftButtonDown += PetImage_MouseLeftButtonDown;
        PetImage.PreviewMouseMove += PetImage_MouseMove;
        PetImage.PreviewMouseLeftButtonUp += PetImage_MouseLeftButtonUp;
        _bubbleTimer.Tick += BubbleTimer_Tick;
        _automaticTimer.Tick += AutomaticTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _scale = _settings.Scale;
        _paused = _settings.AnimationPaused;
        Topmost = _settings.AlwaysOnTop;
        TopmostMenuItem.IsChecked = Topmost;
        ApplyScale(_scale);
        PlaceOnScreen();
        _animation.StartIdle();
        if (_paused) _animation.PauseIdle();
        UpdatePauseLabel();
        ShowBubble(_dialogue.GetGreeting(DateTime.Now));
        ScheduleNextPhrase();
    }

    private void PlaceOnScreen()
    {
        var workAreas = WorkAreaService.GetWorkAreas(this);
        var primaryWork = workAreas.FirstOrDefault();
        var requested = double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top)
            ? DefaultPosition(primaryWork)
            : new ScreenPoint(_settings.Left, _settings.Top);
        var clamped = ScreenPlacementService.Clamp(
            requested, ActualWidth, ActualHeight, workAreas);
        Left = clamped.X;
        Top = clamped.Y;
    }

    private ScreenPoint DefaultPosition(ScreenRect work) =>
        new(work.Right - ActualWidth - 24, work.Bottom - ActualHeight - 24);

    private void PetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = e.GetPosition(this);
        _dragged = false;
        PetImage.CaptureMouse();
        e.Handled = true;
    }

    private async void PetImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragged) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _mouseDown.X) <= 4 && Math.Abs(current.Y - _mouseDown.Y) <= 4) return;

        _dragged = true;
        PetImage.ReleaseMouseCapture();
        DragMove();
        await SaveSettingsAsync();
    }

    private void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PetImage.ReleaseMouseCapture();
        if (!_dragged)
        {
            _animation.PlayClickReaction();
            ShowBubble(_dialogue.GetNextPhrase(Random.Shared));
            ScheduleNextPhrase();
        }
        _dragged = false;
        e.Handled = true;
    }

    private void ShowBubble(string text)
    {
        SpeechText.Text = text;
        SpeechBubble.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();
        _bubbleTimer.Start();
    }

    private void BubbleTimer_Tick(object? sender, EventArgs e)
    {
        _bubbleTimer.Stop();
        SpeechBubble.Visibility = Visibility.Collapsed;
    }

    private void ScheduleNextPhrase()
    {
        _automaticTimer.Stop();
        _automaticTimer.Interval = _scheduler.NextDelay();
        _automaticTimer.Start();
    }

    private void AutomaticTimer_Tick(object? sender, EventArgs e)
    {
        ShowBubble(_dialogue.GetNextPhrase(Random.Shared));
        ScheduleNextPhrase();
    }

    private void SaySomething_Click(object sender, RoutedEventArgs e)
    {
        _animation.PlayClickReaction();
        ShowBubble(_dialogue.GetNextPhrase(Random.Shared));
        ScheduleNextPhrase();
    }

    private async void ToggleAnimation_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        if (_paused) _animation.PauseIdle(); else _animation.ResumeIdle();
        UpdatePauseLabel();
        await SaveSettingsAsync();
    }

    private void UpdatePauseLabel() => PauseMenuItem.Header = _paused ? "继续动画" : "暂停动画";

    private async void SetSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string tag }
            || !Enum.TryParse(tag, out PetScale scale)) return;
        _scale = scale;
        ApplyScale(scale);
        PlaceOnScreen();
        await SaveSettingsAsync();
    }

    private void ApplyScale(PetScale scale) => PetImage.Width = scale switch
    {
        PetScale.Small => 250,
        PetScale.Large => 390,
        _ => 320
    };

    private async void ToggleTopmost_Click(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostMenuItem.IsChecked;
        await SaveSettingsAsync();
    }

    private async void RestorePosition_Click(object sender, RoutedEventArgs e)
    {
        var point = DefaultPosition(WorkAreaService.GetWorkAreas(this).First());
        Left = point.X;
        Top = point.Y;
        await SaveSettingsAsync();
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        await SaveSettingsAsync();
        Application.Current.Shutdown();
    }

    private async Task SaveSettingsAsync()
    {
        _settings = new PetSettings(Left, Top, _scale, _paused, Topmost);
        try { await _settingsService.SaveAsync(_settings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _automaticTimer.Stop();
        _bubbleTimer.Stop();
    }
}
```

- [ ] **Step 5: Run all automated tests**

Run:

```powershell
dotnet test CompanionDesktopPet.sln
```

Expected: greeting, non-repeat, scheduler, settings, placement, and asset tests all pass.

- [ ] **Step 6: Exercise the interaction checklist**

Run the Debug build and verify:

```text
left drag moves the pet
short click bounces and displays one phrase
bubble hides after about five seconds
automatic schedule is armed after each phrase
pause/resume changes idle motion
three sizes visibly differ
topmost checkbox changes the window state
restore position returns to lower-right
exit removes the process
second launch does not create a second window
```

- [ ] **Step 7: Commit the complete runtime behavior**

Run:

```powershell
git add src tests
git commit -m "feat: complete desktop pet interaction and lifecycle"
```

### Task 6: Publish, smoke-test, and deliver the EXE

**Files:**
- Create: `scripts/Verify-Publish.ps1`
- Create: `README.md`
- Create: `outputs/CompanionDesktopPet/使用说明.txt`
- Modify: `src/CompanionDesktopPet/CompanionDesktopPet.csproj`
- Produce: `outputs/CompanionDesktopPet/角色桌宠.exe`

**Interfaces:**
- Consumes: the complete WPF application from Tasks 1–5.
- Produces: the final self-contained executable and verification evidence.

- [ ] **Step 1: Lock single-file publish properties**

Add to the application project:

```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
</PropertyGroup>
```

- [ ] **Step 2: Create the publish verifier**

Create `scripts/Verify-Publish.ps1`:

```powershell
param([Parameter(Mandatory = $true)][string]$ExePath)
$resolved = (Resolve-Path -LiteralPath $ExePath).Path
$siblings = @(Get-ChildItem -LiteralPath (Split-Path $resolved) -File)
if ($siblings.Count -ne 1 -or $siblings[0].Extension -ne '.exe') { throw 'Publish output is not a single executable.' }
$process = Start-Process -FilePath $resolved -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForInputIdle(15000)) { throw 'Desktop pet did not become input-idle.' }
    Start-Sleep -Milliseconds 1500
    $sameName = @(Get-Process -Name $process.ProcessName -ErrorAction SilentlyContinue)
    if ($sameName.Count -ne 1) { throw 'Desktop pet process count is not exactly one.' }
}
finally {
    if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500 }
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
Write-Output "PASS: $resolved"
```

- [ ] **Step 3: Create end-user instructions**

Write `outputs/CompanionDesktopPet/使用说明.txt` with:

```text
角色桌宠 使用说明

双击“角色桌宠.exe”即可启动。
拖动人物：移动桌宠
单击人物：弹跳并说句话
右击人物：暂停动画、调整大小、切换置顶、恢复位置或退出

程序完全本地运行，不联网。
如需彻底关闭，请右击人物并选择“先休息啦（退出）”。
```

Write `README.md` with the exact prerequisites, `dotnet test CompanionDesktopPet.sln`, publish command, output path, controls, and the ten acceptance checks from the design specification.

- [ ] **Step 4: Run clean tests and publish**

Run:

```powershell
dotnet clean CompanionDesktopPet.sln -c Release
dotnet test CompanionDesktopPet.sln -c Release
dotnet publish src/CompanionDesktopPet/CompanionDesktopPet.csproj -c Release -r win-x64 --self-contained true -o publish
New-Item -ItemType Directory -Force outputs/CompanionDesktopPet | Out-Null
Copy-Item publish/CompanionDesktopPet.exe outputs/CompanionDesktopPet/角色桌宠.exe -Force
```

Expected: tests pass and the output directory contains the EXE plus the instruction text before final artifact isolation.

- [ ] **Step 5: Verify the single-file artifact and real GUI**

Copy the EXE alone into a temporary verification directory, then run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Verify-Publish.ps1 -ExePath outputs/verify/角色桌宠.exe
```

Use GUI control to launch the delivered EXE, capture a screenshot, and verify transparency, cute bubble styling, idle animation, click response, drag, all context-menu commands, persistence across restart, off-screen recovery, duplicate prevention, and clean exit.

- [ ] **Step 6: Inspect artifact identity and commit**

Run:

```powershell
Get-Item outputs/CompanionDesktopPet/角色桌宠.exe | Select-Object FullName,Length,LastWriteTime
Get-FileHash outputs/CompanionDesktopPet/角色桌宠.exe -Algorithm SHA256
git status --short
git add README.md scripts src tests outputs/CompanionDesktopPet
git commit -m "build: deliver verified companion desktop pet"
```

Expected: a nonzero EXE size, SHA-256 output, clean Git status after commit, and screenshots proving the requested desktop-pet behavior.
