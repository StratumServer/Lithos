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
