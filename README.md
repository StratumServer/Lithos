# Lithos

[![Release](https://img.shields.io/github/v/release/StratumServer/Lithos?display_name=tag&sort=semver&logo=github&label=release)](https://github.com/StratumServer/Lithos/releases)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Discord](https://img.shields.io/badge/chat-on%20discord-5865F2?logo=discord&logoColor=white)](https://discord.gg/pd24fawhsD)
[![Stars](https://img.shields.io/github/stars/StratumServer/Lithos?logo=github&style=flat)](https://github.com/StratumServer/Lithos/stargazers)
[![Support on OpenCollective](https://img.shields.io/badge/Support-OpenCollective-7FADF2?logo=opencollective&logoColor=white)](https://opencollective.com/stratum)

Lithos is a compatibility-first, high-performance server-side fork of [Vintage Story](https://www.vintagestory.at).

The project starts from the current vanilla server and applies focused, measured optimizations while preserving the behavior expected by stock clients and ordinary mods. Correctness, save safety, and compatibility take priority over performance.

Lithos currently targets Vintage Story 1.22.7 and is in active development.

## Project goals

- Reduce CPU time, allocations, and server operating cost in proven hot paths.
- Preserve vanilla member shape, initialization order, save data, packets, and observable behavior.
- Keep changes small enough to review, measure, and rebase as Vintage Story evolves.
- Maintain one reproducible source layout across Windows, Linux, and macOS.

## Server stats

<div align="center">

![Stratum Network](https://my.stratumvs.dev/stratum-stats.php?graph)

<sub>Network statistics are reported by participating Stratum and Lithos servers.</sub>

</div>

## Links

- [Releases](https://github.com/StratumServer/Lithos/releases)
- [Issue tracker](https://github.com/StratumServer/Lithos/issues)
- [Discord](https://discord.gg/pd24fawhsD)
- [OpenCollective](https://opencollective.com/stratum)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [License](LICENSE)

Lithos uses Stratum as a source of performance ideas, not as a code baseline. Each change starts from vanilla and must justify its compatibility surface.

## License

Original Lithos work is available under the [MIT License](LICENSE). Vintage Story and pinned third-party projects retain their own terms. See [NOTICE](NOTICE) for the exact boundary.
