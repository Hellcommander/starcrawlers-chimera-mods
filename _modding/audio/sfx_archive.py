"""Seal each mod's sounds into one archive, <mod>/sounds.pak, which the loader plays from (see ModArchives.cs for the format).
Sounds are stored as lossless FLAC, or as they are if that is smaller, each encrypted with AES-256.

The key is archive.key next to this script: 32 random bytes as hex, made here on first use and never committed
(.gitignore). ClassLoader.csproj builds the same key into the loader, so the dll and the paks must come from the same key;
a loader built without the file skips archives and plays the fallback sounds. Losing the key costs nothing but a repack
and a rebuild. This keeps the audio out of casual reach and off the disk as loose files; someone who decompiles the
shipped loader can still find the key, so it is sealing, not secrecy.

Usage: python sfx_archive.py <Mods folder> [--remove] [--key <file>]
  --remove deletes the loose sounds it packed: for release copies, never for the working Mods folder.
"""
import argparse
import glob
import hashlib
import hmac
import io
import os
import struct

import soundfile
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

MAGIC = b"BWPK3\0"
KINDS = (".wav", ".ogg", ".mp3")
DEFAULT_KEY = os.path.join(os.path.dirname(os.path.abspath(__file__)), "archive.key")


def load_key(path):
    if not os.path.exists(path):
        with open(path, "w", encoding="ascii") as f:
            f.write(os.urandom(32).hex() + "\n")
        print(f"made a new archive key: {path} (keep it out of the repository; rebuild the loader so it carries it)")
    key = bytes.fromhex(open(path, encoding="ascii").read().strip())
    if len(key) != 32:
        raise SystemExit(f"{path}: expected 32 bytes as hex")
    return key


def key_id(key):
    return hashlib.sha256(key).digest()[:8]


def entry_key(key, name):
    return hmac.new(key, name.lower().encode("utf-8"), hashlib.sha256).digest()


def seal(key, name, data):
    iv = os.urandom(16)
    pad = 16 - len(data) % 16
    enc = Cipher(algorithms.AES(entry_key(key, name)), modes.CBC(iv)).encryptor()
    return iv, enc.update(data + bytes([pad]) * pad) + enc.finalize()


def stored(path):
    raw = open(path, "rb").read()
    if not path.lower().endswith(".wav"):
        return raw
    x, sr = soundfile.read(path, dtype="int16", always_2d=True)
    out = io.BytesIO()
    soundfile.write(out, x, sr, format="FLAC", subtype="PCM_16")
    flac = out.getvalue()
    return flac if len(flac) < len(raw) else raw


def pack(key, folder, names, out):
    blobs = []
    raw_total = 0
    for name in names:
        path = os.path.join(folder, name)
        raw_total += os.path.getsize(path)
        iv, data = seal(key, name, stored(path))
        blobs.append((name.encode("utf-8"), iv, data))
    offset = len(MAGIC) + 8 + 4 + sum(2 + len(n) + 12 + 16 for n, _, _ in blobs)
    index = []
    for n, iv, data in blobs:
        index.append(struct.pack("<H", len(n)) + n + struct.pack("<qi", offset, len(data)) + iv)
        offset += len(data)
    with open(out, "wb") as f:
        f.write(MAGIC + key_id(key) + struct.pack("<i", len(blobs)) + b"".join(index))
        for _, _, data in blobs:
            f.write(data)
    return raw_total, offset


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mods")
    ap.add_argument("--remove", action="store_true")
    ap.add_argument("--key", default=DEFAULT_KEY)
    args = ap.parse_args()
    key = load_key(args.key)
    total = packed = 0
    for mod in sorted(d for d in glob.glob(os.path.join(args.mods, "*")) if os.path.isdir(d)):
        files = [f for f in glob.glob(os.path.join(mod, "**", "*"), recursive=True) if f.lower().endswith(KINDS)]
        if not files:
            continue
        names = sorted(os.path.relpath(f, mod).replace(os.sep, "/") for f in files)
        raw, size = pack(key, mod, names, os.path.join(mod, "sounds.pak"))
        total += raw; packed += size
        print(f"{os.path.basename(mod)}: {len(names)} sounds, {raw / 1048576:.1f} -> {size / 1048576:.1f} MB")
        if args.remove:
            for f in files:
                os.remove(f)
    print(f"all: {total / 1048576:.1f} -> {packed / 1048576:.1f} MB (key {key_id(key).hex()})")


if __name__ == "__main__":
    main()
