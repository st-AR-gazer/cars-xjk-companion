using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MoreCars.Native;

internal interface IGameMemory
{
    ulong Base { get; }
    byte[] Read(ulong address, int size);
}

internal static class GameMemoryValues
{
    internal static ulong Pointer(this IGameMemory memory, ulong address) => BinaryPrimitives.ReadUInt64LittleEndian(memory.Read(address, 8));
    internal static uint UInt32(this IGameMemory memory, ulong address) => BinaryPrimitives.ReadUInt32LittleEndian(memory.Read(address, 4));
    internal static ulong At(this IGameMemory memory, int rva) => checked(memory.Base + (uint)rva);
}

internal sealed class GameMemory : IGameMemory, IDisposable
{
    private readonly SafeProcessHandle _handle;
    private readonly Process _process;
    public ulong Base { get; }
    internal int ProcessId => _process.Id;
    internal long ProcessStartedUtcTicks { get; }

    internal GameMemory(int pid, string expectedExecutable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The native game probe requires Windows.");
        expectedExecutable = Path.GetFullPath(expectedExecutable);
        using (var input = File.OpenRead(expectedExecutable))
        {
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(GameProfile.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsupported Trackmania build. No process memory was inspected.");
            input.Position = 0;
            using var pe = new PEReader(input);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || pe.PEHeaders.PEHeader?.SizeOfImage != GameProfile.ImageSize)
                throw new InvalidDataException("The game image does not match its native profile.");
        }
        _process = Process.GetProcessById(pid);
        ProcessStartedUtcTicks = _process.StartTime.ToUniversalTime().Ticks;
        _handle = OpenProcess(0x1010, false, pid);
        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose(); _process.Dispose(); throw new Win32Exception(error);
        }
        try
        {
            var actual = new StringBuilder(32768);
            var length = actual.Capacity;
            if (!QueryFullProcessImageName(_handle, 0, actual, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(actual.ToString()), expectedExecutable))
                throw new InvalidDataException("The process does not belong to the selected Trackmania installation.");
            Base = checked((ulong)(_process.MainModule?.BaseAddress.ToInt64() ?? throw new InvalidOperationException("The game image is unavailable.")));
            var dos = Read(Base, 64);
            if (BinaryPrimitives.ReadUInt16LittleEndian(dos) != 0x5a4d) throw new InvalidDataException("Invalid loaded DOS header.");
            var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos.AsSpan(60));
            if (peOffset is < 64 or > 4096) throw new InvalidDataException("Invalid loaded PE header location.");
            var header = Read(Base + (uint)peOffset, 88);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x4550 ||
                BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)) != 0x8664 ||
                BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8)) != GameProfile.Timestamp ||
                BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(80)) != GameProfile.ImageSize)
                throw new InvalidDataException("The loaded game image differs from the supported file.");
        }
        catch { Dispose(); throw; }
    }

    public byte[] Read(ulong address, int size)
    {
        if (size is < 1 or > 4096 || address < 0x10000 || address > 0x00007fffffffffffUL - (uint)size)
            throw new InvalidDataException("A native read escaped its bounded address range.");
        var data = new byte[size];
        if (!ReadProcessMemory(_handle, (nint)address, data, (nuint)size, out var read) || read != (nuint)size)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The game state became inaccessible; no refresh was attempted.");
        return data;
    }

    public void Dispose() { _handle.Dispose(); _process.Dispose(); }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, [Out] byte[] data, nuint size, out nuint read);
}
