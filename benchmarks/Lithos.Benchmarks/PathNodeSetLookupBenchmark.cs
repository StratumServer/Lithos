using Vintagestory.API.MathTools;
using Vintagestory.Essentials;

namespace Lithos.Benchmarks;

internal sealed class PathNodeSetLookupBenchmark(bool indexed) : IBenchmarkCase
{
    private const int FrontierSize = 512;

    private static readonly PathNode[] FrontierNodes = CreateFrontierNodes();

    private readonly PathNodeSet indexedSet = new();
    private readonly VanillaPathNodeSet vanillaSet = new();
    private readonly PathNode lookupNode = new(new BlockPos(0));

    public string Name => indexed ? "pathnode-open-set-indexed" : "pathnode-open-set-vanilla";

    public string Description => indexed
        ? "Looks up A* candidates in a 512-node frontier through a coordinate index."
        : "Looks up A* candidates in a 512-node frontier through four linear buckets.";

    public int OperationsPerIteration => 1;

    public void Validate()
    {
        int[] iterationCounts = [1, FrontierSize - 1, FrontierSize, FrontierSize * 2 + 1];
        foreach (int iterations in iterationCounts)
        {
            int expected = RunWorkload(iterations, false);
            int actual = RunWorkload(iterations, true);
            Ensure(expected == actual, $"lookup results changed for {iterations} probes");
        }
        Ensure(RunWorkload(FrontierSize * 2, false) == -218_859_520, "measurement workload checksum changed");

        ValidateSetBehavior();
    }

    public int Run(int iterations)
    {
        return RunWorkload(iterations, indexed);
    }

