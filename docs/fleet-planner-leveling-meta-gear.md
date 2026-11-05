# 편성·육성: 쫄작 · META 보스 · 장비 추천 구현 명세

- 상태: 방향 승인됨(2026-10-17). 기본값은 **기사 수 자동**, **장비 출처 '옮기기 포함'**이다.
- 시안: `Reports/UI/leveling-meta-mockup.html`
  - 로컬 전용이고 커밋하지 않는다. 실제 계정 데이터가 들어 있다.
  - `?frame=leveling`, `?frame=meta`, `&open=1`(선택 목록 열림), `&gear=bag`(창고만), `&anno=1`(시선 순서 번호)로 상태를 바꿔 볼 수 있다.
- 이 문서에 계정 데이터는 넣지 않는다. 수치 예시는 게임 데이터(Lua)와 검증 공식만 쓴다.

**진행 상황** (2026-10-17)

- 완료: 5절 구현 순서의 1단계(맵) 중 생성기 부분과 2단계(Core 계산 로직과 Checks).
  - 맵 생성기
    - `stages.json`에 `exp`를 추가했다.
    - `ship_level.json`에 `awaken`(희귀도 코드 → `gold`·`15008`·`15012`)을 추가했다.
    - `meta_bosses.json`을 새로 만들었다.
    - 생성 위치: `NativeLuaMapBuilder.MetaBossMaps.cs`, `.ProgressionMaps.cs`, `.Source.cs`, `.LuaParsing.cs`.
  - 시드 맵 세 개를 다시 생성했다.
    - 기존 두 파일의 변경은 필드 추가뿐이다.
    - 새러토가·META의 Lua 스킬 설명에 "25초마다 중형/중장갑 교체"가 직접 적혀 있다.
  - Core 계산 로직
    - 쫄작: `LevelingExperience`, `ShipLevelCaps`, `LevelingPlanBuilder`.
    - META 보스: `MetaBossCatalog`, `MetaBossPlanBuilder`.
    - 장비: `RegalGearMap`, `GearAllocator`.
    - 모델 파일: `LevelingPlanRows`, `MetaBossRows`, `GearPlanRows`.
    - `FleetPlannerShip`에 `RegalUid`를 추가했다.
  - Checks: `LevelingPlanChecks`, `MetaBossChecks`, `GearAllocationChecks`.
- 완료(2단계 이후): 데이터·화면 연결.
  - Data
    - `NativeSnapshotService.FleetPlannerInputs.cs`가 스냅샷에 쫄작·META·장비 입력을 싣는다.
      - 전투 랭크: CS 40003과 SC 40004를 짝지어 읽는다.
      - 쫄작 함대 감지, 게임에서 고른 상시 보스(34502), 장비 보유·장착 정보.
    - `meta_bosses.json`과 `meta_boss_guides.json`을 `MapFileStore`에 등록했다.
  - 공략 파일 `maps/meta_boss_guides.json`
    - 새러토가·META 1건을 Suchiguma 원문(2026-09-16)과 대조해 넣었다.
    - Regal 원문 설명에 조건이 적힌 별표 규칙 4건(오로라, 차파예프, 빅토리어스, 시나노)을 넣었다.
  - 개조(改) 함선 매칭 수정
    - 개조 함선은 템플릿 ID로 Regal과 맞춘다(예: 엘드릿지·改 101994).
    - 이전에는 그룹으로만 찾아 편성·평가·장비 추천에서 빠졌다.
  - App: `편성·육성`에 `레벨링 > 쫄작` 탭과 META 보스 선택, 장비 추천 패널을 넣었다.
    - 뷰: `LevelingPlanView`, `MetaBossPlanView`, 공용 스타일 `FleetPlannerShared.xaml`.
    - 설정 저장: 해역, 기사 수, 보스, 장비 출처.
  - Checks: App.Checks가 새 두 탭의 상태 전환과 바인딩 오류를 확인한다.
- 남은 일
  - 상시 보스 18종의 공략을 원문과 대조해서 추가한다.
  - 증강(augment) 추천.
  - 해역 탭에 장비 패널을 붙인다.
