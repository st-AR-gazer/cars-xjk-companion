namespace MoreCars.Companion;

internal static class CompanionSelfTest
{
    public static Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var removeAll = CompanionStorage.UninstallTargets(false);
        var keepData = CompanionStorage.UninstallTargets(true);
        var ownedTargetsAreNarrow = removeAll.Concat(keepData).All(path =>
        {
            try
            {
                CompanionPlatform.RequireOwnedUninstallTarget(path);
                return true;
            }
            catch
            {
                return false;
            }
        });
        var rejectsBroadTarget = false;
        try
        {
            CompanionPlatform.RequireOwnedUninstallTarget(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch (InvalidOperationException)
        {
            rejectsBroadTarget = true;
        }

        return Task.FromResult(
            !cancellationToken.IsCancellationRequested &&
            ownedTargetsAreNarrow &&
            rejectsBroadTarget &&
            removeAll.Count >= 1 &&
            keepData.Any(path => Path.GetFileName(path).Equals(CompanionPlatform.ExecutableFileName, StringComparison.Ordinal)) &&
            ReleaseInstaller.IsManagedPath("GameData/Vehicles/Skins/BayCar.zip") &&
            !ReleaseInstaller.IsManagedPath("GameData/Vehicles/../unknown.zip") &&
            !ReleaseInstaller.IsManagedPath("C:/GameData/Vehicles/Skins/BayCar.zip"));
    }
}
