# Kế hoạch test Ngày 2: Function Calling, RBAC, duyệt có điều kiện

- Ngày: 2026-11-16 | Nhánh: `feat/day-02-function-calling`
- Chỉ là kế hoạch: không sửa code, không viết test. Worker làm theo mục 5–7.

## 1. Phạm vi và tài liệu nguồn

**Tài liệu:**
- Spec: `docs/superpowers/specs/2026-10-28-day-02-function-calling-design.md` (§1.2, §3, §4, §6)
- Báo cáo hiện trạng: `docs/superpowers/plans/2026-11-15-day-02-spec-impl-report.md`

**Code được phân tích:**
- `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`
- `code/src/ErpChatbot.Agent/Security/RbacFunctionInvoker.cs`
- `code/src/ErpChatbot.Agent/Tools/ErpTools.cs`
- `code/src/ErpChatbot.Core/Models/Rbac/*`, `code/src/ErpChatbot.Core/Models/ErpOptions.cs`
- `code/src/ErpChatbot.Infrastructure/Services/InMemoryErpDataService.cs`
- `code/src/ErpChatbot.Cli/CliRunner.cs`, `code/src/ErpChatbot.Cli/Program.cs`

**Test hiện có:** 90 test, chạy `dotnet test --no-build` ngày 16/11: 90 pass. Lưu ý: chạy bằng bản build cũ (`--no-build`) để không đụng phiên smoke đang chạy.

**Trong phạm vi:** RBAC theo tool, default deny, duyệt có điều kiện, event tool, `/user` reset phiên, giới hạn vòng lặp tool, CLI không in lỗi gốc.

**Ngoài phạm vi:** health check Ollama, `/stats` và đếm token (Ngày 1), cấu hình host/DI chung. Riêng việc CLI và agent dùng chung một `IUserContext` chỉ kiểm bằng smoke SM5.

## 2. Luồng (F1…F22)

Đi qua hạ tầng chung: `FunctionInvokingChatClient` / `ChatClientAgent` của framework (chỉ kiểm cấu hình của mình, không kiểm framework).

### 2.1 Dựng agent và RBAC

| Bước | Vị trí | Việc làm | Ví dụ input → kết quả |
|---|---|---|---|
| F1 | `ErpTools.cs:12-66`, `ErpAgentService.cs:43-64` | Lọc tool theo quyền user **lúc dựng agent**; bọc `create_purchase_request` bằng `ApprovalRequiredAIFunction` | `sales_user` → 4 tool (không có `get_customer_debt`); `accountant_user` → 4 tool (không có tool ghi) |
| F2 | `ErpAgentService.cs:48-57` | Cấu hình middleware: 5 vòng, 2 lỗi liên tiếp, ẩn lỗi chi tiết, chạy tuần tự | model gọi tool mãi → dừng sau 5 vòng |
| F3 | `RbacFunctionInvoker.cs:33-37` | Default deny: tool không có trong `ToolPermissions` → trả `ToolSecurityError` | `delete_database` → `{PermissionDenied, "...Default Deny / Fail-Secure"}` |
| F4 | `RbacFunctionInvoker.cs:15-22, 40-45` | Kiểm quyền theo bảng tool → quyền; thiếu quyền → `ToolSecurityError` (không ném lỗi) | `sales_user` gọi `get_customer_debt` → lỗi có chữ `Debt:Read` |
| F5 | `RbacFunctionInvoker.cs:47-55` | Tool ném lỗi → ghi log Error → ném lại nguyên exception | ERP ném `"SELECT * FROM Items"` → log nhận đúng exception, exception đi tiếp |
| F22 | `UserContext.cs:7-68` | Vai trò và quyền mẫu; `SwitchUser` đổi user, tên lạ → `ArgumentException` | `SwitchUser("ghost")` → ném lỗi |

### 2.2 Một lượt chat (`RunStreamingAsync`, `ErpAgentService.cs:82-243`)

| Bước | Vị trí | Việc làm | Ví dụ |
|---|---|---|---|
| F6 | `:139-153` | Ghi `FunctionCallContent` theo `CallId`; khi thấy `FunctionResultContent` thì phát `AgentToolExecutionEvent`. `Success = Exception==null && Result không phải ToolSecurityError`. `Error` chỉ là `ToolSecurityError.Message`, `ToolFailedMessage` hoặc null | tool lỗi có SQL → event `Error="Tool gặp lỗi khi thực thi."` |
| F7 | `:135-138, 158-161` | Gom `ToolApprovalRequestContent`; không có thì kết thúc vòng `while` | |
| F8 | `:181-190` | **Bước 1 của duyệt**: user thiếu `PurchaseRequest:Create` → từ chối, phát event lỗi, KHÔNG hỏi | `accountant_user` + qty 100 → event "Từ chối: Tài khoản 'accountant_user'…" |
| F9 | `:172-176, 192-201` | **Bước 2**: đọc `quantity` bằng `int.TryParse(ToString())`; không đọc được → 0; `<= 0` → từ chối, phát event | qty -10, 0, thiếu, "abc", 20.5 → "phải là số nguyên dương" |
| F10 | `:66-80, 204-210` | **Bước 3**: `cộng dồn + qty > ngưỡng` thì cần hỏi; không thì tự duyệt và cộng dồn. Ngưỡng lấy từ `ErpOptions.AutoApproveQuantityThreshold` | ngưỡng 50: 30 → tự duyệt; tiếp 25 → tổng 55 → hỏi |
| F11 | `:211-226` | Phát `AgentApprovalRequiredEvent`; `approvalHandler == null` → từ chối; đồng ý → cộng dồn | handler null + qty 80 → không tạo PR |
| F12 | `:229-237, 143` | Mọi đường `approved=false` → thêm `CallId` vào `skipResultCallIds` (để kết quả "rejected" không thành event); gửi phản hồi duyệt, lặp `while` | từ chối → không có event thực thi |
| F13 | `:240-242` | Ghi thống kê, phát `AgentTurnCompletedEvent` | |
| F14 | `:248-252` | `ResetSessionAsync`: dựng lại agent (lọc tool lại theo user hiện tại) + session mới; giữ thống kê | |

Biến `turnCumulativePrQuantity` (`:96`) sống trong một lượt, qua mọi vòng `while`; lượt sau bắt đầu lại từ 0.

### 2.3 ERP và CLI

| Bước | Vị trí | Việc làm | Ví dụ |
|---|---|---|---|
| F15 | `InMemoryErpDataService.cs:72-83` | Chốt chặn cuối: qty `<= 0` → `ArgumentOutOfRangeException` | qty 0 → ném |
| F16 | `CliRunner.cs:93-123` | `/user <tên>`: `SwitchUser` → `ResetSessionAsync` (đúng thứ tự này) → in thông tin; tên sai → in `[LỖI]`, không reset; thiếu tên → in hướng dẫn | `/user ghost` → `[LỖI] User 'ghost' không tồn tại…` |
| F17 | `CliRunner.cs:125-135` | `/whoami` in mã, tên, vai trò, quyền | |
| F18 | `CliRunner.cs:249-268` | Hỏi `(y/n)`: chỉ `y`/`Y` (sau Trim) là đồng ý; còn lại (kể cả Enter, `yes`, hết input) là từ chối | `yes` → từ chối |
| F19 | `CliRunner.cs:208-217` | In event tool: thành công in tham số + ms; lỗi chỉ in `Error` | |
| F20 | `CliRunner.cs:232-244` | Bắt lỗi: Ctrl+C → "(đã hủy phản hồi)"; `HttpRequestException`/`TimeoutException`/OCE khác → **in `ex.Message`**; lỗi khác → câu cố định, không in `ex.Message` | |
| F21 | `Program.cs:46, 51-52` | `IUserContext` singleton dùng chung cho `ErpAgentService` và `CliCommandHandler` (DI chọn constructor 4 tham số có `IUserContext`) | |

