using System.Text;

namespace Lithos.Tool;

internal sealed class CaptureCommand(RepositoryPaths paths, RepositoryManifest manifest)
{
    public async Task RunAsync()
    {
        EnsureReady();
        var expectedPatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedOverlays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in manifest.Projects)
        {
            Console.WriteLine($"Capturing src/{project}");
            await CaptureProjectAsync(project, expectedPatches, expectedOverlays);
        }

        RemoveStaleFiles(paths.Patches, expectedPatches, ".patch");
        RemoveStaleFiles(paths.Overlay, expectedOverlays, extension: null);
        RemoveEmptyDirectories(paths.Patches);
        RemoveEmptyDirectories(paths.Overlay);

        Console.WriteLine($"Captured {expectedPatches.Count} patch(es) and {expectedOverlays.Count} overlay file(s).");
    }

    private void EnsureReady()
    {
        if (!Directory.Exists(paths.Source) || !Directory.Exists(paths.BaselineSource) || !File.Exists(paths.StateFile))
        {
            throw new InvalidOperationException("No complete source baseline was found. Run bootstrap first.");
        }
    }

    private async Task CaptureProjectAsync(
        string project,
        HashSet<string> expectedPatches,
        HashSet<string> expectedOverlays)
    {
        var baselineRoot = Path.Combine(paths.BaselineSource, project);
        var sourceRoot = Path.Combine(paths.Source, project);
        if (!Directory.Exists(baselineRoot) || !Directory.Exists(sourceRoot))
        {
            throw new InvalidOperationException($"Project is missing from the materialized tree: {project}");
        }

        var baselineFiles = FileSystem.EnumerateProjectFiles(baselineRoot)
            .ToDictionary(file => Path.GetRelativePath(baselineRoot, file), StringComparer.OrdinalIgnoreCase);
        var sourceFiles = FileSystem.EnumerateProjectFiles(sourceRoot)
            .ToDictionary(file => Path.GetRelativePath(sourceRoot, file), StringComparer.OrdinalIgnoreCase);
        var existingOverlayRoot = Path.Combine(paths.Overlay, project);
        var ownedPaths = Directory.Exists(existingOverlayRoot)
            ? FileSystem.EnumerateProjectFiles(existingOverlayRoot)
                .Select(file => Path.GetRelativePath(existingOverlayRoot, file))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in baselineFiles.Keys.Union(sourceFiles.Keys, StringComparer.OrdinalIgnoreCase).Order())
        {
            var hasBaseline = baselineFiles.TryGetValue(relative, out var baselineFile);
            var hasSource = sourceFiles.TryGetValue(relative, out var sourceFile);

            if (ownedPaths.Contains(relative) || !hasBaseline)
            {
                if (!hasSource) continue;
                var overlayFile = Path.Combine(paths.Overlay, project, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(overlayFile)!);
                File.Copy(sourceFile!, overlayFile, true);
                expectedOverlays.Add(Path.GetFullPath(overlayFile));
                Console.WriteLine($"  overlay/{project}/{ToSlash(relative)}");
                continue;
            }

            if (hasSource && FilesEqual(baselineFile!, sourceFile!)) continue;
            if (!FileSystem.IsTextFile(baselineFile!) || (hasSource && !FileSystem.IsTextFile(sourceFile!)))
            {
                throw new InvalidOperationException(
                    $"Modified or deleted baseline binary files are not supported: src/{project}/{ToSlash(relative)}");
            }

            var patchFile = Path.Combine(paths.Patches, project, relative + ".patch");
            var patch = await CreatePatchAsync(project, relative, baselineFile!, sourceFile);
            FileSystem.WriteNormalizedText(patchFile, patch);
            expectedPatches.Add(Path.GetFullPath(patchFile));
            Console.WriteLine($"  patches/{project}/{ToSlash(relative)}.patch");
        }
    }

    private async Task<string> CreatePatchAsync(string project, string relative, string baselineFile, string? sourceFile)
    {
        var staging = Path.Combine(paths.Temporary, $"diff-{Guid.NewGuid():N}");
        var beforeRoot = Path.Combine(staging, "before");
        var afterRoot = Path.Combine(staging, "after");
        var beforeFile = Path.Combine(beforeRoot, relative);
        var afterFile = Path.Combine(afterRoot, relative);
        try
        {
            FileSystem.WriteNormalizedText(beforeFile, File.ReadAllText(baselineFile));
            Directory.CreateDirectory(afterRoot);
            if (sourceFile is not null)
            {
                FileSystem.WriteNormalizedText(afterFile, File.ReadAllText(sourceFile));
            }

            var result = await ProcessRunner.CaptureAsync(
                "git",
                ["--no-pager", "-c", "core.safecrlf=false", "diff", "--no-color", "--no-index", "-U5", "--", beforeRoot, afterRoot],
                paths.Root,
                1);
            return RewritePatchHeaders(result.StandardOutput, project, relative, sourceFile is not null);
        }
        finally
        {
            if (Directory.Exists(staging)) paths.DeleteGeneratedDirectory(staging);
        }
    }

    private static string RewritePatchHeaders(string patch, string project, string relative, bool hasSource)
    {
        var path = $"src/{project}/{ToSlash(relative)}";
        var output = new StringBuilder();
        foreach (var line in FileSystem.NormalizeText(patch).Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                output.Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n');
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                output.Append("--- a/").Append(path).Append('\n');
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                output.Append(hasSource ? $"+++ b/{path}\n" : "+++ /dev/null\n");
            }
            else if (line.Length > 0)
            {
                output.Append(line).Append('\n');
            }
        }

        return output.ToString();
    }

    private static bool FilesEqual(string left, string right)
    {
        if (FileSystem.IsTextFile(left) && FileSystem.IsTextFile(right))
        {
            return FileSystem.NormalizeText(File.ReadAllText(left)) == FileSystem.NormalizeText(File.ReadAllText(right));
        }

        return File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
    }

    private static void RemoveStaleFiles(string root, HashSet<string> expected, string? extension)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (extension is not null && !file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (!expected.Contains(Path.GetFullPath(file))) File.Delete(file);
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }

    private static string ToSlash(string path) => path.Replace('\\', '/');
}
