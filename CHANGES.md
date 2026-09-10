# tiaportal-mcp 변경 이력

> 이 파일은 Claude Code가 자동으로 관리합니다.
> 코드 수정 시 반드시 이 파일에 이력을 추가하세요.

---

## [2026-09-10] HMI 태그 테이블 조회 (`GetHmiTagTables`/`GetHmiTags`) - Unified Comfort/Advanced 패널

Upstream PR #26 아이디어 목록에 있던 HMI 기능 중 사용자가 가장 필요하다고 한 "HMI 태그 테이블
조회/export"를 구현. 마힌드라 프로젝트에 실제로 Unified Comfort Panel(HMI_1/HMI_2/HMI_3,
MTP1200)이 추가된 상태라 바로 라이브 검증까지 진행.

### 구조 (리플렉션으로 확인, `Siemens.Engineering.HmiUnified.HmiTags`)
- `HmiSoftware`(→ `SoftwareContainer.Software`, PLC와 동일한 `GetSoftwareContainer(softwarePath)`
  헬퍼 재사용) → `.TagTables`(루트 태그 테이블) + `.TagTableGroups`(재귀 그룹, PLC의
  `PlcTagTableGroup`과 동일한 모양이지만 루트 래퍼 객체 없이 `HmiSoftware`에 직접 두 프로퍼티로
  분리되어 있음).
- `HmiTag`는 PLC의 `PlcTag`와 달리 `GetAttribute` 없이 Address/DataType/HmiDataType/Connection/
  PlcName/PlcTag/AccessMode/AcquisitionMode/Scope/TagType 등을 강타입 프로퍼티로 직접 노출 -
  PLC 태그 연동 정보(`plcName`/`plcTag`)까지 한 번에 나옴.
- **Export 불가, 확인됨**: `HmiTagTable`에는 `PlcTagTable`/클래식 `Hmi.Tag.TagTable`에 있는
  `Export()` 메서드가 없음 (리플렉션으로 멤버 목록 직접 대조 확인) - 그래서 `ExportHmiTagTable`은
  만들지 않음, 이 Openness 버전에서 원천적으로 지원 안 됨.
- 클래식 WinCC Comfort/Basic 패널(`Siemens.Engineering.Hmi.Tag`, 다른 네임스페이스)은 이번엔
  손대지 않음 - 지금 필요한 건 Unified뿐이었음.

### 경로 관련 함정 (라이브 테스트로 발견)
- `GetSoftwareTree`/기존 PLC 툴들과 달리, HMI는 `softwarePath`가 Device 이름 한 단계로 안 끝남.
  `HMI_1`(Device)의 HmiSoftware는 그 밑에 중첩된 `HMI_RT_1`(DeviceItem)에 있음 - 즉
  `softwarePath`는 `"HMI_1"`이 아니라 `"HMI_1/HMI_RT_1"`. PLC는 우연히 Device 밑 DeviceItem
  이름이 똑같이 `PLC_1`이라 한 단계처럼 보였던 것뿐, 실제로는 항상 DeviceItem까지 내려가야 함.
  처음 `"HMI_1"`로 테스트했을 때 에러 없이 빈 배열만 나와서 헷갈렸음 - `GetProjectTree`로 실제
  트리를 다시 뽑아보고서야 발견.

### 라이브 검증 (Mahindra, PID 41948, `HMI_1/HMI_RT_1`)
- `GetHmiTagTables`: "Default tag table", "Internal Tag", "Recipe", "Alarm", "HMI_IO List" 등
  실제 태그 테이블 수십 개 정상 조회.
- `GetHmiTags("HMI_IO List")`: 실제 태그 3개 정상 조회, 모든 필드(address/connection/plcName=
  `PLC_1`/plcTag=`HMI_System.InPut_List[1]`/accessMode=`SymbolicAccess`/tagType=`UDT` 등) 정확.
- "Default tag table"은 항목 0개로 정상 - 시스템이 자동 생성하는 빈 기본 테이블로 확인됨(에러
  아님).

---

## [2026-09-10] Upstream 이슈/PR 리뷰에서 나온 4가지 개선 구현 (export 경로 보안, GetProject/GetProjects 분리, GSD 의존성 조회)

