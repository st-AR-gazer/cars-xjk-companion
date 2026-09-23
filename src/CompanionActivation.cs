using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MoreCars.Companion;

internal static partial class CompanionActivation
{
    public static string PairingFromFileName(string path)
    {
        var match = DownloadName().Match(Path.GetFileName(path));
        return match.Success ? match.Groups[1].Value : "";
    }

    public static bool IsPairingCode(string code) => PairCode().IsMatch(code);
    public static string PipeName => "MoreCars-" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(CompanionStorage.AppDirectory).ToUpperInvariant())))[..24];

    public static async Task<bool> SendAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(message + "\n"), timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^MoreCarsCompanion-pair_([A-Za-z0-9_-]{32,128})(?: \(\d+\))?(?:\.exe)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DownloadName();

    [GeneratedRegex("^[A-Za-z0-9_-]{32,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex PairCode();
}
