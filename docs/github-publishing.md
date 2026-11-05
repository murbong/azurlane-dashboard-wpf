# GitHub publishing

이 문서는 기존 로컬 Git 이력을 보존하면서 GitHub에 저장소를 처음 게시하고, 태그 기반 자동 Release를 만드는 절차를 설명합니다.

## 1. 공개 전 확인

현재 작업 트리와 공개 대상 파일을 확인합니다.

```powershell
git status --short
git diff --check
git ls-files --cached --others --exclude-standard
```

다음 파일은 커밋하거나 이슈에 첨부하지 않습니다.

- 패킷 캡처와 로그
- SQLite DB와 Excel 내보내기 결과
- 로컬 게임 리소스와 추출 이미지
- PFX, PEM, KEY 등 개인키 또는 인증서 비밀키
- 계정 ID, 토큰, 닉네임 등 개인 데이터가 포함된 진단 자료

Git 커밋의 작성자 이메일은 공개 저장소 이력에 표시됩니다. 공개가 싫다면 첫 공개 push 전에 GitHub noreply 이메일 사용 여부와 기존 50개 커밋의 작성자 정보를 검토하세요. 기존 이력을 변경하지 않을 경우에는 현재 작성자 정보가 그대로 게시됩니다.

## 2. 로컬 검증

```powershell
dotnet restore .\AzurLaneDashboard.slnx
dotnet build .\AzurLaneDashboard.slnx --configuration Release --no-restore

powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\build_wpf_standalone.ps1 -Clean -Zip
```

publish 출력에서 다음 조건을 확인합니다.

- 빌드 오류 0개
- `defaults\maps`와 `maps` seed metadata 존재
- `AzurLaneDashboard.App.exe`와 필수 DLL 존재
- Python 파일 0개
- ZIP에 DB, 캡처, 로그가 포함되지 않음

## 3. 로컬 커밋 준비

현재 변경 사항은 하나의 공개 프리뷰 준비 커밋으로 묶을 수 있습니다.

```powershell
git add -A
git diff --cached --check
git status --short
git commit -m "feat: prepare public dashboard preview"
git branch -M main
```

커밋 전에 `git status --short`에서 의도하지 않은 파일이 없는지 반드시 확인합니다.

## 4. GitHub 비공개 저장소 생성

GitHub에서 이름이 `azurlane-dashboard-wpf`인 새 **Private** 저장소를 만듭니다.

로컬 저장소에 이미 README, `.gitignore`, LICENSE와 커밋 이력이 있으므로 GitHub 생성 화면에서 다음 항목은 추가하지 않습니다.

- Add a README file
- Add `.gitignore`
- Choose a license

생성 후 표시되는 HTTPS 원격 URL을 사용합니다.

```powershell
git remote add origin https://github.com/OWNER/azurlane-dashboard-wpf.git
git remote -v
git push -u origin main
```

`OWNER`는 실제 GitHub 사용자 또는 조직 이름으로 바꿉니다.

## 5. GitHub 확인 및 공개 전환

비공개 저장소에서 다음 항목을 확인합니다.

1. `Build` Actions가 Windows에서 성공하는지 확인합니다.
2. README, LICENSE, `THIRD_PARTY_NOTICES.md`가 정상 표시되는지 확인합니다.
3. DB, 캡처, 로그, 로컬 리소스가 올라가지 않았는지 확인합니다.
4. 커밋 작성자 이메일과 저장소 설명을 확인합니다.
5. 공개해도 되는 상태라면 Settings에서 저장소 visibility를 Public으로 변경합니다.

## 6. 첫 프리릴리스

`main`의 검증된 커밋에 annotated tag를 만들고 push합니다.

```powershell
git tag -a v0.1.0-preview.1 -m "Azur Lane Dashboard v0.1.0 preview 1"
git push origin v0.1.0-preview.1
```

`Release` Actions가 다음 파일을 자동 생성해 GitHub Release에 첨부합니다.

```text
AzurLaneDashboard-v0.1.0-preview.1-win-x64.zip
AzurLaneDashboard-v0.1.0-preview.1-win-x64.zip.sha256
```

태그 이름에 `-preview.1` 같은 suffix가 있으면 GitHub Release도 prerelease로 생성됩니다.

## 7. 실패 시 확인

- `NU1301`, TLS 또는 인증서 오류: 로컬 NuGet/HTTPS 환경 문제인지 먼저 분리합니다.
- `NU1900`: 패키지 복원 실패가 아니라 취약성 피드 접속 경고일 수 있습니다.
- Actions 빌드 실패: 로컬에만 있는 파일이나 캐시를 참조하지 않는지 확인합니다.
- Release 실패: 태그가 원격에 존재하는지, Actions의 `contents: write` 권한이 허용되는지 확인합니다.
