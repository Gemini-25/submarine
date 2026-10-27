# DEEP LINE

1930년대 디젤펑크 세계의 소형 해저 케이블 수리 잠수함 안에서 계통 고장을 진단하고 고치며 살아남는 1인칭 수리 시뮬레이션.

- 게임 설계서: [docs/DESIGN.md](docs/DESIGN.md)
- 개발 명세서: [docs/SPEC.md](docs/SPEC.md)
- 설계 검토와 질문: [docs/REVIEW.md](docs/REVIEW.md)
- 결정 기록: [docs/DECISIONS.md](docs/DECISIONS.md)
- 마일스톤 보고서: [docs/reports/](docs/reports/)

## 구조

| 폴더 | 내용 |
| --- | --- |
| `sim/` | 시뮬레이션 코어 `DeepLine.Sim`. 순수 .NET 8, Godot 참조 없음 |
| `sim.tests/` | xUnit 테스트. 시나리오는 `sim.tests/scenarios/*.json` |
| `data/` | 튜닝 값과 콘텐츠 (JSON) |
| `tools/DeepLine.SimCli/` | 시나리오를 돌려 시계열을 뽑는 개발 도구 |
| `audio/` | 상태 기반 절차적 소리 합성 라이브러리 `DeepLine.Audio`와 시험 (M5 준비, 아직 게임에 연결 전) |
| `tools/DeepLine.AudioRender/` | 소리 데모를 WAV로 뽑는 도구. 데모는 `docs/reports/audio/` |
| `docs/drafts/story/` | 이야기 글 초안 (지시서, 결산, 기록물, 거짓 신호, 조난과 결말) |
| `data/manual/` | 게임 속 정비 교범 (마크다운) |
| `game/` | Godot 4.7.2 .NET 프로젝트. 장면은 `game/scripts/World/`에서 코드로 짓는다 |

## 실행

.NET 8 SDK가 필요하다.

```sh
dotnet test                                   # 전체 테스트
dotnet run --project tools/DeepLine.SimCli -- validate
dotnet run --project tools/DeepLine.SimCli -- run sim.tests/scenarios/10-wtr01-clogged-intake-floods-battery.json --every 1200
```

`run`은 기대값 판정, 주요 사건, 시계열(CSV)을 출력한다. `--paths battery.voltage,bilge.total,...`로 볼 값을 고르고 `--csv 파일`로 저장한다. 경로 이름은 `sim/Core/SimQuery.cs`에 있다.

## 게임 실행 (M1부터)

Godot 4.7.2 **.NET** 판과 .NET 8 SDK가 필요하다.

1. Godot에서 `game/project.godot`를 연다. 처음 열면 글꼴을 가져오고 C# 프로젝트를 빌드한다(편집기 오른쪽 위 '빌드' 버튼).
2. F5(실행)로 시작한다. `data/` 폴더는 저장소의 것을 그대로 읽는다.

조작은 게임 안 교범(Tab)의 마지막 장 '조작 안내'에 있다. 개발 빌드에서는 F3(값 보기), F4(명령 창), F5(멈춤), F6(시간 배율)을 쓸 수 있다.

명령줄 확인(헤드리스):

```sh
GODOT=/경로/Godot_v4.7.2-stable_mono_linux.x86_64 tools/godot-check.sh          # 빌드, 스모크, 종단 자동 플레이
GODOT=... tools/godot-check.sh --screenshots shots/                                 # 화면 찍기 (xvfb-run 필요)
```
