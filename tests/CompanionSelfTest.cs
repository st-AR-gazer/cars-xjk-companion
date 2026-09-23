using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MoreCars.Companion;

internal static class CompanionSelfTest
{
    private static async Task<bool> GameStartNoticeAsync(CancellationToken token)
    {
        var feed = System.Text.Encoding.UTF8.GetBytes("""
            {"schema":"morecars.companion-updates.v1","version":"0.6.3","releases":[
              {"version":"0.6.3","title":"Startup notice","summary":"Shows changes at game launch.",
               "changes":[{"title":"Choice","description":"Install later or review now."}]},
              {"version":"0.6.2","title":"Older","summary":"Already installed."}]}
            """);
        var (newer, notes) = StartupUpdateProbe.ParseCompanionNotes(feed, "0.6.2");
        var (current, _) = StartupUpdateProbe.ParseCompanionNotes(feed, "0.6.3");
        var currentNotes = StartupUpdateProbe.ParseCurrentReleaseNotes(feed, "0.6.3");
        var running = false;
        var checks = 0;
        var shown = 0;
        var monitor = new GameStartUpdateMonitor(
            () => running,
            _ => { checks++; return Task.FromResult<StartupUpdateNotice?>(new(true, false, "Update", notes)); },
            (_, _) => { shown++; return Task.CompletedTask; });
        await monitor.TickAsync(token);
        running = true;
        await monitor.TickAsync(token);
        await monitor.TickAsync(token);
        running = false;
        await monitor.TickAsync(token);
        running = true;
        await monitor.TickAsync(token);
        return newer && !current && notes.Contains("Startup notice", StringComparison.Ordinal) &&
               currentNotes.Contains("Shows changes at game launch", StringComparison.Ordinal) &&
               !currentNotes.Contains("Already installed", StringComparison.Ordinal) &&
               notes.Contains("Install later or review now", StringComparison.Ordinal) &&
               !notes.Contains("Already installed", StringComparison.Ordinal) && checks == 2 && shown == 2;
    }

    private static bool CompanionUpdateDownloadValidation()
    {
        var hash = new string('a', 64);
        var valid = System.Text.Encoding.UTF8.GetBytes("""
            {"schema":"morecars.companion-updates.v1","version":"0.6.4","downloads":{"windows":
              {"file":"MoreCarsCompanion-0.6.4-dev.exe","sha256":"{HASH}","bytes":71871372}}}
            """.Replace("{HASH}", hash, StringComparison.Ordinal));
        var traversal = System.Text.Encoding.UTF8.GetBytes("""
            {"schema":"morecars.companion-updates.v1","version":"0.6.4","downloads":{"windows":
              {"file":"../MoreCarsCompanion.exe","sha256":"{HASH}","bytes":71871372}}}
            """.Replace("{HASH}", hash, StringComparison.Ordinal));
        var parsed = CompanionSelfUpdater.ParseDownload(valid, "0.6.3");
        var rejectsOldVersion = false;
        var rejectsTraversal = false;
        try { CompanionSelfUpdater.ParseDownload(valid, "0.6.4"); }
        catch (InvalidDataException) { rejectsOldVersion = true; }
        try { CompanionSelfUpdater.ParseDownload(traversal, "0.6.3"); }
        catch (InvalidDataException) { rejectsTraversal = true; }
        return parsed.Version == "0.6.4" && parsed.Bytes == 71871372 && parsed.Sha256 == hash &&
               rejectsOldVersion && rejectsTraversal;
    }

