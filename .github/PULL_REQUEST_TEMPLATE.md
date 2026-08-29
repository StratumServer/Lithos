## Summary

Describe the cost or defect and the focused change that addresses it.

## Vanilla behavior and compatibility

Describe observable behavior that could change. Include member shape, initialization order, event order, save data, packets, scheduling, and mod compatibility where relevant.

## Verification

List the commands and behavior checks performed. Include anything that was not tested.

## Measurements

For performance work, provide the before and after results, game version, build configuration, world or fixture, mods, player or bot count, duration, and metric.

## Checklist

- [ ] The branch contains one logical change and is rebased onto current `upstream/main`.
- [ ] Existing upstream files use focused `Lithos` markers; wholly original overlay files do not.
- [ ] `dotnet run --project tools/Lithos.Tool -- capture` was run and the complete result was reviewed.
- [ ] `dotnet run --project tools/Lithos.Tool -- doctor` passes.
- [ ] `dotnet build Lithos.slnx -c Release` passes.
- [ ] Relevant vanilla behavior and compatibility were tested.
- [ ] Performance claims include repeatable before and after measurements.
- [ ] Documentation was updated when the workflow or contract changed.
