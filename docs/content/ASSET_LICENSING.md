# Asset Licensing

## Project license

Vibe Snake is licensed under the [Apache License 2.0](../../LICENSE). The
[NOTICE](../../NOTICE) file records the project attribution that must travel with
distributed copies.

The project owner released the curated eight-station Vibe Snake radio catalog
under Apache-2.0 as original game soundtrack material. Those tracks live under
`assets/audio/radio/` and are discovered at runtime by filename prefix. Policy
records them with cleared rights and optional runtime use. Ship approval for
native export packs still requires loudness, credit, and allowlist review.

Historical production tooling and rejected candidates may still exist only in
ignored local archives. They are not part of the public radio pack.

## Rights are not release approval

A cleared Apache-2.0 rights record answers whether the project may distribute a
file under the project license. It does not establish that the file is good
enough to ship. A runtime audio asset remains blocked until both provenance and
its applicable quality checks pass:

- complete decode with the release decoder;
- peak, true-peak, clipping, silence, duration, and loudness analysis;
- focused listening review on representative headphones and speakers;
- station identity or typed sound-cue assignment;
- duplicate and near-duplicate review;
- exact content-pack assignment and credit binding.

This separation prevents a truthful ownership declaration from silently becoming
a quality claim.

## Repository and pack delivery

The curated radio catalog ships in public source under `assets/audio/radio/` so
clones receive the full GTA-style station experience offline. Every track is
under the GitHub ordinary-file size limit. Rejected tracks, working stems,
generated analysis, and private production history remain ignored under
`archive/` rather than polluting clones or CI.

Native export packs may still deliver a subset through exact manifests that bind
file hashes, byte sizes, credits, compatibility range, and pack identity. The
player remains playable when optional audio is muted or unavailable and must
never depend on a streaming service at runtime.

## Exceptions and future contributions

Third-party dependencies retain their own licenses. Any future asset with terms
different from Apache-2.0 must have an exact policy rule, attribution, source,
review record, and pack credit before it enters the repository or a player build.
A filename, generator log, or verbal assumption is not enough evidence for a
third-party asset.

The checked-in station badges use project-owned 5x7 pixel glyphs, integer-only
drawing, and a closed native PNG encoder so exact PNG bytes match across platforms.
[NOTICE](../../NOTICE) retains the project attribution for generated artwork.
