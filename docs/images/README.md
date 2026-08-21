# Project Images

The root README uses current-build captures stored under `screenshots/`.

Regenerate the set after a presentation-affecting change:

```powershell
python scripts/capture_readme_screenshots.py
```

Verify the committed PNG hashes, dimensions, README references, and aggregate
presentation-source fingerprint without opening a window:

```powershell
python scripts/capture_readme_screenshots.py --check
```

The source fingerprint covers the Godot project, native C# rules and persistence
sources, the screenshot tool, and presentation assets. A source or rendered-asset
change makes the evidence stale until all four screens are recaptured and visually
reviewed. The capture mode uses an isolated temporary player-data directory and
never exposes a local username, save path, or profile.

Capture state is fixed, but visible text still uses Godot host font fallback. The
gate verifies committed bytes and source freshness; it does not claim that
independent regeneration on different operating systems produces byte-identical
PNGs. Current roles are title menu, Vibe gameplay, customization, and AI channel.

The project logo is the preferred handcrafted 1024 by 1024 brand mark. Verify it
with:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- logo .
```