### 2.4 Các nhánh

- Thành công: tool đọc đủ quyền (F4→F6), tự duyệt (F10), người dùng đồng ý (F11).
- Lỗi validate: qty `<= 0` hoặc không đọc được (F9, F15).
- Không tìm thấy: SP-999 (ERP ném lỗi có chữ `SELECT`, F5→F6), tool không đăng ký (F3).
- Không có quyền: tool bị lọc khỏi danh sách (F1), invoker từ chối (F4), từ chối ở bước duyệt (F8).
- Từ chối duyệt: người dùng `n` (F11), handler null (F11).
- Vượt giới hạn: 5 vòng (F2), 2 lỗi liên tiếp → exception thoát ra (F2→F20).
- Huỷ: Ctrl+C giữa tool (F6, F20).
- Biên: qty 50/51, ngưỡng 0, cộng dồn qua nhiều call, nhiều vòng `while`, nhiều lượt.

## 3. Bảng test case

Ký hiệu trạng thái: **ĐÃ CÓ** `<tên test>` / **THIẾU** / **THIẾU (thay test cũ)** = test cũ quá yếu, thay bằng test mới.
Loại: `unit` = gọi trực tiếp 1 class; `integration` = `ErpAgentService` thật + fake `IChatClient` + ERP in-memory (không cần Ollama); `manual` = smoke với Ollama thật.

### 3.1 Tự động

