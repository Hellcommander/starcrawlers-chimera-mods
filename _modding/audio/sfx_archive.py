"""Seal a mod's sounds into one archive, <mod>/sounds.pak, which the Breachwork Loader plays from (see ModArchives.cs).

Any mod can have embedded audio this way, not only ours: every archive gets its own random key, wrapped with the
loader's public key (RSA-2048, OAEP). Only the loader, which carries the matching private key, can open it, and no mod
author needs anyone else's secret. The public key is in the repository (breachwork_public.xml); the private key
(loader_private.b64) never is, and ClassLoader.csproj writes it into the dll at build. Sounds are stored as they are
(PCM wav, so FMOD has nothing to decode at start: the whole set is ready about 0.2 s after the preload begins; --flac
packs them as lossless FLAC instead, about half the size, at some 0.3 s of FMOD decoding), each under AES-256-CBC with
a key derived from the archive key and its path, and each
carries an HMAC-SHA256 tag, as does the index (which is itself encrypted, so the sound names don't sit in the clear): a
changed or damaged archive is refused, not played as noise. Scrambling against copying and tools, not secrecy: someone
who decompiles the loader can find the private key.

Format BWPK5, little-endian:
  "BWPK5\\0"  u8 flags (1 = index encrypted, 2 = tags present)  8 bytes loader-key id (SHA-256 of the public modulus, first 8)
  u16 wrappedLength, wrapped archive key  u32 indexLength, index (AES-CBC under HMAC(key, "index"), IV first; its tag
  HMAC(key, "index" || ciphertext) follows, 32 bytes)  then the entries' data.
  Index, decrypted: u32 count, per entry u16 nameLen, UTF-8 name, u64 offset, u32 size, 16-byte IV, 32-byte tag
  (HMAC(key, name || IV || ciphertext)). Entry key = HMAC(key, lower-case name).

Mod authors without Python: the mod manager does the same without FLAC: BreachworkModManager.exe --seal-audio <mod folder>.

Usage: python sfx_archive.py <Mods folder or one mod folder> [--remove] [--verify] [--flac]
         --remove deletes the loose sounds it packed: for release copies, never for the working Mods folder.
         --flac stores wavs as lossless FLAC (smaller archive, slower start); the default stores PCM as it is.
         --verify reads every archive back and checks every tag and every FLAC, the way the loader would.
       python sfx_archive.py keygen [--rotate]   once: the loader's key pair (private file kept out of the repository);
                                                 --rotate adds a new current key and keeps the old ones so old archives still open
       python sfx_archive.py list <sounds.pak>      (needs the private key)
       python sfx_archive.py verify <sounds.pak>    (needs the private key) checks the archive's tags and decodes every sound
"""
import argparse
import base64
import glob
import hashlib
import hmac
import io
import os
import re
import struct
import sys

import soundfile
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

MAGIC = b"BWPK5\0"
FLAG_INDEX_ENCRYPTED, FLAG_TAGS = 1, 2
KINDS = (".wav", ".ogg", ".mp3")
HERE = os.path.dirname(os.path.abspath(__file__))
PUBLIC = os.path.join(HERE, "breachwork_public.xml")
PRIVATE = os.path.join(HERE, "loader_private.b64")
MANAGER_KEY_CS = os.path.join(HERE, "..", "ModManager", "SealPublicKey.cs")
OAEP = padding.OAEP(mgf=padding.MGF1(hashes.SHA1()), algorithm=hashes.SHA1(), label=None)   # what Mono's RSACryptoServiceProvider speaks


def b64(n, length):
    return base64.b64encode(n.to_bytes(length, "big")).decode("ascii")


