using System.IO;
using System.Text.Json;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueEndpointSetupTests
{
    [Theory]
    [InlineData("127.0.0.1:11434", "http://127.0.0.1:11434/v1")]
    [InlineData("http://192.168.1.8:1234/v1/", "http://192.168.1.8:1234/v1")]
    [InlineData("https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1")]
    [InlineData("https://api.deepseek.com/v1", "https://api.deepseek.com/v1")]
    public void NormalizeBaseUrl_AcceptsHostPortAndCompletions(string raw, string expected)
    {
        Assert.Equal(expected, DialogueEndpointSetup.NormalizeBaseUrl(raw));
    }

    [Fact]
    public void Save_WritesBearerLocalEndpointAndBlankKeyBecomesLocal()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "llm.runtime.json");
        try
        {
            DialogueEndpointSetup.Save(path, "http://127.0.0.1:11434", "qwen2.5", "  ", DialogueEndpointSetup.Bearer);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("http://127.0.0.1:11434/v1", root.GetProperty("base_url").GetString());
            Assert.Equal("qwen2.5", root.GetProperty("model").GetString());
            Assert.Equal("local", root.GetProperty("api_key").GetString());
            Assert.Equal("bearer", root.GetProperty("auth").GetString());
            Assert.True(DialogueEndpointSetup.TryLoad(path, out var draft));
            Assert.Equal("qwen2.5", draft.Model);
            Assert.Equal("bearer", draft.Auth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_KeepsMissingAuthAsBoth()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "llm.runtime.json");
        try
        {
            File.WriteAllText(path, """{"base_url":"http://10.0.0.8:8588/v1","model":"local-model","api_key":"8588"}""");
            Assert.True(DialogueEndpointSetup.TryLoad(path, out var draft));
            Assert.Equal("both", draft.Auth);
            Assert.Equal("8588", draft.ApiKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
