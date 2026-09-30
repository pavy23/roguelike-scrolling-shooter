"""Offline checks for duplicate-job prevention and lossless output provenance."""
import argparse
import base64
import contextlib
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

MODULE_PATH = Path(__file__).resolve().parents[1] / "artgen.py"
SPEC = importlib.util.spec_from_file_location("artgen", MODULE_PATH)
artgen = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(artgen)
SPRITE = MODULE_PATH.parents[2] / "Assets/Art/Sprites/player_ship.png"


class NativeGenerationSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.prompt = self.root / "prompt.txt"
        self.prompt.write_text("A new native sprite.", encoding="utf-8")
        self.args = argparse.Namespace(out_dir=str(self.root / "output"), resume=False,
            prompt_file=str(self.prompt), width=48, height=30, seed=42, reference=str(SPRITE))
        self.png = SPRITE.read_bytes()
        self.result = {"status": "completed", "last_response": {
            "images": [{"base64": base64.b64encode(self.png).decode("ascii")}]}}

    def generate(self):
        with patch.object(artgen, "_require_env", return_value="test-only-key"), \
             patch.object(artgen, "_post_json", return_value={"background_job_id": "test-job"}) as post, \
             patch.object(artgen, "_wait_background_job", return_value=self.result), \
             contextlib.redirect_stdout(io.StringIO()):
            artgen.cmd_native(self.args)
        return post

    def test_downloaded_sprite_is_preserved_and_key_is_not_recorded(self):
        post = self.generate()
        post.assert_called_once()
        out = Path(self.args.out_dir)
        self.assertEqual((out / "candidate_00.png").read_bytes(), self.png)
        provenance = json.loads((out / "outputs.json").read_text(encoding="utf-8"))
        self.assertEqual(provenance["outputs"][0]["sha256"], hashlib.sha256(self.png).hexdigest())
        request = (out / "request.json").read_text(encoding="utf-8")
        self.assertNotIn("test-only-key", request)
        self.assertNotIn(base64.b64encode(self.png).decode("ascii"), request)

    def test_existing_output_cannot_accidentally_buy_another_job(self):
        self.generate()
        with patch.object(artgen, "_post_json") as post:
            with self.assertRaises(SystemExit):
                artgen.cmd_native(self.args)
            post.assert_not_called()

    def test_resume_polls_saved_job_without_new_generation(self):
        self.generate()
        self.args.resume = True
        with patch.object(artgen, "_require_env", return_value="test-only-key"), \
             patch.object(artgen, "_post_json") as post, \
             patch.object(artgen, "_wait_background_job", return_value=self.result) as poll, \
             contextlib.redirect_stdout(io.StringIO()):
            artgen.cmd_native(self.args)
            post.assert_not_called()
            poll.assert_called_once_with("test-job", "test-only-key")

    def test_resume_with_changed_prompt_is_rejected_before_network(self):
        self.generate()
        self.args.resume = True
        self.prompt.write_text("A different sprite.", encoding="utf-8")
        with patch.object(artgen, "_post_json") as post, patch.object(artgen, "_wait_background_job") as poll:
            with self.assertRaises(SystemExit):
                artgen.cmd_native(self.args)
            post.assert_not_called()
            poll.assert_not_called()


class AnimationGenerationSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.args = argparse.Namespace(out_dir=str(Path(self.temp.name) / "animation"),
            resume=False, first=str(SPRITE), last=str(SPRITE), action="Only exhaust flickers.",
            frames=4, seed=42, drift_threshold=0.02)
        self.image = {"base64": base64.b64encode(SPRITE.read_bytes()).decode("ascii")}
        self.result = {"status": "completed", "echoed_request": {"first_frame": self.image},
            "last_response": {"images": [self.image] * 4, "thumbnail": self.image}}

    def generate(self):
        with patch.object(artgen, "_require_env", return_value="test-only-key"), \
             patch.object(artgen, "_post_json", return_value={"background_job_id": "animation-job"}) as post, \
             patch.object(artgen, "_wait_background_job", return_value=self.result), \
             contextlib.redirect_stdout(io.StringIO()):
            artgen.cmd_animate(self.args)
        return post

    def test_only_output_frames_are_saved_not_input_echoes_or_thumbnails(self):
        self.generate()
        out = Path(self.args.out_dir)
        self.assertEqual(len(list(out.glob("frame_*.png"))), 4)
        self.assertEqual((out / "frame_00.png").read_bytes(), SPRITE.read_bytes())
        request = (out / "request.json").read_text(encoding="utf-8")
        self.assertNotIn(self.image["base64"], request)
        self.assertNotIn("test-only-key", request)
        self.assertEqual(json.loads((out / "job.json").read_text())["background_job_id"], "animation-job")

    def test_completed_output_prevents_duplicate_paid_animation(self):
        self.generate()
        with patch.object(artgen, "_post_json") as post:
            with self.assertRaises(SystemExit):
                artgen.cmd_animate(self.args)
            post.assert_not_called()

    def test_timeout_can_resume_without_buying_another_job(self):
        with patch.object(artgen, "_require_env", return_value="test-only-key"), \
             patch.object(artgen, "_post_json", return_value={"background_job_id": "animation-job"}), \
             patch.object(artgen, "_wait_background_job", side_effect=SystemExit("timeout")), \
             contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit):
                artgen.cmd_animate(self.args)
        self.args.resume = True
        post = self.generate()
        post.assert_not_called()
        self.args.action = "A different animation."
        with patch.object(artgen, "_post_json") as post, patch.object(artgen, "_wait_background_job") as poll:
            with self.assertRaises(SystemExit):
                artgen.cmd_animate(self.args)
            post.assert_not_called()
            poll.assert_not_called()

    def test_invalid_frame_count_is_rejected_before_network(self):
        self.args.frames = 3
        with patch.object(artgen, "_post_json") as post:
            with self.assertRaises(SystemExit):
                artgen.cmd_animate(self.args)
            post.assert_not_called()


if __name__ == "__main__":
    unittest.main()
