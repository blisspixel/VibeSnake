# Asset Licensing

## Project license

Vibe Snake is licensed under the [Apache License 2.0](../../LICENSE). The
[NOTICE](../../NOTICE) file records the project attribution that must travel with
distributed copies.

The project owner confirmed on 2026-08-01 an intent to release the Vibe Snake
music and sound effects under Apache-2.0. Historical production records identify
ElevenLabs-assisted generation for at least part of the library. The owner
declaration alone does not establish that every candidate was made on an account
tier and feature state whose terms permit public, commercial redistribution.
Until those facts are bound to exact files, audio rights remain `unverified` and
the files remain blocked from public source and player artifacts.

For each service-assisted batch, release evidence must record the generation
date, account tier, whether the feature was beta, the applicable terms revision,
required attribution, and the exact output hashes. This is a provenance check,
not a challenge to the owner's creative direction or intended license. Current
[ElevenLabs terms](https://elevenlabs.io/terms-of-use) and
[commercial-use guidance](https://help.elevenlabs.io/hc/en-us/articles/13313564601361-Can-I-publish-the-content-I-generate-on-the-platform)
must be reviewed alongside the applicable
[Music Terms](https://elevenlabs.io/music-terms),
[archived 2025-11-21 Music Terms](https://elevenlabs.io/eleven-music-v1-terms-archived-nov-21-2025),
and [Use Policy](https://elevenlabs.io/use-policy). The exact generation date,
model, feature state, and account plan determine which revision applies before
policy can change from `unverified` to `cleared`.

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

The small, rights-cleared core cue set may live with the source when its total
size remains appropriate for every clone. The full radio catalog must not enter
Git history or routine CI checkout. Approved station packs are separate,
versioned release assets or mirrored pack downloads whose canonical manifests
bind every file hash, byte size, credit, compatibility range, and pack identity.
The player verifies the manifest before use and remains complete offline when
all optional packs are absent.

GitHub blocks ordinary Git files larger than 100 MiB and recommends repositories
remain ideally below 1 GB. Git LFS also has storage and download quotas that make
it unsuitable as the default delivery path for a large public soundtrack.
Rejected tracks, working stems, generated analysis, and local production history
therefore remain ignored rather than inflating clones or CI. See GitHub's
[large-file guidance](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github)
and [release-asset guidance](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

Ignoring a local archive does not make a rights claim. It keeps non-player
production history and unresolved candidates out of a public source checkout.
Curated audio remains offline-capable after an explicit pack install and must
never depend on a streaming service at runtime.

## Exceptions and future contributions

Third-party dependencies retain their own licenses. Any future asset with terms
different from Apache-2.0 must have an exact policy rule, attribution, source,
review record, and pack credit before it enters the repository or a player build.
A filename, generator log, or verbal assumption is not enough evidence for a
third-party asset.

The checked-in station badges use project-owned 5x7 pixel glyphs so PNG bytes
match across platforms. [NOTICE](../../NOTICE) retains the project attribution
for generated artwork.
