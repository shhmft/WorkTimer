"""GitHub release discovery and verified downloads; no GTK dependency."""
import hashlib
import json
import os
import pathlib
import re
import tempfile
import time
import urllib.request

VERSION = "1.3.0"
ENDPOINT = "https://api.github.com/repos/shhmft/WorkTimer/releases/latest"
RELEASE_PREFIX = "https://github.com/shhmft/WorkTimer/releases/"
MAX_SIZE = 100 * 1024 * 1024


def version_tuple(tag):
    match = re.fullmatch(r"[vV]?(\d+)\.(\d+)\.(\d+)(?:\.0)?", tag)
    return tuple(int(part) for part in match.groups()) if match else None


def parse_release(data):
    if data.get("draft") or data.get("prerelease"):
        return None
    version = version_tuple(data.get("tag_name", ""))
    if version is None or version <= version_tuple(VERSION):
        return None
    tag = ".".join(str(part) for part in version)
    page = data.get("html_url", "")
    if not page.startswith(RELEASE_PREFIX):
        raise ValueError("Неверный адрес релиза.")
    for asset in data.get("assets", []):
        if asset.get("name") != "worktimer_%s_all.deb" % tag:
            continue
        url, digest, size = asset.get("browser_download_url", ""), asset.get("digest", ""), asset.get("size", 0)
        if (not url.startswith(RELEASE_PREFIX + "download/") or not isinstance(size, int)
                or not 0 < size <= MAX_SIZE or not isinstance(digest, str)
                or not re.fullmatch(r"sha256:[a-fA-F0-9]{64}", digest)):
            raise ValueError("У файла обновления нет корректной контрольной суммы SHA-256.")
        return {"version": tag, "url": url, "digest": digest[7:].lower(), "size": size, "page": page}
    raise ValueError("Linux-сборка нового релиза ещё не опубликована.")


def request(url):
    return urllib.request.Request(url, headers={"User-Agent": "WorkTimer/" + VERSION,
                                               "Accept": "application/vnd.github+json"})


def check_release():
    with urllib.request.urlopen(request(ENDPOINT), timeout=15) as response:
        data = response.read(1024 * 1024 + 1)
        if len(data) > 1024 * 1024:
            raise ValueError("Ответ сервера слишком большой.")
    return parse_release(json.loads(data))


def verify(path, size, digest):
    hash_value = hashlib.sha256()
    with open(path, "rb") as source:
        for block in iter(lambda: source.read(65536), b""):
            hash_value.update(block)
    if os.path.getsize(path) != size or hash_value.hexdigest() != digest.lower():
        raise ValueError("Файл обновления повреждён: контрольная сумма не совпала.")


def download(release, folder):
    folder = pathlib.Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix="worktimer_%s_" % release["version"], suffix=".deb", dir=str(folder))
    try:
        with os.fdopen(fd, "wb") as output:
            with urllib.request.urlopen(request(release["url"]), timeout=15) as response:
                written, deadline = 0, time.monotonic() + 300
                while True:
                    block = response.read(65536)
                    if not block:
                        break
                    written += len(block)
                    if written > release["size"] or time.monotonic() > deadline:
                        raise ValueError("Загрузка обновления прервана.")
                    output.write(block)
        verify(name, release["size"], release["digest"])
        return name
    except Exception:
        pathlib.Path(name).unlink(missing_ok=True)
        raise
