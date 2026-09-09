# tiaportal-mcp 변경 이력

> 이 파일은 Claude Code가 자동으로 관리합니다.
> 코드 수정 시 반드시 이 파일에 이력을 추가하세요.

---

## [2026-09-09] Claude Desktop이 tiaportal-mcp에 연결 안 되던 문제 (MSIX 가상화 config)

### 증상
- `%APPDATA%\Claude\claude_desktop_config.json`을 V20으로 고치고 Claude Desktop을
  몇 번을 재시작해도 계속 같은 에러(`Server disconnected`, TIA V21로 뜸)가 반복됨.
- 로그(`%LOCALAPPDATA%\Claude\logs\mcp-server-tiaportal-mcp.log`)에는 매번
  `System.IO.FileNotFoundException: ...Siemens.Engineering, Version=20.0.0.0...`
  가 `ModelContextProtocol.Server.AIFunctionMcpServerTool.CreateMetadata` 단계에서
  발생 — 즉 TIA Portal에 연결 시도하기도 전에, MCP 툴 메타데이터를 리플렉션으로
  만드는 중에 어셈블리를 못 찾아서 죽는 패턴이었음.
- 이상한 점: 완전히 같은 exe를 PowerShell로 직접 stdio 프로토콜을 태워서 띄우면
  매번 정상 연결됐음 (V20으로 `Connect`/`GetState` 성공). Claude Desktop이 띄울
  때만 100% 재현되는 실패였음.

### 진짜 원인 — 두 가지가 겹쳐 있었음
1. **가짜 config 파일을 계속 고치고 있었음.** 이 PC의 Claude Desktop은
   `C:\Program Files\WindowsApps\Claude_...\app\Claude.exe` 경로의 **MSIX 패키지
   앱**이라, 일반적으로 앱이 참조하는 `%APPDATA%\Claude\claude_desktop_config.json`
   경로가 Windows의 MSIX 앱 데이터 가상화(app data virtualization)에 의해
   실제로는 아래 경로로 리다이렉트됨:
   ```
   C:\Users\USER\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json
   ```
   `%APPDATA%\Claude\claude_desktop_config.json`(일반 경로)에 아무리 V20으로
   수정해도 앱은 그 파일을 안 읽고 있었던 것 — 그래서 몇 번을 고쳐도 반영이
   안 됐던 것처럼 보였음. **실제 config는 항상 `LocalCache\Roaming\Claude` 쪽을
   봐야 함.** (Claude Desktop 앱 내 설정 화면의 "인수"/"환경 변수" 표시가 이
   진짜 파일 내용을 반영함 — 거기서 여전히 V21이 보이면 진짜 파일이 아직도
   V21이라는 뜻.)
2. **`Engineering.cs`의 레지스트리 조회에 `try/catch`가 없었음.** MSIX 앱이
   띄우는 자식 프로세스는 일반 셸에서 띄운 프로세스보다 권한/환경이 제한적일
   수 있는데, `GetTiaPortalInstallPath(int)`의 레지스트리 접근이 예외를 던지면
   `AssemblyResolve` 핸들러 전체가 조용히 깨지면서 `TiaPortalLocation` 환경변수
   폴백까지 가지도 못하고 원본 `FileNotFoundException`만 밖으로 새어나가는
   구조였음. `src/TiaMcpServer/Siemens/Engineering.cs`의 `Resolver()`와
   `GetTiaPortalInstallPath()`에 try/catch를 추가해서, 레지스트리 조회가
   실패해도 항상 `TiaPortalLocation` 환경변수로 폴백하도록 고침.

### 조치
- 진짜 config 파일(`LocalCache\Roaming\Claude\claude_desktop_config.json`)의
  `tiaportal-mcp` 항목을 `--tia-major-version 20`으로, `env`에
  `TiaPortalLocation`(`Portal V20` 경로) + `SystemRoot`/`TEMP`/`USERPROFILE`
  등 기본 시스템 환경변수를 명시적으로 추가.
- `Engineering.cs` 리졸버를 방어적으로 수정 (커밋 `eede8b0`).

