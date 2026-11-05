# Azur Lane Dashboard WPF

Azur Lane KR 플레이 데이터를 로컬에서 캡처하고 정리하는 Windows용 네이티브 WPF 대시보드입니다.

> [!WARNING]
> 이 프로젝트는 비공식 팬 프로젝트이며 Azur Lane의 개발사, 배급사 또는 권리자와 제휴하거나 승인받은 프로젝트가 아닙니다.
> 현재 공개 빌드는 코드 서명되지 않을 수 있으므로 Windows SmartScreen 경고가 표시될 수 있습니다.

## 주요 기능

- `dumpcap` 또는 `adb tcpdump` 기반 라이브 패킷 캡처
- 함선, 장비, 편성, 레벨링, 스킬, 자원 및 시스템 현황 조회
- 함선/장비 검색, 필터, 다중 기준 정렬
- 편성별 공격 주기, 사속 및 예상 피해량 확인
- 패킷 디코딩 상태와 캡처 로그 확인
- 현재 대시보드 데이터를 `.xlsx` 통합 문서로 내보내기
- 로컬 게임 리소스와 Lua metadata를 이용한 이름 및 이미지 갱신
- SQLite 기반 로컬 상태 저장

이 저장소에는 C# 데스크톱 앱만 포함합니다. 기존 Python 캡처/export 및 웹 대시보드 코드는 포함하지 않습니다.

## 실행 환경

- Windows 10/11 x64
- 라이브 캡처 방식에 따라 다음 중 하나
  - Wireshark/Npcap과 `dumpcap`
  - Android Debug Bridge의 `adb`와 대상 Android 에뮬레이터/기기
- framework-dependent 패키지는 .NET 10 Desktop Runtime x64
- self-contained 패키지는 별도 .NET 설치 불필요

편성·육성의 추천 편성은 커뮤니티 평가(Regal) 데이터를 씁니다. 이 데이터는 저장소와 릴리스에 포함하지 않으므로, 처음 실행한 뒤 설정 화면의 데이터 업데이트(또는 `--update-data`)를 한 번 실행해야 추천이 표시됩니다.

매크로 페이지는 개발자 개인용 ALAuto 폴더(`macros` 포함)가 있는 PC에서만 메뉴에 나타납니다. 공개 릴리스에서는 매크로를 사용할 수 없습니다.

Python은 필요하지 않습니다. `git`과 `adb.exe` 또는 `MuMuManager.exe`는 metadata 및 게임 리소스 갱신 기능을 사용할 때만 필요할 수 있습니다.

## 설치 및 실행

1. GitHub의 **Releases**에서 최신 `AzurLaneDashboard-<version>-win-x64.zip`을 받습니다.
2. ZIP을 쓰기 가능한 폴더에 완전히 압축 해제합니다.
3. 캡처 방식에 맞는 `dumpcap` 또는 `adb` 환경을 준비합니다.
4. `AzurLaneDashboard.App.exe`를 실행합니다.
5. 설정 화면에서 캡처 백엔드, 네트워크 인터페이스 또는 ADB 대상을 선택합니다.

현재 설치 프로그램은 제공하지 않습니다. 프로그램은 실행 폴더 아래에 사용자 설정, SQLite DB, 갱신된 metadata와 선택적 이미지 캐시를 생성합니다.

기존 설정으로 데이터 업데이트 후 캡처까지 시작하려면 다음 실행 옵션을 사용할 수 있습니다.
이미 실행 중인 앱은 먼저 정상 종료하여 같은 DB를 수집하는 인스턴스가 겹치지 않게 하세요.

```powershell
.\AzurLaneDashboard.App.exe --update-data --start-capture
```

두 옵션은 개별로 사용할 수 있습니다. 업데이트가 실패해도 캡처 시작은 시도하며 결과는 설정 화면에서 확인합니다.
필요하면 `--status-file=C:\경로\operation-status.json`으로 진행 상태와 최근 로그를 로컬 JSON에 기록할 수 있습니다.

## 개인정보와 캡처 데이터

캡처한 패킷과 디코딩된 데이터는 기본적으로 로컬 SQLite DB와 로그에 저장됩니다. 데이터에는 게임 계정 및 플레이 상태를 식별할 수 있는 값이 포함될 수 있습니다.

- `*.pcap`, `*.pcapng`, `*.sqlite`, `*.db`, 로그 파일을 공개 이슈에 첨부하지 마세요.
- 문제 재현 자료를 공유하기 전에 계정 ID, 닉네임, 토큰 및 기기 정보를 제거하세요.
- 리소스 갱신을 실행하면 metadata 저장소와 커뮤니티 자산 서버에 네트워크 요청을 보낼 수 있습니다.
- 이 저장소는 캡처, DB, 로그 및 로컬 캐시 경로를 Git 추적에서 제외합니다.

## 화면 사용법

