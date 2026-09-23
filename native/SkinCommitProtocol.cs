using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MoreCars.Native;

internal static class NativeSkinPaths
{
    internal const int LedgerKey = 11;
    internal static readonly string[] CarIds = ["bay", "canyon", "coast", "desert", "island", "lagoon", "rally", "snow", "stadium", "traffic", "valley"];
    internal static readonly string[] Paths = [
        "GameData/Vehicles/Skins/BayCar.zip", "GameData/Vehicles/Skins/HDModelPainted_CanyonCar.zip",
        "GameData/Vehicles/Skins/CoastCar.zip", "GameData/Vehicles/Skins/DesertCar.zip",
        "GameData/Vehicles/Skins/IslandCar.zip", "GameData/Vehicles/Skins/HDModelPainted_LagoonCar.zip",
        "GameData/Vehicles/Skins/RallyCar.zip", "GameData/Vehicles/Skins/SnowCar.zip",
        "GameData/Vehicles/Skins/StadiumCar.zip", "GameData/Vehicles/Skins/TrafficCar.zip",
        "GameData/Vehicles/Skins/ValleyCar.zip", ".morecars/ownership-v1.json"
    ];

    internal static string Resolve(string root, int key)
    {
        if (key is < 0 or > LedgerKey) throw new InvalidDataException("Unknown native skin target.");
        return Path.Combine(Path.GetFullPath(root), Paths[key].Replace('/', Path.DirectorySeparatorChar));
    }
}

internal sealed record NativeCommitFile(int Key, string BeforeHash, long BeforeSize, string AfterHash, long AfterSize);
internal sealed record NativeCommitManifest(string Schema, string Id, string CommandId, DateTimeOffset ExpiresAt,
    int GameProcessId, long GameProcessStartedUtcTicks, List<NativeCommitFile> Files);

internal static class SkinCommitProtocol
{
    internal const int HeaderSize = 48, EntrySize = 88, MaximumFiles = 12, PacketSize = HeaderSize + MaximumFiles * EntrySize;
    internal const string Schema = "morecars.native-skin-commit.v1";

    internal static byte[] Encode(NativeCommitManifest manifest, int ownerPid, long ownerStartedFileTime)
    {
        Validate(manifest);
        if (ownerPid <= 0 || ownerStartedFileTime <= 0) throw new InvalidDataException("Invalid native transaction owner.");
        var bytes = new byte[PacketSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), PacketSize);
        Convert.FromHexString(manifest.Id).CopyTo(bytes, 8);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), manifest.ExpiresAt.UtcDateTime.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), ownerPid);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(36), manifest.Files.Count);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(40), ownerStartedFileTime);
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            var file = manifest.Files[i];
            var entry = bytes.AsSpan(HeaderSize + i * EntrySize, EntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(entry, file.Key);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], file.BeforeSize == 0 ? 1 : 0);
            BinaryPrimitives.WriteInt64LittleEndian(entry[8..], file.BeforeSize);
            BinaryPrimitives.WriteInt64LittleEndian(entry[16..], file.AfterSize);
            if (file.BeforeSize > 0) Convert.FromHexString(file.BeforeHash).CopyTo(entry[24..56]);
            Convert.FromHexString(file.AfterHash).CopyTo(entry[56..88]);
        }
        return bytes;
    }

    internal static void Validate(NativeCommitManifest manifest)
    {
        static bool Hex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if (manifest.Schema != Schema || !Hex(manifest.Id, 32) || manifest.Id.All(c => c == '0') || manifest.Files.Count is < 2 or > MaximumFiles ||
            manifest.Files[^1].Key != NativeSkinPaths.LedgerKey || manifest.GameProcessId <= 0 || manifest.GameProcessStartedUtcTicks <= 0)
            throw new InvalidDataException("Invalid native skin transaction.");
        var previousKey = -1;
        foreach (var file in manifest.Files)
        {
            var maximum = file.Key == NativeSkinPaths.LedgerKey ? 1024 * 1024 : 512L * 1024 * 1024;
            if (file.Key <= previousKey || file.Key > NativeSkinPaths.LedgerKey || file.BeforeSize < 0 || file.BeforeSize > maximum ||
                file.AfterSize <= 0 || file.AfterSize > maximum || !Hex(file.AfterHash, 64) ||
                (file.BeforeSize == 0 ? file.BeforeHash != "" || file.Key == NativeSkinPaths.LedgerKey : !Hex(file.BeforeHash, 64)))
                throw new InvalidDataException("Invalid native skin transaction file.");
            previousKey = file.Key;
        }
    }

    internal static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
