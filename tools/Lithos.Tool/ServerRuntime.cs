using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Lithos.Tool;

internal sealed class ServerRuntime(RepositoryPaths paths)
{
    private const string Configuration = "Release";

    public void RequireBootstrap(string command)
    {
        if (!Directory.Exists(paths.Source)
            || !File.Exists(Path.Combine(paths.Vanilla, "VintagestoryServer.dll"))
            || !Directory.Exists(Path.Combine(paths.Vanilla, "assets")))
        {
            throw new InvalidOperationException($"Run bootstrap before the {command} command.");
        }
    }

    public async Task BuildAsync()
    {
        Console.WriteLine("Building Lithos...");
        await ProcessRunner.RunAsync(
            "dotnet",
            ["build", "Lithos.slnx", "-c", Configuration],
            paths.Root);
    }

    public void Stage(string runtime, string purpose)
    {
        var serverOutput = Path.Combine(paths.Source, "VintagestoryServer", "bin", Configuration, "net10.0");
        var serverAssembly = Path.Combine(serverOutput, "VintagestoryServer.dll");
        if (!File.Exists(serverAssembly))
        {
            throw new InvalidOperationException(
                $"Server build output is missing: {serverAssembly}. Run without --no-build.");
        }

        paths.DeleteGeneratedDirectory(runtime);
        Console.WriteLine($"Staging {purpose} runtime...");
        FileSystem.CopyDirectory(paths.Vanilla, runtime);
        FileSystem.CopyDirectory(serverOutput, runtime);
        OverlayBuiltMods(runtime);
    }

    public static Process CreateProcess(
        string runtime,
        string dataPath,
        IPAddress address,
        int port,
        bool redirectOutput)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = runtime,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = redirectOutput,
                RedirectStandardError = redirectOutput
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add(Path.Combine(runtime, "VintagestoryServer.dll"));
        process.StartInfo.ArgumentList.Add("--dataPath");
        process.StartInfo.ArgumentList.Add(dataPath);
        process.StartInfo.ArgumentList.Add("--ip");
        process.StartInfo.ArgumentList.Add(address.ToString());
        process.StartInfo.ArgumentList.Add("--port");
        process.StartInfo.ArgumentList.Add(port.ToString());
        return process;
    }

    public static int FindAvailablePort(IPAddress address)
    {
        var listener = new TcpListener(address, 0);
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

    public static async Task StopAsync(Process process, TimeSpan timeout)
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
}
