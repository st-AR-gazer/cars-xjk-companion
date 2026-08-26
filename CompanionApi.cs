using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MoreCars.Companion;

internal sealed class CompanionApi(CompanionSettings settings) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = CreateClient(settings);

    private static HttpClient CreateClient(CompanionSettings settings)
    {
        var origin = new Uri(settings.ApiOrigin, UriKind.Absolute);
        if (origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback))
            throw new InvalidOperationException("The companion API origin must use HTTPS.");
        var client = new HttpClient { BaseAddress = origin, Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MoreCarsCompanion/0.3.1");
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

    private static async Task RequireSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"The companion service returned HTTP {(int)response.StatusCode}: {body}");
    }

    public void Dispose() => _http.Dispose();
}
