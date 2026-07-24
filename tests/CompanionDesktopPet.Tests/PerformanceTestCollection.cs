namespace CompanionDesktopPet.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceTestCollection
{
    public const string Name = "performance measurements";
}

internal static class RetainedMemoryMeasurement
{
    public static long Snapshot()
    {
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
        return GC.GetTotalMemory(forceFullCollection: false);
    }
}
