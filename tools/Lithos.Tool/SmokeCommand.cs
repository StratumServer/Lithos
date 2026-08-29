using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Lithos.Tool;

internal sealed class SmokeCommand(RepositoryPaths paths)
{
    private const string Configuration = "Release";
    private static readonly TimeSpan StabilityPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex RunPhasePattern = new(
        @"Entering runphase (?<phase>\S+)",
        RegexOptions.CultureInvariant);

    public async Task RunAsync(SmokeOptions options)
    {
        RequireBootstrap();

        if (options.Build)
        {
            Console.WriteLine("Building Lithos...");
            await ProcessRunner.RunAsync(
                "dotnet",
                ["build", "Lithos.slnx", "-c", Configuration],
                paths.Root);
        }

        var dataPath = PrepareDataPath(options, out var generatedDataPath);
        var runtime = Path.Combine(paths.Workspace, "smoke", "runtime");
        var succeeded = false;
        var serverAttempted = false;

        try
        {
            PrepareRuntime(runtime);
            var port = options.Port == 0 ? FindAvailablePort() : options.Port;
            Console.WriteLine($"Starting smoke server on 127.0.0.1:{port}");
            Console.WriteLine($"Runtime: {runtime}");
            Console.WriteLine($"Data: {dataPath}");
            serverAttempted = true;
            await RunServerAsync(runtime, dataPath, port, TimeSpan.FromSeconds(options.PatienceSeconds));
            succeeded = true;
            Console.WriteLine("Smoke test passed: the server reached RunGame and shut down cleanly.");
        }
        finally
        {
            try
            {
                paths.DeleteGeneratedDirectory(runtime);
            }
            finally
            {
                if (generatedDataPath && !options.KeepData && (succeeded || !serverAttempted))
                {
                    paths.DeleteGeneratedDirectory(dataPath);
                }
                else
                {
                    Console.WriteLine($"Smoke data preserved at {dataPath}");
                }
            }
        }
    }

    private void RequireBootstrap()
    {
        if (!Directory.Exists(paths.Source)
            || !File.Exists(Path.Combine(paths.Vanilla, "VintagestoryServer.dll"))
            || !Directory.Exists(Path.Combine(paths.Vanilla, "assets")))
        {
            throw new InvalidOperationException("Run bootstrap before the smoke test.");
        }
    }

    private void PrepareRuntime(string runtime)
    {
        var serverOutput = Path.Combine(paths.Source, "VintagestoryServer", "bin", Configuration, "net10.0");
        var serverAssembly = Path.Combine(serverOutput, "VintagestoryServer.dll");
        if (!File.Exists(serverAssembly))
        {
            throw new InvalidOperationException(
                $"Server build output is missing: {serverAssembly}. Run without --no-build.");
        }

        paths.DeleteGeneratedDirectory(runtime);
        Console.WriteLine("Staging smoke runtime...");
        FileSystem.CopyDirectory(paths.Vanilla, runtime);
        FileSystem.CopyDirectory(serverOutput, runtime);
        OverlayBuiltMods(runtime);
    }

