import tempfile
import unittest
from pathlib import Path

from sensei_server.profile import ProfileCorrupt, ProfileStore, new_profile


class ProfileTests(unittest.TestCase):
    def test_round_trip_and_corruption_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            store = ProfileStore(Path(directory))
            profile = new_profile("learner_1")
            store.save(profile)
            self.assertEqual(store.load("learner_1"), profile)
            path = store.path("learner_1")
            path.write_text("{bad", encoding="utf-8")
            with self.assertRaises(ProfileCorrupt):
                store.load("learner_1")
            self.assertEqual(path.read_text(encoding="utf-8"), "{bad")

    def test_ids_cannot_escape_profile_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            store = ProfileStore(Path(directory))
            for ident in ("../learner", "UPPER", "", "x" * 41):
                with self.assertRaises(ValueError):
                    store.path(ident)
