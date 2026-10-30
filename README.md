# DEEP LINE

1930년대 디젤펑크 세계의 1인칭 잠수함 수리 시뮬레이션이다. 플레이어는 소형 해저 케이블 수리선 모스호의 기관사가 되어, 좁은 선체 안에서 계통 고장을 진단하고 손으로 고치며 버틴다. 정답을 알려 주는 화면은 없다. 상황판과 계기, 소리와 냄새, 손에 닿는 느낌을 읽고, 배에 실린 정비 교범을 펴서 원인을 좁혀 간다.

게임은 두 층으로 나뉜다.

- **시뮬레이션 코어** (`sim/`): 순수 .NET 8 라이브러리. 20Hz 고정 틱으로 돌고, 같은 시드와 같은 명령이면 언제나 같은 결과를 낸다. Godot 없이 `dotnet test`만으로 전부 검증한다.
- **표현 층** (`game/`): Godot 4.7.2 .NET 프로젝트. 시뮬레이션 상태를 읽어 그리고, 플레이어의 손동작을 명령으로 바꿔 넘긴다. 장면은 에디터가 아니라 코드로 짓는다.

## 지금 할 수 있는 것 (M2)

M2 '8대 계통'까지 들어 있다. 동력, 배전반, 공기, 물(배수와 밸러스트), 선체, 감각, 항법, 케이블 여덟 계통에 고장 26종이 있고, 계통마다 대표 고장을 진단하고 고치는 훈련이 있다.

| 훈련 | 내용 |
| --- | --- |
| 위기 훈련: 후방 기관 구역 | M1의 한 판. 흡입구 막힘, 리벳 누수, 퓨즈 단선이 이어진다 |
| 동력 훈련 A, B | 축전지 셀과 전동기 브러시 / 증기 축압기 |
| 배전반 훈련 A, B | 분기함 누전과 탄 배선 / 칼날 스위치 접점 |
| 공기 훈련 A, B | 송풍 팬과 흡수통 / 산소 화로 |
| 배수 훈련 A, B | 배수 펌프 / 역류 방지 밸브 |
| 밸러스트 훈련 | 기우는 배 |
| 선체 훈련 A, B | 후방 누수 / 현창과 케이블 챔버 |
| 감각 훈련 A, B | 상황판과 청음기 / 상황판 배선, 탐조등, 계기 |
| 항법 훈련 | 자이로컴퍼스와 항법 계산기 |
| 케이블 훈련 | 윈치와 사이펀 기록기 |
| 시운전 | 여러 계통의 고장이 겹친다 |

임무 연쇄, 보급, 이야기는 M3부터 들어간다. 마일스톤 전체는 [docs/SPEC.md](docs/SPEC.md) 12절에 있다.

## 준비물

