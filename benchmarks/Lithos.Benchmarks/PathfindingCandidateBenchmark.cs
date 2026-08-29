using Vintagestory.API.MathTools;
using Vintagestory.Essentials;

namespace Lithos.Benchmarks;

internal sealed class PathfindingCandidateBenchmark : IBenchmarkCase
{
    private const int ExpandedNodeCount = 256;

    private readonly PathNode[] candidateParents;
    private readonly Cardinal[] candidateDirections;
    private readonly bool[] acceptedCandidates;
    private readonly PathNode scratchNode = new(new BlockPos(0));

    public PathfindingCandidateBenchmark()
    {
        PathNode[] expandedNodes = CreateExpandedNodes();
        int candidateCount = ExpandedNodeCount * Cardinal.ALL.Length;
        candidateParents = new PathNode[candidateCount];
        candidateDirections = new Cardinal[candidateCount];
        acceptedCandidates = new bool[candidateCount];
        var candidateIndex = 0;
        for (var nodeIndex = 0; nodeIndex < expandedNodes.Length; nodeIndex++)
        {
            for (var directionIndex = 0; directionIndex < Cardinal.ALL.Length; directionIndex++)
            {
                candidateParents[candidateIndex] = expandedNodes[nodeIndex];
                candidateDirections[candidateIndex] = Cardinal.ALL[directionIndex];
                acceptedCandidates[candidateIndex] = (nodeIndex + directionIndex) % 4 == 0;
                candidateIndex++;
            }
        }
    }

    public string Name => "pathfinding-candidates";

    public string Description => "Cycles through eight candidates for 256 expanded path nodes while retaining one candidate in four.";

    public int OperationsPerIteration => 1;

    public void Validate()
    {
        Ensure(RunReference(candidateParents.Length) == RunOptimized(candidateParents.Length), "candidate coordinates changed");
        Ensure(RunOptimized(1) == -1_002, "measurement workload checksum changed");

        PathNode parent = candidateParents[0];
        Cardinal firstDirection = candidateDirections[0];
        ResetScratch(parent, firstDirection);
        var accepted = new PathNode(scratchNode);
        int acceptedX = accepted.X;
        int acceptedY = accepted.Y;
        int acceptedZ = accepted.Z;

        for (var index = 1; index < Cardinal.ALL.Length; index++)
        {
            ResetScratch(candidateParents[index], candidateDirections[index]);
        }

        Ensure(accepted.X == acceptedX && accepted.Y == acceptedY && accepted.Z == acceptedZ, "an accepted node retained scratch ownership");

        var secondAccepted = new PathNode(scratchNode);
        Ensure(!ReferenceEquals(accepted, secondAccepted), "accepted candidates share object identity");
    }

    public int Run(int iterations)
    {
        return RunOptimized(iterations);
    }

    private int RunReference(int iterations)
    {
        var checksum = 0;
        var candidateIndex = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var candidate = new PathNode(candidateParents[candidateIndex], candidateDirections[candidateIndex]);
            if (acceptedCandidates[candidateIndex]) checksum = AddToChecksum(checksum, candidate);

            if (++candidateIndex == candidateParents.Length) candidateIndex = 0;
        }

        return checksum;
    }

    private int RunOptimized(int iterations)
    {
        var checksum = 0;
        var candidateIndex = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            ResetScratch(candidateParents[candidateIndex], candidateDirections[candidateIndex]);
            if (acceptedCandidates[candidateIndex])
            {
                var accepted = new PathNode(scratchNode);
                checksum = AddToChecksum(checksum, accepted);
            }

            if (++candidateIndex == candidateParents.Length) candidateIndex = 0;
        }

        return checksum;
    }

    private void ResetScratch(PathNode parent, Cardinal direction)
    {
        scratchNode.Set(parent.X + direction.Normali.X, parent.Y + direction.Normali.Y, parent.Z + direction.Normali.Z);
        scratchNode.dimension = parent.dimension;
        scratchNode.gCost = 0;
        scratchNode.hCost = 0;
        scratchNode.HeapIndex = 0;
        scratchNode.Parent = null;
        scratchNode.pathLength = 0;
        scratchNode.Action = EnumTraverseAction.Walk;
    }

    private static int AddToChecksum(int checksum, PathNode node)
    {
        return unchecked(checksum * 31 + node.X * 17 + node.Y * 13 + node.Z + node.dimension * 19);
    }

    private static PathNode[] CreateExpandedNodes()
    {
        var nodes = new PathNode[ExpandedNodeCount];
        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = new PathNode(new BlockPos(index - 128, 100 + index % 7, index * 37 % 251 - 125, index % 2));
        }

        return nodes;
    }

    private void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"{Name}: {message}");
    }
}
