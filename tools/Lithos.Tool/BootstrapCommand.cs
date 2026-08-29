using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lithos.Tool;

internal sealed class BootstrapCommand(RepositoryPaths paths, RepositoryManifest manifest)
{
    private readonly string fingerprint = RepositoryManifest.GetBaselineFingerprint(paths);

    public async Task RunAsync(BootstrapOptions options)
    {
        var state = LoadState();
        if (!options.Refresh && IsReady(state))
        {
            Console.WriteLine($"Lithos source is ready in {paths.Source}");
            return;
        }

        if (Directory.Exists(paths.Source) && !options.Force)
        {
            throw new InvalidOperationException(
                "src/ already exists but does not match the pinned baseline. Capture any work, then use --refresh --force to replace it.");
        }

        if (options.Refresh && !options.Force && (Directory.Exists(paths.Source) || Directory.Exists(paths.BaselineSource)))
        {
            throw new InvalidOperationException("Refresh replaces generated source. Rerun with --refresh --force after capturing any work.");
        }

        if (options.Refresh)
        {
            ResetGeneratedState();
            state = null;
        }

        Directory.CreateDirectory(paths.Workspace);
        Directory.CreateDirectory(paths.Downloads);
        Directory.CreateDirectory(paths.Temporary);

        var archiveHashes = await PrepareVanillaAsync(options, state);
        await PrepareDecompiledProjectsAsync();
        await PrepareOpenSourceProjectsAsync(options.RepositoryCache);
        await MaterializeSourceAsync(options.Force);

        var newState = new BootstrapState
        {
            VintageStoryVersion = manifest.VintageStoryVersion,
            BaselineFingerprint = fingerprint,
            ServerArchiveSha256 = archiveHashes.Server,
            ClientArchiveSha256 = archiveHashes.Client,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(newState, new JsonSerializerOptions { WriteIndented = true });
        FileSystem.WriteNormalizedText(paths.StateFile, json + "\n");

        Console.WriteLine();
        Console.WriteLine("Bootstrap complete.");
        Console.WriteLine("Build with: dotnet build Lithos.slnx -c Release");
    }

    private bool IsReady(BootstrapState? state)
    {
        return state is not null
            && state.VintageStoryVersion == manifest.VintageStoryVersion
            && state.BaselineFingerprint == fingerprint
            && manifest.Projects.All(project => Directory.Exists(Path.Combine(paths.Source, project)))
            && manifest.Projects.All(project => Directory.Exists(Path.Combine(paths.BaselineSource, project)))
            && File.Exists(Path.Combine(paths.Vanilla, "VintagestoryLib.dll"));
    }

    private BootstrapState? LoadState()
    {
        if (!File.Exists(paths.StateFile)) return null;

        return JsonSerializer.Deserialize<BootstrapState>(
            File.ReadAllText(paths.StateFile),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private void ResetGeneratedState()
    {
        paths.DeleteGeneratedDirectory(paths.Source);
        paths.DeleteGeneratedDirectory(paths.BaselineSource);
        paths.DeleteGeneratedDirectory(paths.Vanilla);
        paths.DeleteGeneratedDirectory(paths.Temporary);
        if (File.Exists(paths.StateFile)) File.Delete(paths.StateFile);
    }

    private async Task<ArchiveHashes> PrepareVanillaAsync(BootstrapOptions options, BootstrapState? state)
    {
        var releaseClient = new ReleaseManifestClient();
        string? serverArchive = options.ServerArchive;
        string serverHash;

        if (!Directory.Exists(paths.Vanilla) || !File.Exists(Path.Combine(paths.Vanilla, "VintagestoryLib.dll")))
        {
            serverArchive = await ResolveServerArchiveAsync(serverArchive, releaseClient);
            Console.WriteLine($"Extracting {serverArchive}");
            await ArchiveExtractor.ExtractServerAsync(serverArchive, paths.Vanilla, paths);
            serverHash = await FileSystem.GetSha256Async(serverArchive);
        }
        else if (serverArchive is not null)
        {
            serverArchive = RequireArchive(serverArchive);
            serverHash = await FileSystem.GetSha256Async(serverArchive);
        }
        else
        {
            serverHash = state?.ServerArchiveSha256
                ?? await FileSystem.GetSha256Async(Path.Combine(paths.Vanilla, "VintagestoryLib.dll"));
        }

        var vanillaLib = Path.Combine(paths.Vanilla, "Lib");
        Directory.CreateDirectory(vanillaLib);
        var missingReferences = manifest.RequiredClientReferences
            .Where(reference => !File.Exists(Path.Combine(vanillaLib, reference)))
            .ToArray();
        string? clientHash = state?.ClientArchiveSha256;
        if (missingReferences.Length > 0)
        {
            var clientArchive = await ResolveClientArchiveAsync(options.ClientArchive, releaseClient);
            Console.WriteLine($"Reading required references from {clientArchive}");
            await ArchiveExtractor.ExtractRequiredFilesAsync(clientArchive, vanillaLib, missingReferences);
            clientHash = await FileSystem.GetSha256Async(clientArchive);
        }
        else if (options.ClientArchive is not null)
        {
            var clientArchive = RequireArchive(options.ClientArchive);
            clientHash = await FileSystem.GetSha256Async(clientArchive);
        }

        return new ArchiveHashes(serverHash, clientHash);
    }

    private async Task<string> ResolveServerArchiveAsync(string? suppliedArchive, ReleaseManifestClient client)
    {
        if (suppliedArchive is not null) return RequireArchive(suppliedArchive);

        var archive = await client.GetServerArchiveAsync(manifest.VintageStoryVersion);
        var destination = Path.Combine(paths.Downloads, archive.FileName);
        await client.DownloadAsync(archive, destination);
        return destination;
    }

    private async Task<string> ResolveClientArchiveAsync(string? suppliedArchive, ReleaseManifestClient client)
    {
        if (suppliedArchive is not null) return RequireArchive(suppliedArchive);

        var archive = await client.GetLinuxClientArchiveAsync(manifest.VintageStoryVersion);
        var destination = Path.Combine(paths.Downloads, archive.FileName);
        await client.DownloadAsync(archive, destination);
        return destination;
    }

    private static string RequireArchive(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Archive not found.", fullPath);
        return fullPath;
    }

    private async Task PrepareDecompiledProjectsAsync()
    {
        var missing = manifest.DecompiledProjects
            .Where(project => !Directory.Exists(Path.Combine(paths.BaselineSource, project.Name)))
            .ToArray();
        if (missing.Length == 0) return;

        var decompiler = await ResolveDecompilerAsync();
        foreach (var project in missing)
        {
            var assembly = Directory.EnumerateFiles(paths.Vanilla, project.Assembly, SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new FileNotFoundException($"{project.Assembly} was not found in the official server archive.");
            var staging = Path.Combine(paths.Temporary, $"decompile-{project.Name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            Console.WriteLine($"Decompiling {project.Assembly} into src/{project.Name}");
            var arguments = decompiler.IsDirect
                ? new[]
                {
                    assembly,
                    "--project",
                    "-o",
                    staging,
                    "--referencepath",
                    Path.Combine(paths.Vanilla, "Lib"),
                    "--disable-updatecheck"
                }
                : new[]
                {
                    "tool",
                    "run",
                    "ilspycmd",
                    "--",
                    assembly,
                    "--project",
                    "-o",
                    staging,
                    "--referencepath",
                    Path.Combine(paths.Vanilla, "Lib"),
                    "--disable-updatecheck"
                };
            await ProcessRunner.RunAsync(decompiler.Command, arguments, paths.Root);
            NormalizeDecompilerProjects(staging);
            FileSystem.NormalizeSourceTree(staging);

            var destination = Path.Combine(paths.BaselineSource, project.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
        }
    }

    private async Task<DecompilerCommand> ResolveDecompilerAsync()
    {
        var executable = OperatingSystem.IsWindows() ? "ilspycmd.exe" : "ilspycmd";
        var globalTool = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "tools",
            executable);
        if (File.Exists(globalTool))
        {
            Console.WriteLine($"Using installed ILSpy command-line tool at {globalTool}");
            return new DecompilerCommand(globalTool, IsDirect: true);
        }

        Console.WriteLine("Restoring the pinned ILSpy command-line tool");
        var result = await ProcessRunner.CaptureAsync("dotnet", ["tool", "restore"], paths.Root);
        if (!string.IsNullOrWhiteSpace(result.StandardOutput)) Console.WriteLine(result.StandardOutput.Trim());
        return new DecompilerCommand("dotnet", IsDirect: false);
    }

    private static void NormalizeDecompilerProjects(string root)
    {
        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            var content = File.ReadAllText(project);
            content = Regex.Replace(content, "<LangVersion>15\\.0</LangVersion>", "<LangVersion>latest</LangVersion>");
            File.WriteAllText(project, content);
        }
    }

    private async Task PrepareOpenSourceProjectsAsync(string? repositoryCache)
    {
        foreach (var repository in manifest.Forks)
        {
            var destination = Path.Combine(paths.BaselineSource, repository.Name);
            if (Directory.Exists(destination)) continue;

            var cached = repositoryCache is null
                ? null
                : await FindCachedRepositoryAsync(repositoryCache, repository.Ref);
            if (cached is not null)
            {
                Console.WriteLine($"Using cached {repository.Name} at {repository.Ref}");
                FileSystem.CopyDirectory(cached, destination, excludeRepositoryMetadata: true);
                FileSystem.NormalizeSourceTree(destination);
                continue;
            }

            var staging = Path.Combine(paths.Temporary, $"clone-{repository.Name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            Console.WriteLine($"Fetching {repository.Url} at {repository.Ref}");
            await ProcessRunner.RunAsync("git", ["init", "--quiet"], staging);
            await ProcessRunner.RunAsync("git", ["config", "core.autocrlf", "false"], staging);
            await ProcessRunner.RunAsync("git", ["remote", "add", "origin", repository.Url], staging);
            await ProcessRunner.RunAsync("git", ["fetch", "--quiet", "--depth", "1", "origin", repository.Ref], staging);
            await ProcessRunner.RunAsync("git", ["checkout", "--quiet", "--detach", "FETCH_HEAD"], staging);
            paths.DeleteGeneratedDirectory(Path.Combine(staging, ".git"));
            FileSystem.NormalizeSourceTree(staging);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
        }
    }

    private static async Task<string?> FindCachedRepositoryAsync(string cacheRoot, string expectedRef)
    {
        var fullRoot = Path.GetFullPath(cacheRoot);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException(fullRoot);

        foreach (var candidate in Directory.EnumerateDirectories(fullRoot))
        {
            if (!Directory.Exists(Path.Combine(candidate, ".git"))) continue;
            var result = await ProcessRunner.CaptureAsync("git", ["rev-parse", "HEAD"], candidate);
            var actual = result.StandardOutput.Trim();
            if (actual.StartsWith(expectedRef, StringComparison.OrdinalIgnoreCase)
                || expectedRef.StartsWith(actual, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task MaterializeSourceAsync(bool force)
    {
        var materializedRoot = Path.Combine(paths.Temporary, $"materialize-{Guid.NewGuid():N}");
        var materializedSource = Path.Combine(materializedRoot, "src");
        Directory.CreateDirectory(materializedSource);
        foreach (var project in manifest.Projects)
        {
            FileSystem.CopyDirectory(
                Path.Combine(paths.BaselineSource, project),
                Path.Combine(materializedSource, project));
        }

        if (Directory.Exists(paths.Patches))
        {
            var applyDirectory = Path.GetRelativePath(paths.Root, materializedRoot).Replace('\\', '/');
            foreach (var patch in Directory.EnumerateFiles(paths.Patches, "*.patch", SearchOption.AllDirectories).Order())
            {
                Console.WriteLine($"Applying {Path.GetRelativePath(paths.Root, patch)}");
                await ProcessRunner.RunAsync(
                    "git",
                    ["apply", "--whitespace=nowarn", $"--directory={applyDirectory}", patch],
                    paths.Root);
            }
        }

        if (Directory.Exists(paths.Overlay))
        {
            FileSystem.CopyDirectory(paths.Overlay, materializedSource);
        }

        if (Directory.Exists(paths.Source))
        {
            if (!force) throw new InvalidOperationException("Refusing to replace src/ without --force.");
            paths.DeleteGeneratedDirectory(paths.Source);
        }

        Directory.Move(materializedSource, paths.Source);
        paths.DeleteGeneratedDirectory(materializedRoot);
    }

    private sealed record ArchiveHashes(string Server, string? Client);
    private sealed record DecompilerCommand(string Command, bool IsDirect);
}

internal sealed record BootstrapOptions(
    string? ServerArchive,
    string? ClientArchive,
    string? RepositoryCache,
    bool Refresh,
    bool Force);
