# 외부 자원 목록

SPEC 11절 '에셋 방침'에 따라 저장소에 들어온 외부 자원의 출처와 라이선스를 전부 적는다. 유료 자원은 쓰지 않는다.

## 글꼴

| 파일 | 쓰는 곳 | 출처 | 라이선스 |
| --- | --- | --- | --- |
| `game/assets/fonts/NanumGothic-Regular.ttf` | 화면 글씨 기본 글꼴, 물체의 명판과 눈금 | Google Fonts 저장소 `ofl/nanumgothic` (https://github.com/google/fonts) | SIL Open Font License 1.1 |
| `game/assets/fonts/NanumMyeongjo-Regular.ttf` | 정비 교범, 지시서, 결과 화면 | Google Fonts 저장소 `ofl/nanummyeongjo` | SIL Open Font License 1.1 |
| `game/assets/fonts/NanumPenScript-Regular.ttf` | 메모장 손글씨 | Google Fonts 저장소 `ofl/nanumpenscript` | SIL Open Font License 1.1 |

- 저작권: NHN Corporation (2010). 라이선스 전문은 `game/assets/fonts/OFL.txt`.
- 예약 글꼴 이름(Nanum, NanumGothic 등)이 있어서 글꼴 파일을 고쳐 배포하면 이름을 바꿔야 한다. 지금은 고치지 않고 그대로 쓴다.
- 굵은 글씨는 굵은 글꼴 파일을 싣지 않고 Godot의 `FontVariation.VariationEmbolden`으로 그린다.

## 그 밖

- 3D 모형, 재질, 소리는 모두 코드로 만든다(회색 상자 도형, `ProceduralAudio`의 합성음). 외부 파일이 없다.

## 아트 키트 질감 (M5 준비)

`game/scripts/Art/`의 절차적 셰이더가 쓰는 잔결(도장면 요철과 거칠기, 녹, 금속 긁힘) 질감. 무늬의 큰 모양(모서리 닳음, 때, 판 이음선, 젖음)은 셰이더가 만들고, 이 질감은 표면을 가까이서 볼 때의 결만 보탠다. 모두 ambientCG의 1K-JPG 묶음에서 한 장씩 골라 512×512로 줄였다(회색조 질감은 회색으로 바꿈). 내용은 고치지 않았다.

| 파일 | 쓰는 곳 | 출처 | 라이선스 |
| --- | --- | --- | --- |
| `game/assets/textures/paintedmetal009_normal.jpg` | 도장 강철 셰이더의 잔결 법선 | ambientCG `PaintedMetal009` NormalGL (https://ambientcg.com/view?id=PaintedMetal009) | CC0 1.0 |
| `game/assets/textures/paintedmetal009_roughness.jpg` | 도장 강철 셰이더의 거칠기 얼룩 | ambientCG `PaintedMetal009` Roughness | CC0 1.0 |
| `game/assets/textures/rust004_color.jpg` | 맨 강철이 드러난 곳의 녹, 녹물 줄 | ambientCG `Rust004` Color (https://ambientcg.com/view?id=Rust004) | CC0 1.0 |
| `game/assets/textures/rust004_normal.jpg` | 녹 슨 곳의 요철 | ambientCG `Rust004` NormalGL | CC0 1.0 |
| `game/assets/textures/scratches001_mask.jpg` | 금속 셰이더(황동, 구리, 청동, 맨 강철)의 긁힘 | ambientCG `Scratches001` Color (https://ambientcg.com/view?id=Scratches001) | CC0 1.0 |

- 받은 날: 2026-10-27. ambientCG는 모든 자원을 CC0로 배포한다(https://docs.ambientcg.com/license/). 출처 표시 의무는 없지만 기록해 둔다.
- 가져오기 설정(`*.import`): VRAM 압축, 밉맵 생성, 법선 맵 두 장은 `compress/normal_map=1`.
- 그 밖의 키트 재질(대리석, 나무, 물, 빛줄기)과 모든 메시는 코드와 셰이더(`game/assets/shaders/art/`)로 만든다.