    private static async Task<bool> ThumbnailSidecarsAsync(CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), "MoreCars-thumbnail-selftest-" + Guid.NewGuid().ToString("N"));
        var skins = Path.Combine(root, "GameData", "Vehicles", "Skins");
        Directory.CreateDirectory(skins);
        try
        {
            var factory = new byte[] { 1, 2, 3, 4 };
            var livery = new byte[] { 5, 6, 7, 8 };
            static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var suffixes = new[] { "_Icon_Factory.dds", "_Icon_Current.dds", "_Icon.dds" };
            var ownership = new OwnershipManifest
            {
                InstallationId = "thumbnail-selftest",
                ReleaseId = ThumbnailRelease.ReleaseId,
                ReleaseManifestSha256 = ThumbnailRelease.ReleaseSha256,
                InstalledAt = DateTimeOffset.UtcNow.ToString("O"),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            foreach (var suffix in suffixes)
            {
                var logicalPath = "GameData/Vehicles/Skins/BayCar" + suffix;
                File.WriteAllBytes(Path.Combine(skins, "BayCar" + suffix), factory);
                ownership.Files.Add(new OwnedFile
                {
                    LogicalPath = logicalPath,
                    Sha256 = Hash(factory),
                    ByteSize = factory.Length,
                    PackageId = "bay-vehicle",
                    PackageVersion = "1.2.0",
                    Role = "vehicle-thumbnail"
                });
            }
            Directory.CreateDirectory(Path.Combine(root, ".morecars"));
            var ledger = Path.Combine(root, ".morecars", "ownership-v1.json");
            File.WriteAllText(ledger, JsonSerializer.Serialize(ownership, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var settings = new CompanionSettings { TrackmaniaRoot = root };
            using var api = new CompanionApi(settings);
            var installer = new ReleaseInstaller(settings, api);
            await installer.SyncThumbnailsAsync("bay", livery, "skin-one", token);
            var selected = JsonSerializer.Deserialize<OwnershipManifest>(File.ReadAllText(ledger), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (selected.Overlays.Count != 2 || suffixes.Skip(1).Any(suffix =>
                !File.ReadAllBytes(Path.Combine(skins, "BayCar" + suffix)).SequenceEqual(livery))) return false;
            await installer.SyncThumbnailsAsync("bay", livery, "skin-two", token);
            selected = JsonSerializer.Deserialize<OwnershipManifest>(File.ReadAllText(ledger), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (selected.Overlays.Any(item => item.Reference != "skin-two")) return false;
            await installer.SyncThumbnailsAsync("bay", null, "", token);
            selected = JsonSerializer.Deserialize<OwnershipManifest>(File.ReadAllText(ledger), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (selected.Overlays.Count != 0 || suffixes.Any(suffix =>
                !File.ReadAllBytes(Path.Combine(skins, "BayCar" + suffix)).SequenceEqual(factory))) return false;
            var current = Path.Combine(skins, "BayCar_Icon_Current.dds");
            File.WriteAllBytes(current, [9, 9, 9, 9]);
            try { await installer.SyncThumbnailsAsync("bay", livery, "skin-three", token); return false; }
            catch (IOException) { return File.ReadAllBytes(current).SequenceEqual(new byte[] { 9, 9, 9, 9 }); }
        }
        finally
        {
            var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(prefix, CompanionPlatform.PathComparison))
                throw new InvalidOperationException("Thumbnail self-test cleanup escaped the temporary root.");
            Directory.Delete(root, true);
        }
    }

    private static async Task<bool> LiveryIconExtractionAsync(CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), "MoreCars-icon-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archivePath = Path.Combine(root, "skin.zip");
            var icon = new byte[136];
            "DDS "u8.CopyTo(icon);
            BitConverter.GetBytes(124).CopyTo(icon, 4);
            BitConverter.GetBytes(4).CopyTo(icon, 12);
            BitConverter.GetBytes(4).CopyTo(icon, 16);
            BitConverter.GetBytes(1).CopyTo(icon, 28);
            "DXT1"u8.CopyTo(icon.AsSpan(84));
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using var output = archive.CreateEntry("Icon.dds").Open();
                output.Write(icon);
            }
            var extracted = await SkinArchiveComposer.ReadIconAsync(archivePath, token);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
                archive.GetEntry("Icon.dds")!.Delete();
            var missing = await SkinArchiveComposer.ReadIconAsync(archivePath, token);
            return extracted is not null && extracted.SequenceEqual(icon) && missing is null;
        }
        finally
        {
            var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(prefix, CompanionPlatform.PathComparison))
                throw new InvalidOperationException("Icon self-test cleanup escaped the temporary root.");
            Directory.Delete(root, true);
        }
    }

    public static async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