    private int RunWorkload(int iterations, bool useIndex)
    {
        ResetSets(useIndex);
        var checksum = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            int frontierIndex = iteration * 167 & (FrontierSize - 1);
            PathNode frontierNode = FrontierNodes[frontierIndex];
            if ((iteration & 1) == 0)
            {
                lookupNode.Set(frontierNode.X, frontierNode.Y, frontierNode.Z);
            }
            else
            {
                lookupNode.Set(frontierNode.X, frontierNode.Y, frontierNode.Z + 1_024);
            }
            lookupNode.dimension = frontierNode.dimension + 3;

            PathNode? result = useIndex
                ? indexedSet.TryFindValue(lookupNode)
                : vanillaSet.TryFindValue(lookupNode);
            checksum = result is null
                ? unchecked(checksum * 31 - 1)
                : unchecked(checksum * 31 + result.X * 17 + result.Y * 13 + result.Z);
        }
        return checksum;
    }

    private void ResetSets(bool useIndex)
    {
        if (useIndex)
        {
            indexedSet.Clear();
            foreach (PathNode node in FrontierNodes) indexedSet.Add(node);
            return;
        }

        vanillaSet.Clear();
        foreach (PathNode node in FrontierNodes) vanillaSet.Add(node);
    }

    private void ValidateSetBehavior()
    {
        var expected = new VanillaPathNodeSet();
        var actual = new PathNodeSet();
        PathNode[] nodes = CreateValidationNodes();

        foreach (PathNode node in nodes)
        {
            Ensure(expected.Add(node) == actual.Add(node), "add result changed");
        }

        var duplicate = new PathNode(new BlockPos(nodes[7].X, nodes[7].Y, nodes[7].Z, nodes[7].dimension + 2));
        Ensure(!expected.Add(duplicate) && !actual.Add(duplicate), "dimension changed coordinate equality");
        EnsureSameOrder(expected.Snapshot(), actual.ToArray(), "enumeration order changed after add");

        foreach (PathNode node in nodes)
        {
            lookupNode.Set(node.X, node.Y, node.Z);
            lookupNode.dimension = node.dimension + 5;
            Ensure(ReferenceEquals(expected.TryFindValue(lookupNode), actual.TryFindValue(lookupNode)), "lookup identity changed");
        }

        var removedNode = nodes[11];
        var removalKey = new PathNode(new BlockPos(removedNode.X, removedNode.Y, removedNode.Z, removedNode.dimension + 1));
        expected.Remove(removalKey);
        actual.Remove(removalKey);
        Ensure(expected.Count == actual.Count, "explicit remove count changed");
        EnsureSameOrder(expected.Snapshot(), actual.ToArray(), "enumeration order changed after remove");

        while (expected.Count > 0)
        {
            Ensure(ReferenceEquals(expected.RemoveNearest(), actual.RemoveNearest()), "nearest-node order changed");
        }
        Ensure(actual.Count == 0, "indexed set retained nodes after draining");

        foreach (PathNode node in nodes) actual.Add(node);
        actual.Clear();
        lookupNode.Set(nodes[0].X, nodes[0].Y, nodes[0].Z);
        Ensure(actual.Count == 0 && actual.TryFindValue(lookupNode) is null, "clear retained coordinate membership");
    }

    private void EnsureSameOrder(PathNode[] expected, PathNode[] actual, string message)
    {
        Ensure(expected.Length == actual.Length, message);
        for (var index = 0; index < expected.Length; index++)
        {
            Ensure(ReferenceEquals(expected[index], actual[index]), message);
        }
    }

    private static PathNode[] CreateFrontierNodes()
    {
        var nodes = new PathNode[FrontierSize];
        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = new PathNode(new BlockPos(index % 32 - 16, 64 + index % 7, index / 32 - 8, index % 3))
            {
                gCost = index * 37 % 41,
                hCost = index * 13 % 31
            };
        }
        return nodes;
    }

    private static PathNode[] CreateValidationNodes()
    {
        var nodes = new PathNode[73];
        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = new PathNode(new BlockPos(index % 11 - 5, 70 + index % 4, index / 11 - 3, index % 2))
            {
                gCost = index * 7 % 13,
                hCost = index * 5 % 11
            };
        }
        return nodes;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private sealed class VanillaPathNodeSet
    {
        private int arraySize = 16;
        private readonly PathNode[][] buckets = CreateBuckets();
        private readonly int[] bucketCount = new int[4];

        public int Count { get; private set; }

        public void Clear()
        {
            Array.Clear(bucketCount);
            Count = 0;
        }

        public bool Add(PathNode value)
        {
            int bucket = GetBucket(value);
            PathNode[] set = buckets[bucket];
            int size = bucketCount[bucket];
            var index = size;
            while (--index >= 0)
            {
                if (value.Equals(set[index])) return false;
            }

            if (size >= arraySize)
            {
                ExpandArrays();
                set = buckets[bucket];
            }

            float fCost = value.fCost;
            for (index = size - 1; index >= 0; index--)
            {
                if (set[index].fCost < fCost) continue;
                if (set[index].fCost == fCost && set[index].hCost < value.hCost) continue;
                break;
            }
            index++;
            int destination = size;
            while (destination > index) set[destination] = set[--destination];
            set[index] = value;
            bucketCount[bucket] = size + 1;
            Count++;
            return true;
        }

        public PathNode? RemoveNearest()
        {
            if (Count == 0) return null;
            PathNode? nearestNode = null;
            var bucketToRemoveFrom = 0;
            for (var bucket = 0; bucket < buckets.Length; bucket++)
            {
                int endIndex = bucketCount[bucket] - 1;
                if (endIndex < 0) continue;
                PathNode node = buckets[bucket][endIndex];
                if (nearestNode is null || node.fCost < nearestNode.fCost || node.fCost == nearestNode.fCost && node.hCost < nearestNode.hCost)
                {
                    nearestNode = node;
                    bucketToRemoveFrom = bucket;
                }
            }
            bucketCount[bucketToRemoveFrom]--;
            Count--;
            return nearestNode;
        }

        public void Remove(PathNode value)
        {
            int bucket = GetBucket(value);
            PathNode[] set = buckets[bucket];
            int size = bucketCount[bucket];
            var index = size;
            while (--index >= 0)
            {
                if (!value.Equals(set[index])) continue;
                size = --bucketCount[bucket];
                while (index < size) set[index] = set[++index];
                Count--;
                break;
            }
        }

        public PathNode? TryFindValue(PathNode value)
        {
            int bucket = GetBucket(value);
            PathNode[] set = buckets[bucket];
            var index = bucketCount[bucket];
            while (--index >= 0)
            {
                if (value.Equals(set[index])) return set[index];
            }
            return null;
        }

        public PathNode[] Snapshot()
        {
            var nodes = new PathNode[Count];
            var destination = 0;
            for (var bucket = 0; bucket < buckets.Length; bucket++)
            {
                Array.Copy(buckets[bucket], 0, nodes, destination, bucketCount[bucket]);
                destination += bucketCount[bucket];
            }
            return nodes;
        }

        private void ExpandArrays()
        {
            int newSize = arraySize * 3 / 2;
            for (var bucket = 0; bucket < buckets.Length; bucket++)
            {
                var expanded = new PathNode[newSize];
                Array.Copy(buckets[bucket], expanded, bucketCount[bucket]);
                buckets[bucket] = expanded;
            }
            arraySize = newSize;
        }

        private static int GetBucket(PathNode value)
        {
            int bucket = (value.Z % 2) * 2 + value.X % 2;
            return (bucket + 4) % 4;
        }

        private static PathNode[][] CreateBuckets()
        {
            var buckets = new PathNode[4][];
            for (var index = 0; index < buckets.Length; index++) buckets[index] = new PathNode[16];
            return buckets;
        }
    }
}
