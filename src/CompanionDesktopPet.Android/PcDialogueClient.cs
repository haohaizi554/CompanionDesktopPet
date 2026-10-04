using System.Net.Http;
using System.Text.Json;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Android;

internal sealed class PcDialogueClient : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(50) };
    private readonly string _baseUrl;

    public PcDialogueClient(string voiceHost)
    {
        _baseUrl = FromVoiceHost(voiceHost);
    }

    public string BaseUrl => _baseUrl;

    public static string FromVoiceHost(string voiceHost)
    {
        if (!Uri.TryCreate(voiceHost, UriKind.Absolute, out var uri))
        {
            return LinkDefaults.DialogueHost;
        }

        var builder = new UriBuilder(uri) { Port = 8766, Path = "" };
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(_baseUrl + "/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<PersonaDialogueReply> ReplyAsync(
        string text,
        IReadOnlyDictionary<string, object?>? settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["text"] = text,
                ["settings"] = settings
            });
            using var response = await _http.PostAsync(
                _baseUrl + "/v1/reply",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);
            using var document = await ReadUtf8JsonAsync(response, cancellationToken);
            var root = document.RootElement;
            if (!response.IsSuccessStatusCode)
            {
                var error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : "这句话我没接住。";
                return new PersonaDialogueReply(false, error ?? "这句话我没接住。", false);
            }

            var reply = root.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? "" : "";
            var fallback = root.TryGetProperty("fallback", out var fallbackValue) && fallbackValue.GetBoolean();
            var actions = root.TryGetProperty("actions", out var actionsValue) ? actionsValue.GetRawText() : "[]";
            return new PersonaDialogueReply(!string.IsNullOrWhiteSpace(reply), reply, fallback, actions);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new PersonaDialogueReply(false, "对话这会儿没接上，你再说一次。", false);
        }
    }

    public async Task<bool> RememberAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["text"] = text });
            using var response = await _http.PostAsync(
                _baseUrl + "/v1/remember",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<string?> SaveEndpointAsync(
        string baseUrl,
        string model,
        string apiKey,
        string auth,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["base_url"] = DialogueEndpointSetup.NormalizeBaseUrl(baseUrl),
                ["model"] = model.Trim(),
                ["api_key"] = string.IsNullOrWhiteSpace(apiKey) ? "local" : apiKey.Trim(),
                ["auth"] = DialogueEndpointSetup.NormalizeAuth(auth)
            });
            using var response = await _http.PostAsync(
                _baseUrl + "/v1/endpoint",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);
            using var document = await ReadUtf8JsonAsync(response, cancellationToken);
            var root = document.RootElement;
            if (!response.IsSuccessStatusCode || (root.TryGetProperty("ok", out var ok) && !ok.GetBoolean()))
            {
                return root.TryGetProperty("error", out var error) ? error.GetString() ?? "没保存上。" : "没保存上。";
            }

            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return "对话服务没连上。先在电脑上打开对话宿主。";
        }
    }

    public void Dispose() => _http.Dispose();

    private static async Task<JsonDocument> ReadUtf8JsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            return JsonDocument.Parse("{}"u8.ToArray());
        }

        return JsonDocument.Parse(bytes);
    }
}
