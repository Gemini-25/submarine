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
| `game/` | Godot 4.7.2 .NET 프로젝트 (M1부터) |

## 실행

.NET 8 SDK가 필요하다.

```sh
dotnet test                                   # 전체 테스트
dotnet run --project tools/DeepLine.SimCli -- validate
dotnet run --project tools/DeepLine.SimCli -- run sim.tests/scenarios/10-wtr01-clogged-intake-floods-battery.json --every 1200
```

`run`은 기대값 판정, 주요 사건, 시계열(CSV)을 출력한다. `--paths battery.voltage,bilge.total,...`로 볼 값을 고르고 `--csv 파일`로 저장한다. 경로 이름은 `sim/Core/SimQuery.cs`에 있다.
