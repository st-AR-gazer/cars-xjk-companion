using System.Text.Json.Serialization;

namespace MoreCars.Companion;

internal sealed class CompanionSettings
{
    public string LastPairingCodeSha256 { get; set; } = "";
    public string Schema { get; set; } = "morecars.companion-settings.v1";
    public string ApiOrigin { get; set; } = "https://cars.xjk.yt";
    public string DeviceId { get; set; } = $"installation_{Guid.NewGuid():N}";
    public string DeviceToken { get; set; } = "";
    public string TrackmaniaRoot { get; set; } = "";
    public bool GameConfirmed { get; set; }
}

internal sealed class PairingClaim
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "morecars.companion.v1";

    [JsonPropertyName("pairingCode")]
    public required string PairingCode { get; init; }

    [JsonPropertyName("deviceId")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("deviceToken")]
    public string DeviceToken { get; init; } = "";
}

internal sealed class PairingResult
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "";

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; init; } = "";

    [JsonPropertyName("deviceToken")]
    public string DeviceToken { get; init; } = "";
}

internal sealed class CommandEnvelope
{
    [JsonPropertyName("command")]
    public CompanionCommand? Command { get; init; }
}

internal sealed class CompanionCommand
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("carId")]
    public string CarId { get; init; } = "";

    [JsonPropertyName("skinId")]
    public string SkinId { get; init; } = "";

    [JsonPropertyName("expiresAt")]
    public string ExpiresAt { get; init; } = "";

    [JsonPropertyName("gameId")]
    public string GameId { get; init; } = "";
}

internal sealed class CommandStatus
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "morecars.companion-status.v1";

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("progress")]
    public required int Progress { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("archiveSha256")]
    public string ArchiveSha256 { get; init; } = "";

    [JsonPropertyName("byteSize")]
    public long ByteSize { get; init; }
}

internal sealed class ReleaseManifest
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "";

    [JsonPropertyName("releaseId")]
    public string ReleaseId { get; init; } = "";

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; init; } = "";

    [JsonPropertyName("packages")]
    public List<ReleasePackageReference> Packages { get; init; } = [];
}

internal sealed class ReleasePackageReference
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; init; } = "";

    [JsonPropertyName("carId")]
    public string CarId { get; init; } = "";

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; init; } = "";

    [JsonPropertyName("manifestPath")]
    public string ManifestPath { get; init; } = "";

    [JsonPropertyName("manifestByteSize")]
    public long ManifestByteSize { get; init; }

    [JsonPropertyName("manifestSha256")]
    public string ManifestSha256 { get; init; } = "";
}

internal sealed class PackageManifest
{
    [JsonPropertyName("dependencies")]
    public List<PackageDependency> Dependencies { get; init; } = [];
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "";

    [JsonPropertyName("packageId")]
    public string PackageId { get; init; } = "";

    [JsonPropertyName("carId")]
    public string CarId { get; init; } = "";

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; init; } = "";

    [JsonPropertyName("files")]
    public List<PackageFile> Files { get; init; } = [];
}

internal sealed record PackageDependency(string PackageId, string PackageVersion);
internal sealed record ManagedFileStatus(string Path, string State, string PackageId, long ByteSize);
internal sealed record InstallationStatus(bool Installed, IReadOnlyList<ManagedFileStatus> Files, string ReleaseId = "",
    int BlockingModifications = 0, int AdoptableMatches = 0, int UnownedMissingFiles = 0);
internal sealed record GameInstallation(string Id, string Path);
internal sealed record CompanionRuntime
{
    public string Schema { get; init; } = "morecars.companion-runtime.v1";
    public string Version { get; init; } = CompanionBuild.Version;
    public string Stage { get; init; } = "starting";
    public string Message { get; init; } = "Companion started. Connecting to the website.";
    public bool GameRunning { get; init; }
    public bool GameReady { get; init; }
    public bool Installed { get; init; }
    public IReadOnlyList<GameInstallation> GameChoices { get; init; } = [];
    public string SelectedGameId { get; init; } = "";
    public IReadOnlyList<ManagedFileStatus> Files { get; init; } = [];
    public string[] Capabilities { get; init; } = new[] { "install-release", "cleanup-managed", "apply-skin", "restore-skin", "restore-all-skins",
        "inspect-installation", "install-car", "remove-car", "launch-game", "choose-game", "confirm-game", "uninstall-companion", "uninstall-companion-and-cars" }
        .Where(action => action != "launch-game" || OperatingSystem.IsWindows()).ToArray();
}

internal sealed class PackageFile
{
    [JsonPropertyName("logicalPath")]
    public string LogicalPath { get; init; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";

    [JsonPropertyName("byteSize")]
    public long ByteSize { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = "";
}

internal sealed class OwnedFile
{
    [JsonPropertyName("logicalPath")]
    public required string LogicalPath { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("byteSize")]
    public required long ByteSize { get; init; }

    [JsonPropertyName("packageId")]
    public required string PackageId { get; init; }

    [JsonPropertyName("packageVersion")]
    public required string PackageVersion { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

internal sealed class OwnershipOverlay
{
    [JsonPropertyName("logicalPath")]
    public required string LogicalPath { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "community-skin";

    [JsonPropertyName("reference")]
    public required string Reference { get; init; }

    [JsonPropertyName("baseSha256")]
    public required string BaseSha256 { get; init; }

    [JsonPropertyName("baseByteSize")]
    public required long BaseByteSize { get; init; }

    [JsonPropertyName("replacementSha256")]
    public required string ReplacementSha256 { get; init; }

    [JsonPropertyName("replacementByteSize")]
    public required long ReplacementByteSize { get; init; }

    [JsonPropertyName("appliedAt")]
    public required string AppliedAt { get; init; }
}

internal sealed class OwnershipManifest
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "morecars.ownership.v1";

    [JsonPropertyName("installationId")]
    public required string InstallationId { get; init; }

    [JsonPropertyName("releaseId")]
    public required string ReleaseId { get; set; }

    [JsonPropertyName("releaseManifestSha256")]
    public required string ReleaseManifestSha256 { get; set; }

    [JsonPropertyName("installedAt")]
    public required string InstalledAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; set; }

    [JsonPropertyName("files")]
    public List<OwnedFile> Files { get; init; } = [];

    [JsonPropertyName("overlays")]
    public List<OwnershipOverlay> Overlays { get; init; } = [];
}

internal sealed class InstallJournal
{
    [JsonPropertyName("schema")]
    public string Schema { get; init; } = "morecars.install-transaction.v1";

    [JsonPropertyName("logicalPath")]
    public required string LogicalPath { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("byteSize")]
    public required long ByteSize { get; init; }
}
