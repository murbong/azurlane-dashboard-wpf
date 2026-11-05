# AGENTS.md

## 응답 원칙

- 기본 답변은 한국어로 한다.
- 사용자는 `선생님`이라고 부른다.
- 코딩/운영 요청은 먼저 결론, 실행 내용, 검증 결과를 명확히 말한다.
- 불필요한 캐릭터 말투는 줄이고, 정확성과 실용성을 우선한다.

## 프로젝트 개요

- 저장소: `azurlane-dashboard-wpf`
- 목적: Azur Lane KR 대시보드의 C# WPF 네이티브 앱.
- 원래 `mitmproxy-lab` 저장소에서 분리한 WPF 전용 저장소다.
- Python 캡처/export/웹 대시보드는 원래 `mitmproxy-lab` 저장소에 남긴다.
- 이 저장소에서는 WPF/C# 코드, WPF publish 스크립트, WPF용 기본 metadata만 관리한다.

## 현재 상태

- 초기 커밋: `85bcd32 Initial WPF dashboard repository`
- 주요 구성:
  - `src/AzurLaneDashboard.App`: WPF UI, MVVM shell, views
  - `src/AzurLaneDashboard.Capture`: dumpcap/Npcap 캡처 서비스
  - `src/AzurLaneDashboard.Core`: 공통 모델, 기본 설정, helper
  - `src/AzurLaneDashboard.Data`: SQLite 상태 저장, snapshot 서비스
  - `src/AzurLaneDashboard.Protocol`: 패킷 frame/protobuf 디코딩
  - `src/AzurLaneDashboard.Resources`: Lua/map/resource 업데이트 흐름
  - `maps/`: publish용 기본 JSON seed metadata
  - `scripts/`: WPF publish 스크립트
  - `docs/`: WPF 관련 문서

## 중요한 결정

- 이 저장소에는 Python 웹 대시보드 코드를 넣지 않는다.
- 런타임 데이터와 로컬 캐시는 커밋하지 않는다.
- 커밋하지 않는 대표 경로/파일:
  - `azurlane_tcp_export/`
  - `game_resources/`
  - `external/`
  - `static/game-assets/`
  - `captures/`
  - `logs/`
  - `bin/`, `obj/`, `dist/`, `artifacts/`
  - `*.sqlite`, `*.sqlite3`, `*.db`, `*.pcap`, `*.pcapng`, `*.jsonl`
  - `maps/local_assets.json`
  - `maps/update_state.json`
  - `maps/regal_*.json` (Regal 커뮤니티 평가: 재배포 허락이 없어 데이터 업데이트가 내려받는다. 로컬에는 남겨 둔다)
- publish 기본 데이터로 필요한 `maps/*.json` seed 파일은 커밋 대상이다.
- WPF 작업은 이 저장소 안에서만 진행하고, 원래 `mitmproxy-lab`의 Python 코드와 섞지 않는다.
- 사속 계산기를 구현할 때는 `docs/fire-rate-calculator.md`의 Lua 기준 공식과 데이터 소스를 먼저 확인한다.

## 빌드/검증

일반 검증:

```powershell
dotnet restore .\AzurLaneDashboard.slnx
dotnet build .\AzurLaneDashboard.slnx
```

현재 환경에서는 NuGet HTTPS 인증서 문제로 restore가 실패할 수 있다.

대표 오류:

```text
NU1301
The SSL connection could not be established
Authentication failed
보안 패키지에 사용할 수 있는 인증서가 없습니다.
```

이 오류는 코드 구조 문제가 아니라 로컬 NuGet/TLS/인증서 환경 문제로 본다.

## Publish

Framework-dependent publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish_wpf.ps1 -Clean -Zip
```

Self-contained publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build_wpf_standalone.ps1 -Clean -Zip
```

## 작업 주의

- 삭제/이동 전에 Git 상태를 먼저 확인한다.
- 사용자가 명시하지 않으면 커밋은 신중하게 한다.
- 생성물 정리는 `bin/`, `obj/`, `dist/`, `artifacts/` 같은 명확한 대상에만 제한한다.
- 대량 metadata JSON은 publish seed로 쓰이므로, 목적 없이 재생성하거나 정렬 포맷을 바꾸지 않는다.
- NuGet restore 실패가 나면 먼저 네트워크/인증서 문제인지 분리해서 보고한다.