| ID | P | Loại | Bước F | Mục tiêu | Input | Kỳ vọng | Trạng thái |
|---|---|---|---|---|---|---|---|
| TC01 | P0 | unit | F4 | Bảng quyền đúng cho đủ 3 user × 5 tool | 15 cặp (user, tool) | đủ quyền → chạy hàm; thiếu → `ToolSecurityError` chứa đúng tên quyền | THIẾU (đã có 3 cặp: `Invoker_WhenSalesCallsCustomerDebt_ReturnsStructuredPermissionDeniedError`, `Invoker_WhenAccountantCallsCustomerDebt_ExecutesSuccessfully`, `Invoker_WhenSalesCallsGetStock_ExecutesSuccessfully`) |
| TC02 | P0 | unit | F3 | Default deny | `admin_user` gọi `delete_database` | `PermissionDenied`, có chữ "Default Deny / Fail-Secure" | ĐÃ CÓ `Invoker_WhenCallingUnregisteredTool_ReturnsFailSecureError` |
| TC03 | P1 | unit | F1, F4 | Tên tool trong `ErpTools` khớp đúng khoá `ToolPermissions` | `ErpTools.GetTools(erp)` | hai tập tên bằng nhau | THIẾU |
| TC04 | P1 | unit | F1 | Lọc tool theo vai trò | sales / accountant / admin | 4 / 4 / 5 tool, đúng tool bị loại | ĐÃ CÓ `GetTools_WhenUserIsSales_ExcludesCustomerDebt`, `GetTools_WhenUserIsAccountant_ExcludesPurchaseRequest`, `GetTools_WhenUserIsAdmin_IncludesAll5Tools` |
| TC05 | P0 | unit | F1 | Tool ghi bị bọc duyệt | `GetTools(erp)` | `create_purchase_request` là `ApprovalRequiredAIFunction` | ĐÃ CÓ `GetTools_ShouldReturn5Tools_WithProperNamesAndApproval` |
| TC06 | P1 | unit | F5 | Tool lỗi: log Error + ném lại đúng exception; không logger vẫn chạy | ERP ném `"SELECT…"` | log 1 dòng Error cùng exception; exception gốc thoát ra | ĐÃ CÓ `Invoker_WhenToolThrows_RethrowsOriginalAndLogsItAtError`, `Invoker_WhenToolThrowsAndNoLogger_RethrowsOriginalException` |
| TC07 | P0 | integration | F4, F6 | Invoker từ chối → event lỗi có tên quyền | agent dựng với accountant, đổi sang sales, model gọi `get_customer_debt` | 1 event `Success=false`, `Error` chứa `Debt:Read` | ĐÃ CÓ `ToolEvent_WhenInvokerDenies_EmitsFailedEventWithPermissionError` |
| TC08 | P0 | integration | F6 | Event tool lỗi không lộ SQL (có và không qua duyệt) | ERP ném `"SELECT * FROM Items"` | `Error == ToolFailedMessage`, không chứa `SELECT` | ĐÃ CÓ `ToolEvent_WhenToolThrowsWithoutApproval_EmitsSafeErrorEvent`, `Approval_WhenToolThrows_EmitsSafeErrorEvent` |
| TC09 | P0 | integration | F2 | Model (LLM) không nhận chuỗi SQL trong kết quả tool lỗi (`IncludeDetailedErrors=false`) | ERP `get_stock` ném `"SELECT * FROM Items"` | message gửi model lần 2 có `FunctionResultContent` cho `c1`, nội dung không chứa `SELECT` | THIẾU |
| TC10 | P1 | integration | F6 | Thứ tự event: tool → text → hoàn tất | model gọi `get_stock` rồi trả "Còn 124 cái" | chỉ số event tool < text < turn completed | ĐÃ CÓ `RunStreamingAsync_ToolExecutionEvent_ComesBeforeFollowingTextAndTurnCompleted` |
| TC11 | P1 | integration | F6 | Hai call trong một update → hai event đúng tham số | `get_stock` SP-001 + SP-002 | 2 event, `ArgumentsSummary` đúng thứ tự | ĐÃ CÓ `ToolEvent_TwoCallsInOneUpdate_EmitsTwoEventsInOrderWithMatchingArgs` |
| TC12 | P1 | integration | F6 | Huỷ lúc tool đang chạy → lượt sau không có event cũ | huỷ trong `GetStockAsync` | lượt 2 không có event tool | ĐÃ CÓ `RunStreamingAsync_AfterCancelledTurn_DoesNotReplayStaleToolEvents` |
| TC13 | P0 | integration | F8, F12 | Thiếu quyền PR ở bước duyệt: không hỏi, không ghi ERP, đúng 1 event do service tạo | agent dựng với sales, đổi sang accountant (không reset); qty 20 và 100 | handler không được gọi; không có `AgentApprovalRequiredEvent`; đúng 1 event PR `Success=false`, `Error` bắt đầu "Từ chối: Tài khoản 'accountant_user'"; ERP `CreateCalls == 0` | THIẾU (thay test cũ `RunStreamingAsync_WhenUserLacksPermission_RejectsWithoutAskingApproval`) |
| TC14 | P0 | integration | F9, F12 | Số lượng không hợp lệ: không hỏi, không ghi ERP | qty -10, 0, thiếu khoá, "abc", 20.5 | handler không gọi; đúng 1 event PR `Success=false`, `Error` chứa "nguyên dương"; `CreateCalls == 0` | THIẾU (thay test cũ `RunStreamingAsync_WhenQuantityIsNegativeOrZero_RejectsWithoutAskingApproval`) |
| TC15 | P1 | unit | F9, F10 | Biên ngưỡng ở hàm tĩnh | qty -5, 0, 10, 50, 51, 100; ngưỡng 10 (10/11); ngưỡng 0 (1) | đúng bảng true/false | ĐÃ CÓ `ShouldRequireApproval_QuantityThresholdCheck`, `ShouldRequireApproval_UsesConfiguredThreshold`, `ShouldRequireApproval_ThresholdZero_RequiresApprovalForAnyPositiveQuantity` |
| TC16 | P1 | integration | F10 | Ngưỡng cấu hình đi qua service thật | `AutoApproveQuantityThreshold = 10`, qty 20 | handler được gọi 1 lần; trả `false` → `Created` rỗng | THIẾU (finding 4 của báo cáo 15/11) |
| TC17 | P0 | integration | F10, F6 | Tự duyệt ≤ ngưỡng tạo PR thật, không hỏi, đúng 1 event ok | qty 20 | handler không gọi; 1 PR `PR-\d{4}`; 1 event PR `Success=true` | ĐÃ CÓ `Approval_AutoApprovedBelowThreshold_CreatesPurchaseRequestInErp` |
| TC18 | P0 | integration | F11, F12 | Người dùng từ chối → không PR, không event thực thi | qty 100, handler `false` | `Created` rỗng; không có event PR | ĐÃ CÓ `Approval_UserRejects_DoesNotCreatePurchaseRequest`, `ToolEvent_WhenUserRejectsApproval_EmitsNoToolExecutionEvent` |
| TC19 | P0 | integration | F11 | `approvalHandler == null` → từ chối an toàn | qty 80, handler null | có `AgentApprovalRequiredEvent`; `CreateCalls == 0`; không có event PR thành công | THIẾU (thay test cũ `RunStreamingAsync_WhenApprovalHandlerIsNull_DefaultsToRejectSafely`: chỉ assert có event hỏi, luôn pass dù handler null bị coi là đồng ý) |
| TC20 | P0 | unit + integration | F10, F7 | Cộng dồn trong một message chặn lách ngưỡng; đồng ý → tạo PR | 30 rồi 25 trong 1 message | chỉ hỏi phiếu 25; ERP tạo 2 PR | ĐÃ CÓ `ShouldRequireApproval_WhenCumulativeTurnQuantityExceedsThreshold`, `Approval_TwoRequestsInOneTurn_SecondExceedsCumulativeThreshold_AsksUser` |
| TC21 | P1 | integration | F10, F12 | Cộng dồn qua **hai vòng `while`** (duyệt xong model gọi tiếp) | vòng 1: qty 30; vòng 2: qty 25 | handler gọi 1 lần với qty 25; ERP tạo 2 PR | THIẾU (spec §4 nói đã test "qua vòng while" nhưng test hiện có để 2 phiếu trong cùng 1 message) |
| TC22 | P1 | integration | F10, F11 | Chỉ cộng phiếu được duyệt; phiếu duyệt tay cũng được cộng | 60 rồi 30 trong 1 message; handler trả `false` / `true` cho phiếu 60 | `false`: hỏi 1 lần, ERP tạo 1 PR (30). `true`: hỏi 2 lần (60 và 30) | THIẾU |
| TC23 | P2 | integration | F10 | Cộng dồn làm lại từ 0 ở lượt mới | lượt 1: qty 40; lượt 2: qty 40 | handler không gọi lần nào; ERP tạo 2 PR | THIẾU |
| TC24 | P1 | integration | F9, F10 | `quantity` dạng `JsonElement` (giống Ollama thật) vẫn tự duyệt đúng | qty = `JsonElement` số 20 | handler không gọi; 1 PR qty 20 | THIẾU |
| TC25 | P1 | unit | F15 | ERP chặn qty ≤ 0 | 0, -5, -100 | `ArgumentOutOfRangeException` | ĐÃ CÓ `CreatePurchaseRequestAsync_NegativeOrZeroQuantity_ThrowsArgumentOutOfRangeException` |
| TC26 | P1 | integration | F2 | Giới hạn 5 vòng tool (và không có middleware trùng nâng giới hạn) | model lần nào cũng gọi `get_stock` | ERP `GetStockCalls` trong 1..5; model gọi ≤ 6 lần; test kết thúc | THIẾU |
| TC27 | P1 | integration | F2, F5, F6 | 2 lỗi liên tiếp → dừng và ném; event trước đó vẫn an toàn | model luôn gọi `get_stock`, ERP luôn ném `"SELECT…"` | `RunStreamingAsync` ném `InvalidOperationException`; `GetStockCalls` = số đo được (dự kiến 3); mọi event tool `Error == ToolFailedMessage` | THIẾU |
| TC28 | P0 | unit | F16 | `/user` hợp lệ đổi user và reset | `/user accountant_user` | user = accountant; reset được gọi | ĐÃ CÓ `HandleInputAsync_UserCommand_SwitchesUser_AndResetsSession` |
| TC29 | P0 | integration | F16, F14 | Sau `/user`, phiên mới không còn kết quả công nợ cũ | accountant hỏi KH-002 → `/user sales_user` → hỏi SP-001 | message gửi model ở lượt 2 không chứa "Hoàng Gia"/"120500000" | ĐÃ CÓ `HandleInputAsync_UserCommand_RealAgentSession_DoesNotCarryPreviousDebtResult` |
| TC30 | P1 | integration | F16, F14, F1 | Sau `/user`, danh sách tool gửi model khớp quyền user mới (kiểm thứ tự Switch → Reset) | accountant → `/user sales_user` | lượt 1 có `get_customer_debt`; lượt 2 không có, có `create_purchase_request` | THIẾU |
| TC31 | P2 | unit | F16 | `/user` sai tên hoặc thiếu tên: không đổi user, không reset | `/user`, `/user ghost_user` | `Handled`; user vẫn `sales_user`; `ResetCalls == 0`; in `[HƯỚNG DẪN]` / `[LỖI]` | THIẾU |
| TC32 | P2 | unit | F17 | `/whoami` in đúng thông tin | `admin_user` | output có `admin_user`, `Lê Quản Trị`, `Admin`, đủ 4 quyền | THIẾU (thay test cũ `HandleInputAsync_WhoamiCommand_PrintsUserInfo`: chỉ assert `Handled`) |
| TC33 | P2 | unit + integration | F14, F13 | `/reset` gọi reset, thống kê giữ nguyên | `/reset` | reset 1 lần; `TurnCount` không về 0 | ĐÃ CÓ `HandleInputAsync_ResetCommand_CallsResetSession`, `ResetSessionAsync_ClearsSession_RetainsStats` |
| TC34 | P1 | unit | F18 | Hỏi y/n: chỉ `y`/`Y` là đồng ý | `y`, `Y`, ` y `, `n`, Enter, `yes`, hết input | 3 đầu → `true`; còn lại → `false`; output có dòng `[cần duyệt] create_purchase_request(productCode=SP-003, quantity=100)` | THIẾU |
| TC35 | P0 | unit | F20 | Lỗi bất ngờ có SQL → CLI in câu cố định | `InvalidOperationException("SELECT * FROM Secret…")` | có `[Lỗi xử lý`, không có `SELECT`/`Secret` | ĐÃ CÓ `ProcessLine_WhenUnexpectedErrorMentionsSql_DoesNotPrintRawMessage` |
| TC36 | P1 | unit | F20 | Lỗi loại mạng/timeout mang SQL → CLI KHÔNG được in nguyên văn | `TimeoutException("SELECT * FROM Items")`, `HttpRequestException("SELECT * FROM Items")` | không có `SELECT` trong output | THIẾU — **dự kiến FAIL với code hiện tại** (`CliRunner.cs:238` in `ex.Message`). Cần người dùng quyết định trước (mục 9, Q1) |
| TC37 | P1 | unit | F22 | Quyền 3 vai trò, `SwitchUser`, user lạ ném lỗi | | đúng quyền; ném `ArgumentException` | ĐÃ CÓ 5 test trong `UserContextTests.cs` |
| TC38 | P2 | unit | F6 (hành vi framework) | Canary: framework 1.25.0 vẫn phát call/result/approval như giả định | 8 kịch bản | như spike | ĐÃ CÓ `Case1`…`Case8` trong `ToolEventsFromStreamSpikeTests.cs` |

