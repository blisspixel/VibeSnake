# Manual Validation Tools

These programs require a person, a real audio or display device, or an explicitly configured external service. They are never imported by pytest and never run during normal CI.

Exact-candidate workspace preparation is native `product-review-prepare`. It writes pending session templates and does not record a review. Hash-bound listening records are native `radio-listening`. That command rehashes exact review copies, writes a pending template, and validates an explicit human record. It cannot change release approval, export eligibility, curation, or source bytes. The full-decode qualification campaign is native `radio-audio`, outside `all` and ordinary CI. It does not modify sources, curation, inventory, or export eligibility, and `releaseApproved` stays false. Station review-copy preparation is native `radio-review`, outside `all` and ordinary CI. It does not modify sources, curation, inventory, or export eligibility. `releaseApproved`, `sourceReplacementApproved`, and `exportEligibilityChanged` stay false, and `humanListeningStatus` stays pending. The program below is the remaining sample-preview tool. Native `radio-preview list` checks the archive boundary and lists the fixed eight-station catalog outside `all` and ordinary CI. It does not play audio, write a listening record, or approve a track. Native `radio-audio` and `radio-review` own probe, loudness, and silence parsing. `analyze_radio_audio.py` and its parser tests are removed.

- `preview_radio_samples.py`: lists or plays one fixed candidate from each station
  through Pygame. It accepts only the ignored archive or an external directory;
  `--list` never initializes an audio device. Playback is not a listening approval.

Automatable behavior belongs in `tests/` or `src/vibesnake/qa/`. A manual program belongs here only when the remaining judgment is genuinely perceptual or interactive.
