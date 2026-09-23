using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MoreCars.Companion;

internal sealed class CompanionApi(CompanionSettings settings) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The service names why it refused. Turning those codes into an instruction
    // is the difference between a reader who knows what to do next and one who
    // is handed a status line.
    private static readonly Dictionary<string, string> KnownFailures = new(StringComparer.Ordinal)
    {
        ["pairing_invalid"] =
            "This setup link was already used or has expired. Start setup again on the website to get a fresh one.",
        ["device_conflict"] =
            "This computer is paired with a different account. Uninstall the companion, then set it up again.",
        ["device_auth_failed"] =
            "This computer is no longer paired with the website. Start setup again to reconnect it.",
        ["device_not_found"] =
            "The website no longer knows this computer. Start setup again to pair it.",
        ["command_not_found"] =
            "That request is no longer available. Start it again from the website.",
        ["companion_unavailable"] =
            "The website cannot reach its companion service right now. Try again in a moment."
    };

    private readonly HttpClient _http = CreateClient(settings);

    private static HttpClient CreateClient(CompanionSettings settings)
    {
        var origin = new Uri(settings.ApiOrigin, UriKind.Absolute);
        if (origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback))
            throw new InvalidOperationException("The companion API origin must use HTTPS.");
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = origin, Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MoreCarsCompanion/{CompanionBuild.Version}");
        return client;
    }

    public async Task<PairingResult> ClaimPairingAsync(PairingClaim claim, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("/api/v1/companion/pairings/claim", claim, JsonOptions, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PairingResult>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The pairing service returned an empty response.");
    }

    public async Task<CompanionCommand?> GetCommandAsync(string commandId, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, $"/api/v1/companion/commands/{Uri.EscapeDataString(commandId)}");
        using var response = await _http.SendAsync(request, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CommandEnvelope>(JsonOptions, cancellationToken))?.Command;
    }

    public async Task<CompanionCommand?> NextCommandAsync(CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, "/api/v1/companion/commands/next");
        using var response = await _http.SendAsync(request, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CommandEnvelope>(JsonOptions, cancellationToken))?.Command;
    }

    public async Task HeartbeatAsync(CompanionRuntime runtime, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, "/api/v1/companion/heartbeat");
        request.Content = JsonContent.Create(runtime, options: JsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
    }

    public async Task<string[]> RuntimeCapabilitiesAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("/api/v1/companion/protocol", cancellationToken);
        return CompanionProtocol.Parse(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task ReportAsync(
        CompanionCommand command,
        string status,
        int progress,
        string message,
        CancellationToken cancellationToken,
        string archiveSha256 = "",
        long byteSize = 0)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/v1/companion/commands/{Uri.EscapeDataString(command.Id)}/status");
        request.Content = JsonContent.Create(new CommandStatus
        {
            Status = status,
            Progress = progress,
            Message = message,
            ArchiveSha256 = archiveSha256,
            ByteSize = byteSize
        }, options: JsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
    }

    public async Task<byte[]> DownloadAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var target = new Uri(_http.BaseAddress!, relativeUrl);
        if (target.Scheme != _http.BaseAddress!.Scheme || target.Host != _http.BaseAddress.Host || target.Port != _http.BaseAddress.Port)
            throw new InvalidOperationException("The download escaped the companion API origin.");
        return await _http.GetByteArrayAsync(target, cancellationToken);
    }

    public async Task DownloadToFileAsync(string relativeUrl, string destination, CancellationToken cancellationToken)
    {
        var target = new Uri(_http.BaseAddress!, relativeUrl);
        if (target.Scheme != _http.BaseAddress!.Scheme || target.Host != _http.BaseAddress.Host || target.Port != _http.BaseAddress.Port)
            throw new InvalidOperationException("The download escaped the companion API origin.");
        using var response = await _http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await source.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        if (string.IsNullOrWhiteSpace(settings.DeviceToken)) throw new InvalidOperationException("The companion is not paired.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);
        return request;
    }

    private async Task RequireSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(FailureMessage((int)response.StatusCode, body, _http.BaseAddress));
    }

    // A reply that is not the service speaking usually means the request never
    // reached it, so the origin is named: a companion pointed at the wrong
    // website is the one failure it cannot otherwise report.
    internal static string FailureMessage(int status, string body, Uri? origin)
    {
        var (code, message) = ReadServiceError(body);
        if (KnownFailures.TryGetValue(code, out var known)) return known;
        if (message.Length > 0) return message;
        var website = origin is null ? "The website" : origin.GetLeftPart(UriPartial.Authority);
        return status == 404
            ? $"{website} does not offer this companion service. Check that the companion is set up for the right website."
            : $"{website} could not complete the request (HTTP {status}). Try again in a moment.";
    }

    private static (string Code, string Message) ReadServiceError(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 64 * 1024) return ("", "");
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return ("", "");
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object) return ("", "");
            return (ReadString(error, "code"), ReadString(error, "message"));
        }
        catch (JsonException)
        {
            return ("", "");
        }
    }

    private static string ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    public void Dispose() => _http.Dispose();
}
