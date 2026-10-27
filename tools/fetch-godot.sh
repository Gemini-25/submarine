#!/usr/bin/env bash
# Godot 4.7.2 .NET (리눅스 x86_64)을 받아 풀고 경로를 알려 준다. 받은 것은 저장소 밖에 둔다.
# 쓰는 법: tools/fetch-godot.sh [폴더]   (기본: ${TMPDIR:-/tmp}/godot-4.7.2)
set -euo pipefail
dest="${1:-${TMPDIR:-/tmp}/godot-4.7.2}"
name="Godot_v4.7.2-stable_mono_linux_x86_64"
url="https://github.com/godotengine/godot/releases/download/4.7.2-stable/${name}.zip"
sums="https://github.com/godotengine/godot/releases/download/4.7.2-stable/SHA512-SUMS.txt"
mkdir -p "$dest"
cd "$dest"
if [[ ! -x "$name/Godot_v4.7.2-stable_mono_linux.x86_64" ]]; then
  curl -sSL --fail -o "$name.zip" "$url"
  curl -sSL --fail -o SHA512-SUMS.txt "$sums"
  grep " $name.zip\$" SHA512-SUMS.txt | sha512sum -c -
  unzip -q -o "$name.zip"
fi
echo "GODOT=$dest/$name/Godot_v4.7.2-stable_mono_linux.x86_64"
