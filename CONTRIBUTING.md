# Contributing to Lithos

Lithos exists to reduce server cost without asking stock clients or ordinary mods to handle a different server contract. Correctness, save safety, and compatibility take priority over performance.

## Fork and branch workflow

Contributions are developed on a personal fork. Fork `StratumServer/Lithos` on GitHub, then clone the fork and add the main repository as `upstream`:

```text
git clone https://github.com/<username>/Lithos.git
cd Lithos
git remote add upstream https://github.com/StratumServer/Lithos.git
git fetch upstream
```

Create each branch from the current upstream `main` branch:

```text
git switch -c perf/chunk-cache upstream/main
```

Use a short, lowercase, kebab-case branch name with one of these prefixes:

- `perf/` for measured performance work.
- `fix/` for bug fixes.
- `compat/` for compatibility work.
- `build/` for tooling, dependency, and version rebases.
- `test/` for test coverage.
- `docs/` for documentation.
- `chore/` for maintenance that fits no other category.

Keep one logical change per branch. Do not develop contributor branches directly in the organization repository.

## Set up the source tree

Run the same repository tool on every supported platform:

```text
dotnet run --project tools/Lithos.Tool -- bootstrap
dotnet build Lithos.slnx -c Release
```

Bootstrap performs these steps:

1. Resolves the target server archive from the official Vintage Story release manifest.
2. Verifies and extracts the archive under `.lithos/`.
3. Restores the pinned ILSpy tool and decompiles the closed-source assemblies.
4. Fetches each open-source repository at the exact commit in `forks.json`.
5. Builds a pristine baseline under `.lithos/baseline/src/`.
6. Copies every project into the unified `src/` tree.
7. Applies `patches/` and then `overlay/`.

The generated directories are ignored. Do not add `src/` or `.lithos/` to Git.

Local archives can be supplied with `--server-archive` and `--client-archive`. A directory of existing Git checkouts can be supplied with `--repository-cache`; a checkout is reused only when its `HEAD` matches a pinned commit.

## Make a change

Edit files under `src/` and keep one behavior change per branch. Match the style surrounding any vanilla code and avoid unrelated formatting, cleanup, or renaming.

After editing, capture the repository-owned representation:

```text
dotnet run --project tools/Lithos.Tool -- capture
git diff -- patches overlay
```

Changed upstream files become unified diffs under `patches/`. New Lithos files and deliberate project file replacements live under `overlay/`. The capture command also removes stale entries, so review its complete diff before staging anything.

Do not hand-edit the pristine baseline. If it becomes stale or incomplete, capture current work and reconstruct it:

```text
dotnet run --project tools/Lithos.Tool -- bootstrap --refresh --force
```

The paired flags are intentional. Refresh replaces the ignored `src/` tree, and bootstrap refuses to do that without an explicit force flag.

### Mark changes to upstream files

Mark every logical Lithos change inside an existing upstream file. Put a single concrete comment immediately before the changed block:

```csharp
// Lithos: Cache the immutable lookup table after registry initialization.
```

An inline comment is acceptable for a small repair. For a longer block whose boundary would otherwise be unclear, use:

```csharp
// Lithos start: Avoid rebuilding the candidate list for every player.
// changed upstream code
// Lithos end.
```

Do not tag every changed line. Describe why the code differs from upstream instead of writing a vague label such as `faster`. New members added to an upstream type should use a `Lithos` prefix when practical.

Do not add Lithos markers to wholly original project files under `overlay/`. Their ownership is already clear. A deliberate complete-file replacement also belongs under `overlay/` and does not need line-level markers.

## Commit messages

Use this form:

```text
<type>(<scope>): <imperative summary>
```

The scope is optional. Use the same types as the branch prefixes: `perf`, `fix`, `compat`, `build`, `test`, `docs`, or `chore`.

- Write the summary as an imperative sentence fragment, such as `cache block type lookups`.
- Keep the first line at 72 characters or fewer.
- Use lowercase after the colon unless a proper name requires capitalization.
- Keep one logical change in each commit.
- Add a blank line before the body when more context is needed.
- Use the body to explain the reason, compatibility risk, measurements, and verification. Do not narrate the diff.
- Use `Fixes #123` or `Refs #123` in the footer when applicable.

Examples:

