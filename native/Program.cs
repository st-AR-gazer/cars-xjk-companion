using System.Text.Json;

namespace MoreCars.Native;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--self-test"]) { ProbeTests.Run(); NativeSkinStageTests.RunAsync().GetAwaiter().GetResult(); return 0; }
            if (args is ["--emit-cpp-profile"]) { EmitCppProfile(); return 0; }
            if (args is ["--recover-skin-commit", "--game", var recoveryGame])
            {
                Console.WriteLine(JsonSerializer.Serialize(SkinCommitProbe.RecoverAsync(Path.GetFullPath(recoveryGame)).GetAwaiter().GetResult(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                return 0;
            }
            if ((args.Length != 4 && !(args.Length == 5 && args[4] is "--observe-game-thread" or "--refresh-game-fids" or "--test-skin-commit")) || args[0] != "--pid" || args[2] != "--game" || !int.TryParse(args[1], out var pid) || pid <= 0)
                throw new ArgumentException("Usage: MoreCarsNativeProbe --pid <Trackmania PID> --game <full path to Trackmania.exe>");
            var executable = Path.GetFullPath(args[3]);
            if (args.Length == 5 && args[4] == "--test-skin-commit")
            {
                Console.WriteLine(JsonSerializer.Serialize(SkinCommitProbe.RunAsync(pid, executable).GetAwaiter().GetResult(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
                return 0;
            }
            if (args.Length == 5)
            {
                var result = JsonSerializer.SerializeToElement(NativeObserver.Run(pid, executable, args[4] == "--refresh-game-fids"), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
                Console.WriteLine(result.GetRawText());
                return result.GetProperty("callbackVerified").GetBoolean() && result.GetProperty("originalCallbackRestored").GetBoolean() && result.GetProperty("stopSucceeded").GetBoolean() &&
                    (args[4] != "--refresh-game-fids" || result.GetProperty("fidRefreshPerformed").GetBoolean()) ? 0 : 1;
            }
            using var memory = new GameMemory(pid, executable);
            var snapshot = GameSnapshot.Capture(memory, Path.GetDirectoryName(executable)!);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "morecars.native-probe.v1",
                observedAt = DateTimeOffset.UtcNow,
                processId = memory.ProcessId,
                processStartedUtcTicks = memory.ProcessStartedUtcTicks,
                gameVersion = GameProfile.Version,
                executableSha256 = GameProfile.Sha256,
                snapshot,
                gameThreadDispatcherValidated = false,
                note = "Read-only diagnostic. No injection, FID calls, game writes, or live-refresh capability."
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "morecars.native-probe.v1",
                refreshEnabled = false,
                error = error.Message,
                nativeErrorCode = error is System.ComponentModel.Win32Exception native ? (int?)native.NativeErrorCode : null
            }));
            return 1;
        }
    }

    private static void EmitCppProfile()
    {
        Console.WriteLine("#pragma once\nnamespace Profile {");
        foreach (var field in typeof(GameProfile).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
        {
            if (field.IsLiteral && field.GetRawConstantValue() is int value) Console.WriteLine($"constexpr std::uintptr_t {field.Name} = 0x{value:x};");
            if (field.IsLiteral && field.GetRawConstantValue() is uint number) Console.WriteLine($"constexpr std::uint32_t {field.Name} = 0x{number:x};");
        }
        Console.WriteLine("constexpr unsigned char Sha256[] = {" + string.Join(",", Convert.FromHexString(GameProfile.Sha256).Select(b => $"0x{b:x2}")) + "};\n}");
        Console.WriteLine("namespace Profile { constexpr unsigned char UpdateTreePrefix[] = {" + string.Join(",", GameProfile.UpdateTreePrefix.ToArray().Select(b => $"0x{b:x2}")) + "}; }");
        Console.WriteLine("namespace Profile { constexpr const wchar_t* SkinPaths[] = {" + string.Join(",", NativeSkinPaths.Paths.Select(path => "L\"" + path.Replace("/", "\\\\") + "\"")) + "}; }");
    }
}
