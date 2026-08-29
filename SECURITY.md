# Security Policy

## Reporting a vulnerability

Do not file a public issue for a suspected security vulnerability.

Open a private report through [GitHub Security Advisories](https://github.com/StratumServer/Lithos/security/advisories/new). If that is unavailable, contact a maintainer through [Discord](https://discord.gg/pd24fawhsD).

Include:

- A description of the issue and its impact.
- Steps to reproduce or a minimal proof of concept.
- The Lithos commit or release and Vintage Story version tested.
- Relevant server configuration, logs, and installed mods.
- Any known mitigation.

Avoid including secrets, private player data, or an active server address in the report. Maintainers will acknowledge the report as soon as practical and coordinate disclosure after a fix is available.

## Scope

In scope:

- Remote code execution, privilege escalation, or authentication bypass in Lithos.
- Crashes or resource exhaustion triggerable by an unauthenticated client.
- Save corruption or world tampering through normal network traffic.
- Information disclosure from the server process, configuration, or save data.
- A vanilla vulnerability whose impact is materially increased by a Lithos change.

Out of scope:

- Vanilla behavior that Lithos does not change or amplify.
- Vulnerabilities in third-party mods.
- Issues that require shell access to the server host.
- Denial of service performed through an authorized administrator account.

Only the latest release and the current `main` branch receive security fixes.

## Safe harbor

Lithos maintainers will not pursue action against researchers who:

- Test only systems they own or have explicit permission to test.
- Avoid privacy violations, data loss, and unnecessary service disruption.
- Report findings privately and allow reasonable time for mitigation.
