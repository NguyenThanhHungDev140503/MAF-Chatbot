# Báo cáo công việc: triển khai các thay đổi của spec Ngày 2

Ngày: 2026-11-15 | Nhánh: `feat/day-02-function-calling` | Trạng thái: **chưa commit**. Phần lõi (event tool từ stream) đã làm; còn 2 finding về lộ lỗi ở CLI chưa sửa.
Tài liệu gốc: `docs/superpowers/specs/2026-10-28-day-02-function-calling-design.md` (§1.2, §3.6, §4, §6.1). Sổ theo dõi từng bước: `docs/superpowers/plans/2026-11-15-day-02-spec-impl-run.md`.

---

## A. Task Summary

1. **Phân tích** mức hoàn thành Ngày 2 so với `tutorial/ngay-02/giai-thich-ngay-02-function-calling.md`: code đã có 5 tool, RBAC, duyệt có điều kiện, stream event; build và 64 test pass; chưa có bằng chứng chạy với Ollama thật.
2. **Cập nhật spec** (chưa commit từ trước + phần làm trong phiên này): thêm phân tích hàng đợi event (§3.6 mục 4), hai dòng test ở §4, việc phải làm trước Ngày 4 ở §6.1, rút gọn câu trùng ở §1.2. Một agent `researcher` đối chiếu thiết kế với source `dotnet/extensions`.
3. **Triển khai** bằng đội agent (`/agent-team`, playbook feature): các mục spec mà code chưa có.
4. **Phần lõi cho Ngày 4** (bạn chốt làm luôn): bỏ `ConcurrentQueue`; service tạo event tool từ chính stream của agent. Trước đó chạy hai spike để xác minh hành vi `ChatClientAgent` 1.25.0.
5. **Kết quả:** build 0 lỗi / 0 cảnh báo; test **89 pass, 0 skipped, 0 fail** (trước đó 64 pass).

## B. Code Implementation Details

### B.1 Ngưỡng tự duyệt cấu hình được
- `code/src/ErpChatbot.Core/Models/ErpOptions.cs:6-9`
  ```csharp
  public const int DefaultAutoApproveQuantityThreshold = 50;
  // Số lượng tối đa tự duyệt không hỏi y/n; 0 = mọi phiếu đều phải hỏi.
  public int AutoApproveQuantityThreshold { get; set; } = DefaultAutoApproveQuantityThreshold;
  ```
  Thay số `50` viết cứng. Có `const` mặc định để hàm static `EvaluateRequiresApproval` dùng làm giá trị mặc định mà không lặp số 50.
- `code/src/ErpChatbot.Cli/appsettings.json:14`: thêm `"AutoApproveQuantityThreshold": 50` trong mục `Erp` để giá trị nhìn thấy được khi cấu hình.
- `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs:68, 203`: `EvaluateRequiresApproval(..., int threshold = ErpOptions.DefaultAutoApproveQuantityThreshold)` so sánh `> threshold`; chỗ gọi truyền `_options.AutoApproveQuantityThreshold`. Tham số thêm ở cuối và có mặc định nên các test cũ vẫn biên dịch.

### B.2 Che lỗi nội bộ khỏi event
- `code/src/ErpChatbot.Agent/Security/RbacFunctionInvoker.cs:15, 29, 65-66`
  ```csharp
  public const string ToolFailedMessage = "Tool gặp lỗi khi thực thi.";
  ...
  logger?.LogError(ex, "Tool {Tool} lỗi khi thực thi", toolName);
  onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, false, ToolFailedMessage));
  throw;
  ```
  Event chỉ mang thông điệp cố định; exception đầy đủ đi vào log. `throw;` được giữ để `FunctionInvokingChatClient` vẫn đếm lỗi liên tiếp.
- `ErpAgentService.cs:34, 40, 48`: constructor nhận `ILogger<ErpAgentService>? logger = null` (tuỳ chọn, test cũ 4 tham số không phải đổi) và truyền vào `CreateInvoker`.

