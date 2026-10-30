#!/usr/bin/env bash
# 교범 그림을 다시 만든다 (WP26). 정의는 data/manual/fig/figures.json, 결과는 data/manual/fig/*.png (16색 팔레트 PNG).
# 장면에서 상태를 맞춰 찍고 먹선으로 바꾸므로 화면이 필요하다: xvfb-run과 opengl3.
# 쓰는 법: GODOT=/경로/Godot_v4.7.2-stable_mono_linux.x86_64 tools/make-figures.sh [그림이름,그림이름 ...] [--jobs N]
#   이름을 주지 않으면 전부 만든다. --jobs로 Godot 여러 개를 나란히 띄운다 (기본 3).
# 만든 뒤에는 Godot의 figures 확인 묶음(tools/godot-check.sh figures, 장면을 바꿨으면 figures-full도)과 dotnet test의 교범 시험이 통과해야 한다.
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
LOG="${TMPDIR:-/tmp}/deepline-figures"
mkdir -p "$LOG"
rm -f "$LOG"/make-*.log
jobs=3
only=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --jobs) jobs="$2"; shift 2 ;;
    *) only="${only:+$only,}$1"; shift ;;
  esac
done

dotnet build game/DeepLine.Game.csproj -nologo -v q
"$GODOT" --headless --path game --import > "$LOG/import.log" 2>&1

out="$(realpath data/manual/fig)"
if [[ -z "$only" ]]; then
  only="$(python3 -c 'import json,sys; print(",".join(sorted(json.load(open("data/manual/fig/figures.json")))))')"
fi
# 순서 띠(손동작으로 절차를 끝까지 하는 그림)가 오래 걸리므로 이름을 고루 나눈다.
IFS=',' read -r -a names <<< "$only"
pids=()
for ((j = 0; j < jobs; j++)); do
  part=""
  for ((i = j; i < ${#names[@]}; i += jobs)); do part+="${names[$i]},"; done
  [[ -z "$part" ]] && continue
  xvfb-run -a -s "-screen 0 1600x900x24" "$GODOT" --path game --rendering-driver opengl3 --audio-driver Dummy \
    --resolution 1600x900 -- --figures="$out" --only="${part%,}" > "$LOG/make-$j.log" 2>&1 &
  pids+=($!)
done
failed=0
for j in "${!pids[@]}"; do
  if ! wait "${pids[$j]}"; then
    echo "그림 만들기 실패. 기록: $LOG/make-$j.log"; grep -E "ERROR|AUTOTEST" "$LOG/make-$j.log" | head -20 || true; failed=1
  fi
done
grep -h "FIGURE" "$LOG"/make-*.log | sort
# 만든 그림의 지문을 figures.lock.json에 모은다 (Godot의 figures 확인 묶음이 견준다). 정의에서 빠진 그림의 지문은 지운다.
python3 - "$out" <<'PY'
import json, os, sys
out = sys.argv[1]
lock = os.path.join(out, "figures.lock.json")
data = json.load(open(lock)) if os.path.exists(lock) else {}
names = set(json.load(open(os.path.join(out, "figures.json"))))
part = os.path.join(out, ".lock")
if os.path.isdir(part):
    for n in os.listdir(part):
        data[n] = open(os.path.join(part, n)).read().strip()
        os.remove(os.path.join(part, n))
    os.rmdir(part)
data = {k: data[k] for k in sorted(data) if k in names}
with open(lock, "w") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
    f.write("\n")
PY
[[ $failed -eq 0 ]] && echo "교범 그림 만들기 끝"
exit $failed