- 0절의 착수 조건이었던 DB 정리 작업은 `71fc7b8`로 커밋되었다.
- 추가(2026-11-06): META 보스전 탭 아래 `실전 기록` 섹션.
  - 출처
    - CS 40001(system 50)의 출격 함선.
    - CS 40003의 함선별 준/받은 피해, 남은 HP, 보스 `enemy_info`.
  - 저장: `current_meta_boss_fights`에 출격 시점 장비와 함께 따로 둔다. decoded_packets는 2만 건까지만 남기 때문이다.
  - 표시: 선택한 보스의 전투만 보여 준다. 보스는 적 ID로 가린다.
  - 판정
    - 시작 HP 99% 미만인 전투는 마무리 전투라 비교에서 뺀다.
    - 둘 다 처치한 전투는 시간으로 비교한다.
  - META 편성은 기기에만 있고 보스마다 바뀌므로 `이 보스 마지막 출격과 N/6척 같음`은 그 보스의 마지막 전투와 비교한다.
  - 코드: `MetaBossFightAnalyzer`, `MetaFightRows`, `NativeSnapshotService.MetaBossFights.cs`, `FleetPlannerViewModel.MetaFights.cs`. Checks: `MetaFightChecks`.
- 추가(2026-11-06): 추천 편성의 자리 규칙(`FleetSlotOrder`).
  - 근거: 전투 코드에서 주력 첫 칸은 기함(`BattleFleetVO.appendMainUnit`)이고, 전위 첫 칸은 선두(`_leaderUnit = _scoutList[1]`)다.
  - 선두: 고른 전위 중 Regal `Self Survival`(Portland=100 기준 eHP)이 가장 큰 함선이다.
    - 주력은 별도의 -5~5 척도라 섞지 않는다.
    - Regal 설명이 선두를 피하라는 함선(블뤼허, 시애틀, 르 트리옹팡, 르 아르디, 밀러)은 제외한다.
  - 기함: 고른 주력 중 Regal `Flag Req`(기함이 아니면 잃는 정도, -1~-5)가 가장 큰 함선이다.
    - 아무도 값이 없으면 기함을 표시하지 않는다.
    - 기함을 피하라는 함선(아마기(항모), 던컨, 울리히 폰 후텐)은 첫 칸에서 뺀다.
  - 검토된 META 공략이 있으면 공략의 기함과 선두가 우선한다.
  - 쫄작은 기존 규칙을 따른다(기사가 선두, 레벨 차이 10 이하 쩔이 기함).
  - `Fleet Req`는 Regal이 2026-07에 폐기한 값이라 쓰지 않는다.

## 0. 착수 조건과 담당 범위

별도 세션의 DB 정리 작업이 아직 미커밋 상태다. 바뀌는 파일은 다음과 같다.
- `CurrentStateWriter.*`
- `NativeSnapshotService.cs/.Cache.cs/.SqlJson.cs`, `LivePacketStore.cs`
- `MapFileStore.cs` 신설, `JsonStoreService.cs` 삭제
- `DataUpdateService.*`, `UpdateOptions.cs`

그래서 다음 순서를 지킨다.
- **그 작업이 커밋된 뒤에 착수한다.** 새 맵 파일은 그 작업에서 생긴 `MapFileStore.DashboardMapFiles`에 등록한다. 등록 위치가 최종적으로 바뀌는지는 착수 시 다시 확인한다.
- DB 쪽에 필요한 열(4.4절)은 DB 작업 담당과 먼저 맞춘다. 같은 파일을 동시에 고치지 않는다.

이번 범위가 아닌 것은 다음과 같다.
- 게임 편성에 실제로 반영하는 기능. 추천은 읽기 전용이다.
- 증강(augment) 추천. 2단계에서 다룬다.
- 해역 탭의 장비 패널. 2단계에서 다룬다.

## 1. 대상 화면과 확인한 파일

대상 화면은 `편성·육성` 페이지(`FleetPlannerView`)다.
- 기존 `META 보스전` 탭을 보스 선택형으로 다시 구성한다.
- 탭 그룹 `레벨링`을 새로 만들고 그 안에 `쫄작` 탭을 추가한다.

확인한 파일과 각 파일의 현재 동작은 다음과 같다.

