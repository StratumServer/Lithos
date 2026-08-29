using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lithos.Tool;

internal sealed class RepositoryManifest
{
    public required string VintageStoryVersion { get; init; }
    public required List<UpstreamRepository> Forks { get; init; }
    public required List<DecompiledProject> DecompiledProjects { get; init; }
    public required List<string> RequiredClientReferences { get; init; }

    public static RepositoryManifest Load(RepositoryPaths paths)
    {
        var json = File.ReadAllText(paths.ManifestFile);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var manifest = JsonSerializer.Deserialize<RepositoryManifest>(json, options)
            ?? throw new InvalidDataException($"Could not read {paths.ManifestFile}.");
        manifest.Validate();
        return manifest;
    }

    public static string GetBaselineFingerprint(RepositoryPaths paths)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in new[] { paths.ManifestFile, paths.ToolManifestFile })
        {
            sha256.AppendData(File.ReadAllBytes(file));
        }

        return Convert.ToHexStringLower(sha256.GetHashAndReset());
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(VintageStoryVersion))
        {
            throw new InvalidDataException("forks.json must define vintageStoryVersion.");
        }

        if (Forks.Count == 0 || DecompiledProjects.Count == 0)
        {
            throw new InvalidDataException("forks.json must define forks and decompiledProjects.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in Projects)
        {
            if (!IsSafeName(project) || !names.Add(project))
            {
                throw new InvalidDataException($"Invalid or duplicate project name in forks.json: {project}");
            }
        }

        foreach (var fork in Forks)
        {
            if (!Uri.TryCreate(fork.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException($"Upstream URL must use HTTPS: {fork.Url}");
            }

            if (fork.Ref.Length < 7 || fork.Ref.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException($"Upstream ref must be a commit hash: {fork.Name} {fork.Ref}");
            }
        }
    }

    public IEnumerable<string> Projects => DecompiledProjects.Select(project => project.Name)
        .Concat(Forks.Select(fork => fork.Name));

    private static bool IsSafeName(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }
}

internal sealed class UpstreamRepository
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public required string Ref { get; init; }
}

internal sealed class DecompiledProject
{
    public required string Name { get; init; }
    public required string Assembly { get; init; }
}

internal sealed class BootstrapState
{
    public required string VintageStoryVersion { get; init; }
    public required string BaselineFingerprint { get; init; }
    public required string ServerArchiveSha256 { get; init; }
    public string? ClientArchiveSha256 { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
