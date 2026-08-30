using System.Net;

namespace Lithos.Tool;

internal sealed class ServerCommand(RepositoryPaths paths)
{
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(30);

    public async Task RunAsync(ServerOptions options)
    {
        var serverRuntime = new ServerRuntime(paths);
        serverRuntime.RequireBootstrap("server");

        if (options.Build)
        {
            await serverRuntime.BuildAsync();
        }

        var address = IPAddress.Parse(options.Address);
        var port = options.Port == 0 ? ServerRuntime.FindAvailablePort(address) : options.Port;
        var runtime = Path.Combine(paths.Workspace, "server", "runtime");
        var dataPath = options.DataPath is null
            ? Path.Combine(paths.Workspace, "server", "data")
            : Path.GetFullPath(options.DataPath, paths.Root);
        if (IsWithin(dataPath, runtime))
        {
            throw new InvalidOperationException("The server data path cannot be inside the generated runtime directory.");
        }
        Directory.CreateDirectory(dataPath);

        try
        {
            serverRuntime.Stage(runtime, "playtest");
            Console.WriteLine($"Starting Lithos server on {address}:{port}");
            Console.WriteLine($"Runtime: {runtime}");
            Console.WriteLine($"Data: {dataPath}");
            Console.WriteLine("Press Ctrl+C to stop the server cleanly.");
            await RunServerAsync(runtime, dataPath, address, port);
        }
        finally
        {
            paths.DeleteGeneratedDirectory(runtime);
        }
    }

    private static async Task RunServerAsync(string runtime, string dataPath, IPAddress address, int port)
    {
        using var process = ServerRuntime.CreateProcess(runtime, dataPath, address, port, redirectOutput: true);
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null) Console.WriteLine(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null) Console.Error.WriteLine(eventArgs.Data);
        };

        void RequestStop(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            stopRequested.TrySetResult();
        }

        Console.CancelKeyPress += RequestStop;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start the Lithos server.");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exitTask = process.WaitForExitAsync();
            if (await Task.WhenAny(exitTask, stopRequested.Task) == stopRequested.Task)
            {
                Console.WriteLine("Stopping Lithos server...");
                await ServerRuntime.StopAsync(process, GracefulStopTimeout);
            }
            else if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Lithos server exited with code {process.ExitCode}.");
            }
        }
        finally
        {
            Console.CancelKeyPress -= RequestStop;
            await ServerRuntime.StopAsync(process, GracefulStopTimeout);
        }
    }

    private static bool IsWithin(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record ServerOptions(
    string? DataPath,
    string Address,
    int Port,
    bool Build);
