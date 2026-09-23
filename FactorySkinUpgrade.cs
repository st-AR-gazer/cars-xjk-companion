namespace MoreCars.Companion;

internal static partial class FactorySkinUpgrade
{
    // The caller verifies the live livery bytes first. These release pairs are
    // generated only after all geometry, details and alpha bytes compare equal.
    internal static bool CanPreserve(OwnedFile existing, PackageFile next, OwnershipOverlay overlay)
    {
        if (next.Role == "vehicle-thumbnail" && overlay.Kind == "community-skin-icon" &&
            (next.LogicalPath.EndsWith("_Icon_Current.dds", StringComparison.OrdinalIgnoreCase) ||
             next.LogicalPath.EndsWith("_Icon.dds", StringComparison.OrdinalIgnoreCase)))
            return StringComparer.OrdinalIgnoreCase.Equals(existing.LogicalPath, next.LogicalPath) &&
                   StringComparer.OrdinalIgnoreCase.Equals(overlay.LogicalPath, next.LogicalPath) &&
                   (existing.Sha256 == next.Sha256 ||
                    ThumbnailRelease.CanPreserveIconBase(next.LogicalPath, existing.Sha256, next.Sha256)) &&
                   overlay.BaseSha256 == existing.Sha256 && overlay.BaseByteSize == existing.ByteSize;
        if (next.Role != "vehicle-model-archive" ||
            !StringComparer.OrdinalIgnoreCase.Equals(existing.LogicalPath, next.LogicalPath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(overlay.LogicalPath, next.LogicalPath) ||
            overlay.BaseSha256 != existing.Sha256 || overlay.BaseByteSize != existing.ByteSize)
            return false;
        if (existing.Sha256 == next.Sha256 && existing.ByteSize == next.ByteSize) return true;
        return CompatibleArchives.TryGetValue(next.LogicalPath, out var pair) &&
               (existing.Sha256 == pair.Source || existing.Sha256 == pair.Previous) &&
               next.Sha256 == pair.Target;
    }

    internal static OwnershipOverlay Rebase(OwnershipOverlay overlay, PackageFile next) => new()
    {
        LogicalPath = overlay.LogicalPath,
        Kind = overlay.Kind,
        Reference = overlay.Reference,
        BaseSha256 = next.Sha256,
        BaseByteSize = next.ByteSize,
        ReplacementSha256 = overlay.ReplacementSha256,
        ReplacementByteSize = overlay.ReplacementByteSize,
        AppliedAt = overlay.AppliedAt
    };
}
