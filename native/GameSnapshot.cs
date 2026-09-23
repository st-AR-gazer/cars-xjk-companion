using System.Buffers.Binary;
using System.Text;

namespace MoreCars.Native;

internal sealed record GameSnapshot(string State, bool EditorPresent, bool PlaygroundPresent, bool RootMapPresent,
    int GameDataFolders, int VehicleFolders, string RefreshFunctionRva, bool RefreshEnabled = false)
{
    internal static GameSnapshot Capture(IGameMemory memory, string expectedRoot)
    {
        ValidateMember(memory, 0x1ed0c58, 0x030d300a, GameProfile.Editor);
        ValidateMember(memory, 0x1ed0ca0, 0x030d300b, GameProfile.Playground);
        ValidateMember(memory, 0x1ed0ae8, 0x030d3005, GameProfile.RootMap);
        if (!memory.Read(memory.At(GameProfile.UpdateTree), GameProfile.UpdateTreePrefix.Length).AsSpan().SequenceEqual(GameProfile.UpdateTreePrefix))
            throw new InvalidDataException("The native FID refresh code differs from the verified profile.");
        var app = memory.Pointer(memory.At(GameProfile.AppSingleton));
        if (memory.Pointer(app) != memory.At(GameProfile.AppVtable)) throw new InvalidDataException("The native game app has an unsupported type.");
        var before = ReadState(memory, app);
        var root = memory.Pointer(memory.Pointer(memory.At(GameProfile.FileSystemSingleton)) + GameProfile.GameFolder);
        ValidateFolder(memory, root, true);
        if (!StringComparer.OrdinalIgnoreCase.Equals(FolderName(memory, root).TrimEnd('\\', '/'), expectedRoot.TrimEnd('\\', '/')))
            throw new InvalidDataException("The game-owned FID root does not match the selected installation.");
        var gameData = FindFolder(memory, root, "GameData");
        var vehicles = FindFolder(memory, gameData, "Vehicles");
        var after = ReadState(memory, app);
        if (app != memory.Pointer(memory.At(GameProfile.AppSingleton)) || before != after)
            throw new InvalidOperationException("Game state changed during the read. Take a fresh snapshot.");
        return new(GameProfile.Classify(after.Editor != 0, after.Playground != 0, after.Map != 0),
            after.Editor != 0, after.Playground != 0, after.Map != 0,
            FolderCount(memory, gameData), FolderCount(memory, vehicles), $"0x{GameProfile.UpdateTree:x}");
    }

    private static (ulong Editor, ulong Playground, ulong Map) ReadState(IGameMemory memory, ulong app) =>
        (memory.Pointer(app + GameProfile.Editor), memory.Pointer(app + GameProfile.Playground), memory.Pointer(app + GameProfile.RootMap));

    private static void ValidateMember(IGameMemory memory, int rva, uint id, int offset)
    {
        var data = memory.Read(memory.At(rva), 24);
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 31 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != id ||
            BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(16)) != (uint)offset)
            throw new InvalidDataException("Native game reflection no longer matches the field profile.");
    }

    private static void ValidateFolder(IGameMemory memory, ulong folder, bool root = false)
    {
        var vtable = memory.Pointer(folder);
        if (vtable != memory.At(root ? GameProfile.RootFolderVtable : GameProfile.FolderVtable) ||
            memory.Pointer(vtable + GameProfile.UpdateTreeSlot) != memory.At(GameProfile.UpdateTree))
            throw new InvalidDataException("The game folder has an unsupported native FID implementation.");
    }

    private static int FolderCount(IGameMemory memory, ulong folder)
    {
        var count = memory.UInt32(folder + GameProfile.FolderTreeCount);
        if (count > 2048) throw new InvalidDataException("The folder list exceeds its inspection bound.");
        return (int)count;
    }

    private static ulong FindFolder(IGameMemory memory, ulong parent, string name)
    {
        var count = FolderCount(memory, parent);
        var entries = memory.Pointer(parent + GameProfile.FolderTrees);
        ulong found = 0;
        for (var index = 0; index < count; index++)
        {
            var folder = memory.Pointer(entries + (uint)(index * 8));
            ValidateFolder(memory, folder);
            if (!StringComparer.OrdinalIgnoreCase.Equals(FolderName(memory, folder), name)) continue;
            if (found != 0) throw new InvalidDataException("The game folder name is ambiguous.");
            found = folder;
        }
        return found != 0 ? found : throw new InvalidDataException($"The native {name} folder is unavailable.");
    }

    private static string FolderName(IGameMemory memory, ulong folder)
    {
        var value = memory.Read(folder + GameProfile.FolderName, 16);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(12));
        if (length > 512 || value[11] > 1 || (value[11] == 0 && length > 11))
            throw new InvalidDataException("The native folder name has an unsupported string layout.");
        if (length == 0) return "";
        var bytes = value[11] == 1 ? memory.Read(BinaryPrimitives.ReadUInt64LittleEndian(value), (int)length) : value[..(int)length];
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
