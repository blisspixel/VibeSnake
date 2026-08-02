"""Schema-versioned local top-ten score persistence.

Entries are sorted by descending score and truncated to the ten rows that fit
the current leaderboard screen. The limit is a presentation contract, not a
claim about motivation or memory. Missing names normalize to ``Anonymous``.
"""

import json
from pathlib import Path
from datetime import datetime
from typing import List, Optional, Union

from vibesnake.data.paths import get_data_dir
from vibesnake.data.json_store import (
    UnsupportedSchemaVersionError,
    atomic_write_json,
    backup_corrupt_file,
)


class HighScoreEntry:
    """Mutable, serializable leaderboard name, score, and timestamp."""

    def __init__(self, name: str, score: int, timestamp: str = None):
        """Create an entry, generating a local ISO timestamp when omitted."""
        self.name = name
        self.score = score
        self.timestamp = timestamp or datetime.now().isoformat()

    def to_dict(self) -> dict:
        """Convert to dictionary for JSON serialization."""
        return {"name": self.name, "score": self.score, "timestamp": self.timestamp}

    @staticmethod
    def from_dict(data: dict) -> "HighScoreEntry":
        """Create entry from dictionary."""
        name = str(data.get("name") or "Anonymous").strip() or "Anonymous"
        try:
            score = max(0, int(data.get("score", 0)))
        except (TypeError, ValueError):
            score = 0
        return HighScoreEntry(name=name, score=score, timestamp=data.get("timestamp"))


