using Vintagestory.API.Common;

namespace Lithos.Benchmarks;

internal sealed class RegistryCodePartBenchmark : IBenchmarkCase
{
    private static readonly string[] ValidationPaths =
    [
        "",
        "stone",
        "-stone",
        "stone-",
        "stone--polished",
        "-",
        "--",
        "rock-granite-polished-north",
        "a-b-c-d-e"
    ];

    private readonly BenchmarkRegistryObject longCode = Create("rock-granite-polished-north");
    private readonly BenchmarkRegistryObject mediumCode = Create("ore-poor-copper-basalt");
    private readonly BenchmarkRegistryObject emptyPartsCode = Create("stone--polished-");
    private readonly BenchmarkRegistryObject singlePartCode = Create("granite");

    public string Name => "registry-code-parts";

    public string Description => "Reads first and last asset-code segments across common path shapes.";

    public int OperationsPerIteration => 8;

    public void Validate()
    {
        var registryObject = new BenchmarkRegistryObject();
        Ensure(registryObject.FirstCodePart() == null, "a null code returned a first part");
        Ensure(registryObject.LastCodePart() == null, "a null code returned a last part");

        foreach (string path in ValidationPaths)
        {
            registryObject.Code = new AssetLocation("game", path);
            for (var position = -2; position <= path.Length + 2; position++)
            {
                Compare(
                    $"FirstCodePart({position}) for '{path}'",
                    () => ReferenceFirstCodePart(path, position),
                    () => registryObject.FirstCodePart(position));
                Compare(
                    $"LastCodePart({position}) for '{path}'",
                    () => ReferenceLastCodePart(path, position),
                    () => registryObject.LastCodePart(position));
            }

            Compare(
                $"FirstCodePart({int.MinValue}) for '{path}'",
                () => ReferenceFirstCodePart(path, int.MinValue),
                () => registryObject.FirstCodePart(int.MinValue));
            Compare(
                $"LastCodePart({int.MinValue}) for '{path}'",
                () => ReferenceLastCodePart(path, int.MinValue),
                () => registryObject.LastCodePart(int.MinValue));
            Compare(
                $"FirstCodePart({int.MaxValue}) for '{path}'",
                () => ReferenceFirstCodePart(path, int.MaxValue),
                () => registryObject.FirstCodePart(int.MaxValue));
            Compare(
                $"LastCodePart({int.MaxValue}) for '{path}'",
                () => ReferenceLastCodePart(path, int.MaxValue),
                () => registryObject.LastCodePart(int.MaxValue));

            if (!path.Contains('-'))
            {
                Ensure(ReferenceEquals(registryObject.Code.Path, registryObject.FirstCodePart()), "single first part was copied");
                Ensure(ReferenceEquals(registryObject.Code.Path, registryObject.LastCodePart()), "single last part was copied");
            }
        }

        Ensure(Run(1) == 41, "measurement workload checksum changed");
    }

    public int Run(int iterations)
    {
        var checksum = 0;
        for (var index = 0; index < iterations; index++)
        {
            checksum += longCode.FirstCodePart().Length;
            checksum += longCode.FirstCodePart(2).Length;
            checksum += longCode.LastCodePart().Length;
            checksum += longCode.LastCodePart(2).Length;
            checksum += mediumCode.FirstCodePart(1).Length;
            checksum += mediumCode.LastCodePart(1).Length;
            checksum += emptyPartsCode.FirstCodePart(1).Length;
            checksum += singlePartCode.LastCodePart().Length;
        }

        return checksum;
    }

    private static BenchmarkRegistryObject Create(string path)
    {
        return new BenchmarkRegistryObject
        {
            Code = new AssetLocation("game", path)
        };
    }

    private static string? ReferenceFirstCodePart(string path, int position)
    {
        string[] parts = path.Split('-');
        return position <= parts.Length - 1 ? parts[position] : null;
    }

    private static string? ReferenceLastCodePart(string path, int position)
    {
        string[] parts = path.Split('-');
        int index = parts.Length - 1 - position;
        return index >= 0 ? parts[index] : null;
    }

    private void Compare(string operation, Func<string?> expectedCall, Func<string?> actualCall)
    {
        (string? value, Type? exceptionType) expected = Invoke(expectedCall);
        (string? value, Type? exceptionType) actual = Invoke(actualCall);
        Ensure(expected.exceptionType == actual.exceptionType, $"{operation} changed its exception");
        Ensure(expected.value == actual.value, $"{operation} returned '{actual.value}' instead of '{expected.value}'");
    }

    private static (string? value, Type? exceptionType) Invoke(Func<string?> call)
    {
        try
        {
            return (call(), null);
        }
        catch (Exception exception)
        {
            return (null, exception.GetType());
        }
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private sealed class BenchmarkRegistryObject : RegistryObject
    {
    }
}
