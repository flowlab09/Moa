# 형광펜 도구와 업데이트 안내 카드 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 캔버스에 자를 대고 긋는 형광펜 도구(H)를 더하고, 사이드바 업데이트 알림을 버전·설명·버튼이 있는 카드로 바꾼 뒤 1.4.3으로 낸다.

**Architecture:** 모든 변경은 `app/moa-board.html` 한 파일 안이다. 형광펜은 기존 자유 항목의 선(`el:'line'`) 경로에 `el:'marker'`를 끼워 넣는다. 그리기, 가로 맞춤, 색·굵기 묶음만 새로 만든다. 업데이트 카드는 `moaUpdate(state, version)`이 채우는 사이드바 요소의 모양만 바꾸고, 호스트 메시지는 그대로 둔다.

**Tech Stack:** 단일 HTML(바닐라 JS, SVG, CSS 변수 테마)과 WebView2 호스트(변경 없음). 시험은 숨긴 Edge 헤드리스와 CDP 평가 스크립트로 한다(스크래치패드의 `theme-final-serve.cjs`, `cdp2.cjs`). 저장소에는 시험 파일을 두지 않는 기존 방식을 따른다.

**Spec:** `docs/superpowers/specs/2026-11-08-highlighter-update-card-design.md`

## Global Constraints

- 단축키 `H`, 툴바 위치는 화살표(A)와 펜(P) 사이.
- 형광색 5가지: 노랑(기본), 연두, 분홍, 하늘, 주황. 굵기 4단계 10·16·24·32, 기본 16.
- 가로 맞춤 기준은 가로에서 6° 이내. 새로 그을 때와 끝 손잡이를 끌 때 모두 적용.
- 섞기 방식: 밝은 테마는 `mix-blend-mode:multiply`, `html[data-theme=black]`은 `screen`.
- 형광펜 굵기는 확대·축소를 따라간다(`vector-effect:none`).
- 데이터: `kind:'자유'`, `el:'marker'`, 필드는 선과 동일(`from,to,x,y,w,h,color,stroke`).
- 업데이트 카드 문구(그대로 씀):
  - ready 설명: "새 버전을 받아 두었어요. 지금 적용하면 화면만 새로 고쳐지고, 설치 파일을 다시 실행할 필요가 없습니다."
  - ready 버튼: `지금 적용 · 수정사항 반영`
  - installer 설명: "이번 버전은 프로그램도 바뀌어 설치 파일을 받아야 해요. 위에 덮어 설치하면 작업은 그대로 남습니다."
  - installer 버튼: `설치 파일 받기`
  - 제목: `모아 <버전>`
- C# 호스트, 호스트 API(`moa-host` 1)는 바꾸지 않는다. 버전은 `src/MoaSetup.cs`의 `Version`만 1.4.3으로 올린다.

## Review Focus

1. **세 테마(기본·민트·검은색)에서 형광펜 아래 글씨가 읽혀야 한다.** 검은색 테마에서 multiply가 적용되면 안 보이게 된다. → Task 1 시험에서 테마별 `mix-blend-mode` 계산값 확인, 스크린샷 시트로 확인.
2. **아주 짧게 클릭만 했을 때(끌지 않음)** 160px 가로 형광펜이 생긴다(선과 같은 기본값). → Task 1 시험.
3. **세로·대각선으로 끌면 맞춤 없이 그 방향 그대로** 칠해진다. 6°를 조금 넘으면(예: 10°) 맞추지 않는다. → Task 1 시험.
4. **일반 선 색·굵기가 형광펜 때문에 바뀌면 안 된다.** 형광펜을 노랑·24로 쓴 뒤 선 도구로 돌아가면 이전 선 스타일이 유지된다. → Task 1 시험.
5. **업데이트 카드가 테마와 좁은 사이드바에서 넘치지 않는다.** 긴 설명 줄바꿈, 버튼 클릭 시 올바른 메시지 전송. → Task 2 시험.

---

### Task 1: 형광펜 도구