- .NET 8 SDK
- Godot 4.7.2 **.NET** 판 (C#을 쓰므로 일반 판으로는 열리지 않는다). 명령줄에서는 `tools/fetch-godot.sh`로 받을 수 있다.

## 게임 실행

1. Godot에서 `game/project.godot`를 연다. 처음 열면 글꼴을 가져오고, 편집기 오른쪽 위의 '빌드' 버튼으로 C# 프로젝트를 빌드한다.
2. F5(실행)로 시작한다. 데이터는 저장소의 `data/` 폴더를 그대로 읽는다.

명령줄에서 바로 띄울 수도 있다. `--` 뒤의 `--mission=<임무 ID>`로 시작할 훈련을 고른다.

```sh
"$GODOT" --path game                          # 위기 훈련부터
"$GODOT" --path game -- --mission=m2-air-a    # 공기 훈련 A부터
```

### 한 판의 흐름

1. **지시서 화면.** 시작하면 모선이 보낸 지시서가 뜬다. 왼쪽 '훈련 고르기' 목록에서 훈련을 고르면 그 훈련의 지시서로 바뀐다. Enter를 누르거나 지시서를 누르면 근무가 시작된다.
2. **근무.** 배 안을 돌아다니며 이상을 찾고 고친다. 막히면 Tab으로 교범을 편다.
3. **결과 화면.** 훈련을 마치거나 실패하면 결과가 뜬다. R을 누르면 지시서 화면으로 돌아가고, 거기서 같은 훈련을 다시 하거나 다른 훈련을 고른다.

## 조작

| 입력 | 동작 |
| --- | --- |
| W A S D, 마우스 | 걷기, 둘러보기 |
| Ctrl (누른 채) | 웅크리기 |
| 왼쪽 버튼 | 잡기. 누른 채 마우스를 움직이면 손이 따라 움직인다 |
| 오른쪽 버튼 (누른 채) | 가까이 보기 |
| F | 작업 시점에 들어가고 나오기 (배전반, 흡입 거름망, 사물함, 상황판 등) |
| 1 | 빈손 |
| 2 ~ 6 | 공구띠 칸의 도구 |
| G | 쥔 도구를 바닥에 내려놓기 |
| T | 맨손으로 만져 보기 (뜨거운지, 젖었는지, 떨리는지) |
| Tab | 교범 펴기, 덮기 |
| F1 | 교범의 '조작 안내' 쪽 바로 펴기 |
| N | 메모장 |
| Esc | 창 닫기, 작업 시점에서 나오기 |
| R | 결과 화면에서 지시서로 돌아가기 |

볼트 돌리기, 칼날 스위치 올리고 내리기, 수동 펌프 젓기 같은 손동작은 교범의 '조작 안내' 쪽에 자세히 나온다. 도구는 해치 격벽의 공구판에서 가져와 다섯 칸짜리 공구띠에 건다.

개발 빌드에서는 F3(값 보기), F4(명령 창), F5(멈춤), F6(시간 배율)을 쓸 수 있다.

## 정비 교범 (Tab)

교범은 배에 실린 책이고, 게임 안에서 정답을 찾는 유일한 길이다. 원문은 `data/manual/*.md`이며, 정격 전류나 경보 문턱 같은 숫자는 `{{...}}` 자리표시자로 두어 시뮬레이션 데이터에서 채운다(`sim/Manual/ManualText.cs`). 그래서 튜닝 값을 바꾸면 교범의 숫자도 함께 바뀐다.

- **쪽 번호.** 00~19쪽은 배 전체에 관한 일반 쪽(상황판 읽는 법, 진단 표, 도구, 예비품, 재는 법, 계산 도구, 공기와 몸 상태, 침수 대응, 부력과 기울기)이다. 20쪽부터는 계통마다 수리 절차를 싣는다. 차례는 00쪽에 있다.
- **계통 탭.** 위쪽 탭으로 한 계통의 쪽만 골라 볼 수 있다. 지시서와 조작 안내는 어느 탭에서나 보인다.
- **읽는 순서.** 이상이 생기면 계통 상황판(02쪽)을 보고, 진단 표(03~05쪽)에서 원인을 하나로 좁힌 뒤 그 절차 쪽을 편다.
- **그림.** 쪽마다 위치도, 부품 그림, 절차 순서 띠, 계통도가 들어 있다(`data/manual/fig/`). 장면 그림은 게임 장면을 그 상태로 맞춰 찍은 뒤 먹선 그림으로 바꾼 것이고, 계통도는 데이터에서 그린다. 그림 안에는 한국어를 굽지 않고 번호만 넣으며, 설명은 교범 본문의 캡션에 둔다. 정의는 `data/manual/fig/figures.json`, 다시 만드는 도구는 `tools/make-figures.sh`다.

## 시험

시뮬레이션은 Godot 없이 검증한다.

```sh
dotnet test                                   # 시뮬레이션, 소리, 계산 도구 시험 전부
dotnet run --project tools/DeepLine.SimCli -- validate
dotnet run --project tools/DeepLine.SimCli -- run sim.tests/scenarios/10-wtr01-clogged-intake-floods-battery.json --every 1200
```

- **시나리오 시험** (`sim.tests/scenarios/*.json`): 고장을 넣고 증상이 기대한 순서와 시간 안에 나타나는지 본다.
- **기준 해시** (`sim.tests/fixtures/golden-*`): 정해진 시드와 명령으로 돌린 마지막 상태의 해시. 규칙을 바꾸다 결과가 조금이라도 달라지면 실패한다. 다시 만드는 때와 이유는 `docs/DECISIONS.md`에 적는다.
- **훈련 풀이** (`sim.tests/fixtures/drill-logs/*.json`): 규칙 기반 자동 플레이어(DrillSolver)가 상황판과 단서만 보고, 진단 자료(`data/diagnosis*`)로 원인을 좁혀 훈련을 끝까지 푼 기록이다. 진단 자료나 절차를 바꿔 기록이 달라지면 아래처럼 다시 만들고, 바뀐 줄이 의도한 것인지 확인한다.

  ```sh
  DEEPLINE_REGEN_GOLDEN=1 dotnet test sim.tests --filter "FullyQualifiedName~DrillLogTests"
  ```

- **결정론.** 시뮬레이션 코어에서는 실제 시각, 시드 없는 난수, 플랫폼마다 마지막 자리가 다를 수 있는 수학 함수를 분석기로 막는다(`sim/BannedSymbols.txt`).

SimCli의 `run`은 기대값 판정, 주요 사건, 시계열(CSV)을 출력한다. `--paths battery.voltage,bilge.total,...`로 볼 값을 고르고 `--csv 파일`로 저장한다. 경로 이름은 `sim/Core/SimQuery.cs`에 있다.

### Godot 쪽 확인

Godot 쪽은 헤드리스로 띄워 자동 플레이어가 실제 손동작(조준, 끌기, 돌리기)으로 훈련을 해 보게 한다.

```sh
tools/fetch-godot.sh                                                    # Godot 4.7.2 .NET 받기 (경로를 알려 준다)
GODOT=/경로/Godot_v4.7.2-stable_mono_linux.x86_64 tools/check-all.sh   # dotnet test와 Godot 확인 전부 (커밋 전에)
GODOT=... tools/godot-check.sh crisis workviews drills                 # 확인 묶음만 골라서
GODOT=... tools/godot-check.sh --screenshots shots/                     # 화면 찍기 (xvfb-run 필요)
GODOT=... tools/make-figures.sh                                         # 교범 그림 다시 만들기 (xvfb-run 필요)
```

자주 쓰는 확인 묶음은 다음과 같다. 전체 목록은 `tools/godot-check.sh` 머리말에 있다.

| 묶음 | 확인하는 것 |
| --- | --- |
| `crisis` | 위기 훈련 한 판 |
| `hands` | 위험(감전, 화상, 끼임, 물줄기)과 손의 느낌 |
| `reach`, `workviews` | 서 있는 자리에서 손이 닿는지, 작업 시점마다 대상이 화면에 들어오고 조준되는지 |
| `fixtures`, `tools` | 데이터 장치의 손동작, 공구판과 공구띠, 도구와 물건의 모양 |
| `briefing` | 지시서 화면의 훈련 고르기 |
| `drills` | M2 훈련 열여섯을 DrillSolver로 풀고, 걸음마다 손동작을 해 시뮬레이션 기록과 해시까지 견준다 |
| `drill:<임무>` | 그 훈련 하나만. 예: `drill:m2-cable` |
| `instruments` | 계산 도구(탁상 계산기, 계산자, 노모그램, 저항 브리지)를 손으로 다루기 |
| `figures` | 교범 그림이 지금 장면, 절차, 데이터와 맞는지 |

## 폴더 구조

| 폴더 | 내용 |
| --- | --- |
| `sim/` | 시뮬레이션 코어 `DeepLine.Sim`. Godot을 참조하지 않는다 |
| `sim/Core/` | 틱 루프, 명령 처리, 상태 해시, 값 조회(`SimQuery`) |
| `sim/Systems/` | 계통 규칙(전기, 공기와 소음, 침수, 증기, 기울기, 항법, 케이블 장비 등) |
| `sim/Parts/` | 부품 공통 메커니즘(응급 수리의 수명, 볼트 조이는 순서 같은 숨은 결함) |
| `sim/Repair/` | 손이 닿는 장치 엔진, 손 수리 판정, 수리 절차, 공구판 |
| `sim/Faults/` | 고장 주입, 임무 진행, 시나리오와 조건식 |
| `sim/Data/` | 데이터 읽기와 검증 |
| `sim/Autoplay/` | 자동 플레이어. `DrillSolver`는 관찰 가능한 값(`ObservableQuery`)만 읽고 판단한다 |
| `sim/Manual/` | 교범 자리표시자 채우기와 문법 검사 |
| `sim.tests/` | xUnit 시험. 시나리오는 `scenarios/`, 기준 해시와 풀이 기록은 `fixtures/` |
| `data/` | 튜닝 값과 콘텐츠(JSON). 큰 파일은 `data/<이름>/*.json` 조각으로 나눌 수 있다 |
| `data/faults*`, `data/procedures*`, `data/diagnosis*` | 고장 정의, 수리 절차, 진단 자료 |
| `data/fixtures/`, `data/items.json` | 손이 닿는 장치의 정의와 물건 정보 |
| `data/observables*` | 현장 단서, 제자리 계기, 청진기 대상, 측정 수치(계산값이며 상태가 아니다) |
| `data/missions/` | 훈련 임무(지시서, 고장, 시작 조건) |
| `data/manual/` | 정비 교범(마크다운)과 그림(`fig/`) |
| `data/text/ko/` | 화면에 나오는 한국어 문구 |
| `game/` | Godot 프로젝트. `scripts/World/`(배 짓기), `Art/`(모델 키트, 재질), `Interaction/`과 `Player/`(손과 몸), `UI/`(상황판, 교범, 지시서), `Gadgets/`(계산 도구), `Testing/`(자동 플레이어, 화면 찍기) |
| `instruments/` | 선내 계산 도구의 역학 `DeepLine.Instruments`와 시험. 화면 표시 전용이다 |
| `audio/` | 상태 기반 절차적 소리 합성 `DeepLine.Audio`와 시험(M5에서 게임에 연결) |
| `tools/` | 개발 도구: SimCli(시나리오 실행), AudioRender(소리 데모를 WAV로), 확인과 그림 스크립트 |

## 문서

| 문서 | 내용 |
| --- | --- |
| [docs/DESIGN.md](docs/DESIGN.md) | 게임 설계서: 세계, 이야기, 놀이의 고리 |
| [docs/SPEC.md](docs/SPEC.md) | 개발 명세서: 계통 수치, 고장 목록, 데이터 형식, 마일스톤, 시험 전략, 열린 질문 |
| [docs/DECISIONS.md](docs/DECISIONS.md) | 정한 것과 그 이유를 날짜순으로 적은 기록 |
| [docs/REVIEW.md](docs/REVIEW.md) | 처음 설계 검토와 질문 |
| [docs/plans/](docs/plans/) | 마일스톤 계획. M2는 `M2.md`(요약), `M2-design.md`(작업 묶음별 설계), `WP16-solver.md`, `M2-figures-and-tools.md` |
| [docs/reports/](docs/reports/) | 마일스톤과 작업 묶음 보고서, 스크린샷 |
| [docs/reviews/](docs/reviews/) | 작업 묶음 검토 기록 |
| [docs/ASSETS.md](docs/ASSETS.md) | 글꼴 같은 외부 자원의 출처와 사용 허가 |
| [docs/drafts/story/](docs/drafts/story/) | 이야기 글 초안(지시서, 결산, 기록물, 거짓 신호, 조난과 결말) |
