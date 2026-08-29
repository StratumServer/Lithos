using System.Formats.Tar;
using System.IO.Compression;

namespace Lithos.Tool;

internal static class ArchiveExtractor
{
    public static async Task ExtractServerAsync(string archive, string destination, RepositoryPaths paths)
    {
        var extraction = Path.Combine(paths.Temporary, $"server-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extraction);
        try
        {
            await ExtractAsync(archive, extraction);
            var library = Directory.EnumerateFiles(extraction, "VintagestoryLib.dll", SearchOption.AllDirectories)
                .FirstOrDefault(path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "VintagestoryServer.dll")))
                ?? throw new InvalidDataException("The server archive does not contain VintagestoryLib.dll and VintagestoryServer.dll together.");
            var installRoot = Path.GetDirectoryName(library)!;

            if (Directory.Exists(destination))
            {
                paths.DeleteGeneratedDirectory(destination);
            }

            if (Path.GetFullPath(installRoot).Equals(Path.GetFullPath(extraction), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(extraction, destination);
            }
            else
            {
                Directory.Move(installRoot, destination);
            }
        }
        finally
        {
            if (Directory.Exists(extraction))
            {
                paths.DeleteGeneratedDirectory(extraction);
            }
        }
    }

    public static async Task ExtractRequiredFilesAsync(string archive, string destination, IReadOnlyCollection<string> fileNames)
    {
        var remaining = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(destination);

        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                await ExtractMatchingEntryAsync(entry.Name, entry.Open, destination, remaining);
                if (remaining.Count == 0) return;
            }
        }
        else if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await using var file = File.OpenRead(archive);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.DataStream is null) continue;
                await ExtractMatchingEntryAsync(Path.GetFileName(entry.Name), () => entry.DataStream, destination, remaining);
                if (remaining.Count == 0) return;
            }
        }
        else
        {
            throw new InvalidDataException($"Unsupported archive type: {archive}");
        }

        throw new InvalidDataException($"Client archive is missing required references: {string.Join(", ", remaining)}");
    }

    private static async Task ExtractAsync(string archive, string destination)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, destination, true));
            return;
        }

        if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await using var file = File.OpenRead(archive);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, destination, overwriteFiles: true);
            return;
        }

        throw new InvalidDataException($"Unsupported archive type: {archive}");
    }

    private static async Task ExtractMatchingEntryAsync(
        string entryName,
        Func<Stream> openStream,
        string destination,
        HashSet<string> remaining)
    {
        if (!remaining.Remove(entryName)) return;

        var target = Path.Combine(destination, entryName);
        await using var source = openStream();
        await using var output = File.Create(target);
        await source.CopyToAsync(output);
        Console.WriteLine($"Restored build reference {entryName}");
    }
}
