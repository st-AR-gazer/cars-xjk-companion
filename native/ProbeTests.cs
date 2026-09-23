using System.Buffers.Binary;
using System.Text;

namespace MoreCars.Native;

internal static class ProbeTests
{
    private const string Root = "C:\\Games\\Trackmania";
    private const ulong App = 0x20000000, FileSystem = 0x21000000, RootFolder = 0x22000000,
        DataFolder = 0x23000000, VehiclesFolder = 0x24000000, RootEntries = 0x25000000, DataEntries = 0x26000000;

    internal static int Run()
    {
        var tests = new (string Name, Action Check)[] {
            ("editor and editor test modes remain blocked", () => {
                var m = Fixture(); m.Pointer(App + GameProfile.Editor, 123);
                Check(GameSnapshot.Capture(m, Root).State == "editor");
                m.Pointer(App + GameProfile.Playground, 456);
                Check(GameSnapshot.Capture(m, Root).State == "editor");
            }),
            ("map state is distinct from menu observation", () => {
                var m = Fixture(); m.Pointer(App + GameProfile.Playground, 456);
                Check(GameSnapshot.Capture(m, Root).State == "map");
                m.Pointer(App + GameProfile.Playground, 0);
                Check(GameSnapshot.Capture(m, Root).State == "menu-with-retained-map");
                m.Pointer(App + GameProfile.RootMap, 0);
                var snapshot = GameSnapshot.Capture(m, Root);
                Check(snapshot.State == "menu" && !snapshot.RefreshEnabled);
            }),
            ("mismatched reflection offsets are rejected", () => {
                var m = Fixture(); m.Pointer(m.Base + 0x1ed0c58 + 16, 0x888); Reject(() => GameSnapshot.Capture(m, Root));
            }),
            ("changed native refresh code is rejected", () => {
                var m = Fixture(); m.Write(m.Base + GameProfile.UpdateTree, [0xe9]); Reject(() => GameSnapshot.Capture(m, Root));
            }),
            ("foreign virtual functions are rejected", () => {
                var m = Fixture(); m.Pointer(m.Base + GameProfile.FolderVtable + GameProfile.UpdateTreeSlot, 0x55550000); Reject(() => GameSnapshot.Capture(m, Root));
            }),
            ("wrong game-owned folder roots are rejected", () => Reject(() => GameSnapshot.Capture(Fixture(), "C:\\OtherGame"))),
            ("oversized folder arrays are rejected", () => {
                var m = Fixture(); m.UInt32(DataFolder + GameProfile.FolderTreeCount, 2049); Reject(() => GameSnapshot.Capture(m, Root));
            }),
            ("ambiguous folder matches are rejected", () => {
                var m = Fixture(); m.UInt32(RootFolder + GameProfile.FolderTreeCount, 2); m.Pointer(RootEntries + 8, DataFolder); Reject(() => GameSnapshot.Capture(m, Root));
            }),
            ("corrupted native string lengths are rejected", () => {
                var m = Fixture(); m.UInt32(DataFolder + GameProfile.FolderName + 12, 513); Reject(() => GameSnapshot.Capture(m, Root));
            }),
            ("a state change during traversal invalidates the snapshot", () => {
                var m = Fixture(); var reads = 0;
                m.BeforeRead = (address) => { if (address == App + GameProfile.Editor && ++reads == 2) m.Pointer(address, 789); };
                Reject(() => GameSnapshot.Capture(m, Root));
            }),
        };
        foreach (var test in tests) { test.Check(); Console.WriteLine($"PASS {test.Name}"); }
        Console.WriteLine($"{tests.Length} native probe checks passed.");
        return 0;
    }

    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Native probe self-test failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { return; }
        throw new Exception("Invalid native state was accepted.");
    }

    private static FakeMemory Fixture()
    {
        var m = new FakeMemory();
        foreach (var (rva, id, offset) in new[] { (0x1ed0c58, 0x030d300a, GameProfile.Editor), (0x1ed0ca0, 0x030d300b, GameProfile.Playground), (0x1ed0ae8, 0x030d3005, GameProfile.RootMap) })
        {
            m.Write(m.Base + (uint)rva, new byte[24]); m.UInt32(m.Base + (uint)rva, 31);
            m.UInt32(m.Base + (uint)rva + 4, (uint)id); m.Pointer(m.Base + (uint)rva + 16, (uint)offset);
        }
        m.Write(m.Base + GameProfile.UpdateTree, GameProfile.UpdateTreePrefix.ToArray());
        m.Pointer(m.Base + GameProfile.AppSingleton, App); m.Pointer(App, m.Base + GameProfile.AppVtable);
        m.Pointer(App + GameProfile.Editor, 0); m.Pointer(App + GameProfile.Playground, 0); m.Pointer(App + GameProfile.RootMap, 321);
        m.Pointer(m.Base + GameProfile.FileSystemSingleton, FileSystem); m.Pointer(FileSystem + GameProfile.GameFolder, RootFolder);
        foreach (var folder in new[] { RootFolder, DataFolder, VehiclesFolder })
        {
            var vtable = folder == RootFolder ? GameProfile.RootFolderVtable : GameProfile.FolderVtable;
            m.Pointer(folder, m.Base + (uint)vtable); m.Pointer(m.Base + (uint)vtable + GameProfile.UpdateTreeSlot, m.Base + GameProfile.UpdateTree);
        }
        m.Name(RootFolder, Root); m.Name(DataFolder, "GameData"); m.Name(VehiclesFolder, "Vehicles");
        m.UInt32(RootFolder + GameProfile.FolderTreeCount, 1); m.Pointer(RootFolder + GameProfile.FolderTrees, RootEntries); m.Pointer(RootEntries, DataFolder);
        m.UInt32(DataFolder + GameProfile.FolderTreeCount, 1); m.Pointer(DataFolder + GameProfile.FolderTrees, DataEntries); m.Pointer(DataEntries, VehiclesFolder);
        m.UInt32(VehiclesFolder + GameProfile.FolderTreeCount, 0);
        return m;
    }

    private sealed class FakeMemory : IGameMemory
    {
        private readonly Dictionary<ulong, byte> _bytes = [];
        public ulong Base => 0x140000000;
        internal Action<ulong>? BeforeRead;
        public byte[] Read(ulong address, int size)
        {
            BeforeRead?.Invoke(address);
            return Enumerable.Range(0, size).Select(i => _bytes.TryGetValue(address + (uint)i, out var value) ? value : throw new InvalidDataException("Unexpected native read.")).ToArray();
        }
        internal void Write(ulong address, byte[] data) { for (var i = 0; i < data.Length; i++) _bytes[address + (uint)i] = data[i]; }
        internal void Pointer(ulong address, ulong value) { var data = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(data, value); Write(address, data); }
        internal void UInt32(ulong address, uint value) { var data = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value); Write(address, data); }
        internal void Name(ulong folder, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name); var value = new byte[16];
            if (bytes.Length <= 11) bytes.CopyTo(value, 0);
            else { BinaryPrimitives.WriteUInt64LittleEndian(value, folder + 0x1000); value[11] = 1; Write(folder + 0x1000, bytes); }
            BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(12), (uint)bytes.Length); Write(folder + GameProfile.FolderName, value);
        }
    }
}
