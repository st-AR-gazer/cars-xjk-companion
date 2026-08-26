using System.IO.Compression;

namespace MoreCars.Companion;

internal static class SkinArchiveComposer
{
    private const long MaxTextureBytes = 32 * 1024 * 1024;
    private const long MaxExpandedLiveryBytes = 128 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["body"] = ["Skin_B.dds", "Diffuse.dds", "SkinDiffuse.dds"],
        ["details"] = ["Details_B.dds", "DetailsDiffuse.dds", "Details.dds"],
        ["wheels"] = ["Wheels_B.dds", "WheelsDiffuse.dds"],
        ["glass"] = ["Glass_T.dds"]
    };

    public static async Task<CompositionResult> ComposeAsync(
        string baseArchivePath,
        string liveryArchivePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var textures = await ReadLiveryTexturesAsync(liveryArchivePath, cancellationToken);
        if (!textures.ContainsKey("body")) throw new InvalidDataException("The selected livery has no supported body texture.");
        var replacedSlots = new HashSet<string>(StringComparer.Ordinal);
        await using var sourceStream = new FileStream(baseArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read, false);
        if (source.Entries.Count is < 1 or > 4096) throw new InvalidDataException("The base vehicle archive has an unsafe entry count.");
        await using var outputStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using (var output = new ZipArchive(outputStream, ZipArchiveMode.Create, true))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in source.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArchivePath(entry.FullName);
                if (!names.Add(entry.FullName)) throw new InvalidDataException("The base vehicle archive repeats a path.");
                var created = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                created.LastWriteTime = entry.LastWriteTime;
                if (entry.FullName.EndsWith('/')) continue;
                var slot = TextureSlot(Path.GetFileName(entry.FullName));
                await using var destination = created.Open();
                if (slot is not null && textures.TryGetValue(slot, out var replacement))
                {
                    await destination.WriteAsync(replacement, cancellationToken);
                    replacedSlots.Add(slot);
                }
                else
                {
                    await using var input = entry.Open();
                    await input.CopyToAsync(destination, cancellationToken);
                }
            }
        }
        if (!replacedSlots.Contains("body"))
            throw new InvalidDataException("The base vehicle archive has no compatible body texture slot.");
        return new CompositionResult(replacedSlots.Order(StringComparer.Ordinal).ToArray());
    }

    private static async Task<Dictionary<string, byte[]>> ReadLiveryTexturesAsync(
        string liveryArchivePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(liveryArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
        if (archive.Entries.Count is < 1 or > 256) throw new InvalidDataException("The livery ZIP has an unsafe entry count.");
        long expanded = 0;
        var matches = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            ValidateArchivePath(entry.FullName);
            expanded += entry.Length;
            if (expanded > MaxExpandedLiveryBytes) throw new InvalidDataException("The livery ZIP expands beyond its safety limit.");
            var slot = TextureSlot(Path.GetFileName(entry.FullName));
            if (slot is null) continue;
            if (entry.Length is <= 0 or > MaxTextureBytes || entry.Length > entry.CompressedLength * 200 + 1024 * 1024)
                throw new InvalidDataException("A livery texture exceeds its safety limits.");
            if (!matches.TryGetValue(slot, out var entries)) matches[slot] = entries = [];
            entries.Add(entry);
        }

        var textures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (slot, aliases) in Aliases)
        {
            var candidates = matches.GetValueOrDefault(slot) ?? [];
            ZipArchiveEntry? selected = null;
            foreach (var alias in aliases)
            {
                var aliasMatches = candidates.Where(entry =>
                    Path.GetFileName(entry.FullName).Equals(alias, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (aliasMatches.Length > 1) throw new InvalidDataException($"The livery ZIP repeats {alias}.");
                if (aliasMatches.Length == 1)
                {
                    selected = aliasMatches[0];
                    break;
                }
            }
            if (selected is null) continue;
            await using var input = selected.Open();
            using var buffer = new MemoryStream((int)selected.Length);
            await input.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            ValidateDds(bytes);
            textures[slot] = bytes;
        }
        return textures;
    }

    private static string? TextureSlot(string fileName)
    {
        foreach (var (slot, aliases) in Aliases)
            if (aliases.Any(alias => alias.Equals(fileName, StringComparison.OrdinalIgnoreCase))) return slot;
        return null;
    }

    private static void ValidateArchivePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains('\0') ||
            normalized.Split('/').Any(segment => segment is "." or ".."))
            throw new InvalidDataException("A ZIP contains an unsafe path.");
    }

    private static void ValidateDds(byte[] bytes)
    {
        if (bytes.Length < 128 || BitConverter.ToUInt32(bytes, 0) != 0x20534444 || BitConverter.ToUInt32(bytes, 4) != 124)
            throw new InvalidDataException("A selected livery texture is not a valid DDS file.");
        var width = BitConverter.ToUInt32(bytes, 16);
        var height = BitConverter.ToUInt32(bytes, 12);
        var mipCount = Math.Max(1u, BitConverter.ToUInt32(bytes, 28));
        var fourCc = System.Text.Encoding.ASCII.GetString(bytes, 84, 4);
        if (width is 0 or > 8192 || height is 0 or > 8192 || mipCount > 16 || fourCc is not ("DXT1" or "DXT3" or "DXT5"))
            throw new InvalidDataException("A selected livery texture uses an unsupported DDS format.");
        var blockBytes = fourCc == "DXT1" ? 8L : 16L;
        var requiredBytes = 128L;
        for (var level = 0; level < mipCount; level++)
        {
            var mipWidth = Math.Max(1L, width >> level);
            var mipHeight = Math.Max(1L, height >> level);
            requiredBytes += Math.Max(1, (mipWidth + 3) / 4) * Math.Max(1, (mipHeight + 3) / 4) * blockBytes;
        }
        if (bytes.LongLength < requiredBytes) throw new InvalidDataException("A selected livery texture has a truncated mip chain.");
    }
}

internal sealed record CompositionResult(IReadOnlyList<string> ReplacedSlots);
