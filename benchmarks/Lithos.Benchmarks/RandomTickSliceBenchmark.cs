using Vintagestory.API.MathTools;

namespace Lithos.Benchmarks;

internal sealed class RandomTickSliceBenchmark : IBenchmarkCase
{
    private const int BlockTickInterval = 300;
    private const int MaximumSliceCount = 8;
    private const int ChunkRange = 5;

    private readonly ChunkCoordinate[] chunks = CreateChunks();

    public string Name => "random-tick-slices";

    public string Description => "Assigns an 11 by 11 by 11 chunk neighborhood to stable random tick slices.";

    public int OperationsPerIteration => 1;

    public void Validate()
    {
        Ensure(GetSliceCount(300) == 6, "the default interval did not produce six exact slices");
        Ensure(GetSliceCount(400) == 8, "an interval divisible by eight did not use eight slices");
        Ensure(GetSliceCount(250) == 5, "the largest exact divisor was not selected");
        Ensure(GetSliceCount(301) == 7, "a non-default exact divisor was not selected");
        Ensure(GetSliceCount(1) == 1, "a one millisecond interval was sliced");
        Ensure(GetSliceCount(0) == 1, "a zero interval was sliced");

        int sliceCount = GetSliceCount(BlockTickInterval);
        var visits = new int[chunks.Length];
        var chunksPerSlice = new int[sliceCount];
        for (var slice = 0; slice < sliceCount; slice++)
        {
            for (var offset = 0; offset < chunks.Length; offset++)
            {
                int chunkIndex = (offset + slice * 137) % chunks.Length;
                ChunkCoordinate chunk = chunks[chunkIndex];
                if (GetSlice(chunk, sliceCount) != slice) continue;

                visits[chunkIndex]++;
                chunksPerSlice[slice]++;
            }
        }

        Ensure(visits.All(static count => count == 1), "a chunk was skipped or selected more than once per cycle");
        Ensure(chunksPerSlice.Sum() == chunks.Length, "the cycle changed the aggregate chunk count");
        Ensure(chunksPerSlice.Max() <= 250, "one slice retained too much of the original burst");
        Ensure(Run(chunks.Length * sliceCount) == -1_926_569_962, "measurement workload checksum changed");
    }

    public int Run(int iterations)
    {
        int sliceCount = GetSliceCount(BlockTickInterval);
        var checksum = 0;
        var chunkIndex = 0;
        var currentSlice = 0;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            ChunkCoordinate chunk = chunks[chunkIndex];
            int assignedSlice = GetSlice(chunk, sliceCount);
            checksum = unchecked(checksum * 31 + (assignedSlice == currentSlice ? chunkIndex : -1));

            if (++chunkIndex == chunks.Length)
            {
                chunkIndex = 0;
                if (++currentSlice == sliceCount) currentSlice = 0;
            }
        }

        return checksum;
    }

    private static int GetSliceCount(int blockTickInterval)
    {
        int maximumSliceCount = Math.Min(MaximumSliceCount, blockTickInterval);
        for (int sliceCount = maximumSliceCount; sliceCount >= 2; sliceCount--)
        {
            if (blockTickInterval % sliceCount == 0) return sliceCount;
        }

        return 1;
    }

    private static int GetSlice(ChunkCoordinate chunk, int sliceCount)
    {
        return GameMath.MurmurHash3Mod(chunk.X, chunk.Y + chunk.Dimension * 1024, chunk.Z, sliceCount);
    }

    private static ChunkCoordinate[] CreateChunks()
    {
        int width = ChunkRange * 2 + 1;
        var chunks = new ChunkCoordinate[width * width * width];
        var index = 0;
        for (int x = -ChunkRange; x <= ChunkRange; x++)
        {
            for (int y = -ChunkRange; y <= ChunkRange; y++)
            {
                for (int z = -ChunkRange; z <= ChunkRange; z++)
                {
                    chunks[index++] = new ChunkCoordinate(x, y, z, 0);
                }
            }
        }

        return chunks;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private readonly record struct ChunkCoordinate(int X, int Y, int Z, int Dimension);
}