| 파일 | 현재 동작 |
|---|---|
| `src/AzurLaneDashboard.Core/Services/FleetPlanBuilder.cs` | 콘텐츠 정의 `Contents`(w1516, w14, arbiter, meta, cm, ex)가 있다. `Pick`이 위치별 상위 3척을 고르고 `FindCandidates`가 교체 후보를 찾는다. `Damage()`는 보스전이면 항상 `Heavy Dmg`를 쓴다(4.2절에서 고친다). |
| `src/AzurLaneDashboard.Core/Models/FleetPlanRows.cs` | `FleetPlanDashboard/Content/Fleet/Slot/Candidate`가 있다. |
| `src/AzurLaneDashboard.App/ViewModels/FleetPlannerViewModel.cs` | `ChapterTabs`(그룹 "해역")와 `BossTabs`(그 외)만 있다. 새 그룹을 넣으려면 이 구조를 넓혀야 한다. |
| `src/AzurLaneDashboard.App/Views/FleetPlannerView.xaml(.cs)` | `ContentTab` 라디오 탭과 `SlotTemplate`/`FleetTemplate`이 있다. `Body` 그리드는 `3*` : 20 : `2*`(최소 340)이다. 900px 미만에서는 오른쪽 열을 위로 올리는 스택 배치를 한다. |
| `src/AzurLaneDashboard.Data/Services/NativeSnapshotService.FleetPlan.cs` | `ShipState`와 Regal을 묶어 `FleetPlannerShip`을 만든다. |
| `src/AzurLaneDashboard.Core/Models/AppSettings.cs` | `GrowthGoals`가 있다(쩔 우선순위 1순위의 근거). |
| `tests/AzurLaneDashboard.App.Checks/ShellChecks.cs` | `CheckTabStripStays`, `CheckFleetPlanner`가 있다. |
| `maps/stages.json` | 스테이지별 `level`(적 레벨)은 있고 `exp`는 없다. |
| `maps/ship_level.json` | `exp`, `exp_ur`, `level_limit`만 있고 각성 비용은 없다. |

## 2. 해결할 UX 문제

1. **쫄작**
   - 플레이어가 누구를 기함에 둘지, 기사를 몇 척 둘지, 언제 교대할지를 매번 머리로 계산한다.
   - 전의 보너스와 인식 각성 같은 레벨 구간이 어디에도 보이지 않는다.
2. **META 보스**
   - 보스마다 장갑과 기믹이 다른데, 지금은 한 탭에서 중장갑 피해량 하나로 점수를 매긴다.
   - 상시 보스를 고를 수 없다.
3. **장비**
   - 추천 장비를 갖고 있지 않으면 다음 행동이 없다.
   - 다른 함선이 쓰는 장비를 옮길지, 창고 장비로 대체할지 알 수 없다.

## 3. 레이아웃과 컴포넌트

공통 토큰은 기존 테마 브러시를 그대로 쓴다.

| 브러시 | 용도 |
|---|---|
| `SurfaceBrush`, `LineBrush`, `SubtleLineBrush` | 카드 |
| `TextBrush`, `SecondaryTextBrush`, `MutedBrush` | 글자 |
| `AccentBrush` / `AccentSubtleBrush` | 선택, 옮기기, 공략 |
| `GoodBrush` / `GoodSubtleBrush` | 쩔, EXP, 창고 대체, 유지 |
| `WarnBrush` / `WarnSubtleBrush` | 차선, 경고, 게임 편성 불일치 |

- 글자 크기는 22 / 16 / 14 / 13 / 12 / 11 단계를 유지한다.
- 초상화 아래 띠는 `RarityBrushConverter`의 `edge` 모드를 쓴다.

### 3.1 탭 줄

- 그룹 순서는 `해역 | 단일 보스전 | 레벨링`이다. `레벨링` 그룹에는 `쫄작` 탭 하나만 있다.
- `FleetPlannerViewModel`을 넓힌다. 둘 중 하나를 고른다.
  - 그룹 목록(`TabGroups: [{Label, Tabs}]`)으로 일반화한다.
  - `LevelingTabs`를 추가한다.
  - 어느 쪽이든 탭은 한 번만 만들고 새로고침 뒤에도 그대로 유지한다(현재 규칙).
- 탭을 전환해도 탭 줄의 위치가 바뀌지 않아야 한다. `CheckTabStripStays` 대상에 새 탭을 넣는다.

### 3.2 맥락 줄 `ContextBar` (두 탭 공통)

- 카드 안쪽 여백은 12 × 16이다.
- 왼쪽부터 다음 순서로 놓는다.
  - 라벨: 11px, `MutedBrush`, Bold.
  - 선택 버튼: 높이 34, 14px Bold, 오른쪽에 11px 보조 문구와 ▾.
  - 세로 구분선.
  - 사실 목록: 항목마다 위에 11px 라벨, 아래에 14px Bold 값과 선택적 11px 단위.
  - 오른쪽 끝: 칩 또는 출처 문구.
- 선택 버튼을 누르면 아래로 Popup 목록이 열린다. 테두리는 `AccentBrush`, 포커스 링은 3px이다. 키보드 ↑↓/Enter/Esc를 지원한다.
- 숫자에는 탭 숫자(고정폭)를 쓰고, 줄바꿈 없이 말줄임으로 처리한다.

### 3.3 쫄작 탭

