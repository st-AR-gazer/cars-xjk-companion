using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MoreCars.Companion;

internal sealed record CompanionDownload(string Schema, string Version, string PairingCode, string ApiOrigin, string BaseSha256)
{
    internal static readonly byte[] Marker = Encoding.ASCII.GetBytes("MORECARS-SETUP-V1");
    public long ExecutableBytes { get; private init; }

    public static CompanionDownload? Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length < Marker.Length + 4) return null;
        file.Seek(-Marker.Length, SeekOrigin.End);
        var marker = new byte[Marker.Length];
        file.ReadExactly(marker);
        if (!marker.AsSpan().SequenceEqual(Marker)) return null;
        file.Seek(-Marker.Length - 4, SeekOrigin.End);
        Span<byte> lengthBytes = stackalloc byte[4];
        file.ReadExactly(lengthBytes);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        var executableBytes = file.Length - Marker.Length - 4 - length;
        if (length is < 1 or > 4096 || executableBytes < 1)
            throw new InvalidDataException("The setup download is incomplete. Download it again from the website.");
        file.Position = executableBytes;
        var json = new byte[length];
        file.ReadExactly(json);
        var download = JsonSerializer.Deserialize<CompanionDownload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The setup download is invalid.");
        if (download.Schema != "morecars.download.v1" || download.Version != CompanionBuild.Version ||
            !CompanionActivation.IsPairingCode(download.PairingCode) ||
            !System.Text.RegularExpressions.Regex.IsMatch(download.BaseSha256, "^[a-f0-9]{64}$"))
            throw new InvalidDataException("The setup download does not match this companion version.");
        _ = NormalizeOrigin(download.ApiOrigin);
        file.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var remaining = executableBytes;
        while (remaining > 0)
        {
            var count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0) throw new EndOfStreamException("The setup download is incomplete.");
            hash.AppendData(buffer, 0, count);
            remaining -= count;
        }
        if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != download.BaseSha256)
            throw new InvalidDataException("The downloaded companion failed verification. Download it again.");
        return download with { ExecutableBytes = executableBytes };
    }

    internal static string NormalizeOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("The setup website is invalid.");
        if (uri.Scheme == "https" && uri.Host == "cars.xjk.yt" && uri.IsDefaultPort)
            return "https://cars.xjk.yt";
        if (uri.Scheme == "http" && (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
            return new UriBuilder("http", "127.0.0.1", uri.Port).Uri.GetLeftPart(UriPartial.Authority);
        throw new InvalidDataException("Setup must come from the More Cars website or a local development server.");
    }
}