def rsa_xml(key, private):
    """The .NET RSAKeyValue XML form, which the loader and the mod manager read with FromXmlString."""
    pub = (key.public_key() if hasattr(key, "public_key") else key).public_numbers()
    size = (pub.n.bit_length() + 7) // 8
    parts = [f"<Modulus>{b64(pub.n, size)}</Modulus>", f"<Exponent>{b64(pub.e, 3)}</Exponent>"]
    if private:
        p = key.private_numbers()
        half = size // 2
        parts += [f"<P>{b64(p.p, half)}</P>", f"<Q>{b64(p.q, half)}</Q>", f"<DP>{b64(p.dmp1, half)}</DP>", f"<DQ>{b64(p.dmq1, half)}</DQ>",
                  f"<InverseQ>{b64(p.iqmp, half)}</InverseQ>", f"<D>{b64(p.d, size)}</D>"]
    return "<RSAKeyValue>" + "".join(parts) + "</RSAKeyValue>"


def modulus_id(xml):
    """The 8-byte loader-key id: SHA-256 of the modulus bytes, as the loader computes it from its own key."""
    n = base64.b64decode(re.search(r"<Modulus>(.*?)</Modulus>", xml).group(1))
    return hashlib.sha256(n).digest()[:8]


def write_keys(key, older_private_b64):
    """The private file holds the current key first and older ones after, one base64 line each; the loader tries them by id."""
    lines = [base64.b64encode(rsa_xml(key, True).encode("ascii")).decode("ascii")] + older_private_b64
    open(PRIVATE, "w", encoding="ascii").write("\n".join(lines) + "\n")
    open(PUBLIC, "w", encoding="ascii").write(rsa_xml(key, False) + "\n")
    cs = ("// Generated by _modding/audio/sfx_archive.py keygen: the Breachwork Loader's PUBLIC sound-archive key, for sealing a mod's\n"
          "// sounds so that only the loader can open them. Public, so it belongs in the repository.\n"
          "namespace ChimeraModManager\n{\n    internal static class SealPublicKey\n    {\n"
          f"        internal const string Xml = \"{rsa_xml(key, False)}\";\n    }}\n}}\n")
    open(MANAGER_KEY_CS, "w", encoding="utf-8", newline="\n").write(cs)
    print(f"key pair written: private {PRIVATE} ({len(lines)} key(s), never commit), public {PUBLIC} and {os.path.normpath(MANAGER_KEY_CS)}; loader-key id {modulus_id(rsa_xml(key, False)).hex()}")


def keygen(rotate=False):
    if os.path.exists(PRIVATE) and not rotate:
        raise SystemExit(f"{PRIVATE} exists. To change keys run 'keygen --rotate': a new current key, the old ones kept, so old archives still open.")
    older = [l.strip() for l in open(PRIVATE, encoding="ascii") if l.strip()] if rotate and os.path.exists(PRIVATE) else []
    write_keys(rsa.generate_private_key(public_exponent=65537, key_size=2048), older)
    if rotate:
        print("rotated: new archives seal to the new key; the loader still opens archives sealed to the older ones (rebuild it)")


def public_key():
    if not os.path.exists(PUBLIC):
        raise SystemExit("no breachwork_public.xml: run sfx_archive.py keygen (the project owner) or get the public key from the repository")
    xml = open(PUBLIC, encoding="ascii").read()
    n = int.from_bytes(base64.b64decode(re.search(r"<Modulus>(.*?)</Modulus>", xml).group(1)), "big")
    e = int.from_bytes(base64.b64decode(re.search(r"<Exponent>(.*?)</Exponent>", xml).group(1)), "big")
    return rsa.RSAPublicNumbers(e, n).public_key(), modulus_id(xml)


def private_keys():
    """(loader-key id, private key) for every key in the private file, current first."""
    out = []
    for line in open(PRIVATE, encoding="ascii"):
        if not line.strip():
            continue
        xml = base64.b64decode(line.strip()).decode("ascii")
        g = lambda tag: int.from_bytes(base64.b64decode(re.search(f"<{tag}>(.*?)</{tag}>", xml).group(1)), "big")
        pub = rsa.RSAPublicNumbers(g("Exponent"), g("Modulus"))
        out.append((modulus_id(xml), rsa.RSAPrivateNumbers(g("P"), g("Q"), g("D"), g("DP"), g("DQ"), g("InverseQ"), pub).private_key()))
    return out


