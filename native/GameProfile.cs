namespace MoreCars.Native;

// Verified against Trackmania's own image and live reflection metadata.
// These are game RVAs, never Openplanet addresses. Unknown builds are rejected.
internal static class GameProfile
{
    internal const string Version = "2026.2.2.1751";
    internal const string Sha256 = "3fc7d8cda542beda131c44306b123f4004d07d7e22f512b46b762afc29f6edda";
    internal const uint Timestamp = 1770051079;
    internal const int ImageSize = 0x2cba000;
    internal const int AppSingleton = 0x20b7b60;
    internal const int AppVtable = 0x1cf7cb8;
    internal const int RootMap = 0x360;
    internal const int Editor = 0x7d8;
    internal const int Playground = 0x3d8;
    internal const int FileSystemSingleton = 0x1fbbee0;
    internal const int GameFolder = 0x60;
    internal const int FolderTrees = 0x38;
    internal const int FolderTreeCount = 0x40;
    internal const int FolderName = 0x58;
    internal const int RootFolderVtable = 0x1c09010;
    internal const int FolderVtable = 0x1c08db8;
    internal const int UpdateTreeSlot = 0xf8;
    internal const int UpdateTree = 0x92b600;
    internal const int AppCommands = 0x848;
    internal const int AfterLoopCommand = 0x10;
    internal const int CommandVtable = 0x1b729c0;
    internal const int CommandCallback = 0x28;
    internal const int CommandArgument = 0x30;
    internal const int AfterMainLoop = 0xb4b010;
    internal static ReadOnlySpan<byte> UpdateTreePrefix => [0x48, 0x89, 0x5c, 0x24, 0x18, 0x57, 0x48, 0x83, 0xec, 0x40];

    internal static string Classify(bool editor, bool playground, bool rootMap) =>
        editor ? "editor" : playground ? "map" : rootMap ? "menu-with-retained-map" : "menu";
}