### B.3 Phần lõi: event tool tạo từ stream, bỏ hàng đợi
Bước trung gian (xoá hàng đợi đầu mỗi lượt + `TODO(Ngày 4)`) đã được thay bằng phần lõi sau khi bạn chốt làm luôn.
- `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs` (`RunStreamingAsync`, khoảng dòng 143-153): với mỗi `FunctionCallContent` ghi `(tên, tham số, thời điểm)` theo `CallId`; khi thấy `FunctionResultContent` thì `yield return AgentToolExecutionEvent` ngay, trước text kế tiếp.
  - `Success = Exception == null && Result is not ToolSecurityError`.
  - `Error` chỉ là `ToolSecurityError.Message`, `RbacFunctionInvoker.ToolFailedMessage` hoặc null. Không bao giờ lấy `Exception.Message` (spike xác nhận nó chứa SQL).
  - Phiếu bị từ chối ở bước duyệt: framework vẫn phát kết quả "rejected by the approver"; service nhớ `CallId` (`skipResultCallIds`) để không báo nhầm thành công. Có đúng một điểm hội tụ cho mọi đường `approved == false` (thiếu quyền, số lượng <= 0, người dùng từ chối, handler null).
  - Nhánh tự duyệt không còn event "ok" giả (sửa finding 3 của reviewer).
- `code/src/ErpChatbot.Agent/Security/RbacFunctionInvoker.cs`: `CreateInvoker(IUserContext, ILogger?)`; bỏ callback `onExecuted`, Stopwatch và việc phát event. Chỉ còn Fail-Secure, kiểm quyền, ghi log và `throw;`.
- Không còn `_eventQueue` / `ConcurrentQueue` / `onExecuted` trong `code/src` (đã grep).
- Lý do thiết kế: bỏ state dùng chung giữa invoker và service (nguồn của lỗi event cũ lọt sang lượt sau), tạo sự kiện từ nguồn duy nhất, và Ngày 4 chỉ cần nối Web vào luồng event có sẵn.
- `ElapsedMs` do service đo từ lúc thấy call đến lúc thấy result (xấp xỉ). Kết quả thành công trong stream là `JsonElement`.

### B.4 Test thêm (xem ghi chú dưới bảng)
| Test | Vị trí | Kiểm gì |
|---|---|---|
| `ErpOptions_ShouldHaveDefaultAutoApproveQuantityThreshold` | `CoreModelsTests.cs:32` | mặc định = 50 |
| `Invoker_WhenToolThrows_RethrowsOriginalAndLogsItAtError` | `RbacFunctionInvokerTests.cs` | logger nhận đúng exception ở mức Error, invoker ném lại exception gốc |
| `Invoker_WhenToolThrowsAndNoLogger_RethrowsOriginalException` | `:96` | không logger vẫn không `NullReferenceException` |
| `ShouldRequireApproval_UsesConfiguredThreshold` (2 ca), `..._ThresholdZero_...` | `ConditionalApprovalTests.cs:25, 34` | ngưỡng tuỳ chỉnh 10 và 0 |
| `RunStreamingAsync_ToolExecutionEvent_ComesBeforeFollowingTextAndTurnCompleted` | `ErpAgentServiceTests.cs:176` | thứ tự event tool → text → hoàn tất lượt |
| `RunStreamingAsync_AfterCancelledTurn_DoesNotReplayStaleToolEvents` | `:201` | huỷ khi tool đang chạy → lượt sau không phát lại event cũ |
| `ToolEvent_WhenToolThrowsWithoutApproval_EmitsSafeErrorEvent` | `ErpAgentServiceTests.cs` | tool lỗi không qua duyệt: `Error == ToolFailedMessage`, không chứa `SELECT` |
| `ToolEvent_WhenUserRejectsApproval_EmitsNoToolExecutionEvent` | `ErpAgentServiceTests.cs` | từ chối → không có event thực thi (không báo nhầm thành công) |
| `ToolEvent_WhenInvokerDenies_EmitsFailedEventWithPermissionError` | `ErpAgentServiceTests.cs` | invoker từ chối quyền → `Success=false`, `Error` chứa tên quyền |
| `ToolEvent_TwoCallsInOneUpdate_EmitsTwoEventsInOrderWithMatchingArgs` | `ErpAgentServiceTests.cs` | hai call một update → hai event đúng thứ tự và tham số |
| 8 test `Case1`..`Case8` | `ToolEventsFromStreamSpikeTests.cs` | ghi lại hành vi `ChatClientAgent` 1.25.0 (call/result, Exception, ToolSecurityError, duyệt, từ chối, nhiều call) |
| `Approval_AutoApprovedBelowThreshold_CreatesPurchaseRequestInErp` | `:354` | ≤ ngưỡng: không hỏi, PR-xxxx tạo thật |
| `Approval_UserRejects_DoesNotCreatePurchaseRequest` | `:382` | gõ `n`: ERP không tạo PR |
| `Approval_TwoRequestsInOneTurn_SecondExceedsCumulativeThreshold_AsksUser` | `:403` | 30 rồi 25: chỉ yêu cầu thứ hai bị hỏi |
| `Approval_WhenToolThrows_EmitsSafeErrorEvent` | `:425` | tool lỗi: event an toàn, không rò `SELECT` |
| `HandleInputAsync_UserCommand_RealAgentSession_DoesNotCarryPreviousDebtResult` | `CliCommandHandlerTests.cs:140` | `/user` đổi role → session thật không còn kết quả công nợ |

