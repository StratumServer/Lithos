using System.Numerics;

namespace Lithos.Benchmarks;

internal sealed class PacketBroadcastBenchmark : IBenchmarkCase
{
    private static readonly BenchmarkClient[] Clients = CreateClients();
    private static readonly IBenchmarkPlayer?[] NoSkippedPlayers = [];
    private static readonly IBenchmarkPlayer?[] SkippedPlayers =
    [
        new BenchmarkPlayer(0),
        new BenchmarkPlayer(17),
        null,
        new BenchmarkPlayer(36),
        new BenchmarkPlayer(61)
    ];

    public string Name => "network-packet-broadcast";

    public string Description => "Checks packet recipients for 64 clients with empty and populated skip lists.";

    public int OperationsPerIteration => Clients.Length * 2;

    public void Validate()
    {
        var clients = new BenchmarkClient[]
        {
            new(0, BenchmarkClientState.Playing),
            new(1, BenchmarkClientState.Offline),
            new(2, BenchmarkClientState.Queued),
            new(3, BenchmarkClientState.Connected),
            new(4, BenchmarkClientState.Playing)
        };
        IBenchmarkPlayer?[][] skipPlayerCases =
        [
            [],
            [null],
            [new BenchmarkPlayer(0)],
            [new BenchmarkPlayer(3), new BenchmarkPlayer(3)],
            [null, new BenchmarkPlayer(4), new BenchmarkPlayer(99)]
        ];

        Ensure(
            ReferenceRecipients(clients, null) == OptimizedRecipients(clients, null),
            "an explicit null skip list changed recipients");
        foreach (IBenchmarkPlayer?[] skipPlayers in skipPlayerCases)
        {
            Ensure(
                ReferenceRecipients(clients, skipPlayers) == OptimizedRecipients(clients, skipPlayers),
                "a skip-list shape changed recipients");
        }

        Ensure(Run(1) == 124, "measurement workload checksum changed");
    }

    public int Run(int iterations)
    {
        var checksum = 0;
        for (var index = 0; index < iterations; index++)
        {
            checksum += BitOperations.PopCount(OptimizedRecipients(Clients, NoSkippedPlayers));
            checksum += BitOperations.PopCount(OptimizedRecipients(Clients, SkippedPlayers));
        }

        return checksum;
    }

    private static ulong ReferenceRecipients(BenchmarkClient[] clients, IBenchmarkPlayer?[]? skipPlayers)
    {
        ulong recipients = 0;
        foreach (BenchmarkClient client in clients)
        {
            if (client.State != BenchmarkClientState.Offline
                && client.State != BenchmarkClientState.Queued
                && (skipPlayers == null || !skipPlayers.Any(player => player?.ClientId == client.Id)))
            {
                recipients |= 1UL << client.Id;
            }
        }

        return recipients;
    }

    private static ulong OptimizedRecipients(BenchmarkClient[] clients, IBenchmarkPlayer?[]? skipPlayers)
    {
        ulong recipients = 0;
        foreach (BenchmarkClient client in clients)
        {
            if (client.State != BenchmarkClientState.Offline
                && client.State != BenchmarkClientState.Queued
                && !ShouldSkipClient(skipPlayers, client.Id))
            {
                recipients |= 1UL << client.Id;
            }
        }

        return recipients;
    }

    private static bool ShouldSkipClient(IBenchmarkPlayer?[]? skipPlayers, int clientId)
    {
        if (skipPlayers == null) return false;

        for (var index = 0; index < skipPlayers.Length; index++)
        {
            if (skipPlayers[index]?.ClientId == clientId) return true;
        }

        return false;
    }

    private static BenchmarkClient[] CreateClients()
    {
        var clients = new BenchmarkClient[64];
        for (var index = 0; index < clients.Length; index++)
        {
            clients[index] = new BenchmarkClient(index, BenchmarkClientState.Playing);
        }

        return clients;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }

    private interface IBenchmarkPlayer
    {
        int ClientId { get; }
    }

    private sealed record BenchmarkPlayer(int ClientId) : IBenchmarkPlayer;

    private sealed record BenchmarkClient(int Id, BenchmarkClientState State);

    private enum BenchmarkClientState
    {
        Connected,
        Playing,
        Queued,
        Offline
    }
}