### 3.2 Smoke thủ công (chi tiết ở mục 8)

| ID | P | Loại | Bước F | Mục tiêu | Trạng thái |
|---|---|---|---|---|---|
| SM1 | P0 | manual | F1, F6, F19 | sales hỏi tồn kho → gọi `get_stock`, không gọi tool ghi | THIẾU (chưa chạy) |
| SM2 | P0 | manual | F1, F4 | sales hỏi công nợ → không có số công nợ | THIẾU |
| SM3 | P1 | manual | F16, F4 | accountant hỏi KH-002 → 120.500.000 VND | THIẾU |
| SM4 | P0 | manual | F10, F11, F18 | sales mua 20 → tự duyệt; mua 100 → hỏi y/n | THIẾU |
| SM5 | P0 | manual | F16, F21 | đổi user → không nhớ dữ liệu cũ; CLI và agent dùng chung user | THIẾU |
| SM6 | P1 | manual | F1, F10 | ghi nhận model gọi nhầm tool ghi cho câu chỉ đọc | THIẾU |
| SM7 | P1 | manual | F5, F6, F2 | SP-999 → tool lỗi, không lộ `SELECT` | THIẾU |

## 4. Ma trận coverage

| Bước | Case |
|---|---|
| F1 lọc tool, bọc duyệt | TC03, TC04, TC05, TC30, SM1, SM2, SM6 |
| F2 cấu hình middleware | TC09, TC26, TC27, SM7. `AllowConcurrentInvocation=false` **không có case**: không có rule nghiệp vụ quan sát được ngoài hành vi framework |
| F3 default deny | TC02 |
| F4 kiểm quyền | TC01, TC03, TC07, SM2, SM3 |
| F5 tool lỗi → log + ném lại | TC06, TC27, SM7 |
| F6 event từ stream | TC07, TC08, TC10, TC11, TC12, TC17, TC27, TC38 |
| F7 gom yêu cầu duyệt | TC20, TC21 |
| F8 RBAC trước khi hỏi | TC13 |
| F9 qty không hợp lệ | TC14, TC15, TC24 |
| F10 ngưỡng + cộng dồn | TC15, TC16, TC17, TC20, TC21, TC22, TC23, TC24, SM4 |
| F11 hỏi / handler null | TC18, TC19, TC22, SM4 |
| F12 skipResultCallIds + vòng while | TC13, TC14, TC18, TC21 |
| F13 hoàn tất lượt | TC10, TC33 (đếm token thuộc Ngày 1, ngoài phạm vi) |
| F14 reset dựng lại agent | TC29, TC30, TC33 |
| F15 ERP chặn qty | TC25 |
| F16 `/user` | TC28, TC29, TC30, TC31, SM3, SM5 |
| F17 `/whoami` | TC32, SM5 |
| F18 hỏi y/n | TC34, SM4 |
| F19 CLI in event tool | **không có case tự động**: chỉ là định dạng chuỗi; nội dung `Error` đã an toàn từ TC08; nhìn thấy trực tiếp ở SM1, SM7 |
| F20 CLI bắt lỗi | TC35, TC36, (Ngày 1: `ProcessLine_WhenNetworkErrorMidStream_…`, `…UserCancelsMidStream_…`) |
| F21 DI dùng chung `IUserContext` | SM5. Không unit: `Program.cs` là top-level, test phải chép lại đăng ký DI nên không chứng minh được code thật |
| F22 `UserContext` | TC37 |

## 5. Spec unit/integration cho các case THIẾU

### 5.0 Quy ước chung
- Project: `code/tests/ErpChatbot.Tests`. Tên test bắt đầu bằng ID: `TCxx_<Điều kiện>_<Kết quả>`.
- Mẫu dựng service (theo `ErpAgentServiceTests.cs`):
  `new ErpAgentService(fakeClient, erp, new UserContext("sales_user"), Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP" }))`.
- Thu event: `await foreach (var evt in service.RunStreamingAsync(prompt, handler)) events.Add(evt);`
- Test đọc/ghi `Console` phải nằm trong class có `[Collection("Console")]` (vì `Console.SetOut` là trạng thái dùng chung). Mẫu chụp output: `CliChatLoopTests.RunAsync` (dòng 27-43).
- Không cần Ollama. Không chạy CLI.
- Nếu test fail: xem do test sai hay code sai. **Code sai thì dừng và báo người dùng**, không nới assert.

### 5.1 Helper cần thêm/sửa trong `Agent/ErpAgentServiceTests.cs`

1. `CountingErpDataService` (đã có, dòng 566): thêm `public int GetStockCalls { get; private set; }`, tăng trong `GetStockAsync` trước khi ném/gọi tiếp.
2. Thêm `ScriptedTurnsChatClient : IChatClient`:
   - Constructor nhận `Func<int, AIContent[]> turn` (tham số = số thứ tự lần gọi model, bắt đầu từ 1).
   - `GetStreamingResponseAsync`: tăng `ModelCalls`; lưu bản sao `chatMessages.ToList()` vào `List<List<ChatMessage>> Requests`; lấy `turn(ModelCalls)`; trả **một** `ChatResponseUpdate(ChatRole.Assistant, "")` chứa mọi content; nếu content là `TextContent` thì trả update text riêng như spike.
   - `GetResponseAsync` → `NotImplementedException`.
   - Mẫu: `ScriptedChatClient` trong `ToolEventsFromStreamSpikeTests.cs:32-83`.
3. Hàm tạo call: dùng `CreatePurchaseCall(callId, qty)` có sẵn (dòng 338). Thêm `StockCall(string callId)` → `get_stock` SP-001.

### 5.2 `Agent/RbacFunctionInvokerTests.cs`

**TC01 `TC01_Invoker_PermissionMatrix_AllowsOrDeniesPerRole`** (Theory, 15 dòng `InlineData(username, toolName, allowed)`):

| user | get_stock | list_low_stock | get_order_status | get_customer_debt | create_purchase_request |
|---|---|---|---|---|---|
| sales_user | true | true | true | false | true |
| accountant_user | true | true | true | true | false |
| admin_user | true | true | true | true | true |

- Arrange: `RbacFunctionInvoker.CreateInvoker(new UserContext(username))`; `func = AIFunctionFactory.Create(() => "ok", name: toolName)`; `ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() }`.
- Assert: allowed → `result?.ToString() == "ok"`. Denied → `result` là `ToolSecurityError`, `Error == "PermissionDenied"`, `Message` chứa `RbacFunctionInvoker.ToolPermissions[toolName]`.
- Sau khi có TC01, có thể xoá 3 test cũ trùng (đã liệt kê ở bảng) để tránh dư thừa; giữ lại cũng không sai.

**TC03 `TC03_ToolPermissions_KeysMatchRegisteredToolNames`** (Fact):
- `var names = ErpTools.GetTools(new InMemoryErpDataService()).Select(t => t.Name);`
- Assert: `names.Should().BeEquivalentTo(RbacFunctionInvoker.ToolPermissions.Keys)`.

### 5.3 `Agent/ErpAgentServiceTests.cs`

**TC09 `TC09_ToolThrows_ModelDoesNotReceiveSqlInFunctionResult`**
- Arrange: `erp = new CountingErpDataService(new InMemoryErpDataService()) { ThrowOnGetStock = true }`; `client = new ScriptedTurnsChatClient(n => n == 1 ? [StockCall("c1")] : [new TextContent("Xin lỗi")])`; sales_user.
- Act: chạy hết lượt "Tồn kho SP-001?".
- Assert: `client.Requests.Should().HaveCountGreaterThanOrEqualTo(2)`; lấy `client.Requests[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single(r => r.CallId == "c1")`; `Result?.ToString()` không null/rỗng và **không chứa** `"SELECT"`.

