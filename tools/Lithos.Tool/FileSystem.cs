using System.Security.Cryptography;
using System.Text;

namespace Lithos.Tool;

internal static class FileSystem
{
    private static readonly HashSet<string> NormalizedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".json", ".props", ".targets", ".xml"
    };

    private static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj"
    };

    public static void CopyDirectory(string source, string destination, bool excludeRepositoryMetadata = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (ShouldExclude(relative, excludeRepositoryMetadata))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (ShouldExclude(relative, excludeRepositoryMetadata))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    public static void NormalizeSourceTree(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (NormalizedExtensions.Contains(Path.GetExtension(file)))
            {
                WriteNormalizedText(file, File.ReadAllText(file));
            }
        }
    }

    public static string NormalizeText(string text)
    {
        return text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
    }

    public static void WriteNormalizedText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, NormalizeText(text), new UTF8Encoding(false));
    }

    public static async Task<string> GetSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<string> GetMd5Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !ShouldExclude(Path.GetRelativePath(root, file), excludeRepositoryMetadata: true));
    }

    public static bool IsTextFile(string path)
    {
        return NormalizedExtensions.Contains(Path.GetExtension(path))
            || Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldExclude(string relativePath, bool excludeRepositoryMetadata)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => ExcludedSegments.Contains(segment)
            && (excludeRepositoryMetadata || !segment.Equals(".git", StringComparison.OrdinalIgnoreCase)));
    }
}
