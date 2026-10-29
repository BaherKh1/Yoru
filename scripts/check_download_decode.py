"""Decode the actual saved test archives with all network protocols disabled."""
from pathlib import Path
import json
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
ffmpeg = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'toolchain/test-media/ffmpeg.exe'
if not ffmpeg.exists():
    candidate = shutil.which('ffmpeg')
    if candidate is None:
        raise SystemExit('Install host FFmpeg or pass its executable as the first argument.')
    ffmpeg = Path(candidate)
checks = []
for kind, relative in [
    ('direct', 'selected-server-720.mp4'),
    ('hls', 'selected-hls-720/index.m3u8'),
    ('fmp4', 'selected-fmp4-720/index.m3u8'),
]:
    file = root / 'evidence/offline-download-fixtures' / relative
    result = subprocess.run([str(ffmpeg), '-hide_banner', '-nostdin',
        '-protocol_whitelist', 'file,crypto,data', '-i', str(file), '-f', 'null', '-'],
        capture_output=True, text=True)
    (root / f'evidence/downloads-host-decode-{kind}.txt').write_text(result.stderr)
    if result.returncode != 0 or '1280x720' not in result.stderr or 'Audio: aac' not in result.stderr:
        raise SystemExit(f'Offline decoder verification failed: {kind}\n{result.stderr}')
    checks.append(dict(file=file.relative_to(root).as_posix(), decoded=True, width=1280,
        height=720, video='H.264', audio='AAC', networkProtocolsDisabled=True))
(root / 'evidence/downloads-host-decode.json').write_text(json.dumps(
    dict(success=True, checks=checks, deviceVerified=False), indent=2))
print('PASS: saved direct, MPEG-TS HLS and fMP4 HLS 720p video/audio decode with networking disabled.')