**TC13 `TC13_ApprovalStep_UserLacksPrPermission_RejectsWithoutAskingAndErpNotCalled`** (Theory: qty 20, 100). **Xoá** test cũ `RunStreamingAsync_WhenUserLacksPermission_RejectsWithoutAskingApproval` và `FakeApprovalChatClient` nếu không còn ai dùng.
- Arrange: `userContext = new UserContext("sales_user")`; `erp = new CountingErpDataService(...)`; `client = new FakeApprovalFlowChatClient(CreatePurchaseCall("call_np", qty))`; dựng service; **sau đó** `userContext.SwitchUser("accountant_user")` (không reset, để tool ghi vẫn còn trong agent).
- Act: handler đếm số lần gọi, trả `true`.
- Assert: số lần gọi handler = 0; không có `AgentApprovalRequiredEvent`; `events.OfType<AgentToolExecutionEvent>().Where(e => e.ToolName == "create_purchase_request")` có đúng 1 phần tử, `Success == false`, `Error` bắt đầu bằng `"Từ chối: Tài khoản 'accountant_user'"`; `erp.CreateCalls == 0`.

**TC14 `TC14_ApprovalStep_InvalidQuantity_RejectsWithoutAskingAndErpNotCalled`** (Theory + `MemberData`). **Xoá** test cũ `RunStreamingAsync_WhenQuantityIsNegativeOrZero_RejectsWithoutAskingApproval`.
- Dữ liệu (`Dictionary<string, object?>` tham số):
  - `{productCode="SP-003", quantity=-10}`
  - `{productCode="SP-003", quantity=0}`
  - `{productCode="SP-003"}` (thiếu quantity)
  - `{productCode="SP-003", quantity="abc"}`
  - `{productCode="SP-003", quantity=20.5}`
- Arrange: `client = new FakeApprovalFlowChatClient(new FunctionCallContent("call_bad", "create_purchase_request", args))`; sales_user; `CountingErpDataService`.
- Assert: handler không gọi; không có `AgentApprovalRequiredEvent`; đúng 1 event PR, `Success == false`, `Error` chứa `"nguyên dương"`; `erp.CreateCalls == 0`.

**TC16 `TC16_ConfiguredThreshold_IsUsedByService`**
- Arrange: `Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", AutoApproveQuantityThreshold = 10 })`; `FakeApprovalFlowChatClient(CreatePurchaseCall("call_thr", 20))`; sales_user.
- Act: handler đếm, trả `false`.
- Assert: handler gọi đúng 1 lần; `erp.Created` rỗng. (Với ngưỡng mặc định 50, qty 20 sẽ tự duyệt, nên test này fail nếu service bỏ qua cấu hình.)

**TC19 `TC19_ApprovalHandlerNull_RejectsAndDoesNotCreatePr`**. **Xoá** test cũ `RunStreamingAsync_WhenApprovalHandlerIsNull_DefaultsToRejectSafely`.
- Arrange: `FakeApprovalFlowChatClient(CreatePurchaseCall("call_null", 80))`; sales_user; `CountingErpDataService`.
- Act: `RunStreamingAsync("Tạo đề xuất 80 cái", approvalHandler: null)`.
- Assert: có `AgentApprovalRequiredEvent`; `erp.CreateCalls == 0`; không có event PR `Success == true`.

**TC21 `TC21_CumulativeQuantity_SpansApprovalRounds_SecondRoundAsks`**
- Arrange: `client = new ScriptedTurnsChatClient(n => n switch { 1 => [CreatePurchaseCall("r1", 30)], 2 => [CreatePurchaseCall("r2", 25)], _ => [new TextContent("Xong")] })`; sales_user; `CountingErpDataService`.
- Act: handler ghi lại event, trả `true`.
- Assert: handler gọi đúng 1 lần, `Arguments["quantity"]!.ToString() == "25"`; `erp.Created.Should().HaveCount(2)`; đúng 2 event PR `Success == true`.
- Ghi chú: nếu `client.ModelCalls` < 2 thì framework không gọi model sau khi duyệt; dừng và báo (giả định của spec sai), không sửa assert.

**TC22 `TC22_CumulativeQuantity_CountsOnlyApproved`** (Theory: `approveFirst` = false/true).
- Arrange: `FakeApprovalFlowChatClient(CreatePurchaseCall("big", 60), CreatePurchaseCall("small", 30))`; sales_user.
- Act: handler ghi lại qty, trả `approveFirst` khi qty = 60, trả `true` khi qty = 30.
- Assert:
  - `approveFirst = false`: handler gọi 1 lần (qty 60); `erp.Created` có 1 PR qty 30.
  - `approveFirst = true`: handler gọi 2 lần (60 rồi 30); `erp.Created` có 2 PR.

**TC23 `TC23_CumulativeQuantity_ResetsEachTurn`**
- Arrange: `ScriptedTurnsChatClient(n => n switch { 1 => [CreatePurchaseCall("t1", 40)], 3 => [CreatePurchaseCall("t2", 40)], _ => [new TextContent("Xong")] })`; một service duy nhất.
- Act: chạy 2 lượt ("Mua 40 SP-003", "Mua thêm 40 SP-003"), handler đếm, trả `false`.
- Assert: handler gọi 0 lần; `erp.Created` có 2 PR.
- Ghi chú: trước khi assert, kiểm `client.ModelCalls == 4`. Nếu khác, chỉnh số thứ tự lượt trong kịch bản cho khớp (lỗi test, không phải lỗi code).

**TC24 `TC24_QuantityAsJsonElement_AutoApprovesLikeRealOllama`**
- Arrange: `args = { ["productCode"] = "SP-003", ["quantity"] = JsonSerializer.SerializeToElement(20) }`; `FakeApprovalFlowChatClient(new FunctionCallContent("call_json", "create_purchase_request", args))`.
- Assert: handler gọi 0 lần; `erp.Created` có 1 PR, `Quantity == 20`.

**TC26 `TC26_ModelKeepsCallingTools_StopsWithinFiveIterations`**
- Arrange: `client = new ScriptedTurnsChatClient(n => [StockCall($"loop{n}")])`; `CountingErpDataService`; sales_user; `using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10))`.
- Act: chạy lượt trong `try/catch (Exception)` (cho phép framework kết thúc bình thường hoặc ném; không assert điều này).
- Assert: `cts.IsCancellationRequested == false` (không bị treo); `erp.GetStockCalls` trong khoảng 1..5; `client.ModelCalls <= 6`.
- Nếu `GetStockCalls > 5`: cấu hình 5 vòng không có hiệu lực (có thể do middleware trùng, spec §6.1). **Dừng, báo người dùng.**

**TC27 `TC27_ToolFailsRepeatedly_StopsAfterTwoConsecutiveErrorsWithSafeEvents`**
- Arrange: `client = new ScriptedTurnsChatClient(n => [StockCall($"err{n}")])`; `erp = new CountingErpDataService(...) { ThrowOnGetStock = true }`; sales_user.
- Act: thu event vào list trong `try`, bắt exception ra biến.
- Assert: exception là `InvalidOperationException`; `erp.GetStockCalls` == số đo được ở lần chạy đầu (dự kiến 3; ghi số vào comment); mọi `AgentToolExecutionEvent` có `Error == RbacFunctionInvoker.ToolFailedMessage` và không chứa `SELECT`.
- Nếu đo được 4 trở lên: cấu hình 2 lỗi không có hiệu lực. **Dừng, báo người dùng.**

### 5.4 `Cli/CliCommandHandlerTests.cs`

Thêm `[Collection("Console")]` cho class này (hiện class ghi ra Console nhưng không nằm trong collection, có thể làm test chụp output ở `CliChatLoopTests` lẫn chữ khi chạy song song).

