using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CompanionDesktopPet.Services;

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

    public bool TryGetEnabled(out bool enabled)
    {
        enabled = false;
        return true;
    }

    public bool TrySetEnabled(bool enabled) => false;
}