    private void OverlayBuiltMods(string runtime)
    {
        var modOutput = Path.Combine(paths.Source, "bin", Configuration, "net10.0");
        var runtimeMods = Path.Combine(runtime, "Mods");
        if (!Directory.Exists(modOutput) || !Directory.Exists(runtimeMods))
        {
            throw new InvalidOperationException("Built or staged vanilla mods are missing.");
        }

        foreach (var runtimeMod in Directory.EnumerateFiles(runtimeMods, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(runtimeMod);
            var builtMod = Path.Combine(modOutput, name);
            if (!File.Exists(builtMod)) continue;

            File.Copy(builtMod, runtimeMod, true);
            var builtSymbols = Path.ChangeExtension(builtMod, ".pdb");
            if (File.Exists(builtSymbols))
            {
                File.Copy(builtSymbols, Path.ChangeExtension(runtimeMod, ".pdb"), true);
            }
        }
    }

    private string PrepareDataPath(SmokeOptions options, out bool generated)
    {
        generated = options.DataPath is null;
        var dataPath = generated
            ? Path.Combine(paths.Temporary, $"smoke-data-{Guid.NewGuid():N}")
            : Path.GetFullPath(options.DataPath!, paths.Root);

        if (Directory.Exists(dataPath) && Directory.EnumerateFileSystemEntries(dataPath).Any())
        {
            throw new InvalidOperationException($"Smoke data path must be empty: {dataPath}");
        }

        Directory.CreateDirectory(dataPath);
        return dataPath;
    }

    private static int FindAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RunServerAsync(
        string runtime,
        string dataPath,
        int port,
        TimeSpan patience)
    {
        var logFile = Path.Combine(dataPath, "smoke-process.log");
        using var log = new StreamWriter(logFile, append: false) { AutoFlush = true };
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = runtime,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add(Path.Combine(runtime, "VintagestoryServer.dll"));
        process.StartInfo.ArgumentList.Add("--dataPath");
        process.StartInfo.ArgumentList.Add(dataPath);
        process.StartInfo.ArgumentList.Add("--ip");
        process.StartInfo.ArgumentList.Add(IPAddress.Loopback.ToString());
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add(port.ToString());

        var stateLock = new object();
        var lastOutput = DateTimeOffset.UtcNow;
        var lastPhase = "starting";
        DateTimeOffset? reachedRunGameAt = null;
        string? fatalLine = null;

        void RecordOutput(string stream, string? line)
        {
            if (line is null) return;

            lock (stateLock)
            {
                log.WriteLine(stream == "stdout" ? line : $"[stderr] {line}");
                lastOutput = DateTimeOffset.UtcNow;

                var match = RunPhasePattern.Match(line);
                if (match.Success)
                {
                    var phase = match.Groups["phase"].Value;
                    if (!phase.Equals(lastPhase, StringComparison.Ordinal))
                    {
                        lastPhase = phase;
                        Console.WriteLine($"  reached {phase}");
                    }

                    if (phase.Equals("RunGame", StringComparison.Ordinal))
                    {
                        reachedRunGameAt ??= DateTimeOffset.UtcNow;
                    }
                }

                if (line.Contains("[Server Fatal]", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("[Fatal]", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase))
                {
                    fatalLine ??= line;
                }
            }
        }

        process.OutputDataReceived += (_, eventArgs) => RecordOutput("stdout", eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => RecordOutput("stderr", eventArgs.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the smoke server.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var passed = false;
        string? failure = null;

        try
        {
            while (true)
            {
                DateTimeOffset outputAt;
                DateTimeOffset? runGameAt;
                string phase;
                string? fatal;
                lock (stateLock)
                {
                    outputAt = lastOutput;
                    runGameAt = reachedRunGameAt;
                    phase = lastPhase;
                    fatal = fatalLine;
                }

                if (fatal is not null)
                {
                    failure = $"Server reported a fatal error during {phase}: {fatal}";
                    break;
                }

                if (process.HasExited)
                {
                    failure = $"Server exited with code {process.ExitCode} during {phase}.";
                    break;
                }

                var now = DateTimeOffset.UtcNow;
                if (runGameAt is not null && now - runGameAt >= StabilityPeriod)
                {
                    passed = true;
                    break;
                }

                if (now - outputAt >= patience)
                {
                    failure = $"Server produced no output for {patience.TotalSeconds:0} seconds during {phase}.";
                    break;
                }

                await Task.Delay(250);
            }
        }
        finally
        {
            await StopServerAsync(process, passed ? GracefulStopTimeout : TimeSpan.FromSeconds(5));
        }

        if (!passed)
        {
            throw new InvalidOperationException($"{failure} Process log: {logFile}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Server reached RunGame but exited with code {process.ExitCode}. Process log: {logFile}");
        }
    }

    private static async Task StopServerAsync(Process process, TimeSpan timeout)
    {
        if (process.HasExited) return;

        try
        {
            await process.StandardInput.WriteLineAsync("/stop");
            await process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(timeout)) != exitTask && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }
        }

        await process.WaitForExitAsync();
    }
}

internal sealed record SmokeOptions(
    string? DataPath,
    int Port,
    int PatienceSeconds,
    bool Build,
    bool KeepData);
