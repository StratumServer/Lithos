using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lithos.Runtime;

internal static class NativeLibrarySearchPath
{
    public static void Apply(ProcessStartInfo startInfo, string runtime)
    {
        var variable = OperatingSystem.IsWindows()
            ? "PATH"
            : OperatingSystem.IsMacOS() ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var baseDirectory = Path.GetFullPath(runtime);
        var candidates = new[]
        {
            baseDirectory,
            Path.Combine(baseDirectory, "Lib"),
            Path.Combine(baseDirectory, "runtimes", GetRuntimeIdentifier(), "native")
        };
        startInfo.Environment.TryGetValue(variable, out var existing);
        var paths = candidates
            .Where(Directory.Exists)
            .Concat((existing ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Distinct(comparer);

        startInfo.Environment[variable] = string.Join(Path.PathSeparator, paths);
    }

    private static string GetRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{architecture}";
    }
}
