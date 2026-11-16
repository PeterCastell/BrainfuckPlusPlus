"""Download a zig release from a community mirror and verify it against the official hash.

Usage:
    python fetch_zig.py <version> <target> <out_dir>

    version  zig release, e.g. 0.17.1 (must be a tagged release, not a dev build)
    target   zig target name, e.g. x86_64-linux, x86_64-windows, aarch64-macos
    out_dir  directory to extract into; zig ends up directly inside it (out_dir/zig[.exe])

The expected SHA-256 comes from the official release index on ziglang.org. The archive itself
is downloaded from the community mirrors listed on ziglang.org (in random order, as the zig
project asks), falling back to ziglang.org only if every mirror fails. Any download whose hash
doesn't match is thrown away.
"""

import hashlib
import json
import random
import shutil
import sys
import tarfile
import tempfile
import urllib.request
import zipfile
from pathlib import Path

INDEX_URL = "https://ziglang.org/download/index.json"
MIRRORS_URL = "https://ziglang.org/download/community-mirrors.txt"
# The zig project asks mirror users to identify their tool with a ?source= parameter.
SOURCE_TAG = "bfpp-github-actions"
USER_AGENT = "bfpp-release-workflow"


def fetch(url: str, timeout: float = 60) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def main() -> int:
    if len(sys.argv) != 4:
        print(__doc__)
        return 2
    version, target, out_dir = sys.argv[1], sys.argv[2], Path(sys.argv[3])

    index = json.loads(fetch(INDEX_URL))
    try:
        entry = index[version][target]
    except KeyError:
        print(f"error: zig {version} for {target} is not in the official release index", file=sys.stderr)
        return 1
    official_url = entry["tarball"]
    expected_sha = entry["shasum"].lower()
    file_name = official_url.rsplit("/", 1)[1]
    print(f"zig {version} {target}: {file_name}, expected sha256 {expected_sha}")

    mirrors = [m.strip().rstrip("/") for m in fetch(MIRRORS_URL).decode().splitlines() if m.strip()]
    random.shuffle(mirrors)
    candidates = [f"{m}/{file_name}?source={SOURCE_TAG}" for m in mirrors] + [official_url]

    data = None
    for url in candidates:
        try:
            print(f"trying {url}")
            blob = fetch(url, timeout=300)
        except Exception as e:  # network errors, 404s, timeouts: try the next mirror
            print(f"  failed: {e}")
            continue
        actual_sha = hashlib.sha256(blob).hexdigest()
        if actual_sha != expected_sha:
            print(f"  HASH MISMATCH (got {actual_sha}), discarding")
            continue
        print("  sha256 verified")
        data = blob
        break

    if data is None:
        print("error: could not download a verified copy of zig from any source", file=sys.stderr)
        return 1

    if out_dir.exists():
        shutil.rmtree(out_dir)
    with tempfile.TemporaryDirectory() as tmp:
        archive = Path(tmp) / file_name
        archive.write_bytes(data)
        extract_dir = Path(tmp) / "extract"
        if file_name.endswith(".zip"):
            with zipfile.ZipFile(archive) as z:
                z.extractall(extract_dir)
        else:
            with tarfile.open(archive) as t:
                t.extractall(extract_dir, filter="tar")  # "tar" keeps executable bits, blocks path escapes
        # The archive holds a single top-level folder (zig-<target>-<version>); move it to out_dir.
        (top,) = list(extract_dir.iterdir())
        out_dir.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(top), str(out_dir))

    print(f"zig extracted to {out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