Ghi chú: stub Skip "Ngày 4" đã được thay bằng các test thật ở trên. Test huỷ-khi-tool-đang-chạy vẫn giữ làm hồi quy. Test auto-duyệt nay assert đúng MỘT event thành công.

Kỹ thuật: Fake `IChatClient` trả `FunctionCallContent`, decorator `IErpDataService` (`CountingErpDataService`, `CancellingErpDataService`) quanh `InMemoryErpDataService` thật để quan sát việc ghi PR và giả lập huỷ.

## C. Testing

- **Loại:** unit test + test tích hợp trong tiến trình (service thật + fake `IChatClient`), không cần Ollama.
- **Kết quả cuối** (verifier chạy độc lập sau phần lõi): build 0 lỗi / 0 cảnh báo; `Total 89, Passed 89, Skipped 0, Failed 0`. Verifier blind 8/8 tiêu chí PASS (kể cả: RBAC vẫn chạy trước khi hỏi duyệt, mọi đường từ chối đều đi qua `skipResultCallIds.Add`).
- **Kiểm chứng test có ý nghĩa (mutation):**
  - Test huỷ giữa lượt: lần đầu **vẫn PASS khi bỏ `Clear()`** (vô nghĩa, vì huỷ giữa hai lần gọi model thì hàng đợi đã được xả). Đã sửa kịch bản thành "huỷ khi tool đang chạy"; sau đó FAIL khi bỏ `Clear()`, PASS khi có.
  - Test cô lập phiên: tạm bỏ `ResetSessionAsync()` ở `CliRunner.cs:107` → test FAIL; đã khôi phục (`git diff` = 0).
- **Mutation phần lõi:** đổi `Error` sang `Exception.Message` → 2 test lộ SQL fail; bỏ `skipResultCallIds.Add` → test từ chối fail; đều đã khôi phục (diff khớp).
- **Phân loại lỗi test:** 1 lỗi do **test sai** (`NullReferenceException` ở assert `Error!.Contains` trên event thành công); đã sửa test, không nới điều kiện. Không có lỗi do code sai trong lúc viết test.
- **Verifier blind:** 7/8 tiêu chí PASS. Tiêu chí 7 (không có file lạ trong `git status`) FAIL một phần vì tôi soạn thiếu: các file untracked có từ trước phiên này, không phải lỗi code.
- **Reviewer (opus):** verdict **Needs changes**; chạy thử bằng chương trình nhỏ trong scratchpad và tái hiện được rò rỉ (xem D).

## D. Challenges and Solutions

| Vấn đề | Phân tích | Xử lý |
|---|---|---|
| Test huỷ giữa lượt pass dù bỏ `Clear()` | Worker chỉ ra hàng đợi đã được xả ngay khi update kết quả tool tới, nên không còn event kẹt | Đổi kịch bản: huỷ ngay trong `GetStockAsync` để invoker đẩy event lỗi rồi ném tiếp; resume worker; xác nhận bằng mutation |
| Chưa chắc `ChatClientAgent` 1.25 chạy tool sau khi nhận phản hồi duyệt | Spec §6.1 ghi "chưa xác minh" | 4 test Approval → ERP pass với fake client: middleware đổi call thành yêu cầu duyệt và chạy tool qua invoker |
| `PurchaseRequestResult` dùng `RequestId`, spec ghi `Id` | Lệch tên trong spec | Test dùng `RequestId` |
| Brief tự mâu thuẫn: đổi chữ ký `CreateInvoker` nhưng cấm sửa file spike đang gọi chữ ký cũ | Worker (sonnet) dừng và báo thay vì đoán | Cho phép sửa đúng một dòng trong file spike |
| Không chắc stream có đủ call/result sau khi duyệt hoặc từ chối | Spec ghi "chưa xác minh" | Hai spike (8 ca): có đủ; từ chối vẫn sinh kết quả "rejected" nên cần `skipResultCallIds` |
| Hai worker song song có thể đụng nhau | Bước 1 và 2 sửa file rời nhau | Chạy song song bước 1+2, rồi 4a+5; 4a→4b tuần tự vì cùng file |

