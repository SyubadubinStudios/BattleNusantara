"""Builds the Linux and macOS downloads: the Photino desktop game and the LAN server, self-contained.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

Usage: python scripts/package_unix.py [--rids linux-x64,osx-arm64,osx-x64] [--skip-publish]
Writes into artifacts/installer/:
  BattleNusantara-<version>-linux-x64.tar.gz   battle-of-nusantara/{game,server,play.sh,lan-server.sh,README.txt}
  BattleNusantara-<version>-osx-arm64.zip      Battle of Nusantara.app, LAN Server/, README.txt
  BattleNusantara-<version>-osx-x64.zip        the same for Intel Macs
with a .sha256 file each. Runs on Windows, Linux or macOS; archives keep the executable bits.
On macOS the bundle is signed ad hoc (not with a Developer ID) and is not notarized.
"""
import argparse
import hashlib
import io
import re
import shutil
import subprocess
import sys
import tarfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PUBLISH = ROOT / "artifacts/publish-unix"
OUTPUT = ROOT / "artifacts/installer"
EXECUTABLES = {"BattleNusantara", "BattleNusantara.Server", "createdump"}

README = """Battle of Nusantara {version} — {platform}
Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.

{run}

LAN server / Server LAN: {server}
Then open http://<this computer>:5080 in a browser, or join from the game with that address.
Lalu buka http://<komputer ini>:5080 di browser, atau gabung dari game dengan alamat itu.

Requirements / Persyaratan: {requirements}
Plain HTTP: use the server only on a trusted network. / HTTP biasa: pakai server hanya di jaringan tepercaya.
"""

LINUX_RUN = """Play / Main:   ./play.sh"""
LINUX_REQUIREMENTS = """64-bit Linux with WebKitGTK 4.1, GTK 3 and libnotify
  Debian/Ubuntu: sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0 libnotify4
  Fedora:        sudo dnf install webkit2gtk4.1 gtk3 libnotify
A blank window on some GPUs: run WEBKIT_DISABLE_DMABUF_RENDERER=1 ./play.sh
Jendela kosong pada sebagian GPU: jalankan WEBKIT_DISABLE_DMABUF_RENDERER=1 ./play.sh"""
MAC_RUN = """Play / Main:   open "Battle of Nusantara.app"
The app is not signed. The first time, right-click it and choose Open, or run:
Aplikasi belum ditandatangani. Pertama kali, klik kanan lalu pilih Open, atau jalankan:
  xattr -dr com.apple.quarantine "Battle of Nusantara.app" "LAN Server\""""
MAC_REQUIREMENTS = "macOS 12 or later / macOS 12 atau lebih baru"
SERVER_SCRIPT = '#!/bin/sh\ncd "$(dirname "$0")/server" && exec ./BattleNusantara.Server --urls http://0.0.0.0:5080 "$@"\n'

INFO_PLIST = """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Battle of Nusantara</string>
  <key>CFBundleDisplayName</key><string>Battle of Nusantara</string>
  <key>CFBundleIdentifier</key><string>com.syubadubin.battlenusantara</string>
  <key>CFBundleVersion</key><string>{version}</string>
  <key>CFBundleShortVersionString</key><string>{version}</string>
  <key>CFBundleExecutable</key><string>BattleNusantara</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios</string>
</dict>
</plist>
"""


def version():
    text = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    return re.search(r"<Version>([^<]+)</Version>", text).group(1)


def publish(rid):
    target = PUBLISH / rid
    if target.exists():
        shutil.rmtree(target)
    for project, folder in (("BattleNusantara.Photino", "game"), ("BattleNusantara.Server", "server")):
        subprocess.run(["dotnet", "publish", str(ROOT / "src" / project), "-c", "Release", "-r", rid, "--self-contained", "true",
                        "-o", str(target / folder), "-p:DebugType=none"], check=True)
    # The server must not ship a development database.
    shutil.rmtree(target / "server/data", ignore_errors=True)
    game = target / "game"
    if not (game / "wwwroot/_content/BattleNusantara.UI/world.js").exists():
        raise SystemExit(f"{rid}: the game's web assets were not published.")
    if not any(game.glob("*Photino.Native*")):
        raise SystemExit(f"{rid}: the Photino native library is missing.")
    return target


def mode(path):
    return 0o755 if path.name in EXECUTABLES or path.suffix in (".sh", ".command") else 0o644