def derive(key, label):
    return hmac.new(key, label.encode("utf-8"), hashlib.sha256).digest()


def entry_key(key, name):
    return derive(key, name.lower())


def aes_cbc(key, iv, data, encrypt):
    c = Cipher(algorithms.AES(key), modes.CBC(iv))
    if encrypt:
        pad = 16 - len(data) % 16
        e = c.encryptor()
        return e.update(data + bytes([pad]) * pad) + e.finalize()
    d = c.decryptor()
    plain = d.update(data) + d.finalize()
    pad = plain[-1]
    if not 1 <= pad <= 16 or plain[-pad:] != bytes([pad]) * pad:
        raise ValueError("bad padding")
    return plain[:-pad]


def tag_entry(key, name, iv, enc):
    return hmac.new(key, name.encode("utf-8") + iv + enc, hashlib.sha256).digest()


def stored(path, flac=False):
    raw = open(path, "rb").read()
    if not flac or not path.lower().endswith(".wav"):
        return raw
    x, sr = soundfile.read(path, dtype="int16", always_2d=True)
    out = io.BytesIO()
    soundfile.write(out, x, sr, format="FLAC", subtype="PCM_16")
    flac = out.getvalue()
    return flac if len(flac) < len(raw) else raw


def pack(folder, names, out, pub, loader_id, flac=False):
    key = os.urandom(32)
    wrapped = pub.encrypt(key, OAEP)
    blobs, raw_total = [], 0
    for name in names:
        path = os.path.join(folder, name)
        raw_total += os.path.getsize(path)
        iv = os.urandom(16)
        enc = aes_cbc(entry_key(key, name), iv, stored(path, flac), True)
        blobs.append((name.encode("utf-8"), iv, enc, tag_entry(key, name, iv, enc)))
    plain_index_len = 4 + sum(2 + len(n) + 8 + 4 + 16 + 32 for n, _, _, _ in blobs)
    enc_index_len = 16 + (plain_index_len // 16 + 1) * 16
    offset = len(MAGIC) + 1 + 8 + 2 + len(wrapped) + 4 + enc_index_len + 32
    index = struct.pack("<I", len(blobs))
    for n, iv, enc, tag in blobs:
        index += struct.pack("<H", len(n)) + n + struct.pack("<QI", offset, len(enc)) + iv + tag
        offset += len(enc)
    assert len(index) == plain_index_len
    iv_index = os.urandom(16)
    enc_index = iv_index + aes_cbc(derive(key, "index"), iv_index, index, True)
    assert len(enc_index) == enc_index_len
    index_tag = hmac.new(key, b"index" + enc_index, hashlib.sha256).digest()
    with open(out, "wb") as f:
        f.write(MAGIC + bytes([FLAG_INDEX_ENCRYPTED | FLAG_TAGS]) + loader_id + struct.pack("<H", len(wrapped)) + wrapped)
        f.write(struct.pack("<I", len(enc_index)) + enc_index + index_tag)
        for _, _, enc, _ in blobs:
            f.write(enc)
    return raw_total, offset


def open_archive(path):
    """(archive key, [(name, offset, size, iv, tag)], file bytes) after checking the index tag; needs the private key."""
    data = open(path, "rb").read()
    if data[:6] != MAGIC:
        raise ValueError("not a BWPK5 archive")
    flags = data[6]
    kid = data[7:15]
    pos = 15
    (wl,) = struct.unpack_from("<H", data, pos); pos += 2
    wrapped = data[pos:pos + wl]; pos += wl
    key = None
    for pid, priv in private_keys():
        if pid == kid:
            key = priv.decrypt(wrapped, OAEP)
    if key is None:
        raise ValueError(f"sealed for loader key {kid.hex()}, which the private file does not hold")
    (il,) = struct.unpack_from("<I", data, pos); pos += 4
    enc_index = data[pos:pos + il]; pos += il
    tag = data[pos:pos + 32]; pos += 32
    if flags & FLAG_TAGS and not hmac.compare_digest(tag, hmac.new(key, b"index" + enc_index, hashlib.sha256).digest()):
        raise ValueError("index tag does not match: the archive was changed or damaged")
    index = aes_cbc(derive(key, "index"), enc_index[:16], enc_index[16:], False) if flags & FLAG_INDEX_ENCRYPTED else enc_index
    (count,) = struct.unpack_from("<I", index, 0); p = 4
    entries = []
    for _ in range(count):
        (n,) = struct.unpack_from("<H", index, p); p += 2
        name = index[p:p + n].decode("utf-8"); p += n
        offset, size = struct.unpack_from("<QI", index, p); p += 12
        iv = index[p:p + 16]; p += 16
        etag = index[p:p + 32]; p += 32
        entries.append((name, offset, size, iv, etag))
    return key, entries, data


def read_entry(key, data, entry):
    name, offset, size, iv, etag = entry
    enc = data[offset:offset + size]
    if not hmac.compare_digest(etag, tag_entry(key, name, iv, enc)):
        raise ValueError(f"{name}: tag does not match: changed or damaged")
    return aes_cbc(entry_key(key, name), iv, enc, False)


def verify(path):
    try:
        key, entries, data = open_archive(path)
    except Exception as ex:
        print(f"{path}: REFUSED: {ex}")
        return False
    bad = 0
    for e in entries:
        try:
            soundfile.read(io.BytesIO(read_entry(key, data, e)))
        except Exception as ex:
            bad += 1
            print(f"  BAD {e[0]}: {ex}")
    print(f"{os.path.basename(os.path.dirname(os.path.abspath(path)))}: {len(entries)} entries, {bad} bad, key {hashlib.sha256(key).hexdigest()[:8]}")
    return bad == 0


def seal_mod(mod, remove, pub, loader_id, flac=False):
    files = [f for f in glob.glob(os.path.join(mod, "**", "*"), recursive=True) if f.lower().endswith(KINDS)
             and not any(part in ("plugins", "bin", "obj") for part in os.path.relpath(f, mod).split(os.sep))]
    if not files:
        return None
    names = sorted(os.path.relpath(f, mod).replace(os.sep, "/") for f in files)
    raw, size = pack(mod, names, os.path.join(mod, "sounds.pak"), pub, loader_id, flac)
    print(f"{os.path.basename(mod)}: {len(names)} sounds, {raw / 1048576:.1f} -> {size / 1048576:.1f} MB")
    if remove:
        for f in files:
            os.remove(f)
    return raw, size


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("target", help="Mods folder, one mod folder, 'keygen', or 'list' / 'verify' with a sounds.pak")
    ap.add_argument("what", nargs="?", default=None)
    ap.add_argument("--remove", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--rotate", action="store_true")
    ap.add_argument("--flac", action="store_true", help="store wavs as FLAC (smaller, slower to start) instead of PCM as they are")
    args = ap.parse_args()
    if args.target == "keygen":
        return keygen(args.rotate)
    if args.target == "list":
        key, entries, _ = open_archive(args.what)
        for name, offset, size, iv, tag in entries:
            print(f"{size:9}  {name}")
        print(f"{len(entries)} entries, archive key {hashlib.sha256(key).hexdigest()[:8]}")
        return 0
    if args.target == "verify":
        return 0 if verify(args.what) else 1
    pub, loader_id = public_key()
    total = packed = 0
    mods = [args.target] if os.path.exists(os.path.join(args.target, "mod.json")) else sorted(d for d in glob.glob(os.path.join(args.target, "*")) if os.path.isdir(d))
    for mod in mods:
        r = seal_mod(mod, args.remove, pub, loader_id, args.flac)
        if r:
            total += r[0]; packed += r[1]
            if args.verify:
                verify(os.path.join(mod, "sounds.pak"))
    print(f"all: {total / 1048576:.1f} -> {packed / 1048576:.1f} MB; each archive has its own key, wrapped for loader key {loader_id.hex()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
