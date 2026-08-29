using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Lithos.Benchmarks;

internal sealed class EntityPartitionBenchmark(bool reusePartitions) : IBenchmarkCase
{
    private const int EntityCount = 1_024;
    private const int LayoutCount = 4;
    private const int PartitionCountPerLayout = 128;
    private const int PartitionCacheLifetimeTicks = 300;
    private const int PartitionCacheTrimIntervalTicks = 60;

    private static readonly BenchmarkEntity[] Entities = CreateEntities();
    private static readonly PartitionEntry[][] Layouts = CreateLayouts();

    private readonly PartitionState state = new(reusePartitions);

    public string Name => reusePartitions ? "entity-partition-rebuild-reuse" : "entity-partition-rebuild-vanilla";

    public string Description => reusePartitions
        ? "Rebuilds entity partitions while reusing partition and cell storage."
        : "Rebuilds entity partitions with fresh partition and cell storage.";

    public int OperationsPerIteration => 1;

    public void Validate()
    {
        var reference = new PartitionState(false);
        var optimized = new PartitionState(true);
        Dictionary<long, EntityPartitionChunk> optimizedDictionary = optimized.ActivePartitions;

        foreach (PartitionEntry[] layout in Layouts)
        {
            reference.Rebuild(layout);
            optimized.Rebuild(layout);
            ComparePartitions(reference.ActivePartitions, optimized.ActivePartitions);
            Ensure(ReferenceEquals(optimizedDictionary, optimized.ActivePartitions), "the public dictionary instance changed");
        }

        var foreignPartition = new ForeignPartition();
        optimized.ActivePartitions[999] = foreignPartition;
        optimized.Rebuild([new PartitionEntry(999, 0, Entities[0])]);
        Ensure(!ReferenceEquals(foreignPartition, optimized.ActivePartitions[999]), "a foreign partition type was retained");

        reference.Rebuild([]);
        optimized.Rebuild([]);
        ComparePartitions(reference.ActivePartitions, optimized.ActivePartitions);
        Ensure(optimized.CachedPartitionCount > 0, "inactive partitions were not retained privately");

        for (var tick = 0; tick < PartitionCacheLifetimeTicks + PartitionCacheTrimIntervalTicks; tick++)
        {
            optimized.Rebuild([]);
        }
        Ensure(optimized.CachedPartitionCount == 0, "an expired partition remained cached");
        Ensure(Run(EntityCount * LayoutCount) == 1_285_674_752, "measurement workload checksum changed");
    }

    public int Run(int iterations)
    {
        var checksum = 0;
        var layoutIndex = 0;
        var entityIndex = 0;
        state.BeginRebuild();

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            state.Add(Layouts[layoutIndex][entityIndex]);
            if (++entityIndex != EntityCount) continue;

            state.EndRebuild();
            checksum = unchecked(checksum * 31 + state.GetChecksum());
            entityIndex = 0;
            if (++layoutIndex == LayoutCount) layoutIndex = 0;
            if (iteration + 1 < iterations) state.BeginRebuild();
        }

