#!/usr/bin/env python3
"""Updater tests use local byte streams, never install a real update."""
import copy
import hashlib
import io
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

import worktimer_updates as updates


class UpdateTests(unittest.TestCase):
    def next_version(self):
        major, minor, _patch = (int(part) for part in updates.VERSION.split("."))
        return "%d.%d.0" % (major, minor + 1)

    def release(self):
        version = self.next_version()
        return {"tag_name": "v" + version, "draft": False, "prerelease": False,
                "html_url": updates.RELEASE_PREFIX + "tag/v" + version,
                "assets": [{"name": "worktimer_%s_all.deb" % version, "size": 3,
                            "browser_download_url": updates.RELEASE_PREFIX + "download/v%s/worktimer_%s_all.deb" % (version, version),
                            "digest": "sha256:" + hashlib.sha256(b"deb").hexdigest()}]}

    def test_stable_new_release(self):
        self.assertEqual(updates.parse_release(self.release())["version"], self.next_version())

    def test_no_downgrade_or_equal_version(self):
        for tag in ("v1.2.0", "v" + updates.VERSION, "v" + updates.VERSION + ".0", "nonsense", "v1.4.0-beta"):
            with self.subTest(tag=tag):
                r = self.release(); r["tag_name"] = tag
                self.assertIsNone(updates.parse_release(r))

    def test_draft_and_prerelease(self):
        for flag in ("draft", "prerelease"):
            r = self.release(); r[flag] = True
            self.assertIsNone(updates.parse_release(r))

    def test_invalid_asset(self):
        for key, value in (("digest", None), ("digest", "sha256:bad"),
                           ("browser_download_url", "https://example.com/file.deb"),
                           ("size", 0), ("size", updates.MAX_SIZE + 1)):
            with self.subTest(key=key, value=value):
                r = self.release(); r["assets"][0][key] = value
                with self.assertRaises(ValueError): updates.parse_release(r)

    def test_wrong_release_page(self):
        r = self.release(); r["html_url"] = "https://example.com/release"
        with self.assertRaises(ValueError): updates.parse_release(r)

    def test_missing_platform_asset(self):
        r = self.release(); r["assets"][0]["name"] = "WorkTimer.exe"
        with self.assertRaises(ValueError): updates.parse_release(r)

    def test_check_public_json(self):
        with patch.object(updates.urllib.request, "urlopen", return_value=io.BytesIO(json.dumps(self.release()).encode())) as opened:
            self.assertEqual(updates.check_release()["version"], self.next_version())
            self.assertEqual(opened.call_args.kwargs["timeout"], 15)
            self.assertNotIn("Authorization", opened.call_args.args[0].headers)

    def test_download_and_verify(self):
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(updates.urllib.request, "urlopen", return_value=io.BytesIO(b"deb")):
                path = updates.download(updates.parse_release(self.release()), folder)
            self.assertEqual(pathlib.Path(path).read_bytes(), b"deb")

    def test_corruption_and_partial_download_cleaned(self):
        for payload in (b"", b"de", b"bad", b"too many bytes"):
            with self.subTest(payload=payload), tempfile.TemporaryDirectory() as folder:
                with patch.object(updates.urllib.request, "urlopen", return_value=io.BytesIO(payload)):
                    with self.assertRaises(ValueError): updates.download(updates.parse_release(self.release()), folder)
                self.assertEqual(list(pathlib.Path(folder).iterdir()), [])

    def test_network_error_keeps_no_partial_file(self):
        with tempfile.TemporaryDirectory() as folder:
            with patch.object(updates.urllib.request, "urlopen", side_effect=OSError("offline")):
                with self.assertRaises(OSError): updates.download(updates.parse_release(self.release()), folder)
            self.assertEqual(list(pathlib.Path(folder).iterdir()), [])

    def test_api_response_limit(self):
        with patch.object(updates.urllib.request, "urlopen", return_value=io.BytesIO(b"x" * (1024 * 1024 + 1))):
            with self.assertRaises(ValueError): updates.check_release()


if __name__ == "__main__":
    unittest.main()
