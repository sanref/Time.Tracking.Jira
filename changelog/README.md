# Changelog

One file per published version, named `<version>.md` (for example `4.0.1.md`).
The folder listing acts as the index: there's no separate index to maintain.

Each entry's version matches what the executable reports in `AssemblyFileVersion`, which
comes from `next-version` in [GitVersion.yml](../GitVersion.yml) as long as no major git
tag exists yet.

Each file describes **what changes for the application's users**. Implementation details
are only included when they explain a visible behavior change.
