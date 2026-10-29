"""Generate owned, synthetic H.264/AAC sources; FFmpeg is a host test tool only."""
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[1]
ffmpeg = root / 'toolchain/test-media/ffmpeg.exe'
out = root / 'tests/fixtures/downloads'
out.mkdir(parents=True, exist_ok=True)
for height, color, tone in [(360, '0x8B5CF6', 440), (720, '0xA78BFA', 880)]:
    video = out / f'{height}.mp4'
    subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-y',
        '-f', 'lavfi', '-i', f'color=c={color}:s={height*16//9}x{height}:r=24',
        '-f', 'lavfi', '-i', f'sine=frequency={tone}:sample_rate=48000', '-t', '1.25',
        '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p',
        '-c:a', 'aac', '-movflags', '+faststart', str(video)], check=True)
    subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-y', '-i',
        str(video), '-c', 'copy', '-hls_time', '2', '-hls_playlist_type', 'vod',
        '-hls_segment_filename', str(out / f'{height}part%03d.ts'),
        str(out / f'{height}.m3u8')], check=True)
subprocess.run([str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-y', '-i',
    str(out / '720.mp4'), '-c', 'copy', '-hls_time', '2', '-hls_playlist_type', 'vod',
    '-hls_segment_type', 'fmp4', '-hls_fmp4_init_filename', '720-init.mp4',
    '-hls_segment_filename', str(out / '720fragment%03d.m4s'),
    str(out / '720-fmp4.m3u8')], check=True, cwd=out)
print('Generated H.264 + AAC direct, MPEG-TS HLS and fMP4 HLS fixtures.')
