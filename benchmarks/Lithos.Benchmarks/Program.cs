using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using Vintagestory.API.Common;

namespace Lithos.Benchmarks;

internal static class Program
{
    private const int DefaultIterations = 2_000_000;
    private const int DefaultSamples = 5;
    private const int MaximumWarmupIterations = 250_000;

    private static readonly IBenchmarkCase[] BenchmarkCases =
    [
        new ShapelessRecipeBenchmark(),
        new RegistryCodePartBenchmark(),
        new PacketBroadcastBenchmark(),
        new PathfindingCandidateBenchmark(),
        new RandomTickSliceBenchmark(),
        new EntityPartitionBenchmark(false),
        new EntityPartitionBenchmark(true),
        new EntityPacketGatherBenchmark(false),
        new EntityPacketGatherBenchmark(true),
        new EntityPositionBatchBenchmark(false),
        new EntityPositionBatchBenchmark(true)
    ];

    public static int Main(string[] args)
    {
        try
        {
            var options = BenchmarkOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            if (options.ListBenchmarks)
            {
                PrintBenchmarkList();
                return 0;
            }

            IReadOnlyList<IBenchmarkCase> selectedBenchmarks = SelectBenchmarks(options.BenchmarkNames);
            foreach (IBenchmarkCase benchmark in selectedBenchmarks)
            {
                benchmark.Validate();
                if (options.VerifyOnly)
                {
                    Console.WriteLine($"Verification passed: {benchmark.Name}");
                    continue;
                }

                RunBenchmark(benchmark, options);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static void RunBenchmark(IBenchmarkCase benchmark, BenchmarkOptions options)
    {
        int warmupIterations = Math.Min(options.Iterations, MaximumWarmupIterations);
        benchmark.Run(warmupIterations);

        var samples = new BenchmarkSample[options.Samples];
        for (var index = 0; index < samples.Length; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            int checksum = benchmark.Run(options.Iterations);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            samples[index] = new BenchmarkSample(elapsed, allocated, checksum);
        }

        Array.Sort(samples, static (left, right) => left.Elapsed.CompareTo(right.Elapsed));
        BenchmarkSample median = samples[samples.Length / 2];
        long operationCount = (long)options.Iterations * benchmark.OperationsPerIteration;

        Console.WriteLine($"Benchmark: {benchmark.Name}");
        Console.WriteLine($"Description: {benchmark.Description}");
        Console.WriteLine("Verification: passed");
        Console.WriteLine($"Vintage Story API: {typeof(RecipeBase).Assembly.GetName().Version}");
        Console.WriteLine($"Configuration: {BuildConfiguration}");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        Console.WriteLine($"GC mode: {(GCSettings.IsServerGC ? "server" : "workstation")}");
        Console.WriteLine($"Processor count: {Environment.ProcessorCount}");
        Console.WriteLine($"Iterations per sample: {options.Iterations.ToString("N0", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Operations per iteration: {benchmark.OperationsPerIteration}");
        Console.WriteLine($"Samples: {options.Samples}");
        Console.WriteLine(FormattableString.Invariant(
            $"Median time: {median.Elapsed.TotalMilliseconds:F2} ms ({median.Elapsed.TotalNanoseconds / operationCount:F2} ns/op)"));
        Console.WriteLine(FormattableString.Invariant(
            $"Allocated: {median.AllocatedBytes:N0} bytes ({(double)median.AllocatedBytes / operationCount:F2} B/op)"));
        Console.WriteLine($"Checksum: {median.Checksum}");
        Console.WriteLine();
    }

    private static IReadOnlyList<IBenchmarkCase> SelectBenchmarks(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return BenchmarkCases;

        var selected = new List<IBenchmarkCase>(names.Count);
        foreach (string name in names)
        {
            IBenchmarkCase? benchmark = Array.Find(
                BenchmarkCases,
                candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (benchmark == null)
            {
                throw new ArgumentException($"Unknown benchmark: {name}. Use --list to see available benchmarks.");
            }

            if (!selected.Contains(benchmark)) selected.Add(benchmark);
        }

        return selected;
    }

    private static void PrintBenchmarkList()
    {
        foreach (IBenchmarkCase benchmark in BenchmarkCases)
        {
            Console.WriteLine($"{benchmark.Name}: {benchmark.Description}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Lithos benchmark suite");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --list                  List available benchmarks.");
        Console.WriteLine("  --benchmark <name>      Run one benchmark. May be repeated.");
        Console.WriteLine("  --verify                Run correctness checks without measurements.");
        Console.WriteLine("  --iterations <number>   Set iterations per sample.");
        Console.WriteLine("  --samples <number>      Set an odd number of samples.");
        Console.WriteLine("  --help                  Show this help.");
    }

    private static string BuildConfiguration
    {
        get
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }
    }

    private sealed record BenchmarkSample(TimeSpan Elapsed, long AllocatedBytes, int Checksum);

    private sealed record BenchmarkOptions(
        int Iterations,
        int Samples,
        IReadOnlyList<string> BenchmarkNames,
        bool ListBenchmarks,
        bool VerifyOnly,
        bool ShowHelp)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            var iterations = DefaultIterations;
            var samples = DefaultSamples;
            var benchmarkNames = new List<string>();
            var listBenchmarks = false;
            var verifyOnly = false;
            var showHelp = false;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--benchmark":
                        benchmarkNames.Add(ReadValue(args, ref index));
                        break;
                    case "--iterations":
                        iterations = ReadPositiveInteger(args, ref index);
                        break;
                    case "--samples":
                        samples = ReadPositiveInteger(args, ref index);
                        break;
                    case "--list":
                        listBenchmarks = true;
                        break;
                    case "--verify":
                        verifyOnly = true;
                        break;
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {args[index]}");
                }
            }

            if (samples % 2 == 0)
            {
                throw new ArgumentException("Samples must be odd so the median is unambiguous.");
            }

            return new BenchmarkOptions(
                iterations,
                samples,
                benchmarkNames,
                listBenchmarks,
                verifyOnly,
                showHelp);
        }

        private static int ReadPositiveInteger(string[] args, ref int index)
        {
            string option = args[index];
            string text = ReadValue(args, ref index);
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
            {
                throw new ArgumentException($"{option} requires a positive integer.");
            }

            return value;
        }

        private static string ReadValue(string[] args, ref int index)
        {
            string option = args[index];
            index++;
            if (index >= args.Length || args[index].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            return args[index];
        }
    }
}
