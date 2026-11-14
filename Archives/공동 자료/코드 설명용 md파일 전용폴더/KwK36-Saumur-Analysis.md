# KwK 36 포미 외형 분석

Saumur 후기형 Tiger I의 포미 사진 두 장을 기준으로, 장전수 쪽 후면과 오른쪽 측면에 보이는 형상을 재구성했다. 참고 사진의 녹회색은 해당 전시 차량에서 보이는 도장이다. 사진으로 가려진 접합부와 치수는 보이는 비율에서 추정했다. [Saumur 포미 전체와 확대 사진](https://tiger1.info/EN/Hachette-gun-breech.html)

| 관찰한 형상 | Blender 모델에 반영한 부분 |
| --- | --- |
| 두꺼운 몸체의 후면에서 위로 열린 U자형 홈이 이어진다. | 몸체를 하나의 메시로 만들고, 원호 바닥과 양쪽 벽을 그 안에 파냈다. 위쪽 경사면을 길게 잡아 사진에서 보이는 두꺼운 어깨를 만들었다. |
| 깊은 곳에 원형 장전구가 보이고, 가운데에서 폐쇄 블록이 수직으로 움직인다. | 원형 장전구를 후면보다 앞에 두고, 둥근 윗면의 폐쇄 블록이 그 앞을 통과하도록 구성했다. 개방 시 블록이 탄피의 이동 경로 아래로 내려간다. |
| 오른쪽 아래에 큰 원형 덮개가 있고 그 앞쪽에 작은 기구 하우징이 붙는다. | 큰 원형 덮개와 앞쪽 하우징을 새로 만들었다. 오른쪽 손잡이와 레버의 메시, 재질, 피벗, 66° 회전량은 기존 모델을 사용했다. |
| 왼쪽에 반자동 작동 기구가 따로 있다. | 포미와 함께 움직이는 왼쪽 팔과 고정 브래킷을 별도 부품으로 뒀다. 사진에 드러나는 배치를 반영했으며, 숨은 링크의 형상과 세부 궤적은 추정했다. |

[Saumur 실차 사진](https://tiger1.info/EN/Hachette-gun-breech.html) · [포가와 포미 접합부 사진](https://tiger1.info/EN/Cradle.html) · [Tiger 131 반자동 브래킷 사진](https://tiger1.info/EN/Semi-automatic-bracket.html)

## Blender 동작 확인

| 프레임 | 자세 |
| --- | --- |
| 1, 32 | 닫힘, 오른쪽 레버로 수동 개방 |
| 54, 73 | 포신 축을 따라 삽입, 포탄 안착 |
| 96, 107 | 폐쇄 완료, 발사 |
| 116, 132 | 반동, 복귀 중 자동 개방 |
| 156, 180 | 빈 탄피가 뒤로 이동, 열린 상태 유지 |

탄피의 표시 비율은 571mm 탄피를 기준으로 잡았다. 타임라인의 이동량과 시간은 동작을 읽기 위한 애니메이션 값이다. [The Tank Museum의 KwK 36 탄약 비교](https://tankmuseum.org/article/same-size-different-round) · [1943년 영국군 Tiger 조사 보고서](https://www.military-references.com/wp-content/uploads/books/tanks/germany/panzer_vi_tiger_ausf_h1/Preliminary_Report_No_19_Pz_Kw_VI_Tiger_1943.pdf)