맥락 줄에는 다음을 둔다.
- `쫄작 해역` 선택 버튼.
- 사실 목록: `적 레벨`, `잡몹 1전 (S랭크)`, `전의 120 초과 (×1.2)`, `기함 · MVP (×1.5 · ×2)`.
- 오른쪽 칩: 그 해역 기록이 있으면 `✓ 최근 {해역} N전 모두 S랭크`(Good). S랭크가 아닌 전투가 섞여 있으면 `S랭크 N/M`(Warn).

해역 선택 목록에는 다음을 둔다.
- 행: 최근 쫄작 해역(기록 있음 표시) + 11-4 ~ 15-4.
- 열: 해역 이름(보조 표시) | 적 Lv | `S / 전의 보너스` 1전 EXP | `기록 N전` 또는 `Lua 기준`.

왼쪽 영역 `추천 쫄작 함대`는 기존 `FleetTemplate` 배치(주력 | 구분선 | 전위, 각 3칸)를 재사용한다.
- 머리줄에는 다음을 둔다.
  - `쫄작 함대`와 `N함대에 편성 · 기사 a · 쩔 b`.
  - 오른쪽에 기사 수 세그먼트 `[자동 n] 0 1 2 3`과 게임 편성 일치 칩.
- 슬롯 타일에는 다음을 표시한다.
  - 왼쪽 위 배지: `쩔`(배경 `GoodBrush`, 글자 진한색) 또는 `기사`(배경 #3A4250 계열, 확인 필요: 기존 브러시에 없으면 `ControlPressedBrush` 검토).
  - 기함이면 오른쪽 위에 `★ 기함`(금색 글자).
  - 이름 아래: 쩔이면 `Lv.a → 다음 상한`, 기사면 `Lv.125 · 지금 N함대`.
  - 셋째 줄: 쩔이면 `+N /전`(Good, 12px Bold), 기사면 `기사 · 선두/화력`(Muted).
- 타일 아래에 통계 칸 3개를 둔다. 같은 너비 3열이고 배경은 `InputBrush` 계열이다.
  - 쩔 전투당 EXP 합계(보너스가 끝나면 얼마인지).
  - 전의 보너스가 남은 전투 수.
  - 전의 0까지 남은 전투 수.
- 안내 줄(i): 기사 수를 고른 근거를 보여 준다. 4.1절의 규칙 문장을 그대로 쓴다.
- 다른 함대 줄: 쩔이 없는 출격 함대를 작은 얼굴 6개와 `바꿀 필요 없음`으로 보여 준다.
- 각주(11px Muted): 예상치라는 사실과 확인 필요 가정을 적는다.

오른쪽 영역 `쩔 대기열`의 구성은 다음과 같다.
- 머리말 부제는 `육성 목표 → 콘텐츠 후보 → 쫄작 중`이다. 오른쪽에 `잠금 N척 · 잠수함 제외`를 둔다.
- 그룹 `편성 중 N척`: 상세 행.
  - 40px 얼굴과 이름·함종·Lv, 오른쪽 끝에 `편성 · 기함/주력/전위` 칩(Good).
  - 64px 진행 막대와 `Lv.c까지 N EXP · 약 M전`.
  - 태그: 육성 목표(금색), 콘텐츠 후보/META 공략(Accent), 쫄작 중, 각성(`Lv.c 각성 · 유닛 N`, 툴팁에 금화·유닛 전체 비용), 경고(Warn).
- 그룹 `다음 교대`: 28px 얼굴의 한 줄 행으로 `이름 · 함종 · Lv.a → c · 약 M전`과 상태 칩을 보여 준다. 경고 태그가 있을 때만 둘째 줄을 쓴다.
- 대기열 행에 마우스를 올리면 함대의 같은 함선 타일을 강조한다(기존 `HighlightedShipUid` 방식).

### 3.4 META 보스전 탭

맥락 줄은 세 줄로 구성한다.
- 1행
  - `META 보스` 선택 버튼(보조 문구 `현재 로테이션` 또는 `상시`).
  - 기간 칩 `M/D HH:MM까지`(Accent, 현재 로테이션일 때만).
  - 안내 `상시 보스 18종도 고를 수 있습니다`.
  - 오른쪽 끝에 출처 두 줄(공략 이름 · 원문 날짜 / 앱 데이터 검토 날짜).
- 2행 사실 목록: `장갑`(교대형이면 `중형 ↔ 중장갑`과 주기), `함종`, `HP (Lv.15)`, `전투 시간`, `장비 순위 기준`.
- 3행 기믹 3열: 번호 배지, 굵은 제목, 설명, 그리고 `→ 대응`(Accent).

보스 선택 목록은 다음과 같이 만든다.
- 그룹 `현재 로테이션`(1개)과 `상시 보스 N종`으로 나눈다.
- 열: 이름 | 장갑(경 #7FD0FF, 중형 #F2C46D, 중장갑 #F28F8F, 교대 #E4A7FF, 확인 필요: 테마 브러시로 옮길지) | 함종 | `공략 있음`/`장갑 기준만`.
- 게임에서 고른 상시 보스(SC 34502 `default_boss_id`)에는 `게임 선택` 표시를 붙인다.
- 장갑 색은 문자와 함께 쓴다. 색 하나만으로 구분하지 않는다.

왼쪽 영역 `추천 편성`의 구성은 다음과 같다.
- 타일 배지
  - 왼쪽 위: Regal `META` 등급(기존 `RatingCorner`).
  - 오른쪽 위: `공략`(Accent) 또는 `차선`(Warn).
- 타일 셋째 줄: `공략 1순위` / `★ 기함` / `선두 탱커`(금색) 또는 `{공략 1순위 함선} Lv.a 대신`(Warn).
- 안내 줄: 차선으로 채운 이유와, 그 함선이 쫄작 대기열의 몇 순위인지를 보여 준다.
- 타일을 누르면 선택된다. 선택 표시는 Accent 테두리와 `#1C2431` 계열 배경이고, 오른쪽 장비 패널이 그 함선으로 바뀐다. 기본 선택은 차선 슬롯 중 첫 함선이고, 차선이 없으면 기함이다.

오른쪽 영역 `장비 추천`의 구성은 다음과 같다.
- 머리줄: 30px 얼굴, 이름, `함종 · 위치`, 오른쪽에 세그먼트 `옮기기 포함 | 창고만`(기본은 옮기기 포함).
- 칸 행 5개는 왼쪽 42px에 번호와 칸 종류를 둔다. 내용은 다음과 같다.
  - 1줄: 26px 아이콘, `[n순위]` 배지, 장비 이름(Bold), 오른쪽 끝에 동작.
    - `{보유 함선}에서 옮기기`(Accent)
    - `창고 N개`(Good)
    - `✓ 유지`(Good)
    - `유지`(Muted)
  - 2줄(11px Muted): `지금 {장비} +N · {순위 또는 순위 밖}`. 유지 행이면 막힌 1순위의 이유를 적는다.
  - 3줄(옮기기 포함 모드에서만): `창고 대체 · n순위 {장비} (창고 N개)`(Good) 또는 `창고 대체 없음`.
  - 동작 텍스트에는 툴팁으로 보유 함선 목록과 "편성 밖" 여부를 붙인다.
- 바닥줄: `바꿀 칸 N · 모두 다른 함선에서 옮기기` 또는 `창고 장비만`. 오른쪽에 `중형·중장갑 순위 같음` 또는 `N번 칸은 중장갑 순위가 다름`.

## 4. 데이터와 로직

### 4.1 쫄작

**EXP 공식**

실제 계정 기록과 1의 자리까지 맞는 것을 확인했다(D3 엘리트 2063305, Lua exp 702).

```
s   = floor(lua_exp × 1.2)        // S랭크. A랭크는 ×1.0(나무위키 0.83배와 일치), B 이하는 확인 필요
e   = floor(s × 1.2) if 전의 > 120 else s
f   = floor(e × 1.5) if 기함 else e
exp = floor(f × 2)   if MVP  else f
// 검증: 702 → 842 / 1010, 기함 1515, MVP 2020; 2063213 761 → 913 / 1095
```

**값의 출처**

- 해역 기록(`current_battle_runs.ship_exp_list`)이 있으면 실측 최빈값을 쓰고, 없으면 Lua 값을 쓴다.
- Lua 값은 `expedition_data_template.exp`와 `level`이다.
  - `stages.json` 항목에 `exp` 필드를 추가해야 한다. `NativeLuaMapBuilder`에서 만들고, 목적이 있는 재생성이다.
- 해역 목록의 대표값은 각 장 잡몹 '중형' 함대의 값이다.
- 기본 해역은 최근 쫄작 해역이다.
  - 최근 14일 동안 Lv.120 미만 잠금 함선이 EXP를 받은 전투가 가장 많은 장(chapter)을 고른다.
  - 그런 기록이 없으면 12-4를 쓴다.

**전의**

- 보너스가 남은 전투 수는 `ceil((E − 120) / 2)`다. 판정이 전투 전 전의 기준인지는 확인 필요다.
- 전투 1회에 2를 소모한다. 작전 명령서를 쓰면 배율이 달라지는데, 얼마인지는 확인 필요다.

**레벨 구간**

- 다음 상한은 `level_limit = 1`인 레벨(100/105/110/115/120)이다.
- 상한에 도달해 EXP가 넘치면(exp ≥ 다음 레벨 요구량) `각성 필요`로 표시한다.
- 서버 `max_level`은 현재 DB에 저장되지 않는다(909척 중 22척만 값이 있다). DB 작업에 요청한다.
- 각성 비용은 `ship_level.lua`의 `need_item_rarity{2..6}`에서 가져온다. 형식은 `{{1,1,금화},{2,15008,성정 유닛},{2,15012,성정 유닛II}}`다.
  - `ship_level.json`에 `awaken: {"6": [3000, 300, 0], ...}` 형태로 추가한다.
  - Lv.105 UR의 6,000 / 600은 확인했다.
- 한계돌파 전 함선은 템플릿 `max_level`(70/80/90)까지만 계산하고 `한계돌파 미완료 · Lv.N 상한` 태그를 붙인다.

**쩔 후보**

- 대상: 잠금, Lv < 120, 범용 부린 제외, `Position`이 Main/Vanguard, 잠수함(SS/SSV) 제외.
  - 잠수함은 지원 공격에 참가한 전투만 EXP를 받는다.
- 우선순위는 다음 순서로 매긴다. 같으면 다음 상한까지 필요한 전투 수가 적은 쪽이 먼저다.
  1. `AppSettings.GrowthGoals`에서 `TrackLevel`이고 목표 레벨이 현재보다 높은 함선 → 태그 `육성 목표`.
  2. `FleetPlanBuilder` 후보에 든 함선 → `N개 콘텐츠 후보`. 현재 META 보스 공략 함선이면 `META 공략`.
  3. 쫄작 함대로 감지한 게임 함대에 있는 함선 → `게임 N함대 쫄작 중`.
  4. 그 외는 Regal 최고 등급 순.
- 쫄작 함대 감지: 최근 쫄작 해역 전투에 나간 함선 집합과 가장 많이 겹치는 게임 함대를 고른다.

**기사 수 자동 규칙** (초기값이며 기록이 쌓이면 조정한다)

- `gap`은 해역 적 Lv에서 고른 쩔의 최저 Lv를 뺀 값이다.
- 규칙
  - **0척**: 이 해역 기록 중, 출전 함대에 Lv.120 이상이 없고 최저 Lv가 지금 쩔 최저 Lv 이하인 S랭크 전투가 10전 이상 있다.
  - **1척**: `gap ≤ 10`.
  - **2척**: `gap > 10`.
  - 3척은 수동 선택만 가능하다.
- 안내 줄 문장: `쩔 최저 Lv.a · 적 Lv.b → 기사 n척 권장` + (0척 근거가 있으면) `기록상 기사 없이 N전 모두 S랭크라 0척도 가능합니다` + (gap > 10인 쩔이 있으면) `Lv.x 쩔은 기함에 두지 않습니다`.
- 수동 선택은 `AppSettings.LevelingDriverCount`(null이면 자동)에 저장한다.

**배치**

- 기사
  - 이미 감지된 쫄작 함대에 있는 Lv.120 이상 함선을 먼저 쓴다.
  - 없으면 전위 선두는 `W14 Mob`/`W15/16 Mob` 등급과 `Self Survival`, 주력은 `AoE Dmg`로 고른다.
  - 기사가 있으면 전위 선두는 기사가 맡는다.
- 기함(주력 1번)에는 우선순위가 가장 높은 주력 쩔 중 `gap ≤ 10`인 함선을 둔다. 기함이 격침되면 D랭크이기 때문이다.
- 나머지 쩔은 우선순위대로 위치에 맞춰 채운다. 쩔 수는 `6 − 기사 수`다.
- 게임 쫄작 함대와 몇 척이 겹치는지 칩으로 보여 준다.

### 4.2 META 보스

**생성 맵 `maps/meta_bosses.json`** (`NativeLuaMapBuilder`, 게임 데이터만 쓴다)

- `world_joint_boss_template`: `id`, `name`, `meta_id`, `boss_level_id`, `state`, `description`을 읽는다.
  - `state`는 `"always"`, `"stop"`, 또는 `{{Y,M,D},{h,m,s}}` 기간 쌍이다.
- 레벨 데이터: `world_boss_level[boss_level_id + lv − 1]`의 `hp`, `enemy_id`, `expedition_id`.
- 장갑과 함종
  - `enemy_data_statistics[enemy_id].base`에서 `armor_type`(1 경, 2 중형, 3 중장갑)과 `type`(7 = 항모 등)을 읽는다.
  - 확인한 예: 295346 → base 91298 → armor 2, type 7.
- 전투 시간: `gamecfg/dungeon/{expedition_id}.lua`의 `timeCount`(80)를 쓴다.
- 기간은 원본 그대로 저장하고, 현재 보스인지는 실행 중에 판정한다. 기간이 KST 서버 시각인지는 확인 필요다.

**수작업 맵 `maps/meta_boss_guides.json`**

- Lua를 갱신해도 덮어쓰지 않는다. 보스별 항목은 다음과 같다.

  ```
  { "lastReviewed", "verified": bool, "armorMode": {"alternate": ["Medium","Heavy"], "periodSec"},
    "mechanics": [{"title","text","response"}],
    "fleet": {"main": [[regalName...] x3], "vanguard": [[regalName...] x3], "flagship": index, "notes"},
    "avoid": [{"regalName","reason"}], "sources": [{"title","url","sourceUpdated"}] }
  ```

- 각 슬롯 배열은 공략 우선순위대로 나열한 대안 목록이다.
- 같은 파일에 `regalStarRules`도 둔다. Regal `*` 15척의 조건(`hull`/`armor`)을 담는다.
- **`verified: false`인 항목은 화면에 쓰지 않고 `장갑 기준만`으로 처리한다.** 리서치 요약은 원문과 대조하기 전까지 `false`다.

**점수**

- 등급 키는 Regal `META`다.
- 피해량은 보스 장갑에 맞춰 `Light/Medium/Heavy Dmg`를 쓰고, 교대형이면 두 값의 평균을 쓴다.
- `FleetPlanBuilder.Damage()`를 콘텐츠별 피해 지표를 받는 방식으로 바꾼다.
  - 하드 아비터, 챌린지 모드, EX는 현재 동작(`Heavy`)을 유지한다.

**공략 슬롯 채우기**

- 공략 대안을 순서대로 본다. 보유했고 Lv.120 이상인 첫 함선을 고르며, 1순위면 `공략`, 아니면 `차선`이다.
- 대안이 하나도 없으면 장갑 지표로 자동 선택한다(`자동`).
- `*` 등급은 `regalStarRules`가 이 보스에 맞을 때만 감점을 면제하고, 나머지는 지금처럼 −2를 준다.

**선택 상태**

- 기본 선택은 현재 로테이션 보스이고, 없으면 게임이 고른 상시 보스, 그것도 없으면 상시 첫 보스다.
- 선택은 `AppSettings.MetaBossTemplateId`(0이면 자동)에 저장한다.

### 4.3 장비 추천

**Regal 장비 맵 구조** (확인함)

- `ship[gear][usecase]` → `usecase[id]` = 칸 5개의 id.
- `slot[id]` = 순위 순서의 row id 목록.
- `row[id]` = equip key 목록. 같은 순위의 대안이다.
- `equip[key]` = `{id: 기본 장비 ID}`. 목록이면 첫 항목을 쓴다.

**usecase 선택**

- META: 보스 장갑에 따라 `L`/`M`/`H`를 쓴다.
  - 교대형이면 `M`을 쓰고, `H` 순위가 다른 칸을 표시한다.
- 14 / 15·16 해역: `C-13+`(확인 필요).
- 하드 아비터, 챌린지 모드, EX: `H`(확인 필요).

**보유 판정**

- 강화 단계를 접어서 기본 ID로 맞춘다. `equipment.json[template_id].base`를 쓴다. 강화된 장비는 `template_id`가 `base + 강화 수치`이기 때문이다.
- 창고 수량은 `current_equipment_inventory.bag_count`를 기본 ID별로 더한다.
- 누가 장착 중인지는 `current_ship_equipment_slots`의 `(ship_uid, slot, equipment_template_id)`로만 알 수 있다.

**편성 단위 배정** (탐욕법, 함선 순서는 기함 → 주력 → 전위)

- 칸마다 순위 row를 위에서부터 보면서 쓸 수 있는 첫 장비를 고른다.
- 선호 순서는 다음과 같다.
  1. 이 함선이 이미 장착 중이면 `유지`.
  2. 창고에 여유가 있으면 `창고`.
  3. 편성 밖 함선이 장착 중이면 `옮기기`. '옮기기 포함' 모드에서만 고른다.
- 같은 편성 안의 다른 함선이 장착 중이면 `막힘`이다. 배정한 수량은 바로 차감한다.
  - 다른 칸과 경합해서 차선을 고른 경우 `○○은 N번 칸에 배정`이라고 적는다.
- 옮겨 올 함선은 출격 함대(1~6, 11~12)에 들지 않은 함선을 먼저 고르고, 같으면 레벨이 낮은 쪽이다.
  - 출격 함대 함선에서만 옮길 수 있으면 동작 옆에 `N함대 사용 중`(Warn)을 붙인다.
- 강화 수치는 비교하지 않는다. 같은 순위 안에서는 강화 수치가 높은 것을 먼저 고르고, `+N`을 표시한다.
- 모드는 `AppSettings.GearSourceMode`(`"move"` 기본, `"bag"`)에 저장한다.

**출력 행**

- 칸 번호와 종류, 상태(`keep`/`bag`/`move`/`blocked`/`missing`).
- 추천 장비(이름, 아이콘, 순위, +N), 현재 장비, 창고 대체, 보유 함선 목록.

### 4.4 DB 작업과 맞출 항목 (확인 필요)

- `current_ships`
  - 서버 `max_level`을 저장해야 각성 필요 여부를 정확히 판정할 수 있다.
  - `rarity`, `rarity_score`, `name`, `empty_slots`는 항상 비어 있는 열이다.
- `current_battle_runs`
  - 클리어 랭크가 없다. CS 40003의 `score`(4 = S)만 있다. 전투 기록에 `score`를 남겨야 S랭크 칩과 기사 0척 근거를 계산할 수 있다.
  - `stage_label`, `mvp_name`에 ID 문자열이 들어 있다.
- `current_equipment_inventory`
  - `name`, `base_equipment_id`, `equipped_count`, `equipped_by_json` 등이 항상 비어 있다. 기본 ID는 maps로 풀어도 되므로 필수는 아니다.
- META
  - SC 34502 `default_boss_id`를 현재 상태로 보관해야 한다.
  - SC 34507/34522의 `template_id`로 현재 보스를 교차 확인한다.

## 5. 구현 순서와 완료 조건

**구현 순서**

1. 맵
   - `stages.json`에 `exp`를 추가한다.
   - `ship_level.json`에 `awaken`을 추가한다.
   - `meta_bosses.json` 생성기를 만든다.
   - `meta_boss_guides.json` 스키마를 만들고, 원문과 대조한 현재 보스 1건을 넣는다.
   - `MapFileStore` 등록 목록에 추가한다.
2. Core
   - 새 빌더 3개와 행 모델: `LevelingPlanBuilder`, `MetaBossPlanBuilder`(또는 `FleetPlanBuilder` META 확장), `GearAllocator`.
   - 각 빌더의 Checks를 먼저 작성한다.
3. Data: `NativeSnapshotService.FleetPlan.cs`(또는 새 partial)에서 세 계획을 스냅샷에 싣는다.
4. App
   - 탭 그룹, 선택 목록, 세그먼트 두 개, 설정 저장.
   - XAML 템플릿(3절).
5. 검증: LiveCheck로 실제 DB 복사본을 렌더하고 시안과 비교한다.

**완료 조건**

- `dotnet build` 경고 0, Core.Checks와 App.Checks가 exit 0, XAML 바인딩 오류 0이다.
- 새 Checks는 다음을 확인한다.
  - EXP 공식: 702 → 842 / 1010 / 1515 / 2020.
  - 전의 보너스 전투 수: 134 → 7, 150 → 15.
  - 기사 자동 규칙 3가지 경우와 0척 근거.
  - gap > 10인 쩔은 기함에 두지 않는다.
  - 잠수함 제외, 한계돌파 상한.
  - 보스 기간 판정(always / stop / 기간 안 / 기간 밖), 장갑 매핑, 공략 대안 순서와 `verified: false` 처리, `*` 규칙.
  - 장비 배정의 유지 / 창고 / 옮기기 / 막힘, 경합, 창고만 모드.
- 탭 줄 고정 검사에 `쫄작`을 포함한다.

**실제 화면에서 확인할 시나리오** (1440×900, 1180, 900 미만 스택)

1. 최근 쫄작 해역 기록이 있으면 기사가 자동 1척이다. 0을 고르면 근거 문장이 나온다. Lv.1 쩔을 넣으면 자동 2척이 되고, 그 함선은 기함이 아니다.
2. 대기열에 마우스를 올리면 함대 타일이 강조된다. 육성 목표 태그와 각성 태그가 보인다.
3. 보스를 진츠·META(경장갑)로 바꾸면 편성 점수와 장비 usecase(`L`)가 바뀐다.
4. 공략이 검증되지 않은 보스는 `장갑 기준만`으로 표시하고 공략·차선 배지를 쓰지 않는다.
5. `창고만`으로 바꾸면 옮기기 동작이 사라지고 바닥줄 수가 바뀐다. 앱을 다시 열어도 모드가 유지된다.
6. 쫄작 기록이 없는 DB에서는 해역 기본값이 12-4(Lua 기준)이고 S랭크 칩이 없다.
7. 긴 함선 이름과 장비 이름은 말줄임으로 처리되고, 동작 텍스트는 잘리지 않는다.