class HighScoreTable:
    """Persist a descending, top-ten local leaderboard.

    Writes are schema-versioned and atomic. Corrupt current-schema input is
    preserved as a backup. A newer unsupported schema blocks writes.
    """

    MAX_ENTRIES = 10
    SCHEMA_VERSION = 1
    LEGACY_FILE_NAME = "highscore.json"

    def __init__(self, data_dir: Optional[Union[str, Path]] = None):
        """Resolve storage, restore scores, and migrate the legacy single score."""
        self.data_dir = get_data_dir(data_dir)
        self.scores_file = self.data_dir / "high_scores.json"
        self.legacy_scores_file = self.data_dir / self.LEGACY_FILE_NAME
        self.scores: List[HighScoreEntry] = []
        self._legacy_highscore_migrated = False
        self._write_blocked = False
        self._load_scores()
        self._migrate_legacy_high_score()

    def _load_scores(self):
        """Load high scores from file."""
        if self.scores_file.exists():
            try:
                with open(self.scores_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    if not isinstance(data, dict):
                        raise ValueError("leaderboard root must be a JSON object")
                    schema_version = int(data.get("schema_version", 0))
                    if schema_version > self.SCHEMA_VERSION:
                        self._write_blocked = True
                        raise UnsupportedSchemaVersionError(
                            f"leaderboard schema {schema_version} is newer than supported {self.SCHEMA_VERSION}"
                        )
                    raw_scores = data.get("scores", [])
                    if not isinstance(raw_scores, list):
                        raw_scores = []
                    migrations = data.get("migrations", {})
                    if isinstance(migrations, dict):
                        self._legacy_highscore_migrated = bool(migrations.get("legacy_highscore_json", False))
                    self.scores = [HighScoreEntry.from_dict(entry) for entry in raw_scores if isinstance(entry, dict)]
                    self.scores.sort(key=lambda entry: entry.score, reverse=True)
                    self.scores = self.scores[: self.MAX_ENTRIES]
                    print(f"[HighScores] Loaded {len(self.scores)} score(s)")
                if schema_version < self.SCHEMA_VERSION:
                    self._save_scores()
            except UnsupportedSchemaVersionError as e:
                print(f"[HighScores] Failed to load: {e}")
                self.scores = []
            except Exception as e:
                backup = backup_corrupt_file(self.scores_file)
                print(f"[HighScores] Failed to load: {e}")
                if backup:
                    print(f"[HighScores] Preserved unreadable save at {backup.name}")
                self.scores = []
        else:
            self.scores = []

    def _migrate_legacy_high_score(self):
        """Import the former HUD-owned single-score file exactly once."""
        if self._write_blocked or self._legacy_highscore_migrated or not self.legacy_scores_file.exists():
            return

        try:
            with open(self.legacy_scores_file, "r", encoding="utf-8") as f:
                data = json.load(f)

            if isinstance(data, dict):
                raw_score = data.get("high_score", 0)
                name = str(data.get("name") or "Anonymous").strip() or "Anonymous"
                timestamp = data.get("date")
            else:
                raw_score = data
                name = "Anonymous"
                timestamp = None

            if isinstance(raw_score, bool):
                score = 0
            else:
                score = max(0, int(raw_score))

            already_present = any(entry.name == name and entry.score == score for entry in self.scores)
            if score > 0 and not already_present:
                self.scores.append(HighScoreEntry(name, score, timestamp))
                self.scores.sort(key=lambda entry: entry.score, reverse=True)
                self.scores = self.scores[: self.MAX_ENTRIES]

            self._legacy_highscore_migrated = True
            self._save_scores()
            print(f"[HighScores] Migrated legacy score from {self.legacy_scores_file.name}")
        except (OSError, TypeError, ValueError, json.JSONDecodeError) as e:
            print(f"[HighScores] Failed to migrate legacy score: {e}")

    def _save_scores(self):
        """Save high scores to file."""
        if self._write_blocked:
            print("[HighScores] Save skipped because the file uses a newer schema")
            return
        try:
            atomic_write_json(
                self.scores_file,
                {
                    "schema_version": self.SCHEMA_VERSION,
                    "migrations": {
                        "legacy_highscore_json": self._legacy_highscore_migrated,
                    },
                    "scores": [entry.to_dict() for entry in self.scores],
                },
            )
            print(f"[HighScores] Saved {len(self.scores)} score(s)")
        except Exception as e:
            print(f"[HighScores] Failed to save: {e}")

    def is_high_score(self, score: int) -> bool:
        """
        Check if a score qualifies for the high score table.

        Args:
            score: Score to check

        Returns:
            True if score makes top 10
        """
        if len(self.scores) < self.MAX_ENTRIES:
            return True
        return score > self.scores[-1].score

    def get_rank(self, score: int) -> Optional[int]:
        """
        Get the rank (1-10) this score would achieve.

        Args:
            score: Score to check

        Returns:
            Rank (1-10) or None if doesn't qualify
        """
        if not self.is_high_score(score):
            return None

        for i, entry in enumerate(self.scores):
            if score > entry.score:
                return i + 1

        return len(self.scores) + 1

    def add_score(self, name: str, score: int) -> int:
        """Insert a score, retain the top ten, persist, and return its rank.

        Callers are responsible for checking ``is_high_score`` first. Insertion
        preserves existing order for equal scores.
        """
        entry = HighScoreEntry(name, score)

        # Insert in correct position (sorted by score descending)
        inserted_at = len(self.scores)
        for i, existing in enumerate(self.scores):
            if score > existing.score:
                self.scores.insert(i, entry)
                inserted_at = i
                break
        else:
            # If not inserted, append to end
            self.scores.append(entry)

        # Keep only top MAX_ENTRIES
        if len(self.scores) > self.MAX_ENTRIES:
            self.scores = self.scores[: self.MAX_ENTRIES]

        self._save_scores()
        print(f"[HighScores] New high score! Rank {inserted_at + 1}: {name} - {score}")
        return inserted_at + 1

    def get_top_scores(self, limit: int = None) -> List[HighScoreEntry]:
        """
        Get top scores.

        Args:
            limit: Maximum number of entries to return (default: all)

        Returns:
            List of high score entries
        """
        if limit is None:
            return self.scores[:]
        return self.scores[:limit]

    def clear_scores(self):
        """Clear all high scores."""
        self.scores = []
        self._save_scores()
        print("[HighScores] Cleared all scores")
