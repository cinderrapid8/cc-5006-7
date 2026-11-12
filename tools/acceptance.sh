#!/usr/bin/env bash
# Automated acceptance entry for the compositing end-to-end tests.
#
#   tools/acceptance.sh                 run the compositing acceptance suite
#   tools/acceptance.sh --with-mutation also run the controlled-error (mutation) gate
#
# The suite exports the synthetic lesson through the real pipeline (save project
# -> reopen from disk -> ffmpeg export) and asserts on the decoded composite
# pixels and audio-video deviation. The mutation gate then introduces controlled
# errors into the product and requires the suite to expose every one of them.
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

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK not found (looked in \$HOME/.dotnet and PATH)" >&2
  exit 2
fi
if [ ! -x "$COURSESTUDIO_FFMPEG" ]; then
  echo "ffmpeg not found (set COURSESTUDIO_FFMPEG)" >&2
  exit 2
fi

echo "== compositing acceptance: screen+camera -> saved project -> reopened export =="
dotnet test tests/CourseStudio.Acceptance/CourseStudio.Acceptance.csproj -v q

if [ "${1:-}" = "--with-mutation" ]; then
  echo
  echo "== controlled-error gate =="
  tools/mutation-check.sh
fi

echo "ACCEPTANCE PASSED"
