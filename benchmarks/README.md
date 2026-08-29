# Lithos benchmarks

The benchmark suite keeps a repeatable fixture beside each measured performance change. Every fixture must verify its expected behavior before collecting timings.

Reference results are just mainly historical evidence.

## Run the suite

Build the Release server output before running benchmarks:

```text
dotnet build Lithos.slnx -c Release
dotnet run --project benchmarks/Lithos.Benchmarks -c Release
```

Useful options:

```text
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --list
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --verify
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark crafting-shapeless-recipes --iterations 2000000 --samples 5
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark registry-code-parts --iterations 2000000 --samples 5
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark network-packet-broadcast --iterations 100000 --samples 5
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark pathfinding-candidates --iterations 2000000 --samples 5
```

The suite has no external benchmark package. It measures the Release assemblies already produced by the repository build.

## Add a benchmark

Add an `IBenchmarkCase` implementation with a stable name, a representative workload, and deterministic validation. Register it in `Program.cs`, then record the original before and after evidence below. Keep setup outside the measured `Run` method where practical.

## Result history

### 2026-08-29: crafting-shapeless-recipes

- Change: replace iterator and captured-predicate allocations in `RecipeBase.MergeStacks` and `RecipeBase.MatchWildcardIngredients` with ordered loops.
- Fixture: merge nine empty crafting slots, then filter five exact and four null ingredients.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 iterations per sample, five samples, median result.
- Compatibility: method bodies only; input order, null filtering, exact filtering, cloning, and stack merging are verified.

| Metric | Baseline | Lithos | Difference |
|---|---:|---:|---:|
| Time | 213.57 ns/op | 12.30 ns/op | 94.2% lower |
| Allocation | 232 B/op | 0 B/op | 232 B/op removed |
| Checksum | 2,000,000 | 2,000,000 | unchanged |

Patch: [shapeless recipe enumeration](../patches/VintagestoryApi/Common/Crafting/RecipeBase.cs.patch)

### 2026-08-29: registry-code-parts

- Change: scan asset-code paths directly in `RegistryObject.FirstCodePart` and `RegistryObject.LastCodePart` instead of splitting every segment.
- Fixture: perform eight first- and last-segment lookups per iteration across multi-part, empty-part, and single-part paths.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 iterations per sample, eight operations per iteration, five samples, median result.
- Compatibility: method bodies only; results and exception types are compared with the vanilla algorithm across null codes, empty segments, boundary positions, and extreme integer positions.

| Metric | Baseline | Lithos | Difference |
|---|---:|---:|---:|
| Time | 78.51 ns/op | 11.33 ns/op | 85.6% lower |
| Allocation | 166 B/op | 27 B/op | 139 B/op removed |
| Checksum | 82,000,000 | 82,000,000 | unchanged |

Patch: [registry code-part lookups](../patches/VintagestoryApi/Common/Registry/RegistryObject.cs.patch)

### 2026-08-29: network-packet-broadcast

- Change: scan the packet skip-player array directly instead of running LINQ for every client.
- Fixture: check recipients for 64 playing clients with both an empty skip list and a five-entry skip list containing four players and one null entry.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 100,000 iterations per sample, 128 client checks per iteration, five samples, median result.
- Compatibility: method bodies only; recipient masks are compared with the vanilla algorithm for null, empty, duplicate, missing, and null player entries, plus offline and queued client states.

| Metric | Baseline | Lithos | Difference |
|---|---:|---:|---:|
| Time | 22.59 ns/op | 3.53 ns/op | 84.4% lower |
| Allocation | 88 B/op | 0 B/op | 88 B/op removed |
| Checksum | 12,400,000 | 12,400,000 | unchanged |

Patch: [packet broadcast recipient filtering](../patches/VintagestoryLib/Vintagestory.Server/ServerMain.cs.patch)

### 2026-08-29: pathfinding candidates

- Change: reuse one scratch `PathNode` for A* candidates and allocate an independently owned node only when the candidate enters the open set.
- Fixture: cycle through eight directions for 256 expanded nodes, retaining one candidate in four. This isolates candidate construction and does not measure collision checks or a complete path search.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 candidates per sample, five samples, median result.
- Compatibility: method body only; accepted coordinates, dimensions, object identity, and lifetime after scratch reuse are verified against the vanilla construction path.

| Metric | Baseline | Lithos | Difference |
|---|---:|---:|---:|
| Time | 11.35 ns/op | 5.41 ns/op | 52.3% lower |
| Allocation | 64 B/op | 16 B/op | 48 B/op removed |
| Checksum | 1,666,522,256 | 1,666,522,256 | unchanged |

Patch: [A* candidate allocation](../patches/VSEssentials/Entity/Pathfinding/Astar/AStar.cs.patch)
