using System.Diagnostics;

namespace Lithos.Server;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var release = ReleaseInfo.Load();
            if (HasOption(args, "--lithos-version"))
            {
                Console.WriteLine(release.FullName);
                return 0;
            }

            if (HasOption(args, "--lithos-help") || HasOption(args, "--help") || HasOption(args, "-h"))
            {
                PrintHelp(release);
                return 0;
            }

            var refresh = HasOption(args, "--lithos-refresh");
            var prepareOnly = HasOption(args, "--lithos-prepare-only");
            var serverArgs = RemoveLauncherOptions(args);
            serverArgs = AddDefaultDataPath(serverArgs, out var dataPath);

            Console.WriteLine($"Starting {release.FullName}");
            if (dataPath is not null) Console.WriteLine($"Data: {dataPath}");

            var runtime = await new VanillaRuntime(release).PrepareAsync(refresh);
            PatchedFileOverlay.Apply(runtime);
            if (prepareOnly)
            {
                Console.WriteLine("Lithos: server runtime is ready");
                return 0;
            }

            return await LaunchServerAsync(runtime, serverArgs);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lithos: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> LaunchServerAsync(string runtime, string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = runtime,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add(Path.Combine(runtime, "VintagestoryServer.dll"));
        foreach (var argument in args)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the Vintage Story server process.");
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static string[] AddDefaultDataPath(string[] args, out string? dataPath)
    {
        dataPath = null;
        if (args.Any(argument => argument.Equals("--dataPath", StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith("--dataPath=", StringComparison.OrdinalIgnoreCase)))
        {
            return args;
        }

        dataPath = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataPath);
        return [.. args, "--dataPath", dataPath];
    }

    private static bool HasOption(string[] args, string option)
    {
        return args.Any(argument => argument.Equals(option, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] RemoveLauncherOptions(string[] args)
    {
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--lithos-refresh",
            "--lithos-prepare-only"
        };
        var serverArgs = new List<string>();
        foreach (var argument in args)
        {
            if (options.Contains(argument)) continue;
            if (argument.StartsWith("--lithos-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown Lithos option: {argument}");
            }

            serverArgs.Add(argument);
        }

        return [.. serverArgs];
    }

    private static void PrintHelp(ReleaseInfo release)
    {
        Console.WriteLine(release.FullName);
        Console.WriteLine();
        Console.WriteLine("Lithos options:");
        Console.WriteLine("  --lithos-version        Print version information and exit");
        Console.WriteLine("  --lithos-help           Print launcher options and exit");
        Console.WriteLine("  --lithos-refresh        Rebuild the official server runtime");
        Console.WriteLine("  --lithos-prepare-only   Prepare the server runtime and exit");
        Console.WriteLine();
        Console.WriteLine("Other arguments are passed to the Vintage Story server.");
    }
}
