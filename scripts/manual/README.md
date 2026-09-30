# Manual Validation Tools

These programs require a person, a real audio or display device, or an explicitly configured external service. They are never imported by pytest and never run during normal CI.

Exact-candidate workspace preparation is native `product-review-prepare`. It writes pending session templates and does not record a review. Hash-bound listening records are native `radio-listening`. That command rehashes exact review copies, writes a pending template, and validates an explicit human record. It cannot change release approval, export eligibility, curation, or source bytes. The programs below are the remaining FFmpeg analysis, review-copy preparation, and sample preview tools.

- `preview_radio_samples.py`: lists or plays one fixed candidate from each station
  through Pygame. It accepts only the ignored archive or an external directory;
  `--list` never initializes an audio device.
- `analyze_radio_audio.py`: fully decodes and measures all 95 inventoried public
  radio tracks with FFmpeg, verifies the entire source set before and after the
  concurrent campaign, and
  writes review evidence only under `TestResults/`, the ignored archive, or an
  external directory. Its EBU R 128 measurements can fail technical admission,
  but they can never approve a track or replace listening review.
- `prepare_radio_review_copies.py`: prepares one complete 11-to-13-track station
  at a time as ignored lossless FLAC review copies. It requires the exact analyzer
  evidence, performs trim-aware two-pass loudness normalization, fully decodes and
  remeasures every output, rehashes all 95 sources before publication, and emits a
  hash-bound manifest with every approval and export flag forced false. Use
  `--station the_bureau` for the current first-listening queue.

Automatable behavior belongs in `tests/` or `src/vibesnake/qa/`. A manual program belongs here only when the remaining judgment is genuinely perceptual or interactive.
