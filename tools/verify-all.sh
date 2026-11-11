#!/usr/bin/env bash
# Full verification pipeline: synthetic media -> unit tests -> end-to-end render checks.
# Requires: dotnet 8 SDK on PATH and ffmpeg/ffprobe (set COURSESTUDIO_FFMPEG /
# COURSESTUDIO_FFPROBE to override discovery).
set -euo pipefail

cd "$(dirname "$0")/.."

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

if [ -z "${COURSESTUDIO_FFMPEG:-}" ]; then
  for candidate in "$HOME/.local/opt/ffmpeg/ffmpeg" /usr/local/bin/ffmpeg /usr/bin/ffmpeg; do
    if [ -x "$candidate" ]; then export COURSESTUDIO_FFMPEG="$candidate"; break; fi
  done
fi
if [ -z "${COURSESTUDIO_FFPROBE:-}" ]; then
  for candidate in "$HOME/.local/opt/ffmpeg/ffprobe" /usr/local/bin/ffprobe /usr/bin/ffprobe; do
    if [ -x "$candidate" ]; then export COURSESTUDIO_FFPROBE="$candidate"; break; fi
  done
fi
export PATH="$(dirname "$COURSESTUDIO_FFMPEG"):$HOME/.dotnet:$PATH"

echo "== 1/3 generating synthetic course media =="
python3 -I tools/generate_test_media.py --out artifacts/test-media

echo "== 2/3 unit tests =="
dotnet test tests/CourseStudio.Tests/CourseStudio.Tests.csproj -v q

echo "== 3/3 end-to-end render verification =="
dotnet run --project tools/CourseStudio.Verify -- artifacts/test-media
