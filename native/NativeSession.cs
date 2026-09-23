using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MoreCars.Native;

internal sealed class NativeSession : IDisposable
{
    internal GameMemory Memory { get; }
    internal string InitialState { get; }
    private readonly SafeProcessHandle handle;
    private readonly Dictionary<string, uint> exports;
    private readonly nint module;
    internal NativeSession(int pid, string executable, string library, string? expectedLibraryHash = null)
    {
        if (Path.GetFileName(library) is not ("MoreCars.FrameProbe.dll" or "MoreCars.GameRuntime.dll")) throw new InvalidDataException("Unknown native library.");
        using var libraryLock = new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (expectedLibraryHash is not null && !Convert.ToHexString(SHA256.HashData(libraryLock)).Equals(expectedLibraryHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The native game runtime failed verification.");
        Memory = new GameMemory(pid, executable);
        try
        {
            InitialState = GameSnapshot.Capture(Memory, Path.GetDirectoryName(Path.GetFullPath(executable))!).State;
            exports = ReadExports(library);
            using var game = Process.GetProcessById(pid);
            if (game.StartTime.ToUniversalTime().Ticks != Memory.ProcessStartedUtcTicks) throw new InvalidDataException("The game session changed before connection.");
            handle = OpenProcess(0x143a, false, pid);
            if (handle.IsInvalid)
            {
                var code = Marshal.GetLastWin32Error(); handle.Dispose();
                throw new Win32Exception(code, "Native game attachment was denied.");
            }
            try
            {
                module = FindModule(game, library);
                if (module == 0)
                {
                    var localLoader = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                    using var own = Process.GetCurrentProcess();
                    var owner = own.Modules.Cast<ProcessModule>().Single(m => localLoader >= m.BaseAddress && localLoader < m.BaseAddress + m.ModuleMemorySize);
                    if (owner.ModuleName.ToLowerInvariant() is not ("kernel32.dll" or "kernelbase.dll"))
                        throw new InvalidDataException("The Windows loader belongs to an unexpected module.");
                    var remoteOwner = game.Modules.Cast<ProcessModule>().Single(m => StringComparer.OrdinalIgnoreCase.Equals(m.FileName, owner.FileName));
                    var loader = remoteOwner.BaseAddress + (localLoader - owner.BaseAddress);
                    var pathBytes = Encoding.Unicode.GetBytes(library + "\0");
                    var address = VirtualAllocEx(handle, 0, (nuint)pathBytes.Length, 0x3000, 4);
                    if (address == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var safeToFree = true;
                    try
                    {
                        if (!WriteProcessMemory(handle, address, pathBytes, (nuint)pathBytes.Length, out var written) || written != (nuint)pathBytes.Length)
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        safeToFree = false;
                        Call(handle, loader, address);
                        safeToFree = true;
                    }
                    finally
                    {
                        if (safeToFree) VirtualFreeEx(handle, address, 0, 0x8000);
                    }
                    module = FindModule(game, library);
                    if (module == 0) throw new InvalidOperationException("The observer library did not load.");
                }
            }
            catch { handle.Dispose(); throw; }
        }
        catch { Memory.Dispose(); throw; }
    }
    internal uint Invoke(string name) => Call(handle, module + checked((int)exports[name]), 0);
    internal bool HasExited => GetExitCodeProcess(handle, out var code) && code != 259;
    internal byte[] Read(string name, int size) => Memory.Read(checked((ulong)module + exports[name]), size);
    internal uint Invoke(string name, byte[] packet)
    {
        if (packet.Length is < 1 or > 4096) throw new InvalidDataException("Invalid native packet size.");
        var address = VirtualAllocEx(handle, 0, (nuint)packet.Length, 0x3000, 4);
        if (address == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var safeToFree = true;
        try
        {
            if (!WriteProcessMemory(handle, address, packet, (nuint)packet.Length, out var written) || written != (nuint)packet.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            safeToFree = false;
            var result = Call(handle, module + checked((int)exports[name]), address);
            safeToFree = true;
            return result;
        }
        finally { if (safeToFree) VirtualFreeEx(handle, address, 0, 0x8000); }
    }
    public void Dispose() { handle.Dispose(); Memory.Dispose(); }
    private static Dictionary<string, uint> ReadExports(string file)
    {
        using var input = File.OpenRead(file);
        using var pe = new PEReader(input);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64) throw new InvalidDataException("The native observer must be x64.");
        byte[] Read(uint rva, int length) => pe.GetSectionData((int)rva).GetContent(0, length).ToArray();
        uint U(uint rva) => BinaryPrimitives.ReadUInt32LittleEndian(Read(rva, 4));
        var directory = pe.PEHeaders.PEHeader!.ExportTableDirectory;
        var count = U((uint)directory.RelativeVirtualAddress + 24);
        if (count is 0 or > 32) throw new InvalidDataException("Invalid native observer exports.");
        var functions = U((uint)directory.RelativeVirtualAddress + 28);
        var names = U((uint)directory.RelativeVirtualAddress + 32);
        var ordinals = U((uint)directory.RelativeVirtualAddress + 36);
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (uint i = 0; i < count; i++)
        {
            var name = Encoding.ASCII.GetString(Read(U(names + i * 4), 128)).Split('\0')[0];
            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(Read(ordinals + i * 2, 2));
            var address = U(functions + (uint)ordinal * 4);
            if (address >= directory.RelativeVirtualAddress && address < directory.RelativeVirtualAddress + directory.Size)
                throw new InvalidDataException("Forwarded native observer exports are not supported.");
            result.Add(name, address);
        }
        if (new[] { "MoreCarsStartProbe", "MoreCarsStopProbe", "MoreCarsProbeState", "MoreCarsFidState", "MoreCarsRequestFidRefresh" }.Any(name => !result.ContainsKey(name)))
            throw new InvalidDataException("The native observer's required exports are missing.");
        return result;
    }

    private static nint FindModule(Process process, string file)
    {
        process.Refresh();
        return process.Modules.Cast<ProcessModule>().SingleOrDefault(m => StringComparer.OrdinalIgnoreCase.Equals(m.FileName, file))?.BaseAddress ?? 0;
    }

    private static uint Call(SafeProcessHandle process, nint procedure, nint argument)
    {
        using var thread = CreateRemoteThread(process, 0, 0, procedure, argument, 0, out _);
        if (thread.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (WaitForSingleObject(thread, 10000) != 0) throw new TimeoutException("Trackmania did not finish the native request. Close the game before retrying.");
        if (!GetExitCodeThread(thread, out var result)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint type, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeWaitHandle CreateRemoteThread(SafeProcessHandle process, nint attributes, nuint stackSize, nint procedure, nint argument, uint flags, out uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeThread(SafeWaitHandle thread, out uint code);
}
