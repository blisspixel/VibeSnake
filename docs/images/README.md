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

The source fingerprint covers every active Python module plus canonical asset
JSON and PNG files, including radio badges and the project logo. A source or
rendered-asset change makes the evidence stale until all three screens are
recaptured and visually reviewed. The captures use isolated temporary player and
audio directories and never expose a local username, save path, profile, or
ambient media library.

Capture state is fixed, but visible text still uses host font fallback. The gate
verifies committed bytes and source freshness; it does not claim that independent
regeneration on different operating systems produces byte-identical PNGs.

The project logo is original deterministic pixel geometry. Regenerate or verify
its canonical PNG with:

```powershell
python scripts/visual_generate_logo.py
python scripts/visual_generate_logo.py --check
```
