<p align="center"><img src="assets/moa-logo.png" width="96" alt="모아 로고"></p>

# 모아

글, 도형, 선, 손글씨, 이미지, 영상을 카드 없이 바로 놓을 수 있는 Windows용 개인 캔버스 프로그램입니다.

## 내려받기

[Releases](../../releases/latest)에서 `Moa-<버전>-windows-x86_64-setup.exe`를 받아 실행합니다. 관리자 권한은 필요 없습니다.

- Windows 10/11 64비트와 Microsoft Edge WebView2 런타임이 필요합니다. Windows 11에는 대개 포함되어 있습니다.
- 코드 서명이 없어 SmartScreen 경고가 뜨거나 백신이 실행을 막을 수 있습니다.

## 주요 기능

- **자유 편집:** 글, 도형, 선, 화살표, 펜, 이미지를 바로 놓고 옮기고 크기를 조절합니다.
- **영상:** MP4와 WebM 파일을 보드 안에서 재생합니다. 누를 때만 재생됩니다.
- **카드:** 블록, 할 일, 링크, PDF를 담는 카드와 그룹, 연결선을 쓸 수 있습니다.
- **보드 관리:** 폴더로 정리하고, 이름 변경, 휴지통, 영구 삭제를 지원합니다.
- **되돌리기:** `Ctrl+Z`와 `Ctrl+Shift+Z`를 지원합니다.
- **자동 백업:** 켜고 끌 때 내용이 바뀌었으면 저장하고 최근 30개를 남깁니다.

## 데이터 위치

작업 내용은 프로그램 옆의 `MoaData` 폴더에 저장되며 외부로 전송하지 않습니다. 기본 설치 위치는 `%LOCALAPPDATA%\Programs\Moa`입니다.

| 폴더 | 내용 |
|---|---|
| `MoaData\App` | 보드와 항목, 실행 기록 |
| `MoaData\Originals` | 첨부 파일 원본 |
| `MoaData\Backups` | 자동 백업 |

프로그램을 제거해도 지우겠다고 고르지 않으면 데이터는 남습니다.

## 직접 빌드하기

Windows와 .NET Framework 4.8이 있으면 됩니다. 처음 빌드할 때 WebView2 SDK를 NuGet에서 받습니다.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

결과물은 `dist` 폴더에 생깁니다.

| 경로 | 설명 |
|---|---|
| `app/moa-board.html` | 화면과 기능 (HTML, CSS, JavaScript) |
| `src/MoaApp.cs` | 프로그램 창과 파일 저장 (C#) |
| `src/MoaSetup.cs` | 설치와 제거 프로그램 (C#) |
| `scripts/build.ps1` | 빌드 스크립트 |

## 새 버전 내기

1. `src/MoaSetup.cs`의 `Version`을 올립니다. 버그 수정은 `1.1.1`, 기능 추가는 `1.2.0`처럼 올립니다.
2. 빌드한 뒤 커밋합니다.
3. `v<버전>` 태그로 GitHub 릴리스를 만들고 `dist`의 설치 파일을 첨부합니다.

## 라이선스

프로젝트 라이선스를 아직 정하지 않았습니다. 포함된 외부 구성 요소는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
