# Fire Rate Calculator Notes

이 문서는 WPF 사속 계산기를 구현할 때 기준으로 삼을 Lua 원본과 필요한 데이터 흐름을 정리한다.

## 결론

사속 계산기는 스프레드시트 공식을 그대로 베끼지 말고, 인게임 Lua의 `CalculateReloadTime`와 `weapon_property.reload_max`를 기준으로 만든다.

현재 `maps/equipment.json` seed metadata는 장비 이름, 희귀도, 아이콘 중심이다. `weapon_id`, `reload_max`, `recover_time`, `initial_over_heat` 같은 계산용 필드는 별도 추출이 필요하다.

전체 `AzurLaneLuaScripts` 체크아웃은 `external/AzurLaneLuaScripts` 같은 로컬 캐시로 두고 커밋하지 않는다. 필요한 값은 추출해서 작은 JSON seed나 SQLite `json_store`에 넣는다.

## Lua Source Of Truth

로컬 기준 경로 예시:

```text
external\AzurLaneLuaScripts\KR
```

WPF 저장소 안에서 작업할 때는 같은 repo 루트 아래의 아래 경로를 우선 사용한다.

```text
external\AzurLaneLuaScripts\KR
```

없으면 원래 `mitmproxy-lab`의 `external/AzurLaneLuaScripts`를 참조하거나, 업데이트 흐름으로 다시 받는다.

## Formula Sources

핵심 공식:

```text
mod\battle\data\battleformulas.lua
  CalculateReloadTime(reloadMax, loadSpeed)
```

확인한 KR Lua 공식:

```text
reloadSeconds = reloadMax / K1 / sqrt((loadSpeed + K2) * K3)
K1 = 6
K2 = 100
K3 = 3.14
```

상수 출처:

```text
mod\battle\data\battleconfig.lua
  K1 = 6
  K2 = 100
  K3 = 3.14
  AIR_ASSIST_RELOAD_RATIO = 220

mod\battle\battlestate.lua
  PERCENT = 0.01
```

항모 공습/함재기 평균 reload:

```text
mod\battle\data\battleformulas.lua
  CaclulateAirAssistReloadMax(hiveList)
```

동작은 각 hive weapon의 `reload_max` 평균에 `AIR_ASSIST_RELOAD_RATIO * PERCENT`를 곱한다. 현재 상수 기준 배율은 `2.2`다.

## Runtime Sources

일반 무기 쿨다운:

```text
mod\battle\data\weapon\battleweaponunit.lua
  GetReloadTime()
  FlushReloadMax()
  initial_over_heat
  recover_time / queue GCD
```

항모/공습:

```text
mod\battle\data\weapon\battleallinstrike.lua
  FlushTotalReload()
  GetReloadTime()
```

함선 reload 스탯이 전투 속성으로 들어가는 경로:

```text
mod\battle\data\battleattr.lua
  reload -> loadSpeed

model\const\attributetype.lua
  AttributeType.Reload = "reload"
```

장비 화면 표시:

```text
model\vo\equipment.lua
  CalcWeanponCD()
  GetEquipReloadStatic()
```

## Data Tables

장비에서 실제 무기 reload를 얻는 기본 흐름:

```text
equip_data_statistics[id].weapon_id[0]
  -> weapon_property[weaponId].reload_max
  -> CalculateReloadTime(reload_max, finalLoadSpeed)
```

필요 Lua 테이블:

```text
sharecfg\equip_data_statistics.lua
sharecfg\weapon_property.lua
sharecfg\ship_data_statistics.lua
sharecfg\ship_data_template.lua
```

`sharecfgdata\...`에도 같은 계열 데이터가 있으므로, 파서 구현 시 두 위치 중 실제 구조가 읽기 쉬운 쪽을 사용한다.

## Required Calculator Inputs

최소 입력:

- 함선 UID
- 함선 template/group id
- 현재 level
- 현재 reload/loadSpeed
- 장비 config id 또는 강화 단계가 반영된 장비 id
- 장비 `weapon_id`
- 무기 `reload_max`

정확도를 높이는 입력:

- 장비 스탯으로 더해지는 reload
- 개조/강화/호감도/서약/함대기술/지휘냥 보정
- 스킬로 인한 `loadSpeed` 변경
- 스킬로 인한 weapon reload requirement 변경
- `recover_time`, `queue`, `initial_over_heat`
- 항모의 여러 함재기 weapon 평균과 2.2 배율

## Implementation Plan

1. Lua source locator를 만든다.
2. `equip_data_statistics`와 `weapon_property`에서 사속용 장비 map을 생성한다.
3. 현재 보유 장비/장착 장비의 config id를 장비 map에 연결한다.
4. 함선 현재 reload를 `loadSpeed`로 사용해 `CalculateReloadTime`을 계산한다.
5. UI에는 기본 쿨다운, 실제 쿨다운, 첫 발/공습 보정, GCD 참고값을 분리해서 보여준다.

## Accuracy Notes

스프레드시트에서 흔히 쓰는 아래 형태는 Lua 공식과 같은 의미다.

```text
actualCd = baseCdAtReload100 * sqrt(200 / (loadSpeed + 100))
```

단, `baseCdAtReload100`이 실제 `weapon_property.reload_max`에서 나온 값이어야 한다. 장비 이름만으로 맞추면 강화 단계별 weapon id 차이 때문에 틀릴 수 있다.

예를 들어 같은 이름의 장비라도 `equip_data_statistics`의 현재 id가 달라지면 연결되는 `weapon_id`와 `reload_max`가 달라진다. 사속 계산기는 반드시 현재 장착 장비의 실제 config id를 기준으로 계산한다.