**Files:**
- Modify: `app/moa-board.html`
  - CSS 블록(L8–200): 형광펜 섞기 규칙
  - 툴바 마크업(L218–220 사이): `toolMarker` 버튼
  - `freeLabels`, `toolHints`, `toolButtons`, `freeStyle`(L1375–1379), `freeItem`(L1384)
  - `freeContent`(L1417 선 분기), `addHandles`(L1472), `moveHandle`(L1495), `moveCreate`(L1535), `finishCreate`(L1549)
  - 스타일 막대: 견본 생성(L1694), `applyStyle`(L1688), `updateBar`(L1714)
  - 단축키 표(L1745), 자유 항목 검증(L2187)
- Test: 스크래치패드 `marker-test.js`(CDP 평가), `marker-run.ps1`(세 테마 실행과 스크린샷 시트)

**Interfaces:**
- Produces:
  - `markerColors: string[]`: 형광색 5개 hex. 첫 값이 노랑(기본).
  - `markerWidths: number[]`: `[10,16,24,32]`.
  - `freeStyle.markerColor`, `freeStyle.markerStroke`(기본 `markerColors[0]`, `16`)
  - `isLineLike(el: string): boolean`: `line|arrow|marker`일 때 true. 선 분기마다 이것으로 바꾼다.
  - `snapLevel(a: [x,y], b: [x,y]): [x,y]`: 각도가 가로에서 6° 이내면 `[b[0], a[1]]`, 아니면 `b`.
  - `markerContext(): boolean`: `tool==='marker'`이거나, 선택된 자유 항목이 모두 marker일 때 true.

- [ ] **Step 1: 실패하는 시험 쓰기**: `marker-test.js`. `#toolMarker`를 누르거나 `setTool('marker')`를 부르고, viewport에 pointerdown/move/up을 보내 긋는다.
  - `drawFlat`: (100,200)에서 (400,215)로 그은 뒤(약 2.9°) 마지막 카드는 `el==='marker'`, `from[1]===to[1]`, `color===markerColors[0]`, `stroke===16`이다.
  - `drawSteep`: (100,200)에서 (400,253)으로 그은 뒤(약 10°) `from[1]!==to[1]`이다.
  - `clickOnly`: 끌지 않은 클릭에서 `w===160`, `h===0`이다.
  - `handleSnap`: 끝 손잡이 `to`를 세로로 8px 끌면 맞춤이 유지되어 `from[1]===to[1]`이다.
  - `moveWhole`: 형광펜을 끌어 옮기면 `x`/`y`만 바뀌고 `from`/`to`는 같다.
  - `eraser`: 지우개로 문지르면 카드가 사라지고, Ctrl+Z로 되살아난다.
  - `styleSeparate`: 형광펜을 분홍·24로 바꾼 뒤 `setTool('line')`하면 `freeStyle.color`와 `freeStyle.stroke`는 이전 값 그대로이고, 견본 첫 버튼의 `dataset.color===inkColors[0]`이다.
  - `scalesWithZoom`: 형광펜 path의 계산된 `vector-effect`가 `none`이다.
  - `blend`: 형광펜 노드의 계산된 `mix-blend-mode`는 기본·민트에서 `multiply`, 검은색에서 `screen`이다.
  - `validates`: 백업 검증 함수가 marker 카드를 통과시킨다.
  - `shortcut`: `h` 키로 `tool==='marker'`가 된다.
- [ ] **Step 2: 시험이 실패하는지 확인**: `powershell -File marker-run.ps1`. 기대 결과는 `setTool('marker')`에서 오류가 나거나 `drawFlat:false`로 실패하는 것이다.
- [ ] **Step 3: 구현.** 위 Interfaces와 Global Constraints대로 한다.
  - 그리기: `stroke-linecap:'butt'`, `stroke-opacity:.45`, 형광펜 path에 `vector-effect:none`을 둔다. 맞힘 영역 path의 `stroke-width`는 `max(14, 굵기)`다.
  - 스타일 막대: `markerContext()`일 때 견본을 `markerColors`로, 굵기 목록을 `markerWidths`(얇게·보통·굵게·아주 굵게)로 바꿔 그린다. `applyStyle`은 그때 `markerColor`/`markerStroke`를 바꾼다.
  - `inkOf`는 형광펜에 쓰지 않는다. 형광색은 테마 잉크로 바꾸지 않는다.