- 왼쪽 메뉴에서 홈, 육성 계획, 보유 함선, 장비 창고와 분석 화면을 이동합니다.
- 홈에서 함대 현황과 저장한 육성 목표를 함께 확인하고, `육성 계획`으로 바로 이동합니다.
- 목록을 검색할 수 있는 화면에는 검색창이 나타납니다. `정렬 및 상세 필터`를 펼쳐 조건을 지정하고, `초기화`로 검색과 필터를 해제합니다.
- 설정은 저장 위치, 기기 연결, 업데이트, 데이터 수집 등 작업별로 펼쳐 사용합니다.
- 단축키: `F5` 새로고침, `Ctrl+F` 현재 목록 검색, `Ctrl+1` 홈, `Ctrl+2` 육성 계획, `Esc` 함선 상세 닫기.

### 함선 육성 목표

`육성 계획 → 내 육성 목표`에서 함선을 검색하고 목표 레벨(1~125)과 스킬 레벨(1~10)을 저장합니다.
레벨 또는 스킬만 목표로 삼을 수도 있습니다. 같은 함선의 목표를 저장하면 기존 목표를 수정합니다.
저장 후 입력창은 접혀 계획에 집중할 수 있습니다. `목표 추가 · 수정`을 펼치거나 목표의 수정 버튼으로 다시 편집합니다.

- 현재 상태를 기준으로 남은 경험치, 예상 전투 수, 스킬 부족 단계와 다음 할 일을 표시합니다.
- 예상 전투 수는 해당 함선의 최근 관측 EXP를 기준으로 하며 해역·MVP·컨디션 변화에 따라 달라집니다.
- 스킬은 현재 레벨이 낮은 순으로 제안하고, 각 스킬의 최대 레벨을 존중합니다. 전술적 성능 순위나 교과서 비용 최적화는 아닙니다.
- 경험치 표, 스킬 정보, 전투 표본이 없으면 `정보 확인 필요` 또는 `표본 없음`으로 표시합니다.
- 목표는 앱 설정에 DB 경로별로 보관됩니다. 동일 DB를 여러 게임 계정이 공유하는 경우 계정별 분리는 지원하지 않습니다.
- 자동 갱신 중에도 입력 중인 목표는 유지됩니다. 실제 게임 조작이나 재화 소비는 수행하지 않습니다.

## 소스 빌드

필요 환경:

- .NET 10 SDK
- Windows 데스크톱 빌드 환경

```powershell
dotnet restore .\AzurLaneDashboard.slnx
dotnet build .\AzurLaneDashboard.slnx --configuration Release --no-restore
```

회귀 검사와 WPF 화면 검증(Windows):

```powershell
dotnet run --project tests/AzurLaneDashboard.Core.Checks --configuration Release --no-build
dotnet run --project tests/AzurLaneDashboard.App.Checks --configuration Release --no-build -- artifacts/ux-qa
```

화면 검사는 가상의 함선으로 입력 검증, 목표 저장 요청, DB 전환, 자동 갱신, 10개 메뉴 이동, 필터 초기화, XAML 바인딩과 작은 창 배치를 확인합니다.
다크/라이트 테마 PNG를 지정한 경로에 생성하며 게임 캡처와 실제 사용자 DB를 사용하지 않습니다.

## Publish

Framework-dependent:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\publish_wpf.ps1 -Clean -Zip
```

Self-contained:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\build_wpf_standalone.ps1 -Clean -Zip
```

결과물은 `dist/` 아래에 생성됩니다. publish 스크립트는 기본 metadata 포함 여부, 필수 DLL, Python 파일 혼입 여부를 함께 검사합니다.

## 저장소 구성

```text
src/
  AzurLaneDashboard.App/        WPF UI, MVVM shell, views
  AzurLaneDashboard.Capture/    dumpcap/Npcap/ADB capture service
  AzurLaneDashboard.Core/       shared models and defaults
  AzurLaneDashboard.Data/       SQLite state and snapshot services
  AzurLaneDashboard.Protocol/   packet frame/protobuf decoding
  AzurLaneDashboard.Resources/  metadata and resource update flow
maps/                           bundled seed metadata
scripts/                        build, publish and signing scripts
docs/                           implementation notes
```

런타임 데이터와 로컬 캐시는 저장소에 커밋하지 않습니다. 자세한 목록은 [`.gitignore`](.gitignore)를 참고하세요.

## 릴리스 정책

- 버전 태그는 Semantic Versioning 형식의 `vMAJOR.MINOR.PATCH`를 사용합니다.
- `v*` 태그를 푸시하면 GitHub Actions가 Windows x64 self-contained ZIP과 SHA-256 파일을 생성합니다.
- 안정화 전 버전은 `v0.x.y` 또는 `v0.x.y-preview.n` 형식으로 배포합니다.
- 현재 자동 Release는 코드 서명을 수행하지 않습니다.

저장소 최초 게시와 첫 Release 절차는 [`docs/github-publishing.md`](docs/github-publishing.md)를 참고하세요.

## 라이선스와 권리 고지

이 저장소에서 프로젝트 기여자가 작성한 소스 코드는 [MIT License](LICENSE)로 배포합니다.

게임 이름, 상표, 게임 유래 metadata, 제3자 라이브러리, 글꼴 및 기타 외부 자료에는 각각의 권리와 이용 조건이 적용됩니다. 자세한 내용은 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)를 확인하세요.
