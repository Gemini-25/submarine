#!/usr/bin/env python3
"""데모 WAV의 스펙트럼과 포락선을 재는 분석 도구. 파이썬 표준 라이브러리만 쓴다(소리를 들을 수 없는 에이전트의 검증용).

  python3 tools/DeepLine.AudioRender/analyze.py features 파일.wav ...   특징 표
  python3 tools/DeepLine.AudioRender/analyze.py pump                   펌프 고장 3종 구분 근거(데모 구간 + 눈 가리고 듣기 세트)
  python3 tools/DeepLine.AudioRender/analyze.py hunter                 청음기 상태별 딸깍 빈도와 밝기

경로는 저장소 루트 기준. 특징:
  기본음    30~90Hz 스펙트럼 봉우리(포물선 보간)
  갈림      2.5~5kHz 대역이 전체(20Hz~11kHz)에서 차지하는 비, dB
  중심      스펙트럼 무게 중심, Hz
  첨도      1kHz 고역 통과 뒤 첨도. 가우스 잡음 3, 딱딱 튀는 소리가 많을수록 크다
  출렁임    100~600Hz 포락선(20ms 창)의 0.4~1.2Hz 변조 세기
  박자      100~600Hz 포락선 변조가 가장 센 주파수(0.3~20Hz)
  영교차    초당 영교차 수
"""
import cmath
import math
import os
import struct
import sys
import wave

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
AUDIO = os.path.join(ROOT, "docs", "reports", "audio")


def read_wav(path):
    with wave.open(path, "rb") as w:
        assert w.getnchannels() == 1 and w.getsampwidth() == 2, path
        rate = w.getframerate()
        raw = w.readframes(w.getnframes())
    n = len(raw) // 2
    return [x / 32768.0 for x in struct.unpack("<%dh" % n, raw)], rate


_twiddle = {}


