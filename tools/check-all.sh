#!/usr/bin/env bash
# 커밋 전 확인 전부 (DECISIONS M2 불변 조건 I5): dotnet test, 그리고 Godot 확인.
# Godot이 없으면 크게 실패한다(조용히 건너뛰지 않는다). 받으려면 tools/fetch-godot.sh.
# 쓰는 법: GODOT=/경로/Godot_v4.7.2-stable_mono_linux.x86_64 tools/check-all.sh [묶음...]
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test -nologo -v q
if [[ -z "${GODOT:-}" ]] && ! command -v godot >/dev/null; then
  echo "Godot 실행 파일이 없다. GODOT=경로 를 주거나 tools/fetch-godot.sh로 받는다." >&2
  exit 3
fi
tools/godot-check.sh "$@"