## E. Improvements and Optimizations

- **Bảo mật:** event không còn mang `ex.Message`; log đầy đủ chỉ ở logger. Ngưỡng tự duyệt đổi được mà không cần build lại; đặt `0` để mọi phiếu đều phải hỏi.
- **Đúng đắn:** xoá hàng đợi đầu mỗi lượt chặn event cũ lọt sang lượt sau.
- **Kiến trúc:** bỏ state dùng chung (`_eventQueue`) giữa invoker và service; event có một nguồn duy nhất là stream, sẵn để Ngày 4 nối Web vào. `.planning/STATE.md` còn ghi riêng việc thiết kế lại `approvalHandler` cho nhiều request HTTP.
- **Chưa làm (để quyết định):** các finding bên dưới.

### Finding còn mở từ reviewer
- **Đã xử lý bởi phần lõi:** finding 3 (event "ok" giả ở nhánh tự duyệt) và spec dòng 37/306 (đã viết lại).
1. **Đã sửa (16/11):** tool lỗi lặp lại 3 lần liên tiếp (vượt `MaximumConsecutiveErrorsPerRequest = 2`) thì exception gốc thoát ra, `CliRunner.cs:242` in `ex.Message` (có SQL). Spec §6.1 đã ghi. → nay in câu cố định, có test.
2. **Đã sửa (16/11):** `Host.CreateApplicationBuilder` bật sẵn console logger nên `LogError(ex, ...)` in stack trace ra cửa sổ chat. Cần `ClearProviders()` hoặc lọc category. → nay `ClearProviders()` ở `Program.cs`; chưa còn log nào được lưu.
3. Nhỏ: Ctrl+C lúc tool chạy vẫn bị ghi `LogError` (nên `catch (OperationCanceledException) when (ct.IsCancellationRequested)` riêng).
4. Nhỏ: chưa test nào kiểm ngưỡng cấu hình đi qua service (bỏ đối số ngưỡng thì mọi test vẫn xanh).

## F. Tools and Technologies Used

- **Development:** C# / .NET 10, `Microsoft.Extensions.AI` 10.11.0, `Microsoft.Agents.AI` 1.25.0, `Microsoft.Extensions.Logging`.
- **Testing:** xUnit, FluentAssertions, Fake `IChatClient`, decorator `IErpDataService`; mutation check thủ công (comment tạm một dòng rồi khôi phục).
- **Điều phối:** Claude Code `/agent-team` (playbook feature): 9 lần `worker` haiku (gồm 1 lần resume và 2 spike), 2 lần `worker` sonnet (phần lõi, gồm 1 lần resume; chọn sonnet ngay từ đầu vì logic iterator trên đường bảo mật), 2 `verifier` (sonnet), 1 `reviewer` (opus); trước đó 1 `researcher` (opus). Tổng 14 lần gọi trong đội, sát ngân sách 15.
- **Deployment / Monitoring:** không áp dụng (chưa triển khai; chưa chạy Ollama thật).
- **Code analysis:** `dotnet build`, `git diff`, `grep`.

## Những gì chưa xác minh
- Chưa chạy smoke test 6 bước (spec §6.2) với Ollama `llama3.2:3b` thật.
- Chưa biết model thật có gọi tool lỗi 3 lần liên tiếp hay không (đường rò rỉ ở finding 1 chỉ chứng minh bằng client giả).
- Hai spike dùng fake client: chunking thật của Ollama có thể khác; chưa kiểm nhiều vòng duyệt liên tiếp.
- Nghiên cứu về `FunctionInvokingChatClient` dựa trên nhánh `main` của `dotnet/extensions`; riêng việc `ChatClientAgent` 1.25.0 chuyển `FunctionCallContent`/`FunctionResultContent` vào stream đã được kiểm bằng spike (8 test).
- Mutation check hiện làm tay; chưa có trong CI.