def fft(x):
    """반복형 기수 2 FFT. 길이는 2의 거듭제곱."""
    n = len(x)
    a = list(x)
    j = 0
    for i in range(1, n):
        bit = n >> 1
        while j & bit:
            j ^= bit
            bit >>= 1
        j ^= bit
        if i < j:
            a[i], a[j] = a[j], a[i]
    length = 2
    while length <= n:
        tw = _twiddle.get(length)
        if tw is None:
            tw = [cmath.exp(-2j * math.pi * k / length) for k in range(length // 2)]
            _twiddle[length] = tw
        half = length // 2
        for start in range(0, n, length):
            for k in range(half):
                u = a[start + k]
                v = a[start + k + half] * tw[k]
                a[start + k] = u + v
                a[start + k + half] = u - v
        length <<= 1
    return a


def spectrum(s, n=4096):
    """Hann 창, 반 겹침 평균 전력 스펙트럼."""
    win = [0.5 - 0.5 * math.cos(2 * math.pi * i / (n - 1)) for i in range(n)]
    acc = [0.0] * (n // 2)
    frames = 0
    for start in range(0, len(s) - n + 1, n // 2):
        f = fft([s[start + i] * win[i] for i in range(n)])
        for k in range(n // 2):
            acc[k] += abs(f[k]) ** 2
        frames += 1
    return [v / max(1, frames) for v in acc]


def band(spec, rate, lo, hi):
    n = len(spec) * 2
    return sum(v for k, v in enumerate(spec) if lo <= k * rate / n < hi)


def peak_hz(spec, rate, lo, hi):
    n = len(spec) * 2
    ks = [k for k in range(1, len(spec) - 1) if lo <= k * rate / n < hi]
    k = max(ks, key=lambda i: spec[i])
    a, b, c = (math.log(spec[k - 1] + 1e-30), math.log(spec[k] + 1e-30), math.log(spec[k + 1] + 1e-30))
    p = 0.5 * (a - c) / (a - 2 * b + c) if (a - 2 * b + c) != 0 else 0
    return (k + p) * rate / n


def centroid(spec, rate):
    n = len(spec) * 2
    num = sum(k * rate / n * v for k, v in enumerate(spec))
    return num / (sum(spec) + 1e-30)


def highpass(s, rate, hz):
    a = 1 - math.exp(-2 * math.pi * hz / rate)
    z = 0.0
    out = []
    for x in s:
        z += a * (x - z)
        out.append(x - z)
    return out


def kurtosis(s, rate, hp=1000):
    h = highpass(s, rate, hp)
    m2 = sum(x * x for x in h) / len(h)
    m4 = sum(x ** 4 for x in h) / len(h)
    return m4 / (m2 * m2 + 1e-30)


def envelope(s, rate, lo, hi, frame_hz=50):
    a_lo = 1 - math.exp(-2 * math.pi * lo / rate)
    a_hi = 1 - math.exp(-2 * math.pi * hi / rate)
    zl = zh = acc = 0.0
    win = rate // frame_hz
    env = []
    for i, x in enumerate(s):
        zh += a_hi * (x - zh)
        zl += a_lo * (x - zl)
        b = zh - zl
        acc += b * b
        if (i + 1) % win == 0:
            env.append(math.sqrt(acc / win))
            acc = 0.0
    return env


def modulation(env, frame_hz=50):
    """포락선 변조 스펙트럼: [(주파수, 세기)]. 세기는 포락선 평균에 대한 비."""
    mean = sum(env) / len(env)
    n = 1
    while n < len(env):
        n <<= 1
    x = [(e - mean) * (0.5 - 0.5 * math.cos(2 * math.pi * i / (len(env) - 1))) for i, e in enumerate(env)] + [0.0] * (n - len(env))
    f = fft(x)
    return [(k * frame_hz / n, abs(f[k]) / (mean * len(env) + 1e-30)) for k in range(1, n // 2)]


def features(s, rate):
    spec = spectrum(s)
    total = band(spec, rate, 20, 11000)
    mod = modulation(envelope(s, rate, 100, 600))
    surge = math.sqrt(sum(v * v for f, v in mod if 0.4 <= f <= 1.2))
    beat = max((m for m in mod if 0.3 <= m[0] <= 20), key=lambda m: m[1])[0]
    zc = sum(1 for i in range(1, len(s)) if (s[i - 1] < 0) != (s[i] < 0)) * rate / len(s)
    rms = math.sqrt(sum(x * x for x in s) / len(s))
    return {
        "기본음": peak_hz(spec, rate, 30, 90),
        "갈림": 10 * math.log10(band(spec, rate, 2500, 5000) / total),
        "중심": centroid(spec, rate),
        "첨도": kurtosis(s, rate),
        "출렁임": surge,
        "박자": beat,
        "영교차": zc,
        "RMS": 20 * math.log10(rms + 1e-12),
    }


COLS = [("기본음", "Hz", "%.1f"), ("갈림", "dB", "%.1f"), ("중심", "Hz", "%.0f"), ("첨도", "", "%.1f"),
        ("출렁임", "", "%.3f"), ("박자", "Hz", "%.2f"), ("영교차", "/s", "%.0f"), ("RMS", "dBFS", "%.1f")]


def table(rows):
    head = "| 소리 | " + " | ".join(f"{c} ({u})" if u else c for c, u, _ in COLS) + " |"
    print(head)
    print("|" + " --- |" * (len(COLS) + 1))
    for name, f in rows:
        print(f"| {name} | " + " | ".join(fmt % f[c] for c, _, fmt in COLS) + " |")


def classify(f):
    """듣는 요령을 그대로 옮긴 규칙. 사람에게 줄 판별 순서와 같다."""
    if f["첨도"] > 8:
        return "WTR-01"  # 딱딱 튀는 자갈 소리
    if f["갈림"] > -14:
        return "WTR-02"  # 쉬지 않고 갈리는 고음
    if f["출렁임"] > 0.035:
        return "WTR-03"  # 낮은 물소리가 밀려왔다 빠진다
    return "normal"


def cmd_pump():
    s, rate = read_wav(os.path.join(AUDIO, "pump-sweep.wav"))
    names = ["정상", "WTR-01 흡입구 막힘", "WTR-02 베어링 마모", "WTR-03 역류 방지 밸브 고착"]
    ids = ["normal", "WTR-01", "WTR-02", "WTR-03"]
    rows = []
    # 데모 2.5초 구간마다 바뀌는 처음 0.6초는 뺀다.
    for k in range(4):
        seg = s[int((2.5 * k + 0.6) * rate):int(2.5 * (k + 1) * rate)]
        rows.append((f"데모 {names[k]}", features(seg, rate)))
    print("### pump-sweep.wav 구간별 (1.9초씩)\n")
    table(rows)
    answers = {}
    with open(os.path.join(ROOT, "docs", "reports", "sound-kit-blind-answers.md"), encoding="utf-8") as fh:
        for line in fh:
            if line.startswith("| ") and ".wav" in line:
                cells = [c.strip() for c in line.strip("|\n").split("|")]
                answers[cells[0]] = cells[1].split("(")[-1].rstrip(")")
    print("\n### 눈 가리고 듣기 세트 (8초씩) 와 규칙 판별\n")
    rows = []
    results = []
    for name in sorted(answers):
        b, r = read_wav(os.path.join(AUDIO, "blind", name))
        f = features(b, r)
        rows.append((name, f))
        # 2초 창 네 개도 따로 판별한다.
        wins = [classify(features(b[i * 2 * r:(i + 1) * 2 * r], r)) for i in range(4)]
        results.append((name, classify(f), wins, answers[name]))
    table(rows)
    print("\n| 파일 | 규칙 판별 (8초) | 2초 창 넷 | 정답 |\n| --- | --- | --- | --- |")
    ok = total = 0
    for name, whole, wins, ans in results:
        ok += (whole == ans) + sum(w == ans for w in wins)
        total += 1 + len(wins)
        print(f"| {name} | {whole} | {', '.join(wins)} | {ans} |")
    print(f"\n맞힌 수: {ok}/{total}")


def cmd_hunter():
    rows = []
    for state in ["idle", "curious", "approaching", "searching", "leaving"]:
        s, rate = read_wav(os.path.join(AUDIO, f"hydrophone-{state}.wav"))
        if state == "leaving":
            s = s[int(3 * rate):]  # 앞 2초는 Searching
        # 500Hz 고역 통과 뒤 2ms 창의 최대값이 바다 잡음 바닥(중앙값)의 3배(약 10dB)를 넘는 순간을 센다.
        h = highpass(s, rate, 500)
        w = rate // 500
        peaks = [max(abs(x) for x in h[i:i + w]) for i in range(0, len(h) - w, w)]
        floor = sorted(peaks)[len(peaks) // 2]
        onsets, hold = 0, 0
        for p in peaks:
            if hold:
                hold -= 1
            elif p > floor * 3:
                onsets += 1
                hold = 10
        spec = spectrum(s)
        hf = 10 * math.log10(band(spec, rate, 2000, 8000) / band(spec, rate, 20, 11000))
        rows.append((state, onsets / (len(s) / rate), hf, max(abs(x) for x in s)))
    print("| 상태 | 초당 딸깍(메아리 포함) | 2~8kHz 비 (dB) | 최대값 |\n| --- | --- | --- | --- |")
    for st, rate_, hf, pk in rows:
        print(f"| {st} | {rate_:.2f} | {hf:.1f} | {pk:.2f} |")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    cmd = sys.argv[1]
    if cmd == "features":
        table([(os.path.basename(p), features(*read_wav(p))) for p in sys.argv[2:]])
    elif cmd == "pump":
        cmd_pump()
    elif cmd == "hunter":
        cmd_hunter()
    else:
        print(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
