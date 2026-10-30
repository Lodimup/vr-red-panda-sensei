import unittest

from sensei_server.scene import Scene, SceneError


class SceneTests(unittest.TestCase):
    def test_revisions_selection_move_and_slots(self):
        scene = Scene()
        scene.spawn([{"catalog_id": "apple", "quantity": 2}], 1)
        self.assertEqual(scene.revision, 1)
        self.assertEqual([x["slot"] for x in scene.instances], [1, 2])
        scene.select("apple_1", 1)
        self.assertEqual(scene.revision, 1)
        self.assertEqual(scene.selected_id, "apple_1")
        scene.move("apple_1", "left", None, "player", 1)
        self.assertEqual(scene.revision, 2)
        self.assertEqual(scene.find("apple_1")["slot"], 1)
        self.assertEqual(scene.events[-1]["actor"], "player")
        self.assertNotIn("variant", scene.instances[0])

    def test_container_movement_and_failed_moves_are_atomic(self):
        scene = Scene()
        scene.spawn([{"catalog_id": "box"}, {"catalog_id": "apple"}], 1)
        scene.move("apple_1", None, "box_1", "player", 1)
        event = scene.move("box_1", "right", None, "player", 1)
        self.assertEqual(event["instance_ids"], ["box_1", "apple_1"])
        self.assertEqual(scene.find("apple_1")["region"], "right")
        before = scene.snapshot({}, None)
        with self.assertRaises(SceneError) as failure:
            scene.move("box_1", None, "apple_1", "player", 1)
        self.assertEqual(failure.exception.code, "invalid_destination")
        self.assertEqual(before, scene.snapshot({}, None))

    def test_spawn_validation_is_atomic(self):
        scene = Scene()
        for objects, code in [
            ([{"catalog_id": "apple"}, {"catalog_id": "unknown"}], "unknown_catalog_id"),
            ([{"catalog_id": "apple", "variant": "red"}], "invalid_arguments"),
            ([{"catalog_id": "apple", "region": "learner_hand"}], "invalid_arguments"),
        ]:
            with self.assertRaises(SceneError) as failure:
                scene.spawn(objects, 1)
            self.assertEqual(failure.exception.code, code)
            self.assertEqual(scene.revision, 0)
            self.assertEqual(scene.instances, [])


if __name__ == "__main__":
    unittest.main()
