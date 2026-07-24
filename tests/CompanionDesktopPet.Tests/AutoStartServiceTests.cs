using System.IO;
using Microsoft.Win32;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class AutoStartServiceTests
{
    [Fact]
    public void TryGetEnabled_ReturnsFalseForAMissingRunValue()
    {
        var service = CreateService(new FakeStore());

        Assert.True(service.TryGetEnabled(out var enabled));
        Assert.False(enabled);
    }

    [Fact]
    public void TryGetEnabled_AcceptsTheExactQuotedExecutableCommand()
    {
        var store = new FakeStore
        {
            Value = "\"D:\\可爱 桌宠\\佳怡桌宠.exe\""
        };
        var service = CreateService(store);

        Assert.True(service.TryGetEnabled(out var enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void TryGetEnabled_MatchesTheExecutableCommandCaseInsensitively()
    {
        var store = new FakeStore
        {
            Value = "\"d:\\可爱 桌宠\\佳怡桌宠.EXE\""
        };
        var service = CreateService(store);

        Assert.True(service.TryGetEnabled(out var enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void TryGetEnabled_RejectsAnOldOrMalformedRunValue()
    {
        var store = new FakeStore
        {
            Value = "\"D:\\旧目录\\佳怡桌宠.exe\""
        };
        var service = CreateService(store);

        Assert.True(service.TryGetEnabled(out var enabled));
        Assert.False(enabled);

        store.Value = 42;
        Assert.True(service.TryGetEnabled(out enabled));
        Assert.False(enabled);
    }

    [Fact]
    public void TrySetEnabled_WritesAQuotedUnicodeSpacePathAsAStringValue()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        Assert.True(service.TrySetEnabled(true));
        Assert.Equal("\"D:\\可爱 桌宠\\佳怡桌宠.exe\"", store.Value);
        Assert.Equal(RegistryValueKind.String, store.Kind);
        Assert.True(service.TryGetEnabled(out var enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void TrySetEnabled_OverwritesAnOldRunValue()
    {
        var store = new FakeStore
        {
            Value = "\"D:\\旧目录\\佳怡桌宠.exe\""
        };
        var service = CreateService(store);

        Assert.True(service.TrySetEnabled(true));
        Assert.Equal("\"D:\\可爱 桌宠\\佳怡桌宠.exe\"", store.Value);
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public void TrySetEnabled_DisablesIdempotentlyWhenTheRunValueIsMissing()
    {
        var store = new FakeStore();
        var service = CreateService(store);

        Assert.True(service.TrySetEnabled(false));
        Assert.True(service.TrySetEnabled(false));
        Assert.Equal(2, store.DeleteCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("佳怡桌宠.exe")]
    [InlineData("D:\\可爱\\佳怡\"桌宠.exe")]
    public void TrySetEnabled_RejectsMissingRelativeOrQuotedExecutablePaths(string? processPath)
    {
        var store = new FakeStore();
        var service = new WindowsAutoStartService(store, () => processPath);

        Assert.False(service.TrySetEnabled(true));
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void TryGetEnabled_ReturnsFalseWhenTheStoreCannotBeRead()
    {
        var store = new FakeStore
        {
            ReadException = new IOException("read denied")
        };
        var service = CreateService(store);

        Assert.False(service.TryGetEnabled(out var enabled));
        Assert.False(enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrySetEnabled_ReturnsFalseWhenTheStoreCannotBeWritten(bool enabled)
    {
        var store = new FakeStore();
        if (enabled)
        {
            store.WriteException = new UnauthorizedAccessException("write denied");
        }
        else
        {
            store.DeleteException = new IOException("delete denied");
        }

        var service = CreateService(store);

        Assert.False(service.TrySetEnabled(enabled));
    }

    [Fact]
    public void DisabledAutoStartService_DoesNotEnableAutoStart()
    {
        var service = DisabledAutoStartService.Instance;

        Assert.True(service.TryGetEnabled(out var enabled));
        Assert.False(enabled);
        Assert.False(service.TrySetEnabled(true));
        Assert.False(service.TrySetEnabled(false));
    }

    private static WindowsAutoStartService CreateService(FakeStore store) =>
        new(store, () => @"D:\可爱 桌宠\佳怡桌宠.exe");

    private sealed class FakeStore : IAutoStartRegistryStore
    {
        public object? Value { get; set; }
        public RegistryValueKind? Kind { get; private set; }
        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }
        public Exception? ReadException { get; init; }
        public Exception? WriteException { get; set; }
        public Exception? DeleteException { get; set; }

        public object? Read(string valueName)
        {
            Assert.Equal("CompanionDesktopPet", valueName);
            if (ReadException is not null) throw ReadException;
            return Value;
        }

        public void Write(string valueName, string value, RegistryValueKind kind)
        {
            Assert.Equal("CompanionDesktopPet", valueName);
            if (WriteException is not null) throw WriteException;
            Value = value;
            Kind = kind;
            WriteCount++;
        }

        public void Delete(string valueName)
        {
            Assert.Equal("CompanionDesktopPet", valueName);
            if (DeleteException is not null) throw DeleteException;
            Value = null;
            DeleteCount++;
        }
    }
}
