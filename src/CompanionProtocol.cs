using System.Net;
using System.Text.Json;

namespace MoreCars.Companion;

internal static class CompanionProtocol
{
    private static readonly string[] Supported = new CompanionRuntime().Capabilities;
    internal static string[] LegacyCapabilities => Supported.Intersect(new[] {
        "install-release", "cleanup-managed", "apply-skin", "restore-skin", "inspect-installation",
        "install-car", "remove-car", "launch-game", "choose-game", "confirm-game",
        "uninstall-companion", "uninstall-companion-and-cars"
    }).ToArray();
    internal static string[] Parse(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.NotFound) return LegacyCapabilities;
        if (status != HttpStatusCode.OK) throw new HttpRequestException("The companion service could not report its supported controls.");
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "morecars.companion-protocol.v1")
                throw new InvalidDataException("The companion service protocol is unsupported.");
            var capabilities = root.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()).ToArray();
            return Supported.Where(value => capabilities.Contains(value)).ToArray();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidDataException("The companion service protocol is unreadable.", error); }
    }
}
