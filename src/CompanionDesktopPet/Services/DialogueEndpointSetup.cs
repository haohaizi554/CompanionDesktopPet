using System.IO;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal sealed record DialogueEndpointPreset(
    string Label,
    string BaseUrl,
    string Model,
    string ApiKey,
    string Auth,
    string Hint);

internal static class DialogueEndpointSetup
{
    internal const string Bearer = "bearer";
    internal const string ApiKeyHeader = "x-api-key";

    internal static IReadOnlyList<DialogueEndpointPreset> Presets { get; } =
    [
        new(
            "这台电脑 · Ollama",
            "http://127.0.0.1:11434/v1",
            "qwen2.5",
            "",
            Bearer,
            "Windows、Linux、macOS 上的 Ollama 都用这个地址。模型名改成你本机 ollama list 里的名字。密钥可以空着。"),
        new(
            "这台电脑 · LM Studio",
            "http://127.0.0.1:1234/v1",
            "local-model",
            "",
            Bearer,
            "在 LM Studio 里打开本地服务器。模型名用它正在加载的那个。Windows、Linux、macOS 都是 1234 端口。"),
        new(
            "这台电脑 · llama.cpp",
            "http://127.0.0.1:8080/v1",
            "local-model",
            "",
            Bearer,
            "用 llama-server 的 OpenAI 兼容端口。Windows、Linux、macOS 一样，默认 8080。"),
        new(
            "这台电脑 · vLLM",
            "http://127.0.0.1:8000/v1",
            "local-model",
            "",
            Bearer,
            "vLLM、LocalAI 这类服务多数在 8000。模型名要和它 /v1/models 里的一致。"),
        new(
            "另一台电脑",
            "http://192.168.0.10:11434/v1",
            "qwen2.5",
            "",
            Bearer,
            "模型可以跑在另一台 Windows、Linux 或 macOS 上。把 192.168.0.10 换成那台机器的地址，端口按它的服务改。"),
        new(
            "OpenAI",
            "https://api.openai.com/v1",
            "gpt-4.1-mini",
            "",
            Bearer,
            "直接用 OpenAI。密钥填 sk- 开头的那一串。请求走 Authorization: Bearer。"),
        new(
            "DeepSeek",
            "https://api.deepseek.com/v1",
            "deepseek-chat",
            "",
            Bearer,
            "直接用 DeepSeek。密钥填它控制台给的那一串。"),
        new(
            "自定义 · Bearer",
            "https://example.com/v1",
            "model-name",
            "",
            Bearer,
            "任何兼容 OpenAI /v1/chat/completions 的第三方。密钥放在 Authorization: Bearer。"),
        new(
            "自定义 · x-api-key",
            "http://127.0.0.1:8588/v1",
            "local-model",
            "",
            ApiKeyHeader,
            "服务要求请求头 x-api-key，而不是 Bearer 时用这个。地址、模型、密钥按那台服务填写。")
    ];

    internal static string NormalizeBaseUrl(string raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
        {
            return "";
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        text = text.TrimEnd('/');
        const string completions = "/chat/completions";
        if (text.EndsWith(completions, StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^completions.Length].TrimEnd('/');
        }

        if (!text.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            text += "/v1";
        }

        return text;
    }

    internal static string NormalizeAuth(string? auth)
    {
        var value = (auth ?? "").Trim().ToLowerInvariant();
        return value is Bearer or ApiKeyHeader or "both" ? value : Bearer;
    }

    internal readonly record struct DialogueEndpointDraft(string BaseUrl, string Model, string ApiKey, string Auth);

    internal static bool TryLoad(string path, out DialogueEndpointDraft draft)
    {
        draft = default;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var baseUrl = root.TryGetProperty("base_url", out var url) ? url.GetString() ?? "" : "";
            var model = root.TryGetProperty("model", out var modelValue) ? modelValue.GetString() ?? "" : "";
            var apiKey = root.TryGetProperty("api_key", out var key) ? key.GetString() ?? "" : "";
            var auth = root.TryGetProperty("auth", out var authValue) ? authValue.GetString() : "both";
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            draft = new DialogueEndpointDraft(baseUrl.Trim(), model.Trim(), apiKey.Trim(), NormalizeAuth(auth));
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static void Save(string path, string baseUrl, string model, string apiKey, string auth)
    {
        var payload = new Dictionary<string, object>
        {
            ["base_url"] = NormalizeBaseUrl(baseUrl),
            ["model"] = model.Trim(),
            ["api_key"] = string.IsNullOrWhiteSpace(apiKey) ? "local" : apiKey.Trim(),
            ["auth"] = NormalizeAuth(auth),
            ["max_tokens"] = 180,
            ["temperature"] = 0.7,
            ["timeout_seconds"] = 45
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
