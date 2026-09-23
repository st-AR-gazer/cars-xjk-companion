using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MoreCars.Native;

internal static class NativeSkinStageTests
{
    internal static async Task RunAsync()
    {
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts", "stage-tests", Guid.NewGuid().ToString("N")));
        var checks = 0;
        async Task<(string Root, NativeCommitManifest Manifest)> Fixture()
        {
            var root = Path.Combine(parent, (++checks).ToString());
            var target = NativeSkinPaths.Resolve(root, 0); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var ledger = NativeSkinPaths.Resolve(root, 11); Directory.CreateDirectory(Path.GetDirectoryName(ledger)!);
            await File.WriteAllTextAsync(target, "old-car"); await File.WriteAllTextAsync(target + ".morecars-skin.partial", "new-car");
            await File.WriteAllTextAsync(ledger, "{\"revision\":1}");
            var manifest = await NativeSkinStage.PrepareAsync(root, "fixture", DateTimeOffset.UtcNow.AddMinutes(2), 123, 456,
                [new("bay", target + ".morecars-skin.partial", Hash("old-car"), 7, Hash("new-car"), 7)], "{\"revision\":1}", "{\"revision\":2}", CancellationToken.None);
            return (root, manifest);
        }
        async Task Move(string root, NativeCommitManifest manifest, int key)
        {
            var directory = NativeSkinStage.DirectoryFor(root, manifest.Id); var target = NativeSkinPaths.Resolve(root, key);
            File.Move(target, Path.Combine(directory, key + ".before")); File.Move(Path.Combine(directory, key + ".next"), target);
            await Task.CompletedTask;
        }
        {
            var (root, m) = await Fixture(); var bytes = SkinCommitProtocol.Encode(m, 789, 123456);
            Check(bytes.Length == 1104 && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(36)) == 2, "packet bounds");
            Check(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(48 + 88)) == 11, "ledger last");
            await NativeSkinStage.RecoverAsync(root, false, CancellationToken.None);
            Check(!File.Exists(NativeSkinStage.PendingPath(root)) && await File.ReadAllTextAsync(NativeSkinPaths.Resolve(root, 0)) == "old-car", "unused stage recovery");
        }
        {
            var (root, _) = await Fixture(); await Reject(() => NativeSkinStage.RecoverAsync(root, true, CancellationToken.None));
            Check(File.Exists(NativeSkinStage.PendingPath(root)), "running game keeps journal");
        }
        {
            var (root, m) = await Fixture(); await Move(root, m, 0);
            await NativeSkinStage.RecoverAsync(root, false, CancellationToken.None);
            Check(await File.ReadAllTextAsync(NativeSkinPaths.Resolve(root, 0)) == "old-car", "partial commit restores car");
            Check(await File.ReadAllTextAsync(NativeSkinPaths.Resolve(root, 11)) == "{\"revision\":1}", "partial commit retains ledger");
        }
        {
            var (root, m) = await Fixture(); await Move(root, m, 0); await Move(root, m, 11);
            await NativeSkinStage.RecoverAsync(root, false, CancellationToken.None);
            Check(await File.ReadAllTextAsync(NativeSkinPaths.Resolve(root, 0)) == "new-car" && !File.Exists(NativeSkinStage.PendingPath(root)), "completed commit retained");
        }
        {
            var (root, m) = await Fixture(); await Move(root, m, 0);
            await File.WriteAllTextAsync(NativeSkinPaths.Resolve(root, 0), "user-change");
            await Reject(() => NativeSkinStage.RecoverAsync(root, false, CancellationToken.None));
            Check(await File.ReadAllTextAsync(NativeSkinPaths.Resolve(root, 0)) == "user-change", "unknown car preserved");
        }
        {
            var (root, m) = await Fixture(); await Move(root, m, 0); await Move(root, m, 11);
            var backup = Path.Combine(NativeSkinStage.DirectoryFor(root, m.Id), "0.before"); await File.WriteAllTextAsync(backup, "user-backup");
            await Reject(() => NativeSkinStage.RecoverAsync(root, false, CancellationToken.None));
            Check(await File.ReadAllTextAsync(backup) == "user-backup", "unknown backup preserved");
        }
        {
            var (root, m) = await Fixture();
            await Reject(() => { SkinCommitProtocol.Validate(m with { Files = [m.Files[0], m.Files[0], m.Files[1]] }); return Task.CompletedTask; });
            await Reject(() => { NativeSkinStage.DirectoryFor(root, "../escape"); return Task.CompletedTask; });
        }
        Console.WriteLine($"{checks} native staging/recovery scenarios passed.");
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private static async Task Reject(Func<Task> operation)
    {
        try { await operation(); } catch (Exception error) when (error is IOException or InvalidDataException) { return; }
        throw new InvalidOperationException("Unsafe native recovery was accepted.");
    }
}
