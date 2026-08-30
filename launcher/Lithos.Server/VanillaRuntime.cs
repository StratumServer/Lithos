using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lithos.Server;

internal sealed class VanillaRuntime(ReleaseInfo release)
{
    private static readonly Uri ManifestUri = new("https://api.vintagestory.at/stable-unstable.json");
    private static readonly HttpClient HttpClient = new();

    public async Task<string> PrepareAsync(bool refresh)
    {
        var runtime = AppContext.BaseDirectory;
        var workspace = Path.Combine(runtime, ".lithos");
        if (!refresh && IsCurrent(runtime)) return runtime;

        Directory.CreateDirectory(workspace);
        var lockFile = Path.Combine(workspace, "prepare.lock");
        await using var prepareLock = OpenLock(lockFile);
        if (!refresh && IsCurrent(runtime)) return runtime;

        var archive = await ResolveArchiveAsync();
        var cache = Path.Combine(workspace, "cache");
        var archivePath = Path.Combine(cache, archive.FileName);
        await DownloadAsync(archive, archivePath);
        await ExtractRuntimeAsync(archivePath, runtime, workspace);
        return runtime;
    }

    private bool IsCurrent(string runtime)
    {
        var marker = Path.Combine(runtime, ".vanilla-version");
        return File.Exists(Path.Combine(runtime, "VintagestoryServer.dll"))
            && Directory.Exists(Path.Combine(runtime, "assets"))
            && File.Exists(marker)
            && File.ReadAllText(marker).Trim().Equals(release.VintageStoryVersion, StringComparison.Ordinal);
    }

    private async Task<ArchiveInfo> ResolveArchiveAsync()
    {
        var platform = OperatingSystem.IsWindows()
            ? "windowsserver"
            : OperatingSystem.IsLinux()
                ? "linuxserver"
                : throw new PlatformNotSupportedException("Lithos releases currently support Windows and Linux.");

        using var stream = await HttpClient.GetStreamAsync(ManifestUri);
        using var document = await JsonDocument.ParseAsync(stream);
        if (!document.RootElement.TryGetProperty(release.VintageStoryVersion, out var versionNode)
            || !versionNode.TryGetProperty(platform, out var archiveNode))
        {
            throw new InvalidDataException(
                $"Vintage Story {release.VintageStoryVersion} has no {platform} archive in the release manifest.");
        }

        var fileName = Path.GetFileName(archiveNode.GetProperty("filename").GetString());
        var url = archiveNode.GetProperty("urls").GetProperty("cdn").GetString();
        var md5 = archiveNode.GetProperty("md5").GetString();
        if (string.IsNullOrWhiteSpace(fileName)
            || string.IsNullOrWhiteSpace(url)
            || string.IsNullOrWhiteSpace(md5)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The official release manifest contains incomplete archive metadata.");
        }

        return new ArchiveInfo(fileName, uri, md5);
    }

    private static async Task DownloadAsync(ArchiveInfo archive, string destination)
    {
        if (File.Exists(destination)
            && (await GetMd5Async(destination)).Equals(archive.Md5, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Lithos: using verified cache {destination}");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.partial";
        try
        {
            Console.WriteLine($"Lithos: downloading Vintage Story {archive.Url}");
            using var response = await HttpClient.GetAsync(archive.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(temporary))
            {
                await source.CopyToAsync(output);
            }

            var hash = await GetMd5Async(temporary);
            if (!hash.Equals(archive.Md5, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Downloaded archive failed verification: {archive.FileName}");
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task ExtractRuntimeAsync(string archive, string runtime, string workspace)
    {
        var extraction = Path.Combine(workspace, $"extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extraction);
        try
        {
            Console.WriteLine($"Lithos: preparing Vintage Story {release.VintageStoryVersion}");
            await ExtractAsync(archive, extraction);
            var serverAssembly = Directory
                .EnumerateFiles(extraction, "VintagestoryServer.dll", SearchOption.AllDirectories)
                .FirstOrDefault(path => Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!, "assets")))
                ?? throw new InvalidDataException("The official server archive does not contain a complete server install.");
            var marker = Path.Combine(runtime, ".vanilla-version");
            if (File.Exists(marker)) File.Delete(marker);

            var assets = Path.Combine(runtime, "assets");
            if (Directory.Exists(assets)) Directory.Delete(assets, recursive: true);

            CopyDirectory(Path.GetDirectoryName(serverAssembly)!, runtime);
            File.WriteAllText(marker, release.VintageStoryVersion);
        }
        finally
        {
            if (Directory.Exists(extraction)) Directory.Delete(extraction, recursive: true);
        }
    }

    private static async Task ExtractAsync(string archive, string destination)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true));
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

        throw new InvalidDataException($"Unsupported server archive: {archive}");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static FileStream OpenLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Another Lithos launcher is preparing the server runtime.", exception);
        }
    }

    private static async Task<string> GetMd5Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    private sealed record ArchiveInfo(string FileName, Uri Url, string Md5);
}
