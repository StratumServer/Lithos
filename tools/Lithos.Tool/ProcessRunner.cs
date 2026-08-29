using System.Diagnostics;
using System.Text;

namespace Lithos.Tool;

internal static class ProcessRunner
{
    public static async Task RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        var result = await RunCoreAsync(fileName, arguments, workingDirectory, captureOutput: true);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with code {result.ExitCode}.{Environment.NewLine}"
                + result.StandardOutput
                + result.StandardError);
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput)) Console.Write(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError)) Console.Error.Write(result.StandardError);
    }

    public static async Task<ProcessResult> CaptureAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, params int[] allowedExitCodes)
    {
        var result = await RunCoreAsync(fileName, arguments, workingDirectory, captureOutput: true);
        var allowed = allowedExitCodes.Length == 0 ? [0] : allowedExitCodes;
        if (!allowed.Contains(result.ExitCode))
        {
            throw new InvalidOperationException(
                $"{fileName} exited with code {result.ExitCode}.{Environment.NewLine}{result.StandardError}");
        }

        return result;
    }

    private static async Task<ProcessResult> RunCoreAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, bool captureOutput)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {fileName}.");
        }

        if (!captureOutput)
        {
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, string.Empty, string.Empty);
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
