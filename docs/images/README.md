# Project Images

The root README uses current-build captures stored under `screenshots/`.

Regenerate the set after a presentation-affecting change:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- screenshots-write <godot-executable> .
```

Verify the committed PNG hashes, dimensions, README references, and aggregate
presentation-source fingerprint without opening a window:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- screenshots .
```

The source fingerprint covers the Godot project, native C# rules and persistence
sources, the pinned native toolchain, the native screenshot and full PNG validators,
and presentation assets. A source or rendered-asset change makes the evidence stale
until all four screens are recaptured and visually reviewed. Capture uses an
explicit verified Godot executable, separate temporary output and player-data
directories, validates the complete staged set before replacement, and writes the
manifest last. It never records a local username, save path, or profile.

Capture state is fixed, but visible text still uses Godot host font fallback. The
gate verifies committed bytes and source freshness; it does not claim that
independent regeneration on different operating systems produces byte-identical
PNGs. Current roles are title menu, Vibe gameplay, customization, and AI channel.

The project logo is the preferred handcrafted 1024 by 1024 brand mark. Verify it
with:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- logo .
```
