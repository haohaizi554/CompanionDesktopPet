using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CompanionDesktopPet.UI;

internal interface IHighContrastProvider
{
    bool IsHighContrast { get; }

    event EventHandler? Changed;
}

internal sealed class SystemHighContrastProvider : IHighContrastProvider, IDisposable
{
    private bool _disposed;

    internal SystemHighContrastProvider() =>
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;

    public bool IsHighContrast => SystemParameters.HighContrast;

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class PetThemeManager : IDisposable
{
    private static readonly string[] OverrideKeys =
    [
        "Pet.Theme.HighContrast",
        "Pet.Color.Surface.Bubble",
        "Pet.Color.Surface.BubbleArrow",
        "Pet.Color.Border.Accent",
        "Pet.Color.Text.Primary",
        "Pet.Color.Shadow.Bubble",
        "Pet.Color.Shadow.Popup",
        "Pet.Brush.BubbleSurface",
        "Pet.Brush.BubbleArrow",
        "Pet.Brush.AccentBorder",
        "Pet.Brush.TextPrimary",
        "Pet.Brush.TextDisabled",
        "Pet.Brush.HeartPrimary",
        "Pet.Brush.HeartSecondary",
        "Pet.Brush.HeartTertiary",
        "Pet.Brush.MenuSurface",
        "Pet.Brush.MenuBorder",
        "Pet.Brush.MenuHighlight",
        "Pet.Brush.MenuItemHover",
        "Pet.Brush.MenuItemHoverText",
        "Pet.Brush.MenuChecked",
        "Pet.Brush.MenuSeparator",
        "Pet.Opacity.Disabled",
        "Pet.Opacity.Shadow.Bubble",
        "Pet.Opacity.Shadow.Popup"
    ];

    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private readonly IHighContrastProvider _provider;
    private bool _disposed;

    internal PetThemeManager(
        ResourceDictionary resources,
        Dispatcher dispatcher,
        IHighContrastProvider provider)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "PetThemeManager must be created on its resource dispatcher thread.");
        }

        _provider.Changed += Provider_Changed;
        ApplyCurrentPalette();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _provider.Changed -= Provider_Changed;
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void Provider_Changed(object? sender, EventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            ApplyCurrentPalette();
            return;
        }

        _dispatcher.BeginInvoke(ApplyCurrentPalette, DispatcherPriority.Send);
    }

    private void ApplyCurrentPalette()
    {
        foreach (var key in OverrideKeys)
        {
            _resources.Remove(key);
        }

        if (!_provider.IsHighContrast)
        {
            return;
        }

        _resources["Pet.Theme.HighContrast"] = true;
        _resources["Pet.Color.Surface.Bubble"] = SystemColors.WindowColor;
        _resources["Pet.Color.Surface.BubbleArrow"] = SystemColors.WindowColor;
        _resources["Pet.Color.Border.Accent"] = SystemColors.HighlightColor;
        _resources["Pet.Color.Text.Primary"] = SystemColors.WindowTextColor;
        _resources["Pet.Color.Shadow.Bubble"] = Colors.Transparent;
        _resources["Pet.Color.Shadow.Popup"] = Colors.Transparent;
        _resources["Pet.Brush.BubbleSurface"] = SystemColors.WindowBrush;
        _resources["Pet.Brush.BubbleArrow"] = SystemColors.WindowBrush;
        _resources["Pet.Brush.AccentBorder"] = SystemColors.HighlightBrush;
        _resources["Pet.Brush.TextPrimary"] = SystemColors.WindowTextBrush;
        _resources["Pet.Brush.TextDisabled"] = SystemColors.GrayTextBrush;
        _resources["Pet.Brush.HeartPrimary"] = SystemColors.HighlightBrush;
        _resources["Pet.Brush.HeartSecondary"] = SystemColors.HighlightBrush;
        _resources["Pet.Brush.HeartTertiary"] = SystemColors.HighlightBrush;
        _resources["Pet.Brush.MenuSurface"] = SystemColors.WindowBrush;
        _resources["Pet.Brush.MenuBorder"] = SystemColors.WindowTextBrush;
        _resources["Pet.Brush.MenuHighlight"] = SystemColors.HighlightBrush;
        _resources["Pet.Brush.MenuItemHover"] = SystemColors.HighlightBrush;
        _resources["Pet.Brush.MenuItemHoverText"] = SystemColors.HighlightTextBrush;
        _resources["Pet.Brush.MenuChecked"] = SystemColors.HighlightBrush;
        _resources["Pet.Brush.MenuSeparator"] = SystemColors.WindowTextBrush;
        _resources["Pet.Opacity.Disabled"] = 1d;
        _resources["Pet.Opacity.Shadow.Bubble"] = 0d;
        _resources["Pet.Opacity.Shadow.Popup"] = 0d;
    }
}
