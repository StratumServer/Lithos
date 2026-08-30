using System.Reflection;
using System.Text.Json;

namespace Lithos.Server;

internal sealed record ReleaseInfo(string LithosVersion, string VintageStoryVersion)
{
    private const string ManifestResourceName = "Lithos.forks.json";

    public string FullName => $"Lithos {LithosVersion} for Vintage Story {VintageStoryVersion}";

    public static ReleaseInfo Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var lithosVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(lithosVersion))
        {
            throw new InvalidDataException("The launcher does not contain a Lithos version.");
        }

        using var stream = assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidDataException("The launcher does not contain forks.json.");
        using var document = JsonDocument.Parse(stream);
        var vintageStoryVersion = document.RootElement.GetProperty("vintageStoryVersion").GetString();
        if (string.IsNullOrWhiteSpace(vintageStoryVersion))
        {
            throw new InvalidDataException("The launcher does not contain a Vintage Story version.");
        }

        return new ReleaseInfo(lithosVersion, vintageStoryVersion);
    }
}
