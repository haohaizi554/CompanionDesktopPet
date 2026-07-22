using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void SecondGuardWithSameName_IsNotPrimary()
    {
        var name = "Local\\CompanionDesktopPet-Test-" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceGuard(name);
        using var duplicate = new SingleInstanceGuard(name);

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(duplicate.IsPrimaryInstance);
    }
}
