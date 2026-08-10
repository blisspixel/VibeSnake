# Third-Party Notices

Status: release-material foundation. The final notice bundle must be regenerated and verified against each exact candidate artifact.

Vibe Snake source is licensed under the [Apache License 2.0](LICENSE). The following third-party components are part of the current runtime or build path and remain under their own terms.

## Native player runtime

### Godot Engine 4.7.1 .NET

Godot Engine and its C# assemblies are licensed under the MIT License. The qualified toolchain is fixed to official commit `a13da4feb`. Copyright and complete license text supplied by the Godot project must remain with distributed binaries.

### Microsoft .NET

The self-contained .NET runtime components packaged by the Godot export remain under their applicable Microsoft open-source licenses and third-party notices. The final artifact must retain the notice files produced for the exact runtime identifier and must be checked against the packaged file inventory.

## Temporary Python reference player

The source-playable alpha uses Python, Pygame CE, packaging, setuptools, and wheel from the exact versions in `requirements-runtime.lock`. This is not the 1.0 native runtime. Source and wheel distributions must retain the license metadata installed by those packages.

## Development-only dependencies

Test, coverage, lint, audit, build, and packaging dependencies listed only in `requirements-ci.lock` or native test projects are not player runtime components. Their notices belong in development and source distributions when required, but must not be represented as shipped player payload merely because CI uses them.

## Candidate verification

Before release, generate the lock-derived dependency inventory, inspect the actual artifact, and replace this pending status with an exact per-platform notice set. The notice set must:

1. name every third-party runtime component actually shipped;
2. record its exact version and applicable license;
3. retain required copyright and license text;
4. exclude development-only packages absent from the player;
5. bind to the candidate manifest SHA-256 and source revision;
6. remain byte-identical to the copy included in the public artifact.

Current qualification evidence proves dependency identity and artifact contents, but protected final packaging and notice inclusion remain pending.
