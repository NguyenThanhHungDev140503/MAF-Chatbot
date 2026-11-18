# Day 2 spec implementation — run log
Playbook: feature | Tài liệu: docs/superpowers/specs/2026-10-28-day-02-function-calling-design.md (§1.2, §3.6, §4, §6.1) | Ngân sách: 15 lần gọi

## Design (main viết)
Nhóm A (code): 
1. Core: `ErpOptions.AutoApproveQuantityThreshold` (mặc định 50) + `appsettings.json` + test mặc định.
2. `RbacFunctionInvoker`: `ToolFailedMessage`, tham số `ILogger?`, che `ex.Message` khỏi event + test leak.
3. `ErpAgentService`: `ILogger<>` tuỳ chọn, dùng ngưỡng cấu hình, `_eventQueue.Clear()` đầu lượt, comment TODO Ngày 4 + test ngưỡng tuỳ chỉnh.
Nhóm B (test):
4a. `ErpAgentServiceTests`: thứ tự event tool; huỷ giữa lượt không lọt event sang lượt sau; test Skip Ngày 4.
4b. `ErpAgentServiceTests`: Approval → ERP (auto-duyệt tạo PR, `n` không tạo, cộng dồn 2 yêu cầu, tool ném exception).
5. `CliCommandHandlerTests`: `/user` đổi role → session thật không còn kết quả công nợ.
Main tự làm cuối: dòng Deferred Items trong `.planning/STATE.md`.

| # | Bước | Agent (model) | Kết quả | Ghi chú/leo thang |
|---|---|---|---|---|
| 1 | Core: ngưỡng cấu hình | worker (haiku) | build OK, CoreModelsTests 4/4 | |
| 2 | Invoker che lỗi + ILogger | worker (haiku) | build OK, RbacFunctionInvokerTests 6/6 | main đối chiếu diff: đúng |
| 3 | ErpAgentService: logger, ngưỡng, Clear | worker (haiku) | build OK, suite 70/70 | main đối chiếu diff: đúng |
| 5 | Test cô lập phiên /user | worker (haiku) | CliCommandHandlerTests 6/6 | main chạy mutation (bỏ Reset ở CliRunner:107): test FAIL; đã khôi phục, diff = 0 |
| 4a | Test thứ tự event + huỷ giữa lượt + stub Skip | worker (haiku) | lần 1: test huỷ vẫn PASS khi bỏ Clear (vô nghĩa) | main sửa kịch bản: huỷ KHI tool đang chạy; resume worker; lần 2 FAIL khi bỏ Clear, PASS khi có |
| 4b | Test Approval → ERP (4 ca) | worker (haiku) | suite 77 pass, 1 skipped | 1 lỗi do test sai (NRE ở assert), đã sửa test, không nới assert |
| 6 | Verify blind 8 tiêu chí | verifier (sonnet) | 7 PASS; tiêu chí 7 FAIL một phần | tiêu chí 7 do main soạn thiếu (file untracked có sẵn từ trước); không phải lỗi code |
| 7 | Review diff | reviewer (opus) | Needs changes: 3 Important (CLI in ex.Message, console logger, event ok giả) + 3 Minor | main tự kiểm 1-3 với code: đúng |
| 8 | Spike 1: stream chứa gì khi tool chạy | worker (haiku) | 4 ca; Result là JsonElement, Exception.Message có SQL | user chốt làm luôn phần lõi |
| 9 | Spike 2: sau duyệt/từ chối, nhiều call | worker (haiku) | 4 ca; từ chối vẫn sinh result "rejected" | cần skipResultCallIds |
| 10 | Core: event từ stream, bỏ queue | worker (sonnet) | dừng lần 1: brief mâu thuẫn với file spike | sonnet ngay từ đầu (logic iterator + bảo mật) |
| 10b | Core (resume) | worker (sonnet) | build 0/0, 89/89; 2 mutation FAIL đúng | main cho phép sửa 1 dòng spike |
| 11 | Verify blind 8 tiêu chí | verifier (sonnet) | 8/8 PASS | |
Tổng 14 lần gọi (không tính researcher trước đó). Còn mở: finding 1, 2 (CLI), 3 Minor.
Main tự làm: STATE.md Deferred Items (+1 dòng), spec §3.6 mục 4 / §4 (sửa kịch bản rủi ro cho đúng).