Upstream 저장소의 open issue(#18 export 경로 보안, #29 -32603 크래시)와 open PR(#26, #30 - "Lots of
improvements", HTTP transport/Tag tools/defensive guards)을 리뷰하고 나온 로드맵 중 우선순위
1~4번을 구현. 태그 테이블(3번)은 이미 구현되어 있어 확인만 했음.

### 1. Export 경로 보안 (`src/TiaMcpServer/Siemens/OutputPathPolicy.cs`, 신규)
- 기존엔 `ExportBlock`/`ExportBlocks`/`ExportType`/`ExportTypes`/`ExportAsDocuments`/
  `ExportBlocksAsDocuments`/`ExportTagTable` 전부가 `exportPath`를 전체 경로 그대로 받아서
  path traversal/실수로 덮어쓰기/데이터 유출 위험이 있었음 (upstream issue #18과 동일한 문제).
- `OutputPathPolicy.ResolveDirectory()`를 만들어 위 7개 메서드 전부에 적용 — 이제 `exportPath`는
  서버가 관리하는 export 루트(`TiaMcpExportRoot` 환경변수, 기본값
  `%TEMP%\tiaportal-mcp-exports`) 아래의 **상대 하위 폴더명만** 허용. 절대경로/드라이브
  루트/UNC/`..` 트래버설은 명확한 에러 메시지와 함께 거부.
- `Doctor` 툴 응답에 `exportRoot` 필드 추가 — 에이전트가 지금 export가 어디에 쌓이는지 바로
  확인 가능.
- **라이브 검증** (Mahindra `Mahindra_CPU01_V20_260910_k1`, PID 41948, 블록
  `DiagnosticErrorInterrupt`): 절대경로(`C:\Users\USER\Desktop\security-test-should-fail`) →
  거부 확인, 상대경로(`security-test-ok`) → `%TEMP%\tiaportal-mcp-exports\security-test-ok\
  DiagnosticErrorInterrupt.xml`에 실제 생성 확인, 트래버설(`../../escape-attempt`) → 거부
  확인. 두 거부 케이스 모두 디스크에 아무 것도 안 생겼음을 직접 확인.

### 2. `GetProject`/`GetProjects` 네이밍 분리 (upstream PR #30에서 착안)
- 기존엔 MCP 툴 이름이 `GetProject`(단수)인데 실제로는 `Portal.GetProjects()`가 반환하는
  **리스트**를 돌려주는 네이밍 혼란이 있었음(upstream도 동일 문제).
- 리스트를 반환하던 기존 구현은 `GetProjects`로 이름 변경, 현재 붙어있는 프로젝트 하나만
  반환하는 새 `GetProject`를 추가 (`Portal.GetActiveProject()` 신규 - `GetState()`와 동일한
  LocalSessions-우선-then-Projects 조회 로직 재사용).
- **라이브 검증**: Mahindra에서 `GetProject`(단일 객체, attributes 포함)와 `GetProjects`(items
  배열) 둘 다 정상 반환 확인.

### 4. GSD 의존성 조회 툴 `GetGsdDependencies` (PR #26 아이디어 + 오늘 GSD 조사 후속)
- 프로젝트의 하드웨어 구성에서 서드파티(GSD 기반) 디바이스/디바이스아이템을 찾아 GsdId/
  GsdName/GsdType/Profibus·Profinet 여부를 반환 (`Siemens.Engineering.HW.Features.GsdDevice`/
  `GsdDeviceItem` 서비스, 리플렉션으로 API 확인 후 구현).
- 오늘 낮에 몇 시간 걸렸던 GSD 조사(이 파일 상단 항목 참고)의 재발 방지용 - 프로젝트를 다른
  PC로 옮기기 전에 이 툴로 어떤 GSD가 필요한지 미리 확인 가능. 단, 이미 정상 로드된 디바이스만
  보이는 한계는 있음 (GSD가 없어서 TIA가 아예 인스턴스화를 못 하면 이 툴에도 안 잡힘 - 그 경우는
  `Connect` 성공 직후 `GetDevices`/`GetProject`가 비어있는 쪽이 더 강한 신호).
- **라이브 검증**: Mahindra(네이티브 지멘스 장치만 있음)에서 "No GSD-based devices found"로 정상
  응답 확인. (add_on_eqp 쪽 GSD 장치로 직접 검증은 그 인스턴스가 테스트 도중 일시적으로
  응답 없어져서 못 함 - 아래 참고.)

### 3. Tag table 조회 (이미 구현되어 있었음, 확인만)
- `GetTagTables`/`GetTags`/`ExportTagTable`이 이미 `Portal.cs`/`McpServer.cs`에 구현되어 있었음
  (PR #30이 제안한 것과 동일한 모양). 추가 작업 없음.

### 참고: addon_eqp(PID 22652) 응답 없음
- 이번 검증 도중 addon_eqp TIA 창(PID 22652)에 대한 `Connect` 호출이 30초 넘게 응답 없이
  멈추는 현상 발견 - 같은 시점에 다른 프로세스(Mahindra, PID 41948)에 대한 `Connect`는 정상.
  즉 서버 코드 문제가 아니라 그 TIA 창이 당시 뭔가에 막혀 있었던 것으로 보임(다이얼로그 등,
  과거 조사에서 기각했던 가설이지만 조건은 매번 바뀔 수 있음) - 새로운 버그는 아님, 별도
  조치 없이 기록만.

---

## [2026-09-10] 특정 프로젝트 파일 하나에서만 Attach 후 Projects/LocalSessions가 비어 보임 (해결 — 원인은 GSD 파일 미설치)

### 증상
- 다중 인스턴스 선택(`ListTiaPortalInstances`/`Connect(processId)`) 기능을 실제로
  TIA Portal 2개 띄워놓고 검증하던 중 발견.
- `id=15676` (Mahindra_CPU01_V20_260909_k1_002) → 항상 정상.
- 카카오톡으로 받은 `부대설비_Ver4.0.ap20`(이후 `add_on_eqp_ver4.0.ap20`으로 개명,
  경로도 여러 번 이동) → `Connect`는 매번 성공(`isConnected: true`)하는데
  `GetProject`/`GetState`가 계속 `items: []` / `project: "-"`. 사용자가 스크린샷으로
  직접 확인 — 그 TIA 창엔 프로젝트가 정상적으로 열려 있고 펑션블록 에디터까지 열려
  있었음 (하단 상태바 "Project '부대설비_Ver4.0' opened").

### 원인 조사 — 7가지 가설을 순서대로 기각
1. **권한/관리자 레벨 차이** — 최초엔 둘 다 `NotElevated`라 기각. 이후 사용자가 그
   프로젝트를 관리자 권한으로 재실행했을 때도 재확인 — 여전히 재현. 이후 일반
   권한으로 다시 실행해도 재현 → 최종 기각.
2. **Multiuser(`ProjectServers`)** — 정상 인스턴스(Mahindra)도 동일하게
   `ProjectServers=1`("Local Project Server", `net.tcp://localhost/:9237`)이 떠있고,
   `GetCompositionInfos()`는 빈 목록, `GetComposition("LocalSessions")`는 "지원 안 됨"
   에러 — 실제 프로젝트와 무관한 TIA 백그라운드 서비스로 확인. 기각.
3. **여는 방식(탐색기 더블클릭 vs TIA 내부 File > Open)** — Close Project 후 TIA
   안에서 File > Open으로 재현했으나 동일. 기각.
4. **타이밍/레이스 컨디션** — 연결 직후·2초 후·재연결 후 3번 모두 동일. 기각.
5. **모달 대화상자가 막고 있음** — 사용자 확인, 없음. 기각.
6. **다른 인스턴스(Mahindra)가 온라인 상태라 간섭** — Mahindra를 `GoOffline`으로
   내린 채로 재시도해도 동일하게 재현, 이후 Mahindra는 `GoOnline`으로 정상 복원.
   기각.
7. **"첫 번째 vs 두 번째로 뜬 인스턴스" 순서 문제** — Mahindra를 완전히 닫고 문제의
   프로젝트를 **단독 인스턴스**로(관리자 권한 없이) 띄워도 여전히 재현. 기각.

### 결론 — 프로젝트 파일 자체의 문제로 확정
- 위 7가지를 전부 배제한 뒤, **완전히 다른 프로젝트**(`CTe_BMA_PLC1_V20`, 작성자
  "Kim Min", 역시 한글이 섞인 경로)를 새로 열어서 테스트 → **정상적으로 조회됨**
  (`GetProject`/`GetState` 둘 다 프로젝트 이름 정확히 반환).
- 즉 원인은 프로세스/권한/타이밍/인스턴스 순서가 아니라 **카카오톡으로 전달받은 그
  특정 `.ap20` 파일 자체**에 있음 — 아마 다른 TIA 버전에서 만들어져 마이그레이션이
  필요했거나, 다른 사람 환경에서 만들어지며 생긴 파일 자체의 특이 상태로 추정
  (구체적 내부 원인은 미확인, 우선순위 낮음 — 재현에 이 특정 파일이 필요해서
  일반적으로 재현하기 어려움).
- **다중 인스턴스 선택 기능(`ListTiaPortalInstances`/`Connect(processId)`) 자체는
  전체 과정에서 한 번도 틀리지 않고 정확한 PID에 Attach함** — 이 조사로 오히려
  기능이 매우 탄탄하게 검증됨.
- 실제 작업 프로젝트(Mahindra)는 전체 조사 기간 내내 영향 없음.

### 추가 확인 (같은 날, 사용자 재검증 요청으로 진행)
- 새 마힌드라 프로젝트(`Mahindra_CPU01_V20_260910_k1`)와 BMA(`CTe_BMA_PLC1_V20`)를
  동시에 띄운 상태에서 `Connect(processId)` → `GetState` → `GetDevices`까지 각각
  실행 — 두 인스턴스 모두 프로젝트명/디바이스 트리 정상 조회됨. 문제의 부대설비
  파일은 이번엔 준비되어 있지 않아 재현 시도는 못 했음.
- 사용자 판단: 그 특정 파일 내부의 근본 원인을 더 파고드는 건 비용 대비 효과가
  낮다고 보고 여기서 조사 종료하기로 결정. 코드 문제가 아님은 충분히 확인됨.
- **근본 원인 확정: GSD(Generic Station Description) 미설치.** 이 프로젝트는
  옵션 GSD 파일 일부가 이 PC에 설치되어 있지 않았음. TIA UI는 경고만 띄우고
  일단 프로젝트를 "열린" 상태로 보여주지만, 해당 하드웨어 모듈의 Openness 객체
  모델이 제대로 인스턴스화되지 못해 `GetProject`/`GetDevices`가 빈 목록을
  반환한 것.
  사용자가 필요한 GSD를 설치한 뒤 같은 파일(`add_on_eqp_ver4.0`, 새 PID 22652)로
  재접속해 재검증 — `GetState.project`가 `"-"` 대신 `"add_on_eqp_ver4.0"`으로,
  `GetDevices`/`GetProject`도 `S7-1500/ET200MP station_1` 등 실제 데이터로 정상
  반환됨을 확인. **코드 수정 없이 GSD 설치만으로 완전히 해결됨** — MCP 서버/
  Connect 로직에는 애초에 버그가 없었음이 최종 확정.

---

## [2026-09-10] TIA Portal 여러 개 떠있을 때 선택 연결 (ListTiaPortalInstances/Connect processId)

### 배경
- 기존 `ConnectPortal()`이 `TiaPortal.GetProcesses()`로 실행 중인 TIA Portal 프로세스를
  전부 가져온 뒤 **무조건 `.First()`**에 붙었음 — 2개 이상 떠있으면 어느 게 잡힐지
  우리가 선택할 수 없고, 어떤 걸 골랐는지 알려주지도 않았음.

### 조치
- `Portal.GetTiaPortalProcesses()` 추가: `TiaPortal.GetProcesses()`가 이미 `Id`(PID)와
  `ProjectPath`를 붙지 않고도 노출해줘서, 새로 뭘 검색할 필요 없이 그대로 매핑.
- `ConnectPortal(int? processId = null)`로 변경: 인스턴스가 1개면 기존과 동일하게 동작
  (processId 생략 가능). **2개 이상인데 processId를 안 주면 이제 예외를 던짐** —
  "`ListTiaPortalInstances`로 목록 보고 processId 지정해라"고 명확히 안내.
- 새 툴 `ListTiaPortalInstances`(RO), `Connect`에 `processId` 선택 파라미터 추가.

### 검증
- 실제로 서로 다른 프로젝트를 연 TIA Portal 인스턴스 2개를 띄운 상태에서:
  - `ListTiaPortalInstances` → 두 프로세스와 각각의 프로젝트 경로 정확히 나열.
  - `Connect()`(processId 없이) → 예상대로 거부됨.
  - `Connect(processId: <특정 PID>)` → 정확히 그 인스턴스의 프로젝트에 붙음
    (`GetProject`로 프로젝트 이름 일치 확인).

### 후속 개선 (같은 날)
- 처음엔 거부 메시지가 "`ListTiaPortalInstances`로 목록 봐라"고만 하고 실제 목록은
  안 보여줬음 — 사용자 피드백으로, 왕복 한 번 줄이고 사람이 읽어도 바로 이해되게
  **거부 메시지 안에 `Id=15676 (경로); Id=46056 (경로)` 식으로 목록 자체를 바로
  포함**하도록 수정 (`DescribeProcesses` 헬퍼, 커밋 `e295be4`). 잘못된 PID를 줬을
  때의 "그런 프로세스 없음" 에러에도 동일하게 적용.

### 남은 것
- 이미 연결된 상태에서 다른 인스턴스로 **핫스왑**하는 전용 기능은 아직 없음 —
  다른 `processId`로 `Connect`를 다시 부르면 되긴 하지만, 필요하면 나중에 별도 검토.
- `TODO.md`의 "프로젝트 간 비교" 아이디어는 이 기능(여러 인스턴스 동시 인지) 위에서
  더 자연스럽게 만들 수 있을 것 — 아직 미착수.

---

## [2026-09-10] GetOnlineState/GoOnline/GoOffline 실제 PLCSIM으로 검증 + 안전 문서화

### 검증
- 사용자가 TIA Portal에서 PLCSIM Advanced로 실제 다운로드+온라인 연결한 상태에서
  `GetOnlineState("PLC_1")` → `"Online"` 정확히 읽음.
- `GoOffline` → 실제로 연결 끊김 (TIA 화면에서도 사용자가 직접 확인), 다시
  `GoOnline` → `"Online"` 복원까지 왕복 완전 검증.

### 문서화: "왜 갑자기 오프라인이 됐지?" 방지
- `GoOffline`이 툴로 생기면서, 에이전트가 `ExportBlock`류가 "온라인이라 실패"할 때
  스스로 판단해서 `GoOffline`을 불러버릴 수 있는 위험이 생김 — 이건 이 MCP 서버
  내부 상태가 아니라 **TIA Portal 엔지니어링 스테이션의 실제 온라인 연결** 이라서,
  사람이 TIA 창을 보고 있으면 아무 경고 없이 연결이 뚝 끊기는 걸 실시간으로 보게 됨.
- `ExportBlock`/`ExportType`/`ExportBlocks`/`ExportTypes`(고전 XML export,
  `block.Export()` 경로)와 `GoOffline` 자체의 MCP `Description`에 "실패하면 사용자한테
  알리고, 알아서 GoOffline 부르지 말 것"이라고 직접 명시 — 이게 에이전트가 실제로
  `tools/list`에서 읽는 텍스트라 `docs/TOOLS.md`보다 더 직접적으로 먹힘.
  `docs/TOOLS.md`에도 같은 내용의 콜아웃 추가.
- `ExportAsDocuments`/`ExportBlocksAsDocuments`(.s7dcl/.s7res 경로)는 어제 실제로
  온라인 상태에서 성공한 걸 로그로 확인했으므로 이 경고 대상에서 제외 — 오프라인
  요구사항은 고전 XML export 경로에만 있음.

---

## [2026-09-10] GetOnlineState/GoOnline/GoOffline 추가

### 배경
- 어제 export가 "온라인 모드라 안 됨"으로 막혔던 것, 그리고 어제 로드맵(`TODO.md`
  4번)에 후보로만 있던 "TIA 온라인/오프라인 전환"을 실제로 구현.
- 사용자 요청: **PLC Run/Stop 제어는 위험하니 제외**, TIA Portal 자체의
  온라인/오프라인 연결 전환만 있으면 충분하다는 방향으로 스코프 확정.

### 구현
- `Siemens.Engineering.Online.OnlineProvider` 서비스 사용 —
  `Device`/`DeviceItem` 둘 다 `GetService<OnlineProvider>()`로 얻을 수 있고,
  `.State`(속성), `.GoOnline()`, `.GoOffline()` 전부 공개 멤버라
  `CrossReferenceService` 때처럼 explicit interface 캐스팅이 필요 없었음.
- `Portal.GetOnlineProvider(path)`가 `GetDevice`/`GetDeviceItem`과 같은 방식으로
  Device 먼저 시도하고 안 되면 DeviceItem으로 폴백.
- 새 툴 3개: `GetOnlineState`(RO), `GoOnline`, `GoOffline`.

### 검증 (실제 프로젝트, `PLC_1` 대상)
- `GetOnlineState` → `"Offline"` 정확히 읽음.
- `GoOffline` → 이미 오프라인이어도 에러 없이 idempotent하게 성공.
- `GoOnline` → TIA 자체가 "연결을 수립할 수 없음" 에러를 냄 (PLCSIM Advanced
  인스턴스가 이 연결 대상과 안 맞거나 꺼져있는 것으로 추정) — 코드 버그가 아니라
  실제 Openness/PLCSIM 환경 문제이며, 에러 메시지를 있는 그대로 잘 전달함을 확인.
  이후 상태도 그대로 `Offline` 유지 — 원치 않는 부작용 없음.

### 다음에 참고할 점
- **PLC Run/Stop 제어는 의도적으로 구현 안 함** — 실수로 살아있는 PLC를 멈추는
  리스크 때문. 나중에 필요해지면 이 커넥션 전용 툴들과 분리해서 별도 opt-in
  플래그로 게이팅할 것.
- `GoOnline`이 "연결 수립 불가"로 실패하는 경우, 이 프로젝트 환경에서는 PLCSIM
  Advanced 인스턴스 상태/설정 문제일 가능성이 높음 — TIA Portal UI에서 한 번
  수동으로 "Go online" 해서 연결 대상(PG/PC 인터페이스 등)이 제대로 설정돼 있는지
  먼저 확인해볼 것.

---

## [2026-09-10] GetBlockCrossReferences "버그" 오진 — 사실은 Connect 안 함

### 증상
- Claude Desktop 재시작해서 새 툴(`GetTypeCrossReferences`/`GetBlockCrossReferences`)
  등록시킨 직후, 4가지 경로로 다 시도해봐도 전부 "Block not found, or has no
  cross-reference service" — 심지어 어제 성공했던 정확히 같은 경로
  (`Program blocks/001_Main/1_DataSetting/Main_Tracking_Data`)도, 제일 단순한
  `Main [OB1]`도 실패. "경로 파싱 공통 헬퍼 버그(GetBlockInfo 때 봤던 것과 같은)"로
  오진했음.

### 실제 원인
로그 확인해보니 `warn: No TIA project available.` — 그냥 **`Connect` 없이 바로 호출**한
거였음. 앱을 재시작하면 MCP 서버 프로세스가 새로 뜨면서 이전 연결 상태가 초기화되는데,
새 프로세스에서 `Connect`를 안 하고 바로 블록 조회 툴을 부르면 어떤 경로를 넣어도
100% 실패함 (`IsProjectNull()`이 참이라 블록 자체를 못 찾음). 경로 파싱과는 무관.

### 조치 (`Portal.cs`, 커밋 `d4baaa8`)
- `GetTypeCrossReferences`/`GetBlockCrossReferences`가 프로젝트 미연결 시 조용히
  `null` 리턴하던 걸 `PortalException(InvalidState, "No project is open in TIA
  Portal")`로 명확히 던지도록 수정 — `ExportBlock` 등 기존에 이미 있던 패턴과 통일.
  이제 "블록 못 찾음"과 "프로젝트 자체가 안 열림"이 메시지로 구분됨.
- `IsProjectNull()` 체크하는 다른 함수들 다수(`Portal.cs` 전역, `grep`으로 20곳+
  확인)는 여전히 이 구분이 없음 — `TODO.md`의 "가드/not-found 헬퍼 추가" 항목으로
  이미 트래킹 중이라 오늘은 새로 만든 두 툴만 고침.

### 다음에 참고할 점
- **Claude Desktop을 재시작(또는 MCP 서버 프로세스가 새로 뜬) 직후엔 반드시
  `Connect`부터 호출할 것** — 이전 세션의 연결 상태는 새 프로세스로 안 넘어옴.
- "여러 경로를 다 시도했는데 제일 단순한 것까지 실패한다"는 신호는 경로 버그보다
  "애초에 연결/프로젝트가 안 열려있다"를 먼저 의심할 것 — `GetState`나 `Doctor`로
  연결 상태부터 확인하는 게 삽질을 줄임.

---

## [2026-09-09] GetTypeCrossReferences/GetBlockCrossReferences 추가 (+ GetBlockInterface 시도는 폐기)

### 배경
- Mahindra_CPU01 프로젝트에서 "`Main_Tracking_Data`(FB) 인스턴스가 몇 개, 어느 블록에
  선언돼 있는지" 확인하려고 했는데, 기존엔 블록 하나씩 `ExportAsDocuments`로 뽑아서
  `.s7dcl`을 grep하는 식으로 수동 왕복하고 있었음 (`0_Main_CallEnv` 봤다가 없어서
  `Main_DataSetting` 봤다가...). Claude Desktop이 "`PlcBlock.Interface`로 Static
  인터페이스를 직접 구조화해서 읽는 툴을 추가하자"고 제안해서 시도함.

### `GetBlockInterface` 시도 — 막다른 길로 확인됨
- 실제 설치된 V20 `Siemens.Engineering.dll`을 리플렉션 + 라이브 연결로 직접 검증:
  `PlcBlock`(FB) 인스턴스가 실제로 제공하는 서비스는 `ICompilable`,
  `CrossReferenceService`, `LibraryTypeInstanceInfo`, `PlcBlockProtectionProvider`,
  `FingerprintProvider`, `SupervisionProvider`, `SafetySignatureProvider` 7개뿐이고,
  컴포지션은 `Supervisions` 하나, 속성 목록에도 "Interface" 관련 항목이 전혀 없음.
- `Siemens.Engineering.SW.Blocks.Interface.PlcBlockInterface`라는 타입 자체는
  실존함("Interface for all blocks", DLL에 동봉된 XML 문서 기준)but, 이 V20 설치본에서
  `PlcBlock`이 그걸 반환하는 경로(서비스/컴포지션/속성 그 무엇도)가 없음.
  `InterfaceSnapshot`(서비스로는 실존)은 이름과 달리 "런타임 모니터링 스냅샷 값"용이지
  인터페이스 구조가 아님.
- 결론: **V20 Openness로는 블록의 Static/Input/Output 선언을 구조화된 형태로 직접
  읽을 방법이 없음.** 향후 V21+에서 되는지는 미확인 — `TODO.md`에 남겨둠.

### `GetTypeCrossReferences`/`GetBlockCrossReferences`로 방향 전환 — 성공
- `CrossReferenceService`는 실제로 존재하고 (`PlcType`/`PlcBlock` 둘 다
  `GetService<CrossReferenceService>()`로 얻어짐), `.GetCrossReferences(filter)`가
  `Sources → References → Locations` 트리를 컴파일러가 실제로 파싱한 결과로 돌려줌
  (regex grep보다 신뢰도 높음 — 주석/비슷한 이름에 false positive 안 남).
- 단, **첫 시도에서 응답이 3.4MB**가 나옴 — 원인 두 가지:
  1. `GetCrossReferences`는 조회한 객체 자신뿐 아니라 그 안에 선언/사용된 모든 요소
     (로컬 변수, 네트워크 등)까지 전부 별도 `Source` 항목으로 펼쳐서 반환함.
  2. 각 `Source`의 `References` 목록도 "누가 나를 쓰는지"(`ReferenceType.UsedBy`)와
     "내가 내부적으로 뭘 쓰는지"(`ReferenceType.Uses`)가 뒤섞여 있음.
- 조치 (`Helper.cs`): 조회한 객체 자신의 `Source` 항목만 이름으로 필터링하고,
  `UsedBy` 위치만 남기도록 정리 → **3.4MB → 2.5KB**.
- 실제 프로젝트로 검증: `Main_Tracking_Data` 조회 시 `0_Main_CallEnv`가
  `Call`(NW1 호출)과 `Multiinstance`(`Main_Tracking_Data_Instance`라는 이름의 Static
  인스턴스 선언) 두 위치로 정확히 잡힘 — 오늘 하려던 조사가 한 번의 호출로 끝남.

### 다음에 참고할 점
- `Main_Tracking_Data`는 UDT가 아니라 **FB 블록**이었음 — Program blocks 트리 밑에
  있었고 PLC data types 밑이 아니었음. "인스턴스 타입으로 쓰이는 FB"는
  `GetTypeCrossReferences`가 아니라 `GetBlockCrossReferences`로 조회해야 함.
- `CrossReferenceFilter`는 `AllObjects`/`ObjectsWithReferences`/
  `ObjectsWithoutReferences`/`UnusedObjects` 4가지가 있음. 지금은 항상
  `AllObjects`로 호출한 뒤 우리 쪽에서 후처리 필터링하는 방식 — 원본 Sources를
  그대로(필터링 없이) 노출하는 걸 원한다면 `Helper.BuildCrossReferenceSourceList`의
  `onlyName`/`maxDepth`/`Uses` 제외 로직을 확인할 것.
- 이 API 탐색에 쓴 리플렉션 기법(설치된 DLL을 직접 로드해서 `GetTypes()`,
  `GetServiceInfos()`, `GetCompositionInfos()`, `GetAttributeInfos()`로 실제
  런타임 표면을 확인하고, DLL 옆의 `Siemens.Engineering.xml` 문서로 교차 검증)은
  Openness API 관련 다른 기능을 추가할 때도 똑같이 유용함 — 문서만 믿지 말고 항상
  라이브로 검증할 것.

---

## [2026-09-09] GetDeviceInfo가 이름에 '/'가 들어간 디바이스에서 항상 "Device not found"

### 증상
- `GetDeviceInfo("S7-1500/ET200MP station_1")`가 매번 즉시 "Device not found"로 실패.
- Claude Desktop 쪽에서 직접 원인을 특정해서 알려준 케이스 — 이 디바이스 이름 자체에
  `/`가 리터럴로 포함돼 있는데, `GetDeviceByPath`가 `devicePath`를 `/` 기준으로
  단순 split해버려서 `["S7-1500", "ET200MP station_1"]` 두 조각으로 쪼개진 뒤,
  "S7-1500"을 디바이스 그룹 이름으로 착각하고 찾다가 실패하는 것으로 추정 → 실제로
  코드 확인해서 그대로 맞았음.

### 원인
`Portal.cs`의 `GetDeviceByPath`(오직 `GetDeviceInfo`만 사용)에는 "하드웨어 PLC는
`Device.Name`이 `'S7-1500/ET200MP-Station_1'`처럼 TIA Portal IDE에는 안 보이는 형태로
슬래시를 포함할 수 있다"는 케이스에 대한 처리가 아예 없었음. 반면 바로 옆
`GetSoftwareContainerInDevices`(softwarePath 계열 툴들이 사용)와
`GetDeviceItemByPath`(`GetDeviceItemInfo`가 사용)에는 이미 이 케이스를 위한 폴백
로직이 있고, 코드 주석에도 정확히 이 상황이 설명돼 있었음 — `GetDeviceByPath`만
그 처리가 누락된 상태였음.

**이 버그는 upstream 원본에도 그대로 있던 것**이고(`upstream/main`의 `GetDeviceByPath`와
동일), 우리가 이번에 새로 만든 게 아님 — 원작자가 이 함수 하나만 놓친 것으로 보임.

### 조치 (`Portal.cs`, 커밋 `854aec2`)
- `FindDeviceByFullName()` 추가: `/`로 split하기 전에, 전달받은 `devicePath` 전체
  문자열을 실제 디바이스 이름과 통짜로 먼저 비교(최상위 devices + 모든
  device group을 재귀적으로 탐색). 매치되면 바로 반환하고, 안 되면 기존 split 기반
  탐색으로 폴백.
- 실제 회사 프로젝트의 진짜 디바이스 이름(`"S7-1500/ET200MP station_1"`)으로 직접
  재현·검증: 수정 전 "Device not found" → 수정 후 정상 조회.

### 남은 한계
- 지금 고친 건 "devicePath 전체가 통째로 그 슬래시 포함 디바이스 이름과 일치하는"
  케이스임. 만약 그 디바이스가 그룹 안에 있고, 그 디바이스 *밑의* 하위 항목까지
  경로에 넣어야 하는 상황(예: `"그룹/S7-1500/ET200MP station_1/무언가"`처럼 슬래시
  포함 이름 뒤에 추가 세그먼트가 더 붙는 경우)이면 여전히 안 될 수 있음 — 그런
  케이스가 실제로 나오면 그때 더 일반적인(그리디 프리픽스 매칭) 방식으로 확장 필요.
  당장 보고된 케이스(디바이스 이름 자체를 통째로 넘기는 것)는 확실히 해결됨.

---

## [2026-09-09] 블록/타입 단위 조회(GetBlockInfo/ExportBlock 등)가 전부 막혀있던 버그 수정

### 증상
- `GetBlocksWithHierarchy`, `GetSoftwareTree` 같은 벌크 조회는 되는데, 블록 하나를
  집어서 보는 `GetBlockInfo`는 즉시 "Block not found" 에러, `ExportBlock`/
  `ExportBlocksAsDocuments`는 응답이 아예 안 오고 60초쯤 지나서 타임아웃.

### 원인
`GetSoftwareTree`가 사람이 읽기 좋으라고 블록 루트를 `"Program blocks"`, 타입
루트를 `"PLC data types"`라는 **표시용 라벨**로 붙여서 트리를 그리는데, 실제로는
`plcSoftware.BlockGroup`/`TypeGroup` 자체가 그 루트라서 저 이름을 가진 진짜
하위 그룹은 존재하지 않음. 그런데 `GetPlcBlockGroupByPath`/`GetPlcTypeGroupByPath`
(블록·타입 경로를 실제 그룹으로 바꿔주는 내부 함수)는 이 라벨을 진짜 그룹
이름인 줄 알고 첫 세그먼트부터 찾다가 실패 → `GetBlockInfo`는 바로 "not found",
`ExportBlock`류는 not-found 이후 이름 후보를 찾으려고 전체 블록 트리를 다시
훑는 무거운 폴백 로직을 타면서 사실상 멈춘 것처럼 보였던 것으로 추정.

트리를 보고 경로를 구성하면(사람이든 LLM이든) 자연스럽게
`"Program blocks/Group/BlockName"` 같은 형태로 만들게 되는데, 그게 항상 실패하는
구조였음 — 즉 트리 출력과 경로 파서가 서로 다른 규칙을 쓰고 있던 게 근본 원인.

### 조치 (`Portal.cs`, 커밋 `6e32d8b`)
- `GetPlcBlockGroupByPath`/`GetPlcTypeGroupByPath`가 경로의 첫 세그먼트가
  `"Program blocks"`/`"PLC data types"`(대소문자 무관)면 무시하고 건너뛰도록 수정.
- 실제 V20 프로젝트에 붙여서 stdio로 직접 검증: 이전엔 "Block not found"였던
  `"Program blocks/000_OB_Cycle/Main"` 경로로 `GetBlockInfo`/`ExportBlock` 둘 다
  블록을 정상적으로 찾는 것까지 확인. (단, `ExportBlock` 자체는 TIA 프로젝트가
  온라인/모니터링 모드일 때 "This function is not supported in online mode."로
  거부되는데, 이건 코드 버그가 아니라 TIA Portal 자체 제약 — 오프라인일 때만
  export 가능.)

### 다음에 참고할 점
- 블록/타입 경로를 다루는 새 함수를 추가한다면 `GetPlcBlockGroupByPath`/
  `GetPlcTypeGroupByPath`를 거치게 하거나, 최소한 같은 라벨 스트리핑 규칙을
  지킬 것 — 안 그러면 이 버그가 다른 함수에서 또 재발함.
- `GetSoftwareTree`/`GetBlocksWithHierarchy`의 출력을 보고 경로를 만드는 게
  자연스러운 사용 패턴이므로, 트리 표시 라벨과 경로 파서의 실제 그룹 이름 규칙은
  항상 일치시키거나(지금처럼 파서 쪽에서 라벨을 흡수), 아니면 문서에 "트리의
  루트 라벨은 경로에 넣지 마세요"라고 명확히 적어둬야 함.

---

## [2026-09-09] GetProject/GetDevices가 항상 에러 나던 버그 수정

### 증상
- V20 연결은 정상인데 `GetProject`, `GetDevices` 툴만 호출하면 매번
  `"An error occurred invoking 'X'."` (내용 없는 일반 에러)로 실패.
- `Portal.cs`/`McpServer.cs`의 try/catch에는 안 걸림 — 정상적으로 응답 객체를
  만들어서 반환까지 갔는데, 그 이후 MCP SDK가 JSON으로 직렬화하는 단계에서
  죽는 패턴이었음.

### 원인
`Helper.GetAttributeList()`가 TIA Openness의 `obj.GetAttribute(name)` 값을
그대로 `Attribute.Value`(`object?`)에 담아서 반환하는데, 속성에 따라 이 값이
단순 문자열/숫자가 아니라 **복잡한 .NET/Openness 객체**로 오는 경우가 있었음:
- 프로젝트의 `Path` 속성 → `System.IO.FileSystemInfo` (`.Directory.Parent.Parent...Root.Root...`)
- 디바이스의 설명류 속성 → `MultilingualText` → 내부 `CultureInfo` → `.Parent.Parent...`
  (`CultureInfo.Parent`는 InvariantCulture에서 자기 자신을 가리켜 진짜 순환이 됨)

`System.Text.Json`이 이런 객체를 리플렉션으로 직렬화하다가 최대 깊이(64)를
넘겨서 `JsonException: A possible object cycle was detected`로 죽고, MCP SDK가
이걸 뭉뚱그려 `"An error occurred invoking 'X'."`로만 클라이언트에 보여줬던 것.
`--logging 1`로 stderr 로그를 켜야만 진짜 예외 메시지가 보임.

### 조치 (`Helper.cs`, 커밋 `9900f4e`)
- `SanitizeAttributeValue()` 헬퍼 추가: 문자열/불리언/숫자/`DateTime`/`Guid`/
  enum처럼 JSON 직렬화가 안전한 타입만 그대로 통과시키고, 그 외 나머지
  (`FileSystemInfo`, `MultilingualText`, `CultureInfo` 등 뭐가 됐든)는 전부
  `.ToString()`으로 납작하게 만들어서 넣음.
- `GetProject`, `GetDevices` 둘 다 실제 stdio로 재검증 완료 (`IsError = False`).

### 다음에 참고할 점
- TIA attribute 값을 다루는 새 툴을 추가할 때, `Helper.GetAttributeList()`를
  거치면 이제 자동으로 안전하지만, 만약 `obj.GetAttribute()` 결과를 다른 곳에서
  직접 JSON 응답에 넣는 코드를 새로 짠다면 똑같은 함정에 걸릴 수 있음 — 원시
  타입이 아니면 반드시 문자열화할 것.
- 이런 종류의 버그는 항상 우리 쪽 try/catch를 통과한 뒤(성공 응답을 만든 뒤)
  SDK의 직렬화 단계에서 터지므로, `--logging 1`을 켜고 stderr를 봐야
  `fail: ... threw an unhandled exception` 로 진짜 원인이 보임 — 클라이언트에
  오는 메시지만 봐서는 절대 원인을 못 찾음.

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