def pack_linux(source, name, archive):
    with tarfile.open(archive, "w:gz") as tar:
        def add_tree(folder, prefix):
            for path in sorted(folder.rglob("*")):
                if path.is_file():
                    info = tar.gettarinfo(str(path), f"{prefix}/{path.relative_to(folder).as_posix()}")
                    info.mode = mode(path)
                    info.uid = info.gid = 0
                    info.uname = info.gname = ""
                    with open(path, "rb") as handle:
                        tar.addfile(info, handle)

        def add_text(path, text, executable=False):
            data = text.encode("utf-8")
            info = tarfile.TarInfo(f"{name}/{path}")
            info.size = len(data)
            info.mode = 0o755 if executable else 0o644
            tar.addfile(info, io.BytesIO(data))

        add_tree(source / "game", f"{name}/game")
        add_tree(source / "server", f"{name}/server")
        add_text("play.sh", '#!/bin/sh\ncd "$(dirname "$0")/game" && exec ./BattleNusantara "$@"\n', True)
        add_text("lan-server.sh", '#!/bin/sh\ncd "$(dirname "$0")/server" && exec ./BattleNusantara.Server --urls http://0.0.0.0:5080 "$@"\n', True)
        add_text("README.txt", README.format(version=version(), platform="Linux x64", run=LINUX_RUN, server="./lan-server.sh", requirements=LINUX_REQUIREMENTS))


def is_macho(path):
    """True for Mach-O programs and libraries (thin or universal), the files macOS checks for a signature."""
    with open(path, "rb") as handle:
        return handle.read(4) in (bytes.fromhex("cffaedfe"), bytes.fromhex("cefaedfe"), bytes.fromhex("cafebabe"))


def pack_mac(source, rid, archive):
    """Stages the .app bundle and the LAN server on disk, signs them ad hoc on macOS (Apple silicon refuses unsigned
    arm64 programs), then zips the stage keeping the executable bits."""
    stage = PUBLISH / f"{rid}-stage"
    if stage.exists():
        shutil.rmtree(stage)
    app = stage / "Battle of Nusantara.app"
    shutil.copytree(source / "game", app / "Contents/MacOS")
    (app / "Contents/Info.plist").write_text(INFO_PLIST.format(version=version()), encoding="utf-8")
    shutil.copytree(source / "server", stage / "LAN Server/server")
    (stage / "LAN Server/lan-server.command").write_text(SERVER_SCRIPT, encoding="utf-8", newline="\n")
    platform = "macOS Apple silicon (arm64)" if rid == "osx-arm64" else "macOS Intel (x64)"
    (stage / "README.txt").write_text(README.format(version=version(), platform=platform, run=MAC_RUN, server='open "LAN Server/lan-server.command"', requirements=MAC_REQUIREMENTS), encoding="utf-8", newline="\n")
    if sys.platform == "darwin":
        for path in stage.rglob("*"):
            if path.is_file():
                path.chmod(mode(path))
        # Each Mach-O program and library is signed on its own. The bundle itself is not sealed: codesign takes folders
        # with a dot in their name (wwwroot/_content/BattleNusantara.UI) for nested bundles and refuses to seal it.
        signed = 0
        for path in sorted(stage.rglob("*")):
            if path.is_file() and is_macho(path):
                subprocess.run(["codesign", "--force", "--sign", "-", str(path)], check=True)
                signed += 1
        print(f"{rid}: signed {signed} Mach-O files ad hoc")
    else:
        print(f"warning: {rid} was not signed; build it on macOS, or Apple silicon will refuse to run it.")
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zip_file:
        for path in sorted(stage.rglob("*")):
            if path.is_file():
                info = zipfile.ZipInfo(path.relative_to(stage).as_posix(), date_time=(2026, 11, 19, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = (0o100000 | mode(path)) << 16
                info.create_system = 3  # Unix, so the mode bits are honoured.
                zip_file.writestr(info, path.read_bytes())


def checksum(path):
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    (path.parent / f"{path.name}.sha256").write_text(f"{digest}  {path.name}\n", encoding="ascii")
    return digest


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--rids", default="linux-x64,osx-arm64,osx-x64")
    parser.add_argument("--skip-publish", action="store_true")
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for rid in args.rids.split(","):
        source = PUBLISH / rid if args.skip_publish else publish(rid)
        if rid.startswith("linux"):
            archive = OUTPUT / f"BattleNusantara-{version()}-{rid}.tar.gz"
            pack_linux(source, "battle-of-nusantara", archive)
        else:
            archive = OUTPUT / f"BattleNusantara-{version()}-{rid}.zip"
            pack_mac(source, rid, archive)
        print(f"{archive.name}: {archive.stat().st_size / 1048576:.1f} MB, sha256 {checksum(archive)}")


if __name__ == "__main__":
    main()
