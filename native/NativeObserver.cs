using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MoreCars.Native;

internal static class NativeObserver
{
    internal static object Run(int pid, string executable, bool refreshFids = false)
    {
        using var memory = new GameMemory(pid, executable);
        var initial = GameSnapshot.Capture(memory, Path.GetDirectoryName(Path.GetFullPath(executable))!);
        if (initial.EditorPresent || initial.PlaygroundPresent)
            throw new InvalidOperationException("The frame observer starts only from the main menu.");
        var library = Path.Combine(AppContext.BaseDirectory, "MoreCars.FrameProbe.dll");
        using var session = new NativeSession(pid, executable, library);
        var start = session.Invoke("MoreCarsStartProbe");
        if (start != 0) throw new InvalidOperationException($"The native observer rejected attachment ({start}).");
        FrameState first, last;
        FidSnapshot fidsBefore, fidsAfter;
        uint stopped;
        try
        {
            // Every read after successful attachment belongs inside cleanup.
            fidsBefore = ReadFidState(session.Read("MoreCarsFidState", 24));
            fidsAfter = fidsBefore;
            Thread.Sleep(500);
            first = ReadState(session.Read("MoreCarsProbeState", 48));
            Thread.Sleep(2000);
            last = ReadState(session.Read("MoreCarsProbeState", 48));
            if (refreshFids) {
                if (last.Frames <= first.Frames || last.ThreadId == 0 || last.MixedThreads != 0 || last.Fault != 0)
                    throw new InvalidOperationException("The native frame callback was not verified; FID refresh was not requested.");
                var requested = session.Invoke("MoreCarsRequestFidRefresh");
                if (requested != 0) throw new InvalidOperationException($"Native refresh request rejected ({requested}).");
                for (var attempt = 0; attempt < 50; attempt++) {
                    Thread.Sleep(100);
                    fidsAfter = ReadFidState(session.Read("MoreCarsFidState", 24));
                    if (fidsAfter.Completed > fidsBefore.Completed || fidsAfter.Outcome == 3) break;
                }
            }
        }
        finally { stopped = session.Invoke("MoreCarsStopProbe"); }
        var final = ReadState(session.Read("MoreCarsProbeState", 48));
        var app = memory.Pointer(memory.At(GameProfile.AppSingleton));
        var command = memory.Pointer(memory.Pointer(app + GameProfile.AppCommands) + GameProfile.AfterLoopCommand);
        var restored = memory.Pointer(command + GameProfile.CommandCallback) == memory.At(GameProfile.AfterMainLoop);
        var verified = last.Frames > first.Frames && last.Frames >= 20 && last.ThreadId != 0 && last.MixedThreads == 0 && last.Fault == 0 &&
            final.Frames >= last.Frames && final.ThreadId == last.ThreadId && final.MixedThreads == 0 && final.Fault == 0;
        return new {
            schema = "morecars.native-frame-observation.v1", observedAt = DateTimeOffset.UtcNow,
            processId = pid, processStartedUtcTicks = memory.ProcessStartedUtcTicks,
            gameVersion = GameProfile.Version, executableSha256 = GameProfile.Sha256,
            nativeLibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))).ToLowerInvariant(),
            first, last, final, callbackVerified = verified, originalCallbackRestored = restored,
            stopSucceeded = stopped == 0 && final.Stopped == 1 && final.Running == 0,
            fidRefreshRequested = refreshFids, fidsBefore, fidsAfter,
            fidRefreshPerformed = refreshFids && verified && fidsAfter.Completed > fidsBefore.Completed && fidsAfter.Outcome == 1 && fidsAfter.ThreadId == last.ThreadId && fidsAfter.Fault == 0,
            note = "No skin archives were replaced. Check the restoration and stop results above; the diagnostic DLL remains mapped until game exit."
        };
    }

    private sealed record FrameState(uint Schema, uint Running, ulong Frames, uint ThreadId, uint MixedThreads,
        uint Editor, uint Playground, uint Map, uint Fault, uint Stopped);
    private static FrameState ReadState(byte[] data)
    {
        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        if (U(0) != 1) throw new InvalidDataException("Unsupported native observer state.");
        return new(U(0), U(4), BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8)), U(16), U(20), U(24), U(28), U(32), U(36), U(40));
    }

    private sealed record FidSnapshot(uint Pending, uint Outcome, ulong Completed, uint ThreadId, uint Fault);
    private static FidSnapshot ReadFidState(byte[] data) {
        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        return new(U(0), U(4), BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8)), U(16), U(20));
    }

}
