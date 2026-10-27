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
