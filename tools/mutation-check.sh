#!/usr/bin/env bash
# Controlled-error (mutation) gate for the compositing acceptance suite.
#
# Each mutation below is a single controlled error injected into a COPY of the
# product source. The acceptance suite is then run against the mutated product
# and MUST fail — a mutation that the suite does not catch is reported as
# SURVIVED and fails this gate. The working tree is never touched.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT=$(pwd)
WORK="$ROOT/artifacts/mutation"

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

GRAPH=src/CourseStudio.Core/Rendering/FilterGraphBuilder.cs

# name|anchor (exact source text)|replacement
MUTATIONS=(
  "pip-wrong-side|main_w-overlay_w-{m}|{m}"
  "audio-shift-400ms|adelay=delays={clip.Start.Micros / 1000}:all=1|adelay=delays={clip.Start.Micros / 1000 + 400}:all=1"
  "pip-half-width|Math.Clamp(_project.Pip.WidthRatio, 0.05, 1.0));|Math.Clamp(_project.Pip.WidthRatio, 0.05, 1.0) / 2);"
  "fit-becomes-fill|force_original_aspect_ratio=decrease|force_original_aspect_ratio=increase"
  "opacity-ignored|aa={F(Math.Clamp(_project.Pip.Opacity, 0, 1))}|aa=1"
)

rm -rf "$WORK"
mkdir -p "$WORK"

survivors=0
for entry in "${MUTATIONS[@]}"; do
  IFS='|' read -r name anchor replacement <<<"$entry"
  dir="$WORK/$name"
  mkdir -p "$dir"
  cp -r src tests Directory.Build.props "$dir/"

  ANCHOR="$anchor" REPLACEMENT="$replacement" python3 -I - "$dir/$GRAPH" <<'PY'
import os, sys
path = sys.argv[1]
anchor = os.environ["ANCHOR"]
replacement = os.environ["REPLACEMENT"]
with open(path, encoding="utf-8") as f:
    text = f.read()
if anchor not in text:
    sys.exit(f"mutation anchor not found in {path}: {anchor!r}")
with open(path, "w", encoding="utf-8") as f:
    f.write(text.replace(anchor, replacement, 1))
PY

  echo "== mutation: $name =="
  set +e
  (cd "$dir" && dotnet test tests/CourseStudio.Acceptance/CourseStudio.Acceptance.csproj -v q) \
      >"$dir/test-output.log" 2>&1
  rc=$?
  set -e
  if [ $rc -eq 0 ]; then
    echo "  SURVIVED — acceptance suite passed against the mutated product (BAD)"
    survivors=$((survivors + 1))
  else
    echo "  KILLED — acceptance suite exposed the controlled error"
    grep -E "^\s*(Failed|error)" "$dir/test-output.log" | head -3 | sed 's/^/    /' || true
  fi
done

echo
if [ "$survivors" -gt 0 ]; then
  echo "$survivors mutation(s) SURVIVED — the acceptance suite is blind to them"
  exit 1
fi
echo "all ${#MUTATIONS[@]} controlled errors were exposed by the acceptance suite"
