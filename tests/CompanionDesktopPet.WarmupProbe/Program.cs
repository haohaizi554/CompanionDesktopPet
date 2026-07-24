using System.Diagnostics;
using System.Text.Json;
using CompanionDesktopPet.Services;

const double maxFallbackReplyMs = 100;
const double maxColdCombinedMs = 5_000;
const double maxWarmFirstReplyMs = 250;
const long maxPrivateBytesDelta = 128L * 1024 * 1024;

var process = Process.GetCurrentProcess();
process.Refresh();
var privateBytesBefore = process.PrivateMemorySize64;
var workingSetBefore = process.WorkingSet64;
var managedBytesBefore = GC.GetTotalMemory(forceFullCollection: false);
var failures = new List<string>();
var total = Stopwatch.StartNew();

var dialogue = DialogueService.CreateDeferred();
var fallbackTimer = Stopwatch.StartNew();
var fallback = dialogue.GetReply(
    CompanionEvent.Click,
    DateTime.Now,
    new Random(20260724));
fallbackTimer.Stop();
if (!fallback.SceneId.StartsWith("fallback:", StringComparison.Ordinal)
    || !fallback.ShouldDisplayText
    || fallbackTimer.Elapsed.TotalMilliseconds > maxFallbackReplyMs)
{
    failures.Add("cold fallback reply exceeded its constant-time safety gate");
}

var warmupTimer = Stopwatch.StartNew();
var warmed = await dialogue.WarmupAsync();
warmupTimer.Stop();
if (!warmed || !dialogue.IsReady)
{
    failures.Add("full dialogue warmup did not complete");
}

var warmReplyTimer = Stopwatch.StartNew();
var full = dialogue.GetReply(
    CompanionEvent.Click,
    DateTime.Now.AddSeconds(1),
    new Random(20260725));
warmReplyTimer.Stop();
total.Stop();
if (full.SceneId.StartsWith("fallback:", StringComparison.Ordinal)
    || !full.ShouldDisplayText
    || warmReplyTimer.Elapsed.TotalMilliseconds > maxWarmFirstReplyMs)
{
    failures.Add("the first post-warmup reply did not use the ready runtime within budget");
}

process.Refresh();
var privateBytesAfter = process.PrivateMemorySize64;
var workingSetAfter = process.WorkingSet64;
var managedBytesAfter = GC.GetTotalMemory(forceFullCollection: false);
var privateBytesDelta = Math.Max(0, privateBytesAfter - privateBytesBefore);
if (total.Elapsed.TotalMilliseconds > maxColdCombinedMs)
{
    failures.Add("cold combined warmup plus first full reply exceeded 5000 ms");
}

if (privateBytesDelta > maxPrivateBytesDelta)
{
    failures.Add("cold dialogue private-bytes delta exceeded 128 MiB");
}

var metrics = new
{
    schema = "companion-dialogue-cold-probe.v1",
    fallbackReplyMs = Math.Round(fallbackTimer.Elapsed.TotalMilliseconds, 3),
    warmupMs = Math.Round(warmupTimer.Elapsed.TotalMilliseconds, 3),
    warmFirstReplyMs = Math.Round(warmReplyTimer.Elapsed.TotalMilliseconds, 3),
    coldCombinedMs = Math.Round(total.Elapsed.TotalMilliseconds, 3),
    privateBytesBefore,
    privateBytesAfter,
    privateBytesDelta,
    workingSetBefore,
    workingSetAfter,
    workingSetDelta = workingSetAfter - workingSetBefore,
    managedBytesBefore,
    managedBytesAfter,
    managedBytesDelta = managedBytesAfter - managedBytesBefore,
    passed = failures.Count == 0,
    failures
};
Console.WriteLine(JsonSerializer.Serialize(metrics));
return failures.Count == 0 ? 0 : 1;