### 다음에 새로 빌드/설정할 때 참고할 점
- **이 PC에서 Claude Desktop 설정을 고칠 땐 `%APPDATA%\Claude\`가 아니라
  `%LOCALAPPDATA%\Packages\Claude_<해시>\LocalCache\Roaming\Claude\`를 고쳐야
  함.** 패키지 해시(`pzs8sxrjxfjjc`)는 업데이트되면 바뀔 수 있으니
  `Get-Process Claude | Select Path`나 `C:\Program Files\WindowsApps\Claude_*`
  로 현재 설치 버전을 먼저 확인할 것.
- Claude Desktop 앱 내 설정 화면(연결된 MCP 서버 목록 > tiaportal-mcp 클릭 >
  고급 옵션)에 표시되는 "인수"/"환경 변수"가 실제 반영 여부를 확인하는 가장
  빠른 방법 — 파일을 고친 뒤 이 화면이 새 값을 보여주는지로 진위 판단 가능.
- 설정을 고친 뒤에는 창 닫기(X)가 아니라 **트레이 아이콘에서 Quit**으로 완전히
  종료 후 재실행해야 반영됨 (그냥 재실행하면 기존 인스턴스에 포커스만 감).

---

## [2026-09-09] upstream(origin/main) 동기화 + TIA Portal V20 빌드 복구

### 배경
- 로컬 `main`이 upstream보다 7커밋 뒤처져 있었고, 동시에 로컬에서만 진행 중이던
  `Portal.cs`(태그테이블 조회/내보내기 등, +361줄), `McpServer.cs`, `Helper.cs`,
  `Responses.cs` 커스텀 작업이 커밋되지 않은 채 쌓여 있었음.
- Claude Desktop 연결이 안 되는 문제를 조사하다가 `claude_desktop_config.json`이
  `--tia-major-version 21`로 잘못 설정된 것을 발견 (이 머신은 V21 Openness가
  설치돼 있지 않고 V20만 정상 설치됨) → V20으로 수정.
- 김에 "upstream 최신으로 업데이트하고 V20 연결 재확인" 진행.

### 진행 순서
1. 로컬 미커밋 변경사항(소스 코드만, `exports/`·`snapshots/`·`tia-inventory/`
   같은 대용량 생성 데이터 제외)을 `backup-local-work-20260909` 브랜치에 백업 커밋.
2. `main`을 `git merge --ff-only origin/main`으로 최신(upstream 7커밋, SDK 2.2.0
   업그레이드 + `--doctor` 진단 커맨드 + `Diagnostics.cs` 신규 등) 반영.
3. 백업 브랜치를 `git cherry-pick -n`으로 새 main 위에 얹고 충돌 해결:
   - `TiaMcpServer.csproj`: upstream이 하드코딩 `<Reference HintPath=...>` 방식을
     버리고 NuGet 패키지(`Siemens.Collaboration.Net.TiaPortal.Packages.Openness`)
     기반으로 전환함. 이 패키지는 **버전 번호 자체가 TIA 메이저 버전과 결속**되어
     있어서(예: `21.0.x` → 컴파일 시 V21 헤더만 참조), 기본값인 `21.0.1765349347`
     대신 `20.0.1744190253`으로 고정해야 V20 기준으로 컴파일됨. 로컬이 갖고 있던
     구식 하드코딩 `<Reference>` 블록은 중복이라 제거.
   - `Program.cs`, `Engineering.cs`: 로컬에서 임시로 고쳤던 TIA 설치 경로 탐색
     로직(레지스트리 `TIA_Opns`→`Global` 폴백)이 upstream 버전에서 더 견고하게
     (`EditionMain` 폴백, `Bin` 폴더 존재 검증, `TiaPortalLocation` 환경변수
     폴백까지) 이미 대체되어 있어서 upstream 쪽을 그대로 채택.
   - `McpServer.cs`: 로컬에서만 존재하던 `GetTagTables`/`GetTags`/`ExportTagTable`
     3개 툴은 upstream에 아예 없어서 충돌 없이 그대로 유지. 다만 이 코드가
     `McpException(msg, ex, McpErrorCode)`처럼 **구버전 SDK(0.3.0-preview.4)
     생성자 시그니처**를 쓰고 있었는데, upstream이 SDK를 2.2.0으로 올리면서
     그 3-인자 생성자가 사라짐 → `McpException(string)` / `McpException(string, Exception)`
     2가지 시그니처로 전부 수정.
   - `Helper.cs`, `Responses.cs`, `Portal.cs`: upstream이 건드리지 않은 파일이라
     충돌 없이 그대로 적용됨.
4. `dotnet build` 성공 확인 후, 실제 stdio MCP 프로토콜로 rebuild된 exe를 직접
   구동해서 `Connect`/`GetState` 호출 → TIA Portal V20에 정상 연결되고 현재 열린
   프로젝트(`Mahindra_CPU01_V20_260909_k1_001`)까지 정확히 인식되는 것 확인.

### 다음에 새로 빌드할 때 참고할 점
- **`TiaMcpServer.csproj`의 `Siemens.Collaboration.Net.TiaPortal.Packages.Openness`
  패키지 버전은 반드시 실제 컴파일 대상 TIA 메이저 버전과 일치시켜야 함.**
  이 저장소는 현재 `20.0.1744190253`으로 고정(V20 전용). upstream 기본값은
  `21.0.x`이므로, upstream을 다시 당겨올 때 이 줄이 21로 되돌아가 있는지 항상 확인.
  (nuget 캐시에 20.0.x/21.0.x 둘 다 있으면 버전만 바꿔서 재빌드 가능,
  `C:\Users\USER\.nuget\packages\siemens.collaboration.net.tiaportal.packages.openness\`)
- `McpException`은 SDK 2.2.0 기준 `(string)` / `(string, Exception)` 생성자만 있음.
  `McpErrorCode`를 인자로 넘기는 코드가 남아있으면 그건 구버전 SDK 흔적이니 제거 대상.
- 로컬 전용 기능(`GetTagTables`/`GetTags`/`ExportTagTable`)은 아직 upstream에
  없는 순수 로컬 확장이므로, 다음에 다시 upstream을 당길 때도 같은 방식(충돌 없이
  그대로 유지)으로 살아남을 가능성이 높지만, upstream McpServer.cs 구조가 또
  크게 바뀌면 재확인 필요.
- `claude_desktop_config.json`(`%APPDATA%\Claude\claude_desktop_config.json`)의
  `args`/`env.TiaPortalLocation`도 V20으로 맞춰둔 상태 — V21 Openness를 설치하기
  전까지는 21로 되돌리지 말 것.
- 원본 로컬 작업(백업 시점)은 `backup-local-work-20260909` 브랜치에 그대로 남아있음.

---

## [2026-05-20] 파서 개선 — StructuredText v4 지원

### 수정 파일

#### `tools/tia_xml_parser.py`
- **추가**: `StructuredText v4` 형식 파싱 지원
  - TIA Portal이 FOR/IF/CASE 등 SCL을 토큰 기반 XML로 저장하는 형식
  - namespace `http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4`
  - `reconstruct_st()` 함수로 Token/Blank/NewLine/Access/CallInfo 요소를 재조합해 SCL 코드 복원
- **수정**: `_find_all()`, `_find()`, `_components()` 헬퍼로 네임스페이스 무관 검색
- **변경**: 빈 네트워크 표시 `(no source)` → `(empty network)`

### 계기
- `205_Motion` Network 120 — `210_V90TO(i)` FOR 루프 호출이 파싱 안 됨
- 실제 코드: `FOR i := 1 TO Motion.servo_Quntity BY 1 DO 210_V90TO(i); END_FOR;`

### 현재 파싱 상태
- `exports/all/` — 전체 블록 XML 217개
- `exports/all_parsed/` — 전체 블록 압축 텍스트 217개 (최신)
- 빈 네트워크 325개는 실제로 비어 있는 것 (TIA Portal 구분선/플레이스홀더)

---

## [2026-05-20] 블록 분석 워크플로우 확립

### 추가 파일

#### `tools/tia_xml_parser.py`
- TIA Portal XML export → 압축 텍스트 변환 스크립트 (Python 3.8+)
- 원본 XML 수만 자 → 수백 줄로 압축, Claude 분석 시 토큰 대폭 절감
- **사용법:**
  ```
  python tools/tia_xml_parser.py exports/<블록명>.xml              # 콘솔 출력
  python tools/tia_xml_parser.py exports/<블록명>.xml exports/<블록명>.txt  # 파일 저장
  ```
- **출력 포함 항목:** 블록 타입/번호/언어, 인터페이스 변수, 네트워크별 제목+변수목록+파트 요약, SCL은 소스 직접 출력

### 블록 분석 표준 절차 (다음 대화에서 이 순서로)
1. `ExportBlock` 툴로 XML 저장 → `exports/` 폴더
2. `python tools/tia_xml_parser.py exports/<블록>.xml exports/<블록>.txt`
3. 압축된 `.txt` 파일을 Read로 읽어 분석
4. (필요시) 원본 XML의 특정 네트워크만 추가 확인

### ExportBlock softwarePath / blockPath 규칙
- `softwarePath`: `"BMA #01"` (고정)
- `blockPath`: 소프트웨어 트리 경로 그대로, 예시:
  - `"200_Motion/210_V90TO"`
  - `"700_OP/OP1/1701_BM10I_STG"`
  - `"000_System/001_System"`

### 주의사항
- 블록이 inconsistent 상태면 ExportBlock 실패 → TIA Portal에서 컴파일 먼저
- FB부터 컴파일해야 전체 컴파일 가능 (의존성 순서)
- exports/ 폴더: `C:\Users\USER\tiaportal-mcp\exports\`

---

## [2026-05-20] 초기 연결 문제 해결

### 환경
- TIA Portal V21
- MCP 서버: `C:\Users\USER\tiaportal-mcp\src\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe`

### 수정 파일

#### `src/TiaMcpServer/Program.cs`
- **변경**: V21에서도 `Engineering.Resolver`를 항상 등록하도록 수정
- **Before**: V21이면 `Engineering.Resolver` 미등록, `Openness.Initialize()`만 호출
- **After**: 항상 `AppDomain.CurrentDomain.AssemblyResolve += Engineering.Resolver` 등록 후, V20+ 이면 `Openness.Initialize()`도 호출
- **이유**: `ReflectionTypeLoadException` (Siemens DLL 못 찾음) 해결

#### `src/TiaMcpServer/ModelContextProtocol/McpServer.cs`
- **변경**: `Connect()` 메서드 에러 메시지 상세화
- **After**: 예외 타입(`ex.GetType().Name`)과 `InnerException` 메시지를 포함한 detail 문자열 생성
- **이유**: "Failed to connect to TIA-Portal" 만으로는 원인 진단 불가

### 빌드 설정
- Debug → **Release** 빌드로 전환 (TIA Openness는 Release 빌드에서만 정상 동작)

### Claude Code 설정

#### `C:\Users\USER\.claude\settings.json`
- `permissions.allow`에 `mcp__tiaportal-mcp__*` 30개 툴 전체 추가
- **이유**: 매번 권한 프롬프트 없이 자동 실행하기 위함

### 결과
- `Connect` 툴 호출 성공: `CTe_BMA_PLC1_V21` 프로젝트에 연결됨
- `GetProjectTree` 로 프로젝트 구조 파악 완료 (PLC 1, HMI 4, 서보 59대, 인버터 10대 등)

---

## [2026-06-05] V21 → V20 전환

### 배경
- V20을 메인으로 사용하기로 변경
- V21 상태는 `v21-backup` 브랜치에 보존

### 수정 파일

#### `src/TiaMcpServer/TiaMcpServer.csproj`
- `Siemens.Collaboration.Net.TiaPortal.Packages.Openness` 버전 변경: `21.0.1744190253` → `20.0.1744190253`
- DLL HintPath 변경:
  - Before: `Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll` + `Step7.dll`
  - After: `Portal V20\PublicAPI\V20\Siemens.Engineering.dll` + `Siemens.Engineering.Hmi.dll`
  - V20은 net48 하위폴더 없이 바로 V20/ 폴더에 DLL 위치

#### 환경변수 `TiaPortalLocation`
- 변경: `C:\Program Files\Siemens\Automation\Portal V21` → `C:\Program Files\Siemens\Automation\Portal V20`
- User 레벨로 설정 (Machine 레벨은 관리자 권한 필요)

### 비변경 사항
- `Program.cs`: 이미 기본값이 `TiaMajorVersion = 20`이어서 수정 불필요
- `Engineering.cs`: V20 레지스트리 경로(`TIAP20`) 이미 지원
- `Portal.cs`: 표준 `Siemens.Engineering.*` 네임스페이스 사용으로 호환

### 빌드 결과
- Release 빌드 성공 (경고 0, 오류 0)
- Connect 성공 확인

### V21로 되돌리는 방법
1. `git checkout v21-backup` (코드 복원)
2. `C:\Users\USER\.claude.json` 두 곳 수정:
   - `"--tia-major-version", "20"` → `"21"`
   - `env.TiaPortalLocation`: `Portal V20` → `Portal V21`
3. TiaMcpServer 프로세스 종료 후 `dotnet build -c Release`
4. VS Code 재시작 → Connect
- **주의**: `.claude.json` 수정을 빠뜨리면 DLL 버전과 인자 버전 불일치로 `EngineeringTargetInvocationException` 발생

---

<!-- 새 변경사항은 위에 추가 (최신순) -->