#if WINDOWS
        System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
#endif
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
                Path.GetDirectoryName(CompanionStorage.AppDirectory)!);
        }
        catch (InvalidOperationException)
        {
            rejectsBroadTarget = true;
        }

        var dialogOwnerIsForeground = true;
        var pickerOpened = false;
        string? Browse() { pickerOpened = true; return "chosen"; }
        bool IsRoot(string value) => new[] { "saved", "configured", "detected", "chosen" }.Contains(value);
        var saved = CompanionGame.SelectRoot("saved", "", true, () => "detected", Browse, IsRoot);
        var configured = CompanionGame.SelectRoot("", "configured", true, () => "detected", Browse, IsRoot);
        var detected = CompanionGame.SelectRoot("", "", true, () => "detected", Browse, IsRoot);
        var missing = CompanionGame.SelectRoot("", "", true, () => null, Browse, IsRoot);
        var detectionNeverOpensPicker = !pickerOpened && saved == "saved" && configured == "configured" &&
                                       detected == "detected" && missing is null;
        var manual = CompanionGame.SelectRoot("saved", "configured", false, () => "detected", Browse, IsRoot);
        var cancelled = CompanionGame.SelectRoot("saved", "", false, () => "detected", () => null, IsRoot);
        var explicitFolderChoiceWorks = pickerOpened && manual == "chosen" && cancelled is null;
        var discoveredChoices = CompanionGameDiscovery.Choices(new[] { "saved", "configured", "saved" }, IsRoot);
        var allChoicesAreReported = discoveredChoices.Count == 2 && discoveredChoices.Select(choice => choice.Id).Distinct().Count() == 2 &&
            discoveredChoices.All(choice => Path.IsPathFullyQualified(choice.Path) && choice.Id.StartsWith("game_", StringComparison.Ordinal));
        var cancelledSettings = new CompanionSettings { TrackmaniaRoot = "previous-folder" };
        var cancelledHost = new CompanionHost(cancelledSettings, (_, _, _) => Task.FromResult<string?>(null));
        var cancelledSelection = await cancelledHost.PrepareGameAsync(false, cancellationToken);
        var cancellationKeepsSelection = !cancelledSelection && cancelledSettings.TrackmaniaRoot == "previous-folder" &&
                                         cancelledHost.Runtime.Stage == "ready" && !cancelledHost.Runtime.GameReady;
        var missingHost = new CompanionHost(new CompanionSettings(), (_, _, _) => Task.FromResult<string?>(null));
        var missingSelection = await missingHost.PrepareGameAsync(true, cancellationToken);
        var missingRootReturnsToWebsite = !missingSelection && missingHost.Runtime.Stage == "ready" &&
                                          !missingHost.Runtime.GameReady;
        var invalidSettings = new CompanionSettings { TrackmaniaRoot = "previous-folder" };
        var invalidHost = new CompanionHost(invalidSettings, (_, _, _) => Task.FromResult<string?>("invalid-folder"));
        var invalidSelectionRejected = false;
        try { await invalidHost.PrepareGameAsync(false, cancellationToken); }
        catch (InvalidOperationException) { invalidSelectionRejected = invalidSettings.TrackmaniaRoot == "previous-folder"; }
#if WINDOWS
        using (var owner = CompanionPlatform.CreateDialogOwner())
        {
            dialogOwnerIsForeground = owner.TopMost && owner.ShowInTaskbar && owner.Opacity == 1 &&
                                      owner.ClientSize.Width >= 400 && owner.ClientSize.Height >= 100;
        }
        var candidates = CompanionPlatform.CommonTrackmaniaCandidates("C:\\Program Files", "C:\\Program Files (x86)");
        dialogOwnerIsForeground = dialogOwnerIsForeground && candidates.Count == 4 &&
                                  candidates.Contains("C:\\Program Files\\Ubisoft\\Ubisoft Game Launcher\\games\\Trackmania") &&
                                  candidates.Contains("C:\\Program Files (x86)\\Steam\\steamapps\\common\\Trackmania");