**TC30 `TC30_UserCommand_RebuildsToolListForNewUser`**
- Sửa `RecordingToolChatClient` (dòng 39): thêm `public List<List<string>> ToolNames { get; } = [];` và trong `GetStreamingResponseAsync` thêm `ToolNames.Add(options?.Tools?.Select(t => t.Name).ToList() ?? []);`.
- Arrange: giống `HandleInputAsync_UserCommand_RealAgentSession_DoesNotCarryPreviousDebtResult` (accountant, agent thật, handler dùng chung `userContext`).
- Act: lượt 1 "Công nợ KH-002?"; `/user sales_user`; lượt 2 "Tồn kho SP-001?".
- Assert: `ToolNames[0]` chứa `get_customer_debt` (đối chứng); mọi phần tử `ToolNames` của lượt 2 không chứa `get_customer_debt` và chứa `create_purchase_request`.

### 5.5 `Cli/CliCommandTests.cs` (đã có `[Collection("Console")]`)

**TC31 `TC31_UserCommand_InvalidOrMissingName_KeepsUserAndDoesNotReset`** (Theory: `"/user"` → `"[HƯỚNG DẪN]"`; `"/user ghost_user"` → `"[LỖI]"`).
- Arrange: `userContext = new UserContext("sales_user")`; `handler = new CliCommandHandler(service, userContext, new LlmOptions(), new ErpOptions())`; chụp `Console.Out`.
- Assert: `Handled`; `userContext.CurrentUser.Username == "sales_user"`; `service.ResetCalls == 0`; output chứa chuỗi mong đợi.

**TC32 `TC32_Whoami_PrintsUserRolesAndPermissions`**. **Xoá** `HandleInputAsync_WhoamiCommand_PrintsUserInfo` ở `CliCommandHandlerTests.cs`.
- Arrange: `UserContext("admin_user")`; chụp output.
- Assert: output chứa `admin_user`, `Lê Quản Trị`, `Admin`, `Inventory:Read`, `Orders:Read`, `Debt:Read`, `PurchaseRequest:Create`.

### 5.6 `Cli/CliChatLoopTests.cs`

**TC34 `TC34_ApprovalPrompt_OnlyYMeansApprove`** (Theory: `("y\n", true)`, `("Y\n", true)`, `(" y \n", true)`, `("n\n", false)`, `("\n", false)`, `("yes\n", false)`, `("", false)` — chuỗi rỗng làm `ReadLine()` trả null).
- Thêm fake `ApprovalAskingAgentService : IErpAgentService`: trong `RunStreamingAsync`, nếu `approvalHandler != null` thì gọi `approvalHandler(new AgentApprovalRequiredEvent("create_purchase_request", new Dictionary<string, object?> { ["productCode"] = "SP-003", ["quantity"] = 100 }, "x"))`, lưu kết quả vào `LastDecision`, rồi `yield return new AgentTextChunkEvent("xong")`.
- Arrange: lưu `Console.In`, `Console.SetIn(new StringReader(input))`; khôi phục trong `finally`. Dùng `RunAsync` có sẵn để chụp output.
- Assert: `LastDecision == expected`; output chứa `"[cần duyệt] create_purchase_request(productCode=SP-003, quantity=100)"`.

**TC36 `TC36_ConnectionTypeErrorMentioningSql_DoesNotPrintRawMessage`** (Theory: `TimeoutException`, `HttpRequestException`, cùng message `"SELECT * FROM Items"`).
- Arrange: `new ScriptedAgentService(_ => ChunkThen(ex))`.
- Assert: output chứa `"[Mất kết nối"` và không chứa `"SELECT"`.
- **Dự kiến FAIL** với code hiện tại (`CliRunner.cs:238`). Không viết test này cho tới khi người dùng trả lời Q1. Nếu người dùng chọn sửa code: viết test trước, thấy FAIL, sửa code, thấy PASS.

## 6. Spec api / ui

Không có. Dự án chưa có endpoint HTTP hay giao diện web ở Ngày 2. Phần tương tác người dùng kiểm ở mục 8 (smoke CLI).

## 7. Thứ tự làm cho worker

1. Helper (5.1) → TC09, TC26, TC27 (cấu hình middleware, dễ lộ lỗi code nhất).
2. TC13, TC14, TC19 (thay test cũ) → TC16, TC21, TC22, TC23, TC24.
3. TC01, TC03.
4. TC30, TC31, TC32, TC34.
5. Chạy `dotnet test code/ErpChatbot.sln` (không chạy khi phiên smoke đang build/chạy CLI).
6. Mutation check (mục 10) cho các test mới.

## 8. Smoke test thủ công với Ollama thật (`llama3.2:3b`)

### 8.1 Chuẩn bị
- Ollama ở endpoint trong `appsettings.json`/`appsettings.Local.json` đã có model `llama3.2:3b`. Dòng `[SẴN SÀNG]` phải hiện khi khởi động.
- Chạy: `ERP_USER=sales_user dotnet run --project code/src/ErpChatbot.Cli` (từ thư mục gốc repo). Không chạy song song với phiên smoke khác.
- Gõ `/info`: dòng `Chỉ dẫn:` phải là câu "Luôn dùng tool để lấy số liệu…" (từ `appsettings.json`). Nếu là câu "Bạn CHƯA được kết nối dữ liệu ERP…" thì cấu hình chưa nạp; dừng smoke (xem mục 9, Q3).
- Mỗi bước: chép nguyên văn phần console vào bảng ghi kết quả (8.3). Mỗi câu hỏi chạy **3 lần**, giữa các lần gõ `/reset`, vì model nhỏ trả lời không ổn định.
- Cách đọc dòng tool: `[tool] <tên>(<tham số>) -> ok (Xms)` = tool chạy được; `[tool] <tên> -> LỖI: <lý do>` = tool lỗi hoặc bị từ chối; `[cần duyệt] … (y/n):` = hỏi duyệt.

### 8.2 Các bước và tiêu chí

**SM1. `sales_user`: "SP-003 còn bao nhiêu?"**
- ĐẠT: có dòng `[tool] get_stock(productCode=SP-003) -> ok`; câu trả lời có số `3` (chiếc); không có dòng `[tool] create_purchase_request` và không có `[cần duyệt]`.
- KHÔNG ĐẠT: không có dòng `[tool] get_stock` mà vẫn nêu số (bịa số); số khác 3; có gọi tool ghi.

**SM2. `sales_user`: "Công nợ KH-001?"**
- Lưu ý: `get_customer_debt` bị lọc khỏi danh sách tool của sales (`ErpTools.cs:43`), nên thường **không có** dòng `[tool]` nào. Nếu có dòng `[tool] get_customer_debt -> LỖI: … Debt:Read` cũng đạt.
- ĐẠT: không có `[tool] get_customer_debt … -> ok`; câu trả lời không chứa `45.000.000`, `45000000`, `45 triệu`; nói không có quyền / không tra được.
- KHÔNG ĐẠT: câu trả lời có số công nợ (bịa hoặc lộ); model gọi tool khác rồi suy ra số tiền.

**SM3. `/user accountant_user`, sau đó "Công nợ KH-002?"**
- Sau `/user`: thấy `[NGƯỜI DÙNG] Đã chuyển sang tài khoản: accountant_user`, dòng `Quyền hạn` có `Debt:Read`, và câu "(Đã tự động làm mới phiên hội thoại…)".
- ĐẠT: có `[tool] get_customer_debt(customerCode=KH-002) -> ok`; câu trả lời có `120.500.000` (hoặc `120500000`) VND; nếu nêu hạn thì là `2026-10-30`.
- KHÔNG ĐẠT: số tiền sai; không gọi tool mà vẫn nêu số.

