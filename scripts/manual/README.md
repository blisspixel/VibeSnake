# Manual Validation Tools

These programs require a person, a real audio or display device, or an explicitly configured external service. They are never imported by pytest and never run during normal CI.

- `preview_radio_samples.py`: lists or plays one fixed candidate from each station
  through Pygame. It accepts only the ignored archive or an external directory;
  `--list` never initializes an audio device.

Automatable behavior belongs in `tests/` or `src/vibesnake/qa/`. A manual program belongs here only when the remaining judgment is genuinely perceptual or interactive.
