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
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark pathnode-open-set-vanilla --benchmark pathnode-open-set-indexed --iterations 2000000 --samples 5
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark random-tick-slices --iterations 2000000 --samples 5
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark entity-partition-rebuild-vanilla --benchmark entity-partition-rebuild-reuse --iterations 2000000 --samples 5
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark entity-packet-gather-vanilla --benchmark entity-packet-gather-reuse --iterations 2000000 --samples 5
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --benchmark entity-position-batches-vanilla --benchmark entity-position-batches-pooled --iterations 2000000 --samples 5
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

### 2026-08-29: A* open-set coordinate lookup

- Change: retain a private coordinate index beside `PathNodeSet`'s existing priority buckets.
- Fixture: probe a 512-node frontier with an even mix of present and absent coordinates distributed across all four buckets.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 candidate lookups per sample, five samples, median result.
- Compatibility: duplicate handling, dimension-independent coordinate equality, enumeration order, explicit removal, nearest-node priority and tie order, and clear behavior are compared with the original implementation. Public members and bucket ownership remain unchanged.

| Metric | Linear buckets | Coordinate index | Change |
|---|---:|---:|---:|
| Median time | 94.21 ns/lookup | 12.09 ns/lookup | 87.2% faster |
| Allocation | 0 B/lookup | 0 B/lookup | unchanged |
| Checksum | -1,303,597,382 | -1,303,597,382 | unchanged |

Patch: [A* open-set coordinate lookup](../patches/VSEssentials/Entity/Pathfinding/Astar/PathNodeSet.cs.patch)

### 2026-08-29: random tick slices

- Change: divide the configured random tick interval into chunk slices instead of processing the complete eligible chunk set in one burst.
- Fixture: assign the 1,331 candidates in an 11 by 11 by 11 chunk neighborhood across a complete cycle while changing enumeration order between slices.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 chunk eligibility checks per sample, five samples, median result.
- Scheduling: the default 300 ms interval uses six 50 ms slices. Each candidate is selected exactly once per cycle, and no fixture slice contains more than 250 candidates.
- Compatibility: scheduling and random-number consumption order change within each cycle. Thread ownership, configured aggregate interval, player-overlap deduplication, and the attempt count for continuously eligible chunks remain unchanged.

| Metric | Vanilla batch | Lithos slices |
|---|---:|---:|
| Nominal interval | 300 ms | 6 passes at 50 ms |
| Candidate chunks per cycle | 1,331 | 1,331 |
| Maximum candidates per fixture pass | 1,331 | 250 or fewer |
| Slice eligibility overhead | Not applicable | 5.30 ns/check, 0 B/check |

Patch: [random tick slicing](../patches/VintagestoryLib/Vintagestory.Server/ServerSystemBlockSimulation.cs.patch)

### 2026-08-29: entity partition rebuild reuse

- Change: retain recently used entity partitions and cell lists in a private cache keyed by chunk index.
- Fixture: rebuild four overlapping layouts of 1,024 entities across 128 partitions per layout, including creature and inanimate cells.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 entity insertions per sample, five samples, median result.
- Lifetime: inactive partitions expire after 300 rebuilds and are checked every 60 rebuilds.
- Compatibility: the public dictionary instance, active keys, key order, cell membership, and entity order remain unchanged. Partition and list object identity and retained capacity differ because their storage is reused.

| Metric | Vanilla rebuild | Lithos reuse | Change |
|---|---:|---:|---:|
| Median time | 65.57 ns/entity | 35.75 ns/entity | 45.5% faster |
| Allocation | 49.77 B/entity | 0 B/entity | 49.77 B/entity removed |
| Checksum | -1,585,616,704 | -1,585,616,704 | unchanged |

Patch: [entity partition reuse](../patches/VSEssentials/Systems/EntityPartitioning.cs.patch)

### 2026-08-29: entity packet gather buffers

- Change: retain one set of position, animation, and tag gather lists per physics worker instead of allocating them for every send pass.
- Fixture: cycle four worker indexes through passes containing 96 position packets, 24 animation packets, and six tag packets.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 gathered position updates per sample, five samples, median result.
- Ownership: each buffer index follows the existing worker-indexed packet dictionaries. Main-thread use of index zero happens only after worker completion.
- Compatibility: recipient selection, packet ordering, batch construction, send calls, and thread scheduling remain unchanged. Only private scratch-list identity and capacity differ.

| Metric | Fresh lists | Worker-owned lists | Change |
|---|---:|---:|---:|
| Median time | 38.17 ns/update | 10.75 ns/update | 71.8% faster |
| Allocation | 31.00 B/update | 0 B/update | 31.00 B/update removed |
| Checksum | -1,330,102,592 | -1,330,102,592 | unchanged |

Patch: [entity packet gather buffers](../patches/VintagestoryLib/Vintagestory.Server/PhysicsManager.cs.patch)

### 2026-08-29: queued entity position storage

- Change: pool the position arrays and bulk packet wrappers used by remote-client entity updates.
- Fixture: cycle a 23-position UDP send split into 8-position batches, a 23-position TCP fallback send, a five-position UDP send, and a 23-position single-player send.
- Configuration: Vintage Story API 1.22.7.0, .NET 10.0.11, Release, Windows x64, workstation GC.
- Sampling: 2,000,000 client send passes per sample, five samples, median result.
- Ownership: pooled storage stays attached to the queued packet until synchronous UDP or TCP serialization completes. Expired and failed sends also return it. Single-player retains fresh storage because its dummy transport queues the raw packet object.
- Compatibility: packet order, UDP batch boundaries, count and length fields, serialized bytes, public send behavior, and single-player object lifetime are verified. The isolated construction loop trades CPU time for substantially lower allocation pressure.

| Metric | Fresh storage | Pooled remote storage | Change |
|---|---:|---:|---:|
| Median time | 99.98 ns/send | 146.48 ns/send | 46.5% slower |
| Allocation | 244.00 B/send | 62.00 B/send | 182.00 B/send removed |
| Checksum | 1,262,985,376 | 1,262,985,376 | unchanged |

Patches: [entity position batching](../patches/VintagestoryLib/Vintagestory.Server/PhysicsManager.cs.patch), [UDP network send path](../patches/VintagestoryLib/Vintagestory.Server.Systems/ServerUdpNetwork.cs.patch), [UDP queue lifetime](../patches/VintagestoryLib/Vintagestory.Server.Systems/ServerUdpQueue.cs.patch), and [queued packet ownership](../patches/VintagestoryLib/Vintagestory.Server.Systems/QueuedUDPPacket.cs.patch).
