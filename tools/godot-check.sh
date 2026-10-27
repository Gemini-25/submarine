#!/usr/bin/env bash
# Godot 쪽 확인 (SPEC 13절 'Godot 쪽 확인'): 빌드, 가져오기, 헤드리스 스모크, 종단 자동 플레이.
# 쓰는 법: GODOT=/경로/Godot_v4.7.2-stable_mono_linux.x86_64 tools/godot-check.sh [묶음...] [--screenshots 폴더]
#   묶음: crisis(위기 한 판), hands(위험과 손의 느낌), reach(서 있는 자리에서 손이 닿는지), workviews(작업 시점 28곳), fixtures(데이터 장치의 손동작), tools(공구판과 공구띠), drill-switchboard(배전반 훈련 A를 손동작으로). 주지 않으면 전부.
# 스크린샷은 화면(또는 xvfb-run)이 필요하다.
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
LOG="${TMPDIR:-/tmp}/deepline-godot"
mkdir -p "$LOG"

dotnet build game/DeepLine.Game.csproj -nologo -v q
"$GODOT" --headless --path game --import > "$LOG/import.log" 2>&1

run() {
  local name="$1"; shift
  if ! "$GODOT" --headless --path game --resolution 1600x900 -- "$@" > "$LOG/$name.log" 2>&1; then
    echo "$name 실패. 기록: $LOG/$name.log"; grep -E "ERROR|AUTOTEST|SMOKE" "$LOG/$name.log" || true; exit 1
  fi
  if grep -q "ERROR" "$LOG/$name.log"; then
    echo "$name 중 오류가 기록되었다. 기록: $LOG/$name.log"; grep -A3 "ERROR" "$LOG/$name.log"; exit 1
  fi
  grep -E "SMOKE|AUTOTEST" "$LOG/$name.log" || true
}

suites=()
shots=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --screenshots) shots="${2:-$LOG/shots}"; shift 2 ;;
    *) suites+=("$1"); shift ;;
  esac
done
joined="$(IFS=,; echo "${suites[*]:-}")"

run smoke --smoke=300
run autotest "--autotest=$joined"

if [[ -n "$shots" ]]; then
  out="$(realpath -m "$shots")"
  xvfb-run -a -s "-screen 0 1600x900x24" "$GODOT" --path game --rendering-driver opengl3 --audio-driver Dummy \
    --resolution 1600x900 -- --screenshot="$out" > "$LOG/shots.log" 2>&1
  grep SCREENSHOT "$LOG/shots.log"
fi
echo "Godot 확인 통과"
