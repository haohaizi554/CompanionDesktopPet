using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal static class AtomicJsonFile
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DestinationGates =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task WriteAsync<T>(
        string destinationPath,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            destinationPath,
            value,
            options,
            cancellationToken,
            File.Exists,
            File.Delete);

    internal static async Task WriteAsync<T>(
        string destinationPath,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken,
        Func<string, bool> temporaryFileExists,
        Action<string> deleteTemporaryFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(temporaryFileExists);
        ArgumentNullException.ThrowIfNull(deleteTemporaryFile);

        destinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $"{Path.GetFileName(destinationPath)}.{Path.GetRandomFileName()}.tmp");
        var destinationGate = DestinationGates.GetOrAdd(
            destinationPath,
            static _ => new SemaphoreSlim(1, 1));
        await destinationGate.WaitAsync(cancellationToken);

        Exception? primaryFailure = null;
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    options,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                if (temporaryFileExists(temporaryPath))
                {
                    deleteTemporaryFile(temporaryPath);
                }
            }
            catch (Exception cleanupFailure) when (
                primaryFailure is not null
                && !IsFatalException(cleanupFailure))
            {
                try
                {
                    Trace.TraceError(
                        "Atomic JSON cleanup also failed after the primary write failure: {0}",
                        cleanupFailure);
                }
                catch (Exception traceFailure) when (!IsFatalException(traceFailure))
                {
                    // Preserve the primary persistence failure even if diagnostics are unavailable.
                }
            }
            finally
            {
                destinationGate.Release();
            }
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
}