- [ ] **Step 4: 시험 통과 확인**: `marker-run.ps1` 출력에서 세 테마 모두 위 키가 `true`다. 스크린샷 시트에서 형광펜 아래 글씨가 읽힌다. 브라우저 기본 창 호출은 0이다.
- [ ] **Step 5: 커밋**: `git add app/moa-board.html && git commit -m "형광펜 도구 추가(H)"`

### Task 2: 업데이트 안내 카드

**Files:**
- Modify: `app/moa-board.html`
  - 사이드바 마크업(L205): `#updateNotice` 버튼을 카드 요소로 바꾼다. 안에 `#updateTitle`(strong), `#updateText`(p), `#updateApply`(button)를 둔다.
  - CSS(L153 `.update-notice` 규칙): 카드 모양으로 바꾼다. 배경은 `var(--paper)`, 테두리는 `var(--ui-line)` 계열, 버튼은 테두리형이다.
  - `moaUpdate`(L1775), 클릭 연결(L1788), 끄기 토글이 카드를 숨기는 줄(L1786)
- Test: 스크래치패드 `update-card-test.js`, 세 테마 실행

**Interfaces:**
- Consumes: 호스트가 부르는 `moaUpdate(state: 'ready'|'installer', version: string)`, 그리고 `hostView.postMessage('update-apply'|'update-page')`. 둘 다 그대로 쓴다.
- Produces: 없음

- [ ] **Step 1: 실패하는 시험 쓰기**: `update-card-test.js`. `hostView`를 가짜 `{postMessage:m=>sent.push(m)}`로 두고 시험한다.
  - `ready`: `moaUpdate('ready','1.4.3')` 뒤 `#updateNotice`가 보이고, 제목은 `모아 1.4.3`, 설명과 버튼은 Global Constraints 문구와 정확히 같다.
  - `applySends`: `#updateApply`를 누르면 `sent`의 마지막 값이 `'update-apply'`다.
  - `installer`: `moaUpdate('installer','1.5.0')` 뒤 문구가 installer용으로 바뀌고, 누르면 `'update-page'`를 보낸다.
  - `fits`: 카드의 `scrollWidth<=clientWidth`, 버튼의 `offsetWidth<=` 카드 내부 폭.
  - `offHides`: `#updateToggle`로 끄면 카드가 숨겨진다.
- [ ] **Step 2: 시험이 실패하는지 확인.** 기대 결과는 `#updateApply`가 없어서 `applySends:false`로 실패하는 것이다.
- [ ] **Step 3: 구현.** 위 Files대로 바꾼다. 카드 자체가 아니라 버튼만 눌리게 한다.
- [ ] **Step 4: 시험 통과 확인**: 세 테마에서 모든 키가 `true`이고, 스크린샷으로 모양을 확인한다.
- [ ] **Step 5: 커밋**: `git commit -am "업데이트 안내를 카드로 바꿈"`

### Task 3: 1.4.3 빌드

**Files:**
- Modify: `src/MoaSetup.cs` (`Version = "1.4.3"`)

- [ ] **Step 1:** 버전을 올리고 `scripts/build.ps1`을 실행한다. `dist/Moa-1.4.3-windows-x86_64-setup.exe`와 `dist/moa-1.4.3.html`이 생기는지 확인한다.
- [ ] **Step 2:** `dist/moa-1.4.3.html`에서 확인한다. `<meta name="moa-host" content="1">`이 있고, `id="toolMarker"`와 `id="updateApply"`가 있으며, 브라우저 기본 창 호출은 0이다.
- [ ] **Step 3:** 커밋(`모아 1.4.3: 형광펜, 업데이트 안내 카드`). 푸시와 릴리스는 사용자 확인 뒤에 한다.
