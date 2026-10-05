using System.Net.Http;
using System.Text.Json;
using Android.Content;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Android;

internal sealed class PcDialogueClient : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(8)
    })
    { Timeout = TimeSpan.FromSeconds(50) };
    private readonly string _baseUrl;
    private readonly Context? _context;
    private readonly string? _stateDirectory;

    public PcDialogueClient(string voiceHost, Context? context = null, string? stateDirectory = null)
    {
        _baseUrl = FromVoiceHost(voiceHost);
        _context = context;
        _stateDirectory = stateDirectory;
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
        if (await PingPublicAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

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
        if (_context is not null && !string.IsNullOrWhiteSpace(_stateDirectory))
        {
            var packed = await Task.Run(
                () => PackedAgent.TryReply(_context, _stateDirectory, text, settings),
                cancellationToken).ConfigureAwait(false);
            var agent = PackedAgent.Read(packed);
            if (agent is not null)
            {
                return agent.Value;
            }
        }

        var direct = await AskPublicAsync(text, cancellationToken).ConfigureAwait(false);
        if (direct is not null)
        {
            return direct.Value;
        }

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

    private async Task<bool> PingPublicAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = PublicRequest(HttpMethod.Get, LinkDefaults.ModelUrl.TrimEnd('/') + "/models");
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<PersonaDialogueReply?> AskPublicAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var relationship = string.IsNullOrWhiteSpace(_stateDirectory) ? "" : PackedAgent.Relationship(_stateDirectory);
            var history = string.IsNullOrWhiteSpace(_stateDirectory)
                ? []
                : PackedAgent.Transcript(_stateDirectory);
            var system = "你是佳怡，停在屏幕边上陪着对方。只用中文，说一两句口语，不要列表，不要承认自己是模型或程序。"
                + "接住前面已经说过的话，不要装成第一次见面。"
                + (relationship.StartsWith("她是对方的", StringComparison.Ordinal)
                    ? "现在的关系是" + relationship + "。顺着这个身份说话，不要推开，也不要解释这是设定。"
                    : "关系听对方的。对方把她当成女朋友时，她就顺着陪。");
            var messages = new List<object> { new Dictionary<string, string> { ["role"] = "system", ["content"] = system } };
            foreach (var turn in history.TakeLast(12))
            {
                messages.Add(new Dictionary<string, string> { ["role"] = turn.Role, ["content"] = turn.Text });
            }

            if (history.Count == 0 || history[^1].Role != "user" || history[^1].Text != text)
            {
                messages.Add(new Dictionary<string, string> { ["role"] = "user", ["content"] = text });
            }

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["model"] = LinkDefaults.ModelName,
                ["messages"] = messages,
                ["max_tokens"] = 180,
                ["temperature"] = 0.7,
                ["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = false }
            });
            using var request = PublicRequest(HttpMethod.Post, LinkDefaults.ModelUrl.TrimEnd('/') + "/chat/completions");
            request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            using var document = await ReadUtf8JsonAsync(response, cancellationToken);
            var root = document.RootElement;
            if (!response.IsSuccessStatusCode || !root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var message = choices[0].GetProperty("message");
            var reply = message.TryGetProperty("content", out var content) ? content.GetString() : "";
            if (string.IsNullOrWhiteSpace(reply) && message.TryGetProperty("reasoning_content", out var reasoning))
            {
                reply = reasoning.GetString();
            }

            reply = (reply ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reply))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(_stateDirectory))
            {
                PackedAgent.AppendExchange(_stateDirectory, text, reply);
            }

            return new PersonaDialogueReply(true, reply, false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static HttpRequestMessage PublicRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("x-api-key", LinkDefaults.ApiKey);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + LinkDefaults.ApiKey);
        return request;
    }

    public async Task<bool> RememberAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (_context is not null && !string.IsNullOrWhiteSpace(_stateDirectory))
        {
            var remembered = await Task.Run(
                () => PackedAgent.TryRemember(_context, _stateDirectory, text),
                cancellationToken).ConfigureAwait(false);
            if (remembered)
            {
                return true;
            }
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
