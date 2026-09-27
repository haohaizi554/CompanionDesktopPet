using System.Diagnostics;
using System.IO;
using System.Text;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class PersonaDialogueClientTests
{
    [Fact]
    public async Task Reply_RoundTripsOneLineAndStopsOnDispose()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "echo.py");
        await File.WriteAllTextAsync(
            script,
            """
            import json, sys
            print(json.dumps({"type": "ready", "lines": 1}), flush=True)
            for raw in sys.stdin:
                message = json.loads(raw)
                if message.get("type") == "exit":
                    break
                print(json.dumps({
                    "id": message.get("id"),
                    "type": "reply",
                    "text": "我在。" + message.get("text", ""),
                    "fallback": False
                }, ensure_ascii=False), flush=True)
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var start = new ProcessStartInfo
        {
            FileName = "python",
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add(script);
        start.Environment["PYTHONIOENCODING"] = "utf-8";

        using var client = new PersonaDialogueClient(start);
        Assert.True(await client.StartAsync());
        var reply = await client.ReplyAsync("今天有点累");
        Assert.True(reply.Ok);
        Assert.Equal("我在。今天有点累", reply.Text);
        Assert.False(reply.Fallback);
    }

    [Fact]
    public async Task Remember_is_acked_without_a_reply()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "echo.py");
        await File.WriteAllTextAsync(
            script,
            """
            import json, sys
            print(json.dumps({"type": "ready", "lines": 1}), flush=True)
            heard = ""
            for raw in sys.stdin:
                message = json.loads(raw)
                if message.get("type") == "exit":
                    break
                if message.get("type") == "remember":
                    heard = message.get("text") or ""
                    print(json.dumps({"id": message.get("id"), "type": "remembered"}), flush=True)
                    continue
                print(json.dumps({
                    "id": message.get("id"),
                    "type": "reply",
                    "text": heard,
                    "fallback": False
                }, ensure_ascii=False), flush=True)
            """,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var start = new ProcessStartInfo
        {
            FileName = "python",
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add(script);
        start.Environment["PYTHONIOENCODING"] = "utf-8";

        using var client = new PersonaDialogueClient(start);
        Assert.True(await client.StartAsync());
        Assert.True(await client.RememberAsync("湿热的天气把树叶养得很绿。"));
        var reply = await client.ReplyAsync("我有点累");
        Assert.True(reply.Ok);
        Assert.Equal("湿热的天气把树叶养得很绿。", reply.Text);
    }
}
