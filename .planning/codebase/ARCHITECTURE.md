# Architecture baseline

**Surveyed:** 2026-10-21. Khảo sát có giới hạn phục vụ khởi tạo; không phải full codebase mapping hoặc code review.

## Layout

- `code/ErpChatbot.sln`: solution mới, .NET 10.
- `code/src/ErpChatbot.Core`: options, stats, response model, `IErpAgentService`, `IChatClientFactory`.
- `code/src/ErpChatbot.Infrastructure`: Ollama `ChatClientFactory`, health probe và DI options.
- `code/src/ErpChatbot.Agent`: tại lần đọc đầu có project chưa track và test WIP; implementation có thể thay đổi bởi phiên khác.
- `code/tests/ErpChatbot.Tests`: Core tests và Infrastructure tests đã commit; Agent tests WIP.
- `tutorial/ngay-01`: ví dụ học; không coi là ứng dụng giao cho người dùng.

## Intended dependency direction

CLI → Agent → Core; CLI → Infrastructure → Core. Agent không tham chiếu Infrastructure. CLI là composition root để dựng client rồi inject vào Agent. Một Agent service instance sở hữu một conversation.

## Existing contracts

- `IErpAgentService.RunStreamingAsync(string, CancellationToken)` trả `IAsyncEnumerable<string>`; `ResetSessionAsync()`; `GetStats()`.
- `IChatClientFactory.CreateClient()` trả `IChatClient`; `CheckHealthAsync(CancellationToken)` trả `(bool IsHealthy, string Message)`.
- `SessionStats.RecordTurn` cộng lượt và token vào thống kê tích lũy.
- Core hiện có `Microsoft.Extensions.AI.Abstractions` để biểu diễn `IChatClient`; không tham chiếu provider cụ thể.

## Baseline evidence

- `bd1051b`: Core bootstrap/models/interfaces + 3 Core test methods.
- `ad09bbd`: Infrastructure factory + 6 test methods cho provider và health ready/missing/timeout/unreachable.
- Chỉ xác minh sự tồn tại và nội dung file; chưa chạy build/test trong phiên này. Không đánh dấu requirement complete.
- Code WIP phải được refresh khi plan-phase bắt đầu. Không tạo lại solution/project hoặc ghi đè thay đổi từ phiên khác theo plan cũ.
