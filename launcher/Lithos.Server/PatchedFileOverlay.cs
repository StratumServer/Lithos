using System.Reflection;

namespace Lithos.Server;

internal static class PatchedFileOverlay
{
    private const string RootPrefix = "Lithos.PatchedRoot.";
    private const string ModsPrefix = "Lithos.PatchedMods.";

    public static void Apply(string runtime)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(RootPrefix, StringComparison.Ordinal)
                || name.StartsWith(ModsPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
        {
            throw new InvalidDataException("The launcher does not contain Lithos server files.");
        }

        var written = 0;
        foreach (var resourceName in resources)
        {
            var modsFile = resourceName.StartsWith(ModsPrefix, StringComparison.Ordinal);
            var prefix = modsFile ? ModsPrefix : RootPrefix;
            var fileName = resourceName[prefix.Length..];
            var destination = modsFile
                ? Path.Combine(runtime, "Mods", fileName)
                : Path.Combine(runtime, fileName);
            using var resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Could not read embedded file {fileName}.");
            if (Matches(resource, destination)) continue;

            resource.Position = 0;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + $".lithos-{Guid.NewGuid():N}.tmp";
            try
            {
                using (var output = File.Create(temporary))
                {
                    resource.CopyTo(output);
                }

                File.Move(temporary, destination, overwrite: true);
                written++;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        if (written > 0)
        {
            Console.WriteLine($"Lithos: applied {written} server file(s)");
        }
    }

    private static bool Matches(Stream resource, string destination)
    {
        if (!File.Exists(destination)) return false;

        using var file = File.OpenRead(destination);
        if (file.Length != resource.Length) return false;

        Span<byte> resourceBuffer = stackalloc byte[8192];
        Span<byte> fileBuffer = stackalloc byte[8192];
        while (true)
        {
            var resourceRead = resource.Read(resourceBuffer);
            var fileRead = file.Read(fileBuffer);
            if (resourceRead != fileRead) return false;
            if (resourceRead == 0) return true;
            if (!resourceBuffer[..resourceRead].SequenceEqual(fileBuffer[..fileRead])) return false;
        }
    }
}
