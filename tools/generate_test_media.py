#!/usr/bin/env python3
"""
Generate synthetic course media for end-to-end verification.

Outputs (all under the given output directory):
  screen_clicks_a.mp4   1920x1080, click number drawn every second + 8-bit code strip
  screen_clicks_b.mp4   1280x720  (resolution change mid-lesson), same encoding scheme
  camera_blocks.mp4     640x480 solid color block that changes every second
  voice_commands.wav    per-click 440/880 Hz command beeps, silence between them
  pause_tail.mp4        short extra screen segment representing wait time

The code strip encodes the click index in eight vertical bars so an external
verifier can recover frame identity without OCR.
"""
import argparse
import math
import os
import site
import subprocess
import sys
import wave

# Safe under `python -I`: explicitly opt into the user-installed packages instead
# of relying on cwd or implicit site auto-imports.
sys.path.insert(0, site.getusersitepackages())

import numpy as np
from PIL import Image, ImageDraw, ImageFont

FPS = 10


def _font(size: int) -> ImageFont.FreeTypeFont:
    for path in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def _code_strip(draw: ImageDraw.ImageDraw, index: int, x: int, h: int) -> None:
    bar_w, gap = 14, 6
    for bit in range(8):
        on = (index >> bit) & 1
        fill = (255, 255, 255) if on else (40, 40, 40)
        bx = x + bit * (bar_w + gap)
        draw.rectangle([bx, 10, bx + bar_w, h - 10], fill=fill)


def write_screen(path: str, width: int, height: int, clicks: int, label: str) -> None:
    cmd = [
        "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{width}x{height}",
        "-r", str(FPS), "-i", "-",
        "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", path,
    ]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    font_big = _font(height // 6)
    font_small = _font(height // 14)
    frames = clicks * FPS
    for f in range(frames):
        click = f // FPS
        img = Image.new("RGB", (width, height), (18, 22, 30))
        draw = ImageDraw.Draw(img)
        # A moving cursor dot gives motion between click boundaries.
        phase = (f % FPS) / FPS
        cx = int(width * (0.2 + 0.6 * phase))
        cy = int(height * (0.3 + 0.1 * math.sin(phase * math.pi)))
        draw.ellipse([cx - 16, cy - 16, cx + 16, cy + 16], fill=(255, 210, 60))
        draw.text((40, 40), f"{label} click #{click + 1}", font=font_big, fill=(230, 230, 235))
        draw.text((40, height - 80), f"{width}x{height} native pixels", font=font_small, fill=(150, 170, 200))
        _code_strip(draw, click + 1, width - 200, height)
        proc.stdin.write(img.tobytes())
    proc.stdin.close()
    if proc.wait() != 0:
        raise RuntimeError(f"ffmpeg failed while writing {path}")


def write_camera(path: str, seconds: int = 12) -> None:
    width, height = 640, 480
    palette = [
        (180, 60, 90), (90, 160, 70), (70, 110, 200), (200, 140, 50),
        (150, 80, 190), (60, 180, 180), (210, 90, 150), (120, 120, 120),
        (200, 200, 70), (90, 200, 130), (170, 110, 60), (80, 90, 160),
    ]
    cmd = [
        "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{width}x{height}",
        "-r", str(FPS), "-i", "-",
        "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", path,
    ]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    font = _font(44)
    for f in range(seconds * FPS):
        block = f // FPS
        img = Image.new("RGB", (width, height), palette[block % len(palette)])
        draw = ImageDraw.Draw(img)
        draw.text((30, 30), f"CAM block {block + 1}", font=font, fill=(255, 255, 255))
        proc.stdin.write(img.tobytes())
    proc.stdin.close()
    if proc.wait() != 0:
        raise RuntimeError(f"ffmpeg failed while writing {path}")


def write_voice(path: str, click_count: int, sample_rate: int = 44100) -> None:
    """One command beep per click, with a different pitch so sync is audible/measurable."""
    total_seconds = click_count
    samples = np.zeros(total_seconds * sample_rate, dtype=np.float32)
    for click in range(click_count):
        freq = 440.0 if click % 2 == 0 else 880.0
        t0 = click * sample_rate
        t = np.arange(int(0.35 * sample_rate)) / sample_rate
        env = np.minimum(1.0, t * 20) * np.maximum(0.0, 1.0 - (t - 0.25) * 8)
        tone = 0.5 * np.sin(2 * np.pi * freq * t) * env
        samples[t0:t0 + len(tone)] += tone.astype(np.float32)
    pcm = np.clip(samples, -1, 1)
    pcm16 = (pcm * 32767).astype("<i2")
    with wave.open(path, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(pcm16.tobytes())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default="artifacts/test-media")
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)

    write_screen(os.path.join(args.out, "screen_clicks_a.mp4"), 1920, 1080, 6, "A")
    write_screen(os.path.join(args.out, "screen_clicks_b.mp4"), 1280, 720, 4, "B")
    write_screen(os.path.join(args.out, "pause_tail.mp4"), 1280, 720, 3, "WAIT")
    write_camera(os.path.join(args.out, "camera_blocks.mp4"), seconds=12)
    write_voice(os.path.join(args.out, "voice_commands.wav"), click_count=10)
    print("generated:")
    for name in sorted(os.listdir(args.out)):
        print(" -", name)


if __name__ == "__main__":
    main()
