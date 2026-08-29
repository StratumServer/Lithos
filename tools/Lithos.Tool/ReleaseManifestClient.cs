using System.Text.Json;

namespace Lithos.Tool;

internal sealed class ReleaseManifestClient
{
    private const string ManifestUrl = "https://api.vintagestory.at/stable-unstable.json";
    private readonly HttpClient httpClient = new();
    private JsonDocument? manifest;

    public async Task<ArchiveInfo> GetServerArchiveAsync(string version)
    {
        var platform = OperatingSystem.IsWindows() ? "windowsserver" : "linuxserver";
        return await GetArchiveAsync(version, platform);
    }

    public Task<ArchiveInfo> GetLinuxClientArchiveAsync(string version)
    {
        return GetArchiveAsync(version, "linux");
    }

    public async Task DownloadAsync(ArchiveInfo archive, string destination)
    {
        if (File.Exists(destination))
        {
            var cachedHash = await FileSystem.GetMd5Async(destination);
            if (cachedHash.Equals(archive.Md5, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Using verified cache {destination}");
                return;
            }

            File.Delete(destination);
        }

        Console.WriteLine($"Downloading {archive.Url}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".partial";
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }

        using var response = await httpClient.GetAsync(archive.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(temporary))
        {
            await source.CopyToAsync(target);
        }

        var hash = await FileSystem.GetMd5Async(temporary);
        if (!hash.Equals(archive.Md5, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporary);
            throw new InvalidDataException($"Downloaded archive failed MD5 verification: {archive.FileName}");
        }

        File.Move(temporary, destination);
    }

    private async Task<ArchiveInfo> GetArchiveAsync(string version, string platform)
    {
        manifest ??= await LoadManifestAsync();
        if (!manifest.RootElement.TryGetProperty(version, out var versionNode)
            || !versionNode.TryGetProperty(platform, out var archiveNode))
        {
            throw new InvalidDataException($"Vintage Story {version} has no {platform} archive in the release manifest.");
        }

        var fileName = archiveNode.GetProperty("filename").GetString();
        var url = archiveNode.GetProperty("urls").GetProperty("cdn").GetString();
        var md5 = archiveNode.GetProperty("md5").GetString();
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(md5))
        {
            throw new InvalidDataException($"Vintage Story {version} has incomplete {platform} archive metadata.");
        }

        return new ArchiveInfo(fileName, url, md5);
    }

    private async Task<JsonDocument> LoadManifestAsync()
    {
        using var stream = await httpClient.GetStreamAsync(ManifestUrl);
        return await JsonDocument.ParseAsync(stream);
    }
}

internal sealed record ArchiveInfo(string FileName, string Url, string Md5);
