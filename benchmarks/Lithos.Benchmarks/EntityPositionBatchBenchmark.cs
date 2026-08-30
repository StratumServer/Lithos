using System.Buffers;
using System.Collections.Concurrent;

namespace Lithos.Benchmarks;

internal sealed class EntityPositionBatchBenchmark(bool reuseStorage) : IBenchmarkCase
{
    private const int PositionCount = 23;
    private const int UdpBatchSize = 8;

    private static readonly Packet_EntityPosition[] Positions = CreatePositions();

    private readonly ConcurrentQueue<Packet_BulkEntityPosition> packetPool = [];
    private readonly QueuedBatch[] queuedBatches = new QueuedBatch[3];

    public string Name => reuseStorage ? "entity-position-batches-pooled" : "entity-position-batches-vanilla";

    public string Description => reuseStorage
        ? "Builds queued entity position batches with lifetime-safe array and wrapper pooling."
        : "Builds queued entity position batches with fresh arrays and wrappers.";

    public int OperationsPerIteration => 1;

    public void Validate()
    {
        int[] iterationCounts = [1, 3, 4, 17];
        foreach (int iterations in iterationCounts)
        {
            int expected = RunWorkload(iterations, false);
            int actual = RunWorkload(iterations, true);
            Ensure(expected == actual, $"batch contents changed for {iterations} send passes");
        }
        Ensure(RunWorkload(4, false) == 689_978_301, "measurement workload checksum changed");

        ValidateQueuedLifetime();
        ValidateSinglePlayerOwnership();
        ValidateSerialization();
    }

    public int Run(int iterations)
    {
        return RunWorkload(iterations, reuseStorage);
    }

    private int RunWorkload(int iterations, bool usePool)
    {
        var checksum = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            int route = iteration & 3;
            var queuedCount = 0;
            checksum = unchecked(checksum * 31 + route);

            switch (route)
            {
                case 0:
                    for (var offset = 0; offset < PositionCount; offset += UdpBatchSize)
                    {
                        int count = Math.Min(UdpBatchSize, PositionCount - offset);
                        queuedBatches[queuedCount++] = CreateBatch(offset, count, usePool);
                    }
                    break;
                case 1:
                    queuedBatches[queuedCount++] = CreateBatch(0, PositionCount, usePool);
                    break;
                case 2:
                    queuedBatches[queuedCount++] = CreateBatch(0, 5, usePool);
                    break;
                default:
                    queuedBatches[queuedCount++] = CreateBatch(0, PositionCount, false);
                    break;
            }

            checksum = DrainBatches(checksum, queuedCount);
        }
        return checksum;
    }

    private QueuedBatch CreateBatch(int offset, int count, bool usePool)
    {
        Packet_EntityPosition[] positions;
        Packet_BulkEntityPosition packet;
        if (usePool)
        {
            positions = ArrayPool<Packet_EntityPosition>.Shared.Rent(count);
            if (!packetPool.TryDequeue(out packet!)) packet = new Packet_BulkEntityPosition();
        }
        else
        {
            positions = new Packet_EntityPosition[count];
            packet = new Packet_BulkEntityPosition();
        }

        for (var index = 0; index < count; index++)
        {
            positions[index] = Positions[offset + index];
        }
        packet.SetEntityPositions(positions, count, count);
        return new QueuedBatch(packet, offset, count, usePool);
    }

    private int DrainBatches(int checksum, int queuedCount)
    {
        for (var batchIndex = 0; batchIndex < queuedCount; batchIndex++)
        {
            QueuedBatch queued = queuedBatches[batchIndex];
            Packet_BulkEntityPosition packet = queued.Packet;
            Ensure(packet.EntityPositionsCount == queued.Count, "packet count changed");
            Ensure(packet.EntityPositionsLength == queued.Count, "packet length changed");

            for (var index = 0; index < queued.Count; index++)
            {
                Packet_EntityPosition expected = Positions[queued.Offset + index];
                Ensure(ReferenceEquals(packet.EntityPositions[index], expected), "queued packet storage changed before send completion");
                checksum = unchecked(checksum * 31 + (int)packet.EntityPositions[index].EntityId);
            }

            if (queued.Pooled) ReturnBatch(packet);
            queuedBatches[batchIndex] = default;
        }
        return checksum;
    }

    private void ValidateQueuedLifetime()
    {
        QueuedBatch first = CreateBatch(0, UdpBatchSize, true);
        QueuedBatch second = CreateBatch(UdpBatchSize, UdpBatchSize, true);
        Ensure(!ReferenceEquals(first.Packet, second.Packet), "queued wrappers were reused before send completion");
        Ensure(!ReferenceEquals(first.Packet.EntityPositions, second.Packet.EntityPositions), "queued arrays were reused before send completion");
        ReturnBatch(first.Packet);
        ReturnBatch(second.Packet);
    }

    private void ValidateSinglePlayerOwnership()
    {
        QueuedBatch first = CreateBatch(0, PositionCount, false);
        QueuedBatch second = CreateBatch(0, PositionCount, false);
        Ensure(!ReferenceEquals(first.Packet, second.Packet), "single-player wrapper entered the pool");
        Ensure(!ReferenceEquals(first.Packet.EntityPositions, second.Packet.EntityPositions), "single-player array entered the pool");
    }

    private void ValidateSerialization()
    {
        QueuedBatch vanilla = CreateBatch(0, PositionCount, false);
        QueuedBatch pooled = CreateBatch(0, PositionCount, true);
        Packet_BulkEntityPositionSerializer.GetSize(vanilla.Packet);
        Packet_BulkEntityPositionSerializer.GetSize(pooled.Packet);
        byte[] expected = Packet_BulkEntityPositionSerializer.SerializeToBytes(vanilla.Packet);
        byte[] actual = Packet_BulkEntityPositionSerializer.SerializeToBytes(pooled.Packet);
        Ensure(expected.AsSpan().SequenceEqual(actual), "serialized packet changed");
        ReturnBatch(pooled.Packet);
    }

    private void ReturnBatch(Packet_BulkEntityPosition packet)
    {
        Packet_EntityPosition[] positions = packet.EntityPositions;
        int count = packet.EntityPositionsCount;
        packet.EntityPositions = null!;
        packet.EntityPositionsCount = 0;
        packet.EntityPositionsLength = 0;
        packet.size = 0;
        Array.Clear(positions, 0, count);
        ArrayPool<Packet_EntityPosition>.Shared.Return(positions);
        packetPool.Enqueue(packet);
    }

    private static Packet_EntityPosition[] CreatePositions()
    {
        var positions = new Packet_EntityPosition[PositionCount];
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] = new Packet_EntityPosition
            {
                EntityId = 10_000 + index,
                X = index * 16_384L,
                Y = (index + 64) * 16_384L,
                Z = (index - 8) * 16_384L,
                Yaw = index * 10,
                Tick = index * 3,
                PositionVersion = index & 3
            };
        }
        return positions;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private readonly record struct QueuedBatch(
        Packet_BulkEntityPosition Packet,
        int Offset,
        int Count,
        bool Pooled);
}