```text
perf(world): cache immutable block type lookups
fix(network): preserve vanilla packet ordering
build: rebase sources onto Vintage Story 1.22.8
docs: document benchmark reporting
```

## Verify the change

At minimum, run:

```text
dotnet run --project tools/Lithos.Tool -- doctor
dotnet build Lithos.slnx -c Release
dotnet run --project tools/Lithos.Tool -- smoke --no-build
```

The sequence above builds and then boots a temporary loopback-only server, waits for the `RunGame` phase, and requests a clean shutdown. Generated data is removed after success and preserved after failure.

Compilation and smoke testing are only the first gates. Test the vanilla behavior that the change touches. Depending on the subsystem, this can include existing world load, save and reload, player connection, chunk lifecycle, networking, entity behavior, or world generation determinism.

Performance changes need a repeatable before and after measurement. Record the game version, build configuration, world or fixture, mods, player or bot count, duration, metric, and both results. Do not keep an invasive change based only on a theoretical gain.

The benchmark suite uses the existing Release server output and requires no additional benchmark packages:

```text
dotnet build Lithos.slnx -c Release
dotnet run --project benchmarks/Lithos.Benchmarks -c Release -- --verify
dotnet run --project benchmarks/Lithos.Benchmarks -c Release
```

Run the same command and workload against the vanilla baseline and then do the proposed change. You should use an odd sample count so the fixture can report an unambiguous median. See the [benchmark catalog](benchmarks/README.md) for targeted commands and the result history.

## Rebase a contribution branch

Rebase onto current upstream `main` before requesting final review:

```text
git fetch upstream
git rebase upstream/main
git push --force-with-lease
```

Do not merge `main` into a contribution branch.

The materialized `src/` directory is ignored, so capture and commit all source changes before rebasing. After the rebase, reconstruct the source tree from the rebased repository state:

```text
dotnet run --project tools/Lithos.Tool -- bootstrap --refresh --force
```

Resolve conflicts in the repository-owned `patches/` and `overlay/` files, not by preserving an uncommitted ignored source tree. Build and retest after reconstruction.

## Open a pull request

Push the branch to your fork:

```text
git push -u origin perf/chunk-cache
```

Open a pull request from that branch into `StratumServer/Lithos:main`.

A pull request should state:

- The cost being removed.
- Why the path matters.
- Why the implementation is the smallest safe change.
- The topology, scheduling, shape, or body compatibility risk.
- Observable vanilla behavior that could be affected.
- Tests performed and anything not tested.
- Before and after measurements when performance is the reason for the change.

Substantial compatibility or concurrency changes require review from another maintainer.

## Rebase pinned projects

Use a dedicated `build/vs-<version>` branch for a Vintage Story update, or a `build/rebase-<project>` branch when only one pinned project changes. Start with a clean worktree and keep rebase work separate from unrelated performance changes.

For a game version update:

1. Update `vintageStoryVersion` and every matching repository ref in `forks.json`.
2. Run `dotnet run --project tools/Lithos.Tool -- bootstrap --refresh --force`.
3. Reapply each Lithos change deliberately against the new vanilla source.
4. Capture the reconstructed result and review every regenerated patch.
5. Run the full build, behavior checks, compatibility checks, and relevant benchmarks.

Update a single project ref without changing the game version only when that upstream revision is known to match the pinned game release and compatibility has been verified.

If a patch no longer applies, bootstrap reports the failing patch and stops before replacing the current `src/` tree. Move that patch temporarily outside `patches/` into the ignored `.local/` directory, then run bootstrap again without `--refresh` to materialize the new baseline. Reimplement the patch's intent against the new source and run `capture`. Compare the old and regenerated patches before removing the temporary copy.

Do not force old hunks through changed upstream code. Check whether vanilla already removed the cost or changed member shape, method bodies, event order, serialization, or packet behavior.

Prefer a readable commit sequence that separates manifest and baseline changes, patch rebases, and any compatibility fixes. Do not mix new performance work into the version rebase.

## Licensing contributions

By submitting a contribution, you agree that your original work may be distributed under the repository's [MIT License](LICENSE). Submit only work you have the right to contribute.

Do not commit reconstructed source trees, official archives, third-party binaries, or code copied from a license that is incompatible with this repository. Patches may express original Lithos changes against pinned upstream files, but they do not relicense the surrounding Vintage Story or third-party code. See [NOTICE](NOTICE) for the repository's licensing boundary.
