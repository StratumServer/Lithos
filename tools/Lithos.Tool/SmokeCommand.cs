using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace Lithos.Tool;

internal sealed class SmokeCommand(RepositoryPaths paths)
{
    private static readonly TimeSpan StabilityPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex RunPhasePattern = new(
        @"Entering runphase (?<phase>\S+)",
        RegexOptions.CultureInvariant);

    public async Task RunAsync(SmokeOptions options)
    {
        var serverRuntime = new ServerRuntime(paths);
        serverRuntime.RequireBootstrap("smoke");

        if (options.Build)
        {
            await serverRuntime.BuildAsync();
        }

        var dataPath = PrepareDataPath(options, out var generatedDataPath);
        var runtime = Path.Combine(paths.Workspace, "smoke", "runtime");
        var succeeded = false;
        var serverAttempted = false;

        try
        {
            serverRuntime.Stage(runtime, "smoke");
            var port = options.Port == 0 ? ServerRuntime.FindAvailablePort(IPAddress.Loopback) : options.Port;
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

    private static async Task RunServerAsync(
        string runtime,
        string dataPath,
        int port,
        TimeSpan patience)
    {
        var logFile = Path.Combine(dataPath, "smoke-process.log");
        using var log = new StreamWriter(logFile, append: false) { AutoFlush = true };
        using var process = ServerRuntime.CreateProcess(
            runtime,
            dataPath,
            IPAddress.Loopback,
            port,
            redirectOutput: true);

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
            await ServerRuntime.StopAsync(process, passed ? GracefulStopTimeout : TimeSpan.FromSeconds(5));
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

}

internal sealed record SmokeOptions(
    string? DataPath,
    int Port,
    int PatienceSeconds,
    bool Build,
    bool KeepData);