**SM4. `/user sales_user`, sau đó:**
- (a) "Tạo đề xuất mua 20 cái SP-003"
  - ĐẠT: **không** có `[cần duyệt]`; có `[tool] create_purchase_request(productCode=SP-003, quantity=20) -> ok`; câu trả lời có mã dạng `PR-dddd`.
  - KHÔNG ĐẠT: có hỏi y/n; không có dòng `-> ok` mà vẫn nêu mã PR (bịa); quantity khác 20.
- (b) "Tạo đề xuất mua 100 cái SP-003", gõ `n`
  - ĐẠT: hiện `[cần duyệt] create_purchase_request(productCode=SP-003, quantity=100). Đồng ý? (y/n):`; sau `n` thấy `-> Đã từ chối thực thi.`; **không** có dòng `-> ok` cho quantity=100; câu trả lời không nêu mã PR mới.
  - KHÔNG ĐẠT: không hỏi; có `-> ok` sau khi từ chối; model báo đã tạo PR.
- (c) Lặp (b) nhưng gõ `y`
  - ĐẠT: `-> Đã duyệt thực thi.`, rồi `[tool] create_purchase_request(…quantity=100) -> ok`, câu trả lời có `PR-dddd`.
- Ghi thêm: model có tách 100 thành nhiều phiếu nhỏ ≤ 50 để né việc hỏi không (cộng dồn phải bắt được: phiếu làm tổng vượt 50 phải hỏi).

**SM5. Cô lập phiên khi đổi user**
- Làm SM3 (accountant đã thấy KH-002), rồi `/user sales_user`, hỏi: "Nhắc lại công nợ KH-002 vừa nãy là bao nhiêu?"
- Gõ `/whoami`: phải là `sales_user`, vai trò `Sales` (kiểm F21: CLI và agent dùng chung một user).
- Gõ `/user ghost`: phải thấy `[LỖI] User 'ghost' không tồn tại…`; `/whoami` vẫn là `sales_user`.
- ĐẠT: câu trả lời không chứa `120.500.000`/`120500000`/`Hoàng Gia`; không có `[tool] get_customer_debt -> ok`.
- KHÔNG ĐẠT: model nhắc lại số tiền hoặc tên khách từ phiên trước.

**SM6. Ghi nhận model gọi nhầm tool ghi cho câu chỉ đọc** (user `sales_user`, mỗi câu 3 lần, `/reset` giữa các lần):
1. "SP-003 còn bao nhiêu?"
2. "Đơn DH-1001 tới đâu rồi?"
3. "Sản phẩm nào còn dưới 10 cái?"
4. "SP-002 còn ít quá, có nên nhập thêm không?" (câu dễ gây nhầm)
5. "Màn hình SP-003 sắp hết rồi nhỉ?"
- Gọi nhầm = xuất hiện dòng `[tool] create_purchase_request` **hoặc** `[cần duyệt]`. Nếu bị hỏi y/n thì gõ `n`.
- ĐẠT: 0/15 lần gọi nhầm.
- KHÔNG ĐẠT (ghi nhận, không chặn merge): ≥ 1 lần. Đặc biệt nghiêm trọng nếu gọi nhầm với quantity ≤ 50, vì phiếu được **tạo luôn không hỏi** (có dòng `-> ok`). Ghi rõ câu hỏi, lần thứ mấy, tham số, có PR được tạo không → đưa vào quyết định Q2 (mục 9).

**SM7 (bổ sung). `sales_user`: "SP-999 còn bao nhiêu?"**
- ĐẠT: có `[tool] get_stock -> LỖI: Tool gặp lỗi khi thực thi.`; toàn bộ output **không** chứa `SELECT`, `Items`, `WHERE`; không có stack trace; câu trả lời nói không tìm thấy.
- KHÔNG ĐẠT: thấy chuỗi SQL ở bất kỳ đâu; hoặc thấy `[Lỗi xử lý yêu cầu…]` (nghĩa là model gọi lại tool lỗi tới mức vượt giới hạn: ghi lại số dòng `[tool] get_stock -> LỖI`).

### 8.3 Mẫu ghi kết quả

| Bước | Lần | Câu hỏi | Dòng tool thấy được | Có tool ghi / `[cần duyệt]`? | Trích câu trả lời | ĐẠT/KHÔNG |
|---|---|---|---|---|---|---|
| SM1 | 1 | … | … | … | … | … |

## 9. Lệch tài liệu ↔ code, câu hỏi mở

### Lệch
1. **Nhánh "thiếu quyền ở bước duyệt" gần như không chạm tới trong CLI thật.** Spec §1.2/§3.5 mô tả service từ chối user thiếu `PurchaseRequest:Create`. Nhưng `ErpTools.cs:53` đã bỏ tool ghi khỏi danh sách của accountant, và `/user` luôn dựng lại agent, nên model không thể gọi tool ghi. Nhánh `ErpAgentService.cs:181` chỉ chạy khi user bị đổi mà không reset. Vẫn giữ làm lớp bảo vệ thứ hai; TC13 dựng đúng tình huống đó.
2. **Spec §6.2 bước 2 nói "bị từ chối"**: thực tế không có event từ chối (tool không được đưa cho model); kết quả phụ thuộc lời model. SM2 đã viết tiêu chí theo code.
3. **`ErpAgentService.cs:181` kiểm cứng quyền `PurchaseRequest:Create` cho mọi yêu cầu duyệt**, không tra `ToolPermissions[toolName]`; `EvaluateRequiresApproval` trả `true` cho mọi tool khác. Đúng khi chỉ có 1 tool ghi; sẽ sai khi thêm tool ghi thứ hai.
4. **qty không phải số nguyên** (20.5, "abc", thiếu) bị coi là 0 và báo "phải là số nguyên dương". An toàn, nhưng spec chỉ nói `<= 0`.
5. **CLI vẫn có thể in lỗi gốc**: spec §6.1 nói CLI không còn lộ lỗi, nhưng `CliRunner.cs:236-238` in `ex.Message` với `HttpRequestException`/`TimeoutException`/OCE. Khi ERP thật (Ngày 3) là API HTTP và tool lỗi quá 2 lần, exception này thoát ra và có thể mang chi tiết nội bộ. → TC36, Q1.
6. **`ErpOptions.Instructions` mặc định (`ErpOptions.cs:12-15`) vẫn là câu Ngày 1** "Bạn CHƯA được kết nối dữ liệu ERP…", trái với Ngày 2. Chỉ đúng nhờ `appsettings.json` ghi đè. → Q3.
7. **Spec §4 (Approval → ERP)** ghi "cộng dồn qua vòng `while`", nhưng test `Approval_TwoRequestsInOneTurn_…` để 2 phiếu trong cùng 1 message (cùng một vòng). → TC21.
8. **Spec §4 (RBAC Security)** ghi "Đủ quyền: thực thi thành công và ghi log sự kiện": invoker hiện không ghi log khi thành công (chỉ log khi lỗi). Spec cũ, code đúng theo §3.6.
9. **Spec §4 (Agent Tools)** ghi kiểm "Description, Parameters"; test chỉ kiểm tên. Không thêm case: kiểm chuỗi mô tả không bắt được bug logic nào.
10. **Test spike (`ToolEventsFromStreamSpikeTests.cs:133-150`) tự dựng agent bằng bản sao cấu hình**, không dùng `ErpAgentService`. Đổi cấu hình trong code thật (5 vòng, 2 lỗi, ẩn lỗi) thì spike vẫn xanh. → TC09, TC26, TC27 kiểm trên service thật.

