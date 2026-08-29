namespace Lithos.Benchmarks;

internal sealed class EntityPacketGatherBenchmark(bool reuseBuffers) : IBenchmarkCase
{
    private const int WorkerCount = 4;
    private const int PacketsPerPass = 96;

    private static readonly PacketToken[] PositionPackets = CreatePackets(1_000);
    private static readonly PacketToken[] AnimationPackets = CreatePackets(2_000);
    private static readonly PacketToken[] TagPackets = CreatePackets(3_000);

    private readonly List<PacketToken>[] workerPositionBuffers = CreateBuffers();
    private readonly List<PacketToken>[] workerAnimationBuffers = CreateBuffers();
    private readonly List<PacketToken>[] workerTagBuffers = CreateBuffers();

    public string Name => reuseBuffers ? "entity-packet-gather-reuse" : "entity-packet-gather-vanilla";

    public string Description => reuseBuffers
        ? "Gathers entity updates into worker-owned reusable lists."
        : "Gathers entity updates into fresh lists for every worker pass.";

    public int OperationsPerIteration => 1;

    public void Validate()
    {
        int[] iterationCounts = [1, PacketsPerPass - 1, PacketsPerPass, PacketsPerPass * WorkerCount + 17];
        foreach (int iterations in iterationCounts)
        {
            int expected = RunWorkload(iterations, false);
            int actual = RunWorkload(iterations, true);
            Ensure(expected == actual, $"packet order changed for {iterations} updates");
        }

        Ensure(Run(WorkerCount * PacketsPerPass) == -706_695_616, "measurement workload checksum changed");
    }

    public int Run(int iterations)
    {
        return RunWorkload(iterations, reuseBuffers);
    }

    private int RunWorkload(int iterations, bool reuse)
    {
        var checksum = 0;
        var workerIndex = 0;
        var packetIndex = 0;
        GetBuffers(workerIndex, reuse, out List<PacketToken> positions, out List<PacketToken> animations, out List<PacketToken> tags);

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            PacketToken position = PositionPackets[packetIndex];
            positions.Add(position);
            if ((packetIndex & 3) == 0) animations.Add(AnimationPackets[packetIndex]);
            if ((packetIndex & 15) == 0) tags.Add(TagPackets[packetIndex]);
            if (++packetIndex != PacketsPerPass) continue;

            checksum = AddChecksum(checksum, positions, animations, tags);
            packetIndex = 0;
            if (++workerIndex == WorkerCount) workerIndex = 0;
            if (iteration + 1 < iterations)
            {
                GetBuffers(workerIndex, reuse, out positions, out animations, out tags);
            }
        }

        if (packetIndex > 0) checksum = AddChecksum(checksum, positions, animations, tags);
        return checksum;
    }

    private void GetBuffers(
        int workerIndex,
        bool reuse,
        out List<PacketToken> positions,
        out List<PacketToken> animations,
        out List<PacketToken> tags)
    {
        if (reuse)
        {
            positions = workerPositionBuffers[workerIndex];
            animations = workerAnimationBuffers[workerIndex];
            tags = workerTagBuffers[workerIndex];
            positions.Clear();
            animations.Clear();
            tags.Clear();
            return;
        }

        positions = [];
        animations = [];
        tags = [];
    }

    private static int AddChecksum(
        int checksum,
        List<PacketToken> positions,
        List<PacketToken> animations,
        List<PacketToken> tags)
    {
        foreach (PacketToken packet in positions)
        {
            checksum = unchecked(checksum * 31 + packet.Id);
        }
        foreach (PacketToken packet in animations)
        {
            checksum = unchecked(checksum * 31 + packet.Id);
        }
        foreach (PacketToken packet in tags)
        {
            checksum = unchecked(checksum * 31 + packet.Id);
        }
        return checksum;
    }

    private static PacketToken[] CreatePackets(int idOffset)
    {
        var packets = new PacketToken[PacketsPerPass];
        for (var index = 0; index < packets.Length; index++)
        {
            packets[index] = new PacketToken(idOffset + index);
        }
        return packets;
    }

    private static List<PacketToken>[] CreateBuffers()
    {
        var buffers = new List<PacketToken>[WorkerCount];
        for (var index = 0; index < buffers.Length; index++)
        {
            buffers[index] = [];
        }
        return buffers;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private sealed record PacketToken(int Id);
}
