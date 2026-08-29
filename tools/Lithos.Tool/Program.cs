namespace Lithos.Tool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var paths = RepositoryPaths.Find();
            var manifest = RepositoryManifest.Load(paths);
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp(manifest);
                return 0;
            }

            switch (args[0])
            {
                case "bootstrap":
                    await new BootstrapCommand(paths, manifest).RunAsync(ParseBootstrap(args[1..]));
                    return 0;
                case "capture":
                    RequireNoArguments(args[1..], "capture");
                    await new CaptureCommand(paths, manifest).RunAsync();
                    return 0;
                case "doctor":
                    RequireNoArguments(args[1..], "doctor");
                    await RunDoctorAsync(paths, manifest);
                    return 0;
                default:
                    throw new ArgumentException($"Unknown command: {args[0]}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static BootstrapOptions ParseBootstrap(string[] args)
    {
        string? serverArchive = null;
        string? clientArchive = null;
        string? repositoryCache = null;
        var refresh = false;
        var force = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--server-archive":
                    serverArchive = ReadValue(args, ref index);
                    break;
                case "--client-archive":
                    clientArchive = ReadValue(args, ref index);
                    break;
                case "--repository-cache":
                    repositoryCache = ReadValue(args, ref index);
                    break;
                case "--refresh":
                    refresh = true;
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown bootstrap option: {args[index]}");
            }
        }

        if (force && !refresh)
        {
            throw new ArgumentException("--force is only valid together with --refresh.");
        }

        return new BootstrapOptions(serverArchive, clientArchive, repositoryCache, refresh, force);
    }

    private static string ReadValue(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length || args[index].StartsWith('-'))
        {
            throw new ArgumentException($"{args[index - 1]} requires a value.");
        }

        return args[index];
    }

    private static void RequireNoArguments(string[] args, string command)
    {
        if (args.Length != 0) throw new ArgumentException($"{command} does not accept arguments.");
    }

    private static async Task RunDoctorAsync(RepositoryPaths paths, RepositoryManifest manifest)
    {
        var dotnet = await ProcessRunner.CaptureAsync("dotnet", ["--version"], paths.Root);
        var git = await ProcessRunner.CaptureAsync("git", ["--version"], paths.Root);
        Console.WriteLine($"Repository: {paths.Root}");
        Console.WriteLine($"Vintage Story: {manifest.VintageStoryVersion}");
        Console.WriteLine($".NET SDK: {dotnet.StandardOutput.Trim()}");
        Console.WriteLine($"Git: {git.StandardOutput.Trim()}");
        Console.WriteLine($"Source tree: {(Directory.Exists(paths.Source) ? "present" : "not bootstrapped")}");
        Console.WriteLine($"Baseline: {(Directory.Exists(paths.BaselineSource) ? "present" : "not bootstrapped")}");
    }

    private static void PrintHelp(RepositoryManifest manifest)
    {
        Console.WriteLine($"Lithos repository tool for Vintage Story {manifest.VintageStoryVersion}");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/Lithos.Tool -- <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  bootstrap   Reconstruct every project as a peer under src/");
        Console.WriteLine("  capture     Store src/ changes in patches/ and overlay/");
        Console.WriteLine("  doctor      Validate prerequisites and report repository state");
        Console.WriteLine();
        Console.WriteLine("Bootstrap options:");
        Console.WriteLine("  --server-archive PATH   Reuse an official server zip or tarball");
        Console.WriteLine("  --client-archive PATH   Reuse an official client zip or tarball for missing build references");
        Console.WriteLine("  --repository-cache DIR  Reuse checked-out upstream repositories at the pinned commits");
        Console.WriteLine("  --refresh --force       Rebuild generated baseline and src/ after capturing local work");
    }
}