        return checksum;
    }

    private void ComparePartitions(
        Dictionary<long, EntityPartitionChunk> expected,
        Dictionary<long, EntityPartitionChunk> actual)
    {
        Ensure(expected.Count == actual.Count, "the active partition count changed");
        Ensure(expected.Keys.SequenceEqual(actual.Keys), "partition insertion order changed");

        foreach ((long key, EntityPartitionChunk expectedPartition) in expected)
        {
            if (!actual.TryGetValue(key, out EntityPartitionChunk? actualPartition))
            {
                throw new InvalidOperationException($"{Name}: an active partition was missing");
            }
            for (var gridIndex = 0; gridIndex < EntityPartitioning.partitionsLength * EntityPartitioning.partitionsLength; gridIndex++)
            {
                CompareLists(expectedPartition.Entities[gridIndex], actualPartition.Entities[gridIndex]);
                CompareLists(expectedPartition.InanimateEntities?[gridIndex], actualPartition.InanimateEntities?[gridIndex]);
            }
        }
    }

    private void CompareLists(List<Entity>? expected, List<Entity>? actual)
    {
        int expectedCount = expected?.Count ?? 0;
        int actualCount = actual?.Count ?? 0;
        Ensure(expectedCount == actualCount, "cell membership changed");
        for (var index = 0; index < expectedCount; index++)
        {
            Ensure(ReferenceEquals(expected![index], actual![index]), "entity order changed within a cell");
        }
    }

    private static BenchmarkEntity[] CreateEntities()
    {
        var entities = new BenchmarkEntity[EntityCount];
        for (var index = 0; index < entities.Length; index++)
        {
            entities[index] = new BenchmarkEntity(index, index % 4 != 0);
        }

        return entities;
    }

    private static PartitionEntry[][] CreateLayouts()
    {
        var layouts = new PartitionEntry[LayoutCount][];
        for (var layoutIndex = 0; layoutIndex < layouts.Length; layoutIndex++)
        {
            var layout = new PartitionEntry[EntityCount];
            for (var entryIndex = 0; entryIndex < layout.Length; entryIndex++)
            {
                int entityIndex = (entryIndex * 73 + layoutIndex * 19) % EntityCount;
                long partitionKey = 10_000 + layoutIndex * 64 + entityIndex * 37 % PartitionCountPerLayout;
                int gridIndex = (entityIndex * 11 + layoutIndex * 5) % (EntityPartitioning.partitionsLength * EntityPartitioning.partitionsLength);
                layout[entryIndex] = new PartitionEntry(partitionKey, gridIndex, Entities[entityIndex]);
            }
            layouts[layoutIndex] = layout;
        }

        return layouts;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private sealed class PartitionState(bool reuse)
    {
        private readonly Dictionary<long, CachedPartition> cache = new();
        private readonly List<long> trimKeys = [];
        private int generation;
        private int trimTicks;

        public Dictionary<long, EntityPartitionChunk> ActivePartitions { get; } = new();

        public int CachedPartitionCount => cache.Count;

        public void Rebuild(PartitionEntry[] entries)
        {
            BeginRebuild();
            foreach (PartitionEntry entry in entries)
            {
                Add(entry);
            }
            EndRebuild();
        }

        public void BeginRebuild()
        {
            int currentGeneration = unchecked(++generation);
            if (reuse)
            {
                foreach ((long key, EntityPartitionChunk partition) in ActivePartitions)
                {
                    if (partition.GetType() != typeof(EntityPartitionChunk)) continue;
                    ClearPartition(partition);
                    cache[key] = new CachedPartition(partition, currentGeneration);
                }
            }

            ActivePartitions.Clear();
        }

        public void EndRebuild()
        {
            if (!reuse || ++trimTicks < PartitionCacheTrimIntervalTicks) return;
            trimTicks = 0;

            trimKeys.Clear();
            foreach ((long key, CachedPartition partition) in cache)
            {
                if (unchecked(generation - partition.Generation) >= PartitionCacheLifetimeTicks)
                {
                    trimKeys.Add(key);
                }
            }
            foreach (long key in trimKeys)
            {
                cache.Remove(key);
            }
        }

        public void Add(PartitionEntry entry)
        {
            if (!ActivePartitions.TryGetValue(entry.PartitionKey, out EntityPartitionChunk? partition))
            {
                if (reuse && cache.Remove(entry.PartitionKey, out CachedPartition cachedPartition))
                {
                    partition = cachedPartition.Partition;
                }
                else
                {
                    partition = new EntityPartitionChunk();
                }
                ActivePartitions[entry.PartitionKey] = partition;
            }

            partition.Add(entry.Entity, entry.GridIndex);
        }

        public int GetChecksum()
        {
            int checksum = ActivePartitions.Count;
            foreach ((long key, EntityPartitionChunk partition) in ActivePartitions)
            {
                checksum = unchecked(checksum * 31 + (int)key);
                for (var gridIndex = 0; gridIndex < EntityPartitioning.partitionsLength * EntityPartitioning.partitionsLength; gridIndex++)
                {
                    checksum = AddListChecksum(checksum, gridIndex, partition.Entities[gridIndex]);
                    checksum = AddListChecksum(checksum, gridIndex + 16, partition.InanimateEntities?[gridIndex]);
                }
            }

            return checksum;
        }

        private static int AddListChecksum(int checksum, int gridIndex, List<Entity>? entities)
        {
            if (entities == null) return checksum;
            foreach (BenchmarkEntity entity in entities)
            {
                checksum = unchecked(checksum * 31 + entity.Id * 37 + gridIndex);
            }
            return checksum;
        }

        private static void ClearPartition(EntityPartitionChunk partition)
        {
            foreach (List<Entity>? entities in partition.Entities)
            {
                entities?.Clear();
            }

            if (partition.InanimateEntities == null) return;
            foreach (List<Entity>? entities in partition.InanimateEntities)
            {
                entities?.Clear();
            }
        }

        private readonly record struct CachedPartition(EntityPartitionChunk Partition, int Generation);
    }

    private sealed class BenchmarkEntity(int id, bool isCreature) : Entity(0)
    {
        public int Id { get; } = id;

        public override bool IsCreature => isCreature;
    }

    private sealed class ForeignPartition : EntityPartitionChunk
    {
    }

    private readonly record struct PartitionEntry(long PartitionKey, int GridIndex, BenchmarkEntity Entity);
}