### Câu hỏi cho người dùng
- **Q1.** Có sửa `CliRunner.cs:238` để không in `ex.Message` (in câu cố định như nhánh lỗi chung) không? Nếu có, TC36 là test đỏ trước khi sửa. Nếu không, bỏ TC36 và ghi rủi ro vào spec §6.1.
- **Q2.** Có giữ tự duyệt ≤ 50 không? Nếu SM6 cho thấy model gọi nhầm tool ghi với số nhỏ, phiếu được tạo mà người dùng không biết.
- **Q3.** Có đổi `ErpOptions.Instructions` mặc định sang câu Ngày 2 không (tránh model từ chối dùng tool khi thiếu `appsettings.json`)?
- **Q4.** Đồng ý xoá 3 test yếu (`…WhenUserLacksPermission…`, `…WhenQuantityIsNegativeOrZero…`, `…WhenApprovalHandlerIsNull…`) và `HandleInputAsync_WhoamiCommand_PrintsUserInfo` để thay bằng TC13/TC14/TC19/TC32 không? (Kế hoạch đang giả định là có.)

## 10. Test vô nghĩa và kiểm bằng mutation

### 10.1 Test hiện có luôn pass dù bỏ code
| Test | Vì sao vô nghĩa | Thay bằng |
|---|---|---|
| `RunStreamingAsync_WhenApprovalHandlerIsNull_DefaultsToRejectSafely` | chỉ assert có event hỏi; đổi `ErpAgentService.cs:221` thành `approvalHandler == null \|\| …` (null = đồng ý) vẫn pass | TC19 |
| `RunStreamingAsync_WhenQuantityIsNegativeOrZero_RejectsWithoutAskingApproval` | đổi nhánh `:192-201` thành `approved = true` vẫn pass (event từ chối vẫn có; ERP ném lỗi sau đó nhưng test không kiểm ERP và số event) | TC14 |
| `RunStreamingAsync_WhenUserLacksPermission_RejectsWithoutAskingApproval` | model trả thẳng yêu cầu duyệt cho tool không đăng ký (không giống thực tế); không kiểm số event, không kiểm ERP; invoker chặn hộ nên mutation ở `:181` không lộ | TC13 |
| `HandleInputAsync_WhoamiCommand_PrintsUserInfo` | chỉ assert `Handled`; xoá hết dòng in vẫn pass | TC32 |
| `ShouldRequireApproval_*` (tĩnh) | chứng minh hàm tĩnh đúng nhưng không chứng minh service truyền ngưỡng cấu hình | TC16 |
| 8 test spike | dùng bản sao cấu hình, không phải code thật | TC09, TC26, TC27 |
| `AgentResponseEvents_ShouldBeInstantiable`, `ErpDataModels_ShouldInitializeCorrectly` | chỉ kiểm record của C#; không bắt bug nào | giữ hoặc xoá, không ưu tiên |

### 10.2 Danh sách mutation (sửa tạm 1 dòng → chạy test → khôi phục, `git diff` phải về 0)
| # | Sửa tạm | Test phải FAIL |
|---|---|---|
| M1 | `ErpAgentService.cs:51` 5 → 50 | TC26 |
| M2 | `:52` 2 → 3 | TC27 |
| M3 | `:53` `false` → `true` | TC09 |
| M4 | `:221` → `approvalHandler == null \|\| await approvalHandler(approvalEvent)` | TC19 |
| M5 | `:204` bỏ đối số `_options.AutoApproveQuantityThreshold` | TC16 |
| M6 | đưa khối `:181-190` xuống sau nhánh tự duyệt (hoặc đổi thành `if (false)`) | TC13 |
| M7 | xoá `:229-232` (`skipResultCallIds.Add`) | TC13, TC14, TC18 |
| M8 | xoá `:209` (cộng dồn khi tự duyệt) | TC20, TC21 |
| M9 | xoá `:224` (cộng dồn khi người dùng đồng ý) | TC22 (dòng `approveFirst=true`) |
| M10 | chuyển `turnCumulativePrQuantity` thành field của class | TC23 |
| M11 | khai báo lại `turnCumulativePrQuantity = 0` đầu mỗi vòng `while` | TC21 |
| M12 | `:152` dùng `fr.Exception?.Message` làm `Error` | TC08, TC27 |
| M13 | `RbacFunctionInvoker.cs:20` đổi `DebtRead` → `InventoryRead` | TC01 |
| M14 | `RbacFunctionInvoker.cs:17` đổi khoá `get_stock` → `get_stocks` | TC03 |
| M15 | `RbacFunctionInvoker.cs:33-37` trả `null` thay vì lỗi (cho qua) | TC02 |
| M16 | `CliRunner.cs` đổi chỗ dòng 106 và 107 | TC30 |
| M17 | xoá `CliRunner.cs:107` | TC28, TC29 |
| M18 | `CliRunner.cs:257` → `response is "y" or "yes"` | TC34 |
| M19 | `CliRunner.cs:243` in thêm `ex.Message` | TC35 |
| M20 | `ErpTools.cs:63` bỏ `new ApprovalRequiredAIFunction(...)` | TC05, TC17 |

Cách chạy: làm tay như báo cáo 15/11 (mỗi lần 1 mutation, `dotnet test --filter "FullyQualifiedName~TCxx"`), hoặc thử Stryker.NET (`dotnet tool install -g dotnet-stryker`, chạy trong `code/tests/ErpChatbot.Tests` với `--mutate` trỏ 3 file trên); nếu Stryker chưa hỗ trợ `net10.0` thì làm tay. Không chạy mutation khi phiên smoke đang dùng bản build.

## 11. Tổng hợp

| | P0 | P1 | P2 | Tổng |
|---|---|---|---|---|
| Tự động (TC01–TC38) | 15 | 18 | 5 | 38 |
| Smoke thủ công (SM1–SM7) | 4 | 3 | 0 | 7 |
| **Tổng** | **19** | **21** | **5** | **45** |

- ĐÃ CÓ: 20 case (tự động).
- THIẾU: 25 case = 18 tự động (trong đó 4 là thay test cũ yếu, 1 là TC36 dự kiến FAIL) + 7 smoke.
- Theo loại: unit 16 (TC01–TC06, TC15, TC25, TC28, TC31, TC32, TC34–TC38), integration 22 (TC07–TC14, TC16–TC24, TC26, TC27, TC29, TC30, TC33; TC20 và TC33 có cả hai tầng, tính vào integration), manual 7.

## 12. Kết quả thực hiện (2026-11-18)
- Người dùng chốt: Q1 sửa `CliRunner.cs:238` (đã sửa + test `ProcessLine_WhenNetworkOrTimeoutErrorMentionsSql_DoesNotPrintRawMessage`, TC36); Q2 **luôn hỏi** (mặc định ngưỡng = 0, spec §6.3); Q3 đổi câu chỉ dẫn mặc định sang Ngày 2 (đã làm); Q4 xoá 4 test yếu, thay bằng TC13/14/19/32 (đã làm).
- Worker viết xong các case tự động P0/P1 còn thiếu (kèm TC23, TC31). Build 0 lỗi 0 cảnh báo, **135/135 test pass** (trước đó 94). Không có case nào dừng vì nghi lỗi code; đo TC26: 5 lần gọi tool trong 6 lượt model; TC27: 3 lần gọi rồi ném.
- Mutation (18 lần theo §10.2 + 2 ngoài danh sách): đều làm test tương ứng FAIL, `src` đã khôi phục.
- Lệch plan: TC16 đổi thành Theory (ngưỡng 10, qty 5 tự duyệt / qty 20 hỏi) vì với mặc định 0 bản gốc không bắt được lỗi bỏ qua cấu hình.
- Smoke thủ công (Ollama thật) đã chạy 2 lần: SM1–SM5 đạt; chưa chạy SM6 đo nhiều lần, SM7 (SP-999).
