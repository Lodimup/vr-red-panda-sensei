"""Learner profile storage. Profiles are atomically replaced, never repaired silently."""
from __future__ import annotations

import json
import os
import re
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PROFILE_ID = re.compile(r"^[a-z0-9_-]{1,40}$")
LEVELS = {"pre_n5", "n5", "n4", "n3", "n2", "n1"}
BANDS = {"zero", "early_beginner", "developing_beginner", "intermediate", "advanced"}
DEFAULT_SUPPORT = {"show_japanese": True, "show_reading": True, "show_romaji": True,
                   "show_translation": True, "pace": "normal", "correction_style": "light",
                   "language_mode": "english_explanations"}


class ProfileCorrupt(ValueError):
    pass


def now() -> str:
    return datetime.now(timezone.utc).isoformat()


def new_profile(profile_id: str) -> dict[str, Any]:
    timestamp = now()
    return {"version": 1, "profile_id": profile_id, "created_at": timestamp,
            "updated_at": timestamp, "starting_band": None, "self_reported_jlpt": None,
            "working_level": "pre_n5", "level_history": [], "support": DEFAULT_SUPPORT.copy(),
            "pinned": [], "evidence": [], "notes": {}, "sessions": []}


def validate_profile(profile: Any, profile_id: str) -> None:
    if not isinstance(profile, dict) or profile.get("version") != 1 or profile.get("profile_id") != profile_id:
        raise ProfileCorrupt("invalid profile identity or version")
    if profile.get("starting_band") not in (*BANDS, None) or profile.get("working_level") not in tuple(LEVELS):
        raise ProfileCorrupt("invalid band or level")
    if profile.get("self_reported_jlpt") not in (*(LEVELS - {"pre_n5"}), None):
        raise ProfileCorrupt("invalid self-reported level")
    for key in ("created_at", "updated_at"):
        if not isinstance(profile.get(key), str):
            raise ProfileCorrupt("invalid profile timestamp")
        try:
            datetime.fromisoformat(profile[key].replace("Z", "+00:00"))
        except ValueError as exc:
            raise ProfileCorrupt("invalid profile timestamp") from exc
    for key in ("level_history", "pinned", "evidence", "sessions"):
        if not isinstance(profile.get(key), list):
            raise ProfileCorrupt("invalid profile collection")
    if not isinstance(profile.get("notes"), dict):
        raise ProfileCorrupt("invalid notes")
    support = profile.get("support")
    if not isinstance(support, dict) or any(type(support.get(k)) is not bool for k in
        ("show_japanese", "show_reading", "show_romaji", "show_translation")):
        raise ProfileCorrupt("invalid support")
    if support.get("pace") not in ("normal", "slow") or support.get("correction_style") not in ("light", "detailed") or support.get("language_mode") not in ("english_explanations", "mixed", "japanese_only"):
        raise ProfileCorrupt("invalid support")
    if any(not isinstance(k, str) or k not in DEFAULT_SUPPORT for k in profile["pinned"]):
        raise ProfileCorrupt("invalid pinned preference")
    if any(not isinstance(s, dict) or not isinstance(s.get("preference_changes"), list)
           for s in profile["sessions"]):
        raise ProfileCorrupt("invalid session")
    if any(not isinstance(e, dict) or not isinstance(e.get("target"), dict) or
           any(not isinstance(e.get(k), list) for k in ("event_ids", "actors", "assistance"))
           for e in profile["evidence"]):
        raise ProfileCorrupt("invalid evidence")
    if any(not isinstance(e, dict) or e.get("from") not in tuple(LEVELS) or e.get("to") not in tuple(LEVELS)
           for e in profile["level_history"]):
        raise ProfileCorrupt("invalid level history")


class ProfileStore:
    def __init__(self, directory: Path) -> None:
        self.directory = Path(directory)

    def path(self, profile_id: str) -> Path:
        if not isinstance(profile_id, str) or not PROFILE_ID.fullmatch(profile_id):
            raise ValueError("invalid learner id")
        return self.directory / f"{profile_id}.json"

    def load(self, profile_id: str) -> dict[str, Any] | None:
        path = self.path(profile_id)
        try:
            with path.open(encoding="utf-8") as stream:
                profile = json.load(stream)
        except FileNotFoundError:
            return None
        except (UnicodeError, json.JSONDecodeError) as exc:
            raise ProfileCorrupt("profile is not valid JSON") from exc
        validate_profile(profile, profile_id)
        return profile

    def save(self, profile: dict[str, Any]) -> None:
        path = self.path(profile["profile_id"])
        validate_profile(profile, profile["profile_id"])
        self.directory.mkdir(parents=True, exist_ok=True)
        fd, temp = tempfile.mkstemp(prefix=".profile-", suffix=".tmp", dir=self.directory)
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as stream:
                json.dump(profile, stream, ensure_ascii=False, separators=(",", ":"))
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temp, path)
        finally:
            if os.path.exists(temp):
                os.unlink(temp)

    def delete(self, profile_id: str) -> None:
        self.path(profile_id).unlink(missing_ok=True)
