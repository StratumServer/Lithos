namespace Lithos.Tool;

internal sealed class RepositoryPaths
{
    private RepositoryPaths(string root)
    {
        Root = root;
        ManifestFile = Path.Combine(root, "forks.json");
        ToolManifestFile = Path.Combine(root, ".config", "dotnet-tools.json");
        Source = Path.Combine(root, "src");
        Patches = Path.Combine(root, "patches");
        Overlay = Path.Combine(root, "overlay");
        Workspace = Path.Combine(root, ".lithos");
        BaselineSource = Path.Combine(Workspace, "baseline", "src");
        Downloads = Path.Combine(Workspace, "downloads");
        Vanilla = Path.Combine(Workspace, "vanilla");
        Temporary = Path.Combine(Workspace, "tmp");
        StateFile = Path.Combine(Workspace, "state.json");
    }

    public string Root { get; }
    public string ManifestFile { get; }
    public string ToolManifestFile { get; }
    public string Source { get; }
    public string Patches { get; }
    public string Overlay { get; }
    public string Workspace { get; }
    public string BaselineSource { get; }
    public string Downloads { get; }
    public string Vanilla { get; }
    public string Temporary { get; }
    public string StateFile { get; }

    public static RepositoryPaths Find()
    {
        var candidates = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(Path.GetFullPath(candidate));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "forks.json"))
                    && File.Exists(Path.Combine(current.FullName, "global.json")))
                {
                    return new RepositoryPaths(current.FullName);
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("Could not find the Lithos repository root.");
    }

    public void DeleteGeneratedDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var allowedRoots = new[] { Source, Workspace };
        if (!allowedRoots.Any(allowed => IsWithin(fullPath, allowed)))
        {
            throw new InvalidOperationException($"Refusing to delete a path outside generated roots: {fullPath}");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, true);
        }
    }

    private static bool IsWithin(string path, string allowedRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