#endif

        var helperPath = Path.Combine(Path.GetTempPath(), "MoreCarsCompanion-uninstall-" + new string('a', 32), CompanionPlatform.ExecutableFileName);
        var helperBounded = CompanionUninstaller.RequireHelperDirectory(helperPath) == Path.GetDirectoryName(helperPath);
        try { CompanionUninstaller.RequireHelperDirectory(Path.Combine(Path.GetTempPath(), CompanionPlatform.ExecutableFileName)); helperBounded = false; }
        catch (InvalidOperationException) { }
        var uninstallPreservesOtherFiles = true;
#if WINDOWS
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MoreCarsCompanion-selftest-" + Guid.NewGuid().ToString("N")));
        var previousDataHome = Environment.GetEnvironmentVariable("MORECARS_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("MORECARS_DATA_HOME", testRoot);
            Directory.CreateDirectory(CompanionStorage.CacheDirectory);
            File.WriteAllText(CompanionStorage.InstalledExecutablePath, "test companion");
            File.WriteAllText(CompanionStorage.SettingsPath, "test settings");
            File.WriteAllText(Path.Combine(CompanionStorage.CacheDirectory, "test.zip"), "test cache");
            var game = Path.Combine(testRoot, "Trackmania");
            Directory.CreateDirectory(game);
            var car = Path.Combine(game, "car.zip");
            File.WriteAllText(car, "keep car");
            var protocolRemoved = false;
            CompanionStorage.Uninstall(false, () => protocolRemoved = true);
            uninstallPreservesOtherFiles = protocolRemoved && !Directory.Exists(CompanionStorage.AppDirectory) &&
                                           File.ReadAllText(car) == "keep car";
        }
        finally
        {
            Environment.SetEnvironmentVariable("MORECARS_DATA_HOME", previousDataHome);
            var tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!testRoot.StartsWith(tempPrefix, CompanionPlatform.PathComparison)) throw new InvalidOperationException("Self-test cleanup escaped its temporary root.");
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
#endif

        var checks = new Dictionary<string, bool>
        {
#if WINDOWS
            ["native-runtime-is-bundled"] = typeof(Program).Assembly.GetManifestResourceNames().Contains("MoreCars.GameRuntime.dll"),
#endif
            ["not-cancelled"] = !cancellationToken.IsCancellationRequested,
            ["skin-writes-wait-for-game-exit"] = await CompanionSkinGateSelfTest.RunAsync(),
            ["livery-icon-extraction"] = await LiveryIconExtractionAsync(cancellationToken),
            ["thumbnail-sidecars"] = await ThumbnailSidecarsAsync(cancellationToken),
            ["game-start-update-notice"] = await GameStartNoticeAsync(cancellationToken),
            ["companion-update-download-validation"] = CompanionUpdateDownloadValidation(),
            ["modified-cars-require-review"] = new StartupUpdateNotice(false, true, "", "") { HasModifiedCars = true }
                .InstallLabel == "Review modified files",
            ["matching-camera-tuning-can-be-adopted"] =
                !ReleaseInstaller.BlocksReleaseAdoption(5, new string('a', 64), 5, new string('a', 64)) &&
                ReleaseInstaller.BlocksReleaseAdoption(5, new string('a', 64), 5, new string('b', 64)) &&
                ReleaseInstaller.BlocksReleaseAdoption(4, new string('a', 64), 5, new string('a', 64)),
            ["owned-uninstall-targets"] = ownedTargetsAreNarrow,
            ["rejects-broad-uninstall"] = rejectsBroadTarget,
            ["dialog-owner-and-detection"] = dialogOwnerIsForeground,
            ["discovery-never-opens-picker"] = detectionNeverOpensPicker,
            ["all-discovered-installations-are-choices"] = allChoicesAreReported,
            ["new-settings-require-folder-confirmation"] = !new CompanionSettings().GameConfirmed,
            ["explicit-folder-choice-and-cancel"] = explicitFolderChoiceWorks,
            ["cancellation-keeps-selection"] = cancellationKeepsSelection,
            ["missing-root-returns-to-website"] = missingRootReturnsToWebsite,
            ["invalid-folder-keeps-selection"] = invalidSelectionRejected,
            ["uninstall-helper-is-bounded"] = helperBounded,
            ["full-uninstall-preserves-car-files"] = uninstallPreservesOtherFiles,
            ["uninstall-actions"] = CompanionUninstaller.IsUninstall("uninstall-companion") &&
                                      CompanionUninstaller.IsUninstall("uninstall-companion-and-cars") &&
                                      !CompanionUninstaller.IsUninstall("cleanup-managed"),
            ["personalized-download-name"] = CompanionActivation.PairingFromFileName("MoreCarsCompanion-pair_" + new string('a', 43) + " (1).exe") == new string('a', 43),
            ["ordinary-download-name"] = CompanionActivation.PairingFromFileName("MoreCarsCompanion.exe") == "",
            ["invalid-pairing-name"] = CompanionActivation.PairingFromFileName("MoreCarsCompanion-pair_bad.exe") == "",
            ["invalid-pairing-code"] = !CompanionActivation.IsPairingCode("../untrusted"),
            ["legacy-service-capabilities"] = !CompanionProtocol.Parse(System.Net.HttpStatusCode.NotFound, "").Contains("restore-all-skins"),
            ["negotiated-service-capabilities"] = CompanionProtocol.Parse(System.Net.HttpStatusCode.OK,
                "{\"schema\":\"morecars.companion-protocol.v1\",\"capabilities\":[\"restore-all-skins\",\"untrusted-command\"]}")
                .SequenceEqual(new[] { "restore-all-skins" }),
            ["local-download-origin"] = CompanionDownload.NormalizeOrigin("http://cars.localhost:8080") == "http://127.0.0.1:8080",
            ["uninstall-root-count"] = removeAll.Count >= 1,
            ["keep-data-executable"] = keepData.Any(path => Path.GetFileName(path).Equals(CompanionPlatform.ExecutableFileName, StringComparison.Ordinal)),
            ["known-failure-is-an-instruction"] =
                CompanionApi.FailureMessage(401, "{\"error\":{\"code\":\"pairing_invalid\",\"message\":\"x\"}}", null)
                    .Contains("Start setup again", StringComparison.Ordinal),
            ["service-wording-is-kept"] =
                CompanionApi.FailureMessage(400, "{\"error\":{\"code\":\"invalid_request\",\"message\":\"Bad body.\"}}", null) ==
                    "Bad body.",
            ["silent-404-names-the-website"] =
                CompanionApi.FailureMessage(404, "", new Uri("https://cars.xjk.yt")).StartsWith("https://cars.xjk.yt", StringComparison.Ordinal),
            ["unreadable-body-never-leaks"] =
                !CompanionApi.FailureMessage(500, "<html>oops</html>", new Uri("http://127.0.0.1:8080")).Contains("<html>", StringComparison.Ordinal),
            ["managed-relative-path"] = ReleaseInstaller.IsManagedPath("GameData/Vehicles/Skins/BayCar.zip"),
            ["reject-traversal"] = !ReleaseInstaller.IsManagedPath("GameData/Vehicles/../unknown.zip"),
            ["reject-absolute-path"] = !ReleaseInstaller.IsManagedPath("C:/GameData/Vehicles/Skins/BayCar.zip")
        };
        foreach (var check in checks.Where(check => !check.Value)) Console.Error.WriteLine("Self-test failed: " + check.Key);
        return checks.Values.All(value => value);
    }
}
