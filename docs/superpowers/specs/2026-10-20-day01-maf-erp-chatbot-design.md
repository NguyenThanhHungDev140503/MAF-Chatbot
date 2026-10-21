# Design Document: Ngày 1 — Nền tảng Microsoft Agent Framework (MAF) cho Chatbot ERP (.NET 10)

- **Ngày tạo**: 2026-10-20
- **Tác giả**: Nguyen Thanh Hung & Antigravity
- **Dự án**: MAF-Chatbot ERP
- **Mục tiêu**: Xây dựng Solution .NET 10 hoàn chỉnh tại `code/` hiện thực hóa trọn vẹn luồng Ngày 1 (tổng hợp từ `tutorial/ngay-01/` Ex1–Ex4) theo kiến trúc module sạch (Clean Architecture), đóng vai trò nền tảng bền vững cho toàn bộ 7 giai đoạn trong lộ trình Chatbot ERP.

---

## 1. Bối cảnh & Mục tiêu

### 1.1. Bối cảnh từ Tutorial Ngày 1
Qua khảo sát tài liệu `tutorial/ngay-01/giai-thich-luong-tung-example.md` và mã nguồn 4 ví dụ (Ex1–Ex4):
- **Ex1**: Gọi một lượt không trạng thái (stateless) qua `IChatClient` (`OllamaApiClient`), đo token usage.
- **Ex2**: Định nghĩa System prompt, tinh chỉnh `ChatOptions` (temperature = 0.2 cho ERP), stream từng token bằng `IAsyncEnumerable<ChatResponseUpdate>`.
- **Ex3**: Vòng lặp chat nhiều lượt thủ công (`List<ChatMessage>`), tổng hợp response bằng `updates.ToChatResponse()`, cộng dồn token, hỗ trợ chuyển đổi đa nhà cung cấp (Ollama vs OpenAI-compatible).
- **Ex4**: Bước chuyển mình sang Microsoft Agent Framework (MAF): dùng extension method `client.AsAIAgent(instructions: ...)` và ủy quyền quản lý ngữ cảnh hội thoại cho `AgentSession`.

### 1.2. Mục tiêu kiến trúc
Tránh tạo code tạm bợ (throwaway script). Thay vào đó, xây dựng một Solution .NET 10 chuẩn mực tại `/home/nguyen-thanh-hung/Documents/MAF-Chatbot/code` với các mục tiêu:
1. **Chuẩn hóa MAF**: Tập trung 100% vào `AIAgent` và `AgentSession` (loại bỏ hoàn toàn code quản lý mảng thủ công `List<ChatMessage>` theo nguyên tắc YAGNI).
2. **Khả năng mở rộng (Extensibility)**: Chuẩn bị sẵn các điểm neo (hooks) để tiếp nhận ERP Tools (Ngày 2), MCP Server (Ngày 3), Web AG-UI Hosting (Ngày 4), Observability/Guardrails (Ngày 5), và Workflow duyệt ghi (Ngày 6).
3. **Chịu lỗi & Trải nghiệm (Resilience & UX)**: Hỗ trợ cấu hình linh hoạt (Ollama/OpenAI), Health check tự động, xử lý mất kết nối, streaming mượt mà và bộ lệnh tương tác (`/reset`, `/stats`, `/info`, `/exit`).
4. **Kiểm thử độc lập**: Có unit test với Fake `IChatClient`, không phụ thuộc môi trường cài đặt Ollama thật.

---

## 2. Kiến trúc Solution & Phân tầng (.NET 10)

Thư mục: `code/`
Solution: `ErpChatbot.sln`

```mermaid
graph TD
    subgraph Solution: ErpChatbot.sln (.NET 10)
        CLI[ErpChatbot.Cli<br/>Console Application] --> AGENT[ErpChatbot.Agent<br/>MAF Core & Orchestrator]
        CLI --> INFRA[ErpChatbot.Infrastructure<br/>Providers & Adapters]
        AGENT --> CORE[ErpChatbot.Core<br/>Domain & Abstractions]
        INFRA --> CORE
        AGENT --> INFRA
    end

    subgraph "Lộ trình mở rộng tương lai (Giai đoạn 2 - 6)"
        FUTURE_TOOLS["ErpChatbot.Tools.Erp (Giai đoạn 2: Read Tools)"] -.-> AGENT
        FUTURE_MCP["ErpChatbot.McpServer (Giai đoạn 3: MCP Server)"] -.-> FUTURE_TOOLS
        FUTURE_API["ErpChatbot.Api (Giai đoạn 4: Web AG-UI SSE)"] -.-> AGENT
    end
```

### 2.1. Phân định trách nhiệm từng Project

| Dự án (.NET 10) | Loại | Trách nhiệm | Dependencies |
|---|---|---|---|
| **`ErpChatbot.Core`** | Class Library | - Interface: `IErpAgentService`, `IChatClientFactory`.<br/>- Models/DTOs: `SessionStats`, `ChatTurnResponse`, `LlmOptions`, `ErpOptions`.<br/>- System Persona & Business Instructions cho ERP.<br/>- Độc lập hoàn toàn, không phụ thuộc thư viện AI bên thứ 3. | Không |
| **`ErpChatbot.Infrastructure`** | Class Library | - Triển khai `IChatClientFactory`.<br/>- **Ngày 1: chỉ hỗ trợ Ollama** (qua `OllamaSharp`). OpenAI/Azure OpenAI bị **lùi lại** (xem "NOT in scope") — thêm khi thực sự cần đổi provider, không làm trước theo YAGNI.<br/>- Health check: gọi 1 request thật cực ngắn (không chỉ check socket) để xác nhận model đã load được, timeout cứng 5s — timeout hoặc lỗi model-not-found trả message riêng, khác message "mất kết nối".<br/>- Đọc config phân tầng: `appsettings.json` + biến môi trường (`LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT`). | `Microsoft.Extensions.AI`, `OllamaSharp`, `Microsoft.Extensions.Configuration.Binder` |
| **`ErpChatbot.Agent`** | Class Library | - Đóng gói Agent của Microsoft Agent Framework (`Microsoft.Agents.AI`).<br/>- Triển khai `IErpAgentService` với `AIAgent` và `AgentSession`.<br/>- **Chỉ phụ thuộc `ErpChatbot.Core`, KHÔNG phụ thuộc `ErpChatbot.Infrastructure`** — nhận `IChatClient` đã dựng sẵn qua constructor (DI), không tự gọi factory. Composition (`IChatClientFactory` → `IChatClient` → `AsAIAgent`) diễn ra ở `ErpChatbot.Cli/Program.cs` (composition root).<br/>- Quản lý streaming, theo dõi token usage.<br/>- Sẵn sàng nhận danh sách Tools (`AIFunction`) cho Ngày 2.<br/>- **Lưu ý sở hữu state:** instance này giữ `AgentSession`+`SessionStats` làm mutable field riêng, giả định **1 instance = 1 conversation**. Ngày 4 (Web API) PHẢI tạo 1 instance/scope riêng mỗi user (keyed DI hoặc scoped per-connection) — KHÔNG đăng ký `ErpAgentService` làm Singleton trong DI web, sẽ làm lộ dữ liệu giữa user. | `Microsoft.Agents.AI` (1.24.0), `Microsoft.Extensions.AI`, `ErpChatbot.Core` |
| **`ErpChatbot.Cli`** | Console App | - Điểm khởi động ứng dụng console (`Program.cs`).<br/>- Giao diện console tương tác (nhận input, in stream real-time, thống kê token, lệnh điều khiển `/reset`, `/stats`, `/info`, `/exit`).<br/>- Cấu hình Dependency Injection qua `Microsoft.Extensions.Hosting`. | `Microsoft.Extensions.Hosting`, `ErpChatbot.Agent`, `ErpChatbot.Infrastructure`, `ErpChatbot.Core` |
| **`ErpChatbot.Tests`** | xUnit Test | - Unit test kiểm tra luồng tạo Client, gom token, quản lý session.<br/>- Test với Fake `IChatClient` (không cần chạy LLM thật). | `xunit`, `Microsoft.Extensions.AI`, `ErpChatbot.Core`, `ErpChatbot.Agent`, `ErpChatbot.Infrastructure` |

---

## 3. Thiết kế Chi tiết các Thành phần

### 3.1. Cấu hình (`ErpChatbot.Core` & `appsettings.json`)

Mô hình cấu hình `LlmOptions` và `ErpOptions`:
```json
{
  "Llm": {
    "Provider": "ollama",
    "Model": "llama3.2",
    "Temperature": 0.2,
    "MaxOutputTokens": 500,
    "Ollama": {
      "Endpoint": "http://localhost:11434"
    }
  },
  "Erp": {
    "AssistantName": "Trợ lý ERP",
    "CompanyName": "Doanh nghiệp Demo",
    "Instructions": "Bạn là 'Trợ lý ERP' của công ty. Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng. Bạn CHƯA được kết nối dữ liệu ERP. Nếu người dùng hỏi số liệu cụ thể (tồn kho, đơn hàng, công nợ...), hãy nói rõ là bạn chưa có quyền truy cập dữ liệu, tuyệt đối không được bịa số."
  }
}
```

Biến môi trường ưu tiên ghi đè:
- `LLM_PROVIDER`: Chọn nhà cung cấp. **Ngày 1 chỉ chấp nhận `ollama`**; giá trị khác → `CreateClient()` throw `InvalidOperationException` rõ ràng (tránh im lặng dùng nhầm provider).
- `LLM_MODEL`: Tên model (mặc định `llama3.2`).
- `OLLAMA_ENDPOINT`: Địa chỉ Ollama server (mặc định `http://localhost:11434`).

### 3.2. Factory tạo `IChatClient` (`ErpChatbot.Infrastructure`)

- Interface `IChatClientFactory`:
  - `IChatClient CreateClient()`
  - `Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken ct)`
- Triển khai:
  - Khi `Provider == "ollama"`: Trả về `new OllamaApiClient(endpoint, model)` (tự triển khai `IChatClient`).
  - Provider khác: throw `InvalidOperationException($"LLM_PROVIDER không hợp lệ: {provider}")` (pattern giống switch của Ex3).
  - `CheckHealthAsync`: gửi 1 request thật cực ngắn (ví dụ prompt rỗng/1 token) qua `IChatClient` để xác nhận model đã load được — KHÔNG chỉ check socket/HTTP, vì Ollama có thể "sống" nhưng thiếu model. Timeout cứng 5s qua `CancellationTokenSource`; hết giờ → `(false, "Không phản hồi sau 5s")`. Lỗi "model not found" trả message riêng, khác message "mất kết nối" (hai nguyên nhân khác nhau, user cần biết để sửa đúng chỗ: `ollama pull` vs kiểm tra endpoint).

### 3.3. MAF Agent Service (`ErpChatbot.Agent`)

- Interface `IErpAgentService`:
  ```csharp
  public interface IErpAgentService
  {
      IAsyncEnumerable<string> RunStreamingAsync(string userPrompt, CancellationToken cancellationToken = default);
      Task ResetSessionAsync();
      SessionStats GetStats();
  }
  ```
- Triển khai `ErpAgentService`:
  - Khởi tạo Agent:
    ```csharp
    _agent = _chatClient.AsAIAgent(
        instructions: _erpOptions.Instructions,
        name: _erpOptions.AssistantName
    );
    ```
  - Quản lý phiên: Duy trì biến `AgentSession? _session`. Khi `_session == null` hoặc khi người dùng gõ `/reset`, gọi `await _agent.CreateSessionAsync()`. `/reset` chỉ tạo session mới — KHÔNG reset `SessionStats` (stats là thống kê cả phiên CLI, độc lập với ngữ cảnh hội thoại).
  - Stream phản hồi: Gọi `_agent.RunStreamingAsync(userPrompt, _session)`, nhận `IAsyncEnumerable<AgentResponseUpdate>`. Với mỗi `update`, lặp qua `update.Contents` tìm `UsageContent` để cộng dồn vào `SessionStats` — **KHÔNG dùng `response.Usage`/`ToChatResponse()` kiểu Ex3** (pattern đó chỉ đúng cho vòng lặp thủ công, không áp dụng cho `AIAgent`).
  - Xử lý lỗi/hủy giữa stream (kết nối rớt HOẶC user Ctrl+C): KHÔNG tự động gọi lại `CreateSessionAsync()` — để nguyên `AgentSession` hiện tại, in thông báo tách biệt rõ khỏi phần text đã stream (ví dụ xuống dòng mới + `[Mất kết nối, thử lại hoặc /reset]` cho lỗi mạng, `(đã hủy)` cho Ctrl+C). Quyết định này nhất quán cho cả hai trường hợp — để người dùng tự quyết có cần `/reset` hay không, tránh mất ngữ cảnh ngoài ý muốn.

### 3.4. Giao diện CLI (`ErpChatbot.Cli`)

- Hiển thị banner khởi động đẹp mắt kèm thông số kết nối.
- Thực hiện health check lúc khởi động (timeout 5s — xem §3.2); nếu endpoint không khả dụng hoặc model chưa load được, in cảnh báo hướng dẫn rõ ràng, phân biệt rõ 2 nguyên nhân.
- Composition root: DI ở đây dựng `IChatClientFactory` → `IChatClient` → gọi `AsAIAgent()` → inject `IChatClient` vào `ErpAgentService` (xem §2.1 — Agent không tự biết Infrastructure).
- Vòng lặp nhận diện các lệnh:
  - `/reset`: Tạo `AgentSession` mới, thông báo đã xóa ngữ cảnh hội thoại. KHÔNG xóa `SessionStats`.
  - `/stats`: Hiển thị số lượt chat và tổng số token đã dùng (cộng dồn từ lúc mở app, không bị `/reset` ảnh hưởng).
  - `/info`: Hiển thị cấu hình hiện tại và System Instructions.
  - `/exit`: Thoát ứng dụng an toàn.
  - Input bắt đầu bằng `/` nhưng không khớp lệnh nào: in `(lệnh không hợp lệ, gõ /info để xem danh sách)`, KHÔNG gửi cho LLM.
  - Input rỗng: bỏ qua, không gọi LLM.
- Bắt lỗi mất kết nối (`HttpRequestException`, `TimeoutException`) giữa stream: in thông báo lỗi tách biệt khỏi text đã stream, không tự reset session (xem §3.3).
- Hỗ trợ hủy stream qua `Ctrl+C`: nối `CancellationToken` từ `Console.CancelKeyPress` xuống `RunStreamingAsync(ct)` để hủy được cả request HTTP đang treo (không chỉ dừng vòng lặp phía client); in `(đã hủy)`, không tự reset session (cùng logic với lỗi mạng ở §3.3).

---

## 4. Kế hoạch Kiểm thử & Điểm neo Ngày 2–6

1. **Unit Testing (`ErpChatbot.Tests`)**:
   - Sử dụng Fake `IChatClient` để mô phỏng streaming phản hồi (kể cả throw giữa stream, để test không cần Ollama thật).
   - `ChatClientFactoryTests`: `CreateClient()` với provider hợp lệ và không hợp lệ (throw `InvalidOperationException`); `CheckHealthAsync()` cả nhánh model sẵn sàng / model thiếu / timeout 5s.
   - `ErpAgentServiceTests`:
     - Stream đầy đủ, `AgentSession` giữ ngữ cảnh qua nhiều turn.
     - Cộng dồn token qua `UsageContent` đúng qua nhiều turn liên tiếp (cumulative, không reset nhầm).
     - `ResetSessionAsync()`: session mới nhưng `SessionStats` giữ nguyên.
     - Fake `IChatClient` throw giữa stream → không crash, message lỗi rõ, `AgentSession` không tự bị reset.
     - Cancel (`CancellationToken`) giữa stream → xử lý giống case lỗi mạng (không tự reset session).
   - **Cli (qua `ErpAgentService` + input giả lập)**: lệnh `/reset /stats /info /exit` từng cái; lệnh sai cú pháp (`/xxx`) → in hint, không gọi LLM; input rỗng → bỏ qua.
2. **Kế thừa xuyên suốt lộ trình**:
   - **Ngày 2**: Thêm project `ErpChatbot.Tools.Erp` hoặc danh sách `AIFunction` tra cứu kho, công nợ và inject vào Agent.
   - **Ngày 3**: Tách hoặc bọc các Tool thành MCP Server (`ModelContextProtocol.AspNetCore`).
   - **Ngày 4**: Thêm project Web API (`ErpChatbot.Api`) sử dụng chung `IErpAgentService` hoặc AG-UI SSE hosting (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`). **Bắt buộc**: tạo `ErpAgentService` theo scope/instance riêng mỗi user — không đăng ký Singleton (xem §2.1).
   - **Ngày 5 & 6**: Bổ sung OpenTelemetry tracing, Guardrails và `ApprovalRequiredAIFunction` cho các thao tác ghi.

---

## 5. NOT in scope (Ngày 1)

| Hạng mục | Lý do lùi lại |
|---|---|
| Provider OpenAI/Azure OpenAI trong `IChatClientFactory` | YAGNI — không ngày nào trong lộ trình 2-6 cần đổi provider thật; thêm khi thực sự cần, interface đã sẵn để mở rộng không phải viết lại. |
| ERP Tools (`AIFunction` tra kho/công nợ) | Chưa có entity/DTO ERP thật — thuộc Ngày 2. |
| MCP Server wrapper | Cần Tools xong trước — thuộc Ngày 3. |
| Web API / AG-UI SSE hosting | Cần quyết định session-ownership (đã ghi ở §2.1) ổn định trước khi build multi-user — thuộc Ngày 4. |
| OpenTelemetry, Guardrails, `ApprovalRequiredAIFunction` | Chưa có luồng ghi dữ liệu nào để guard — thuộc Ngày 5-6. |
| CI/CD build/publish pipeline cho CLI | Đây là local dev tool chạy qua `dotnet run`, không phân phối ra ngoài máy dev ở giai đoạn này — không phải artifact cần publish. |
| Circuit breaker / auto-reconnect khi Ollama rớt giữa phiên | CLI đơn luồng, user tự gõ lại câu hỏi là đủ cho Ngày 1; có thể cần ở Ngày 5 (Resilience) nếu chuyển sang host nhiều user. |

## 6. What already exists

- `tutorial/ngay-01/Ex1-Ex4`: 4 script độc lập đã verify API thật (xác nhận qua Context7: `Microsoft.Agents.AI` 1.24.0 — `AIAgent.RunStreamingAsync(string, AgentSession?, ...)` trả `IAsyncEnumerable<AgentResponseUpdate>`, token usage qua `UsageContent` trong `update.Contents`). Doc chủ động KHÔNG tái dùng Ex1-3 (quản lý `List<ChatMessage>` thủ công) vì đó là anti-pattern theo YAGNI đã tuyên bố ở §1.2 — chỉ productionize pattern của Ex4 (`AsAIAgent` + `AgentSession`).
- `code/` hiện chỉ có `code/docs/` trống — đây là greenfield thật, không có code sản xuất nào để tái dùng ngoài tutorial.

## 7. Worktree parallelization strategy

| Step | Module | Depends on |
|---|---|---|
| Core bootstrap | `ErpChatbot.Core` | — |
| Infrastructure | `ErpChatbot.Infrastructure` | Core |
| Agent | `ErpChatbot.Agent` | Core (KHÔNG phụ thuộc Infrastructure — xem §2.1 quyết định D6) |
| Cli | `ErpChatbot.Cli` | Infrastructure, Agent |
| Tests | `ErpChatbot.Tests` | Infrastructure, Agent, Cli (viết song song từng module, không phải cuối cùng mới viết) |

**Lanes:**
- Lane 0 (sequential, bootstrap trước): Core — interface + DTO + persona, nhỏ, không đáng tách worktree riêng.
- Lane A: Infrastructure (độc lập sau khi Core xong).
- Lane B: Agent (độc lập sau khi Core xong, không đụng Infrastructure nhờ D6).
- Lane C (sequential, chờ A+B): Cli — composition root, cần cả hai.

**Thực thi:** Core xong trước (nhanh, 1 người/1 session). Sau đó launch Lane A + Lane B song song (2 worktree). Merge cả hai. Rồi làm Lane C. Test viết kèm từng lane, không dồn cuối.

**Conflict flag:** không có — A và B không đụng chung thư mục nào sau khi D6 áp dụng (Agent không còn import Infrastructure).

## 8. Implementation Tasks

Tổng hợp từ review này. Mỗi task bám 1 finding cụ thể ở trên.

- [ ] **T7 (P1, human: ~5min / CC: ~1min)** — Core — Ghi rõ session-ownership trong `IErpAgentService`
  - Surfaced by: Architecture D1 — blast radius multi-tenant nếu Ngày 4 đăng ký Singleton
  - Files: `docs/superpowers/specs/2026-10-20-day01-maf-erp-chatbot-design.md` (đã sửa), XML doc comment khi code thật
  - Verify: review bằng mắt khi viết `IErpAgentService`, confirm Ngày 4 dùng scoped/keyed instance
- [ ] **T1 (P1, human: ~10min / CC: ~2min)** — Agent — Bỏ dependency Agent→Infrastructure, chuyển composition sang Cli
  - Surfaced by: Outside voice D6 — dependency leak phá Clean Architecture
  - Files: `ErpChatbot.Agent.csproj`, `ErpChatbot.Cli/Program.cs`
  - Verify: `dotnet build` không còn package reference OllamaSharp/OpenAI trong Agent project
- [ ] **T2 (P1, human: ~15min / CC: ~3min)** — Infrastructure — CheckHealthAsync xác nhận model thật + timeout 5s
  - Surfaced by: Failure mode D5+D7 — health check false-positive khi thiếu model, treo vô thời hạn khi Ollama "treo"
  - Files: `ErpChatbot.Infrastructure/ChatClientFactory.cs`
  - Verify: test timeout trả `(false, "Không phản hồi sau 5s")`; test model-not-found trả message khác "mất kết nối"
- [ ] **T3 (P1, human: ~10min / CC: ~2min)** — Agent — Xử lý lỗi/hủy giữa stream nhất quán, không tự reset session
  - Surfaced by: Architecture D2 + Outside voice D10
  - Files: `ErpChatbot.Agent/ErpAgentService.cs`, `ErpChatbot.Cli/Program.cs`
  - Verify: test Fake `IChatClient` throw giữa stream + test Ctrl+C — cả hai không tự gọi `CreateSessionAsync()`
- [ ] **T8 (P1, human: ~2h / CC: ~20min)** — Tests — Viết 17 test case theo coverage diagram
  - Surfaced by: Test review — 0/17 path có test cụ thể trong doc gốc
  - Files: `ErpChatbot.Tests/*`
  - Verify: `dotnet test` xanh toàn bộ, coverage khớp diagram §4.1
- [ ] **T4 (P2, human: ~5min / CC: ~1min)** — Agent — Trích xuất token qua `UsageContent`, không theo pattern Ex3
  - Surfaced by: Outside voice D8
  - Files: `ErpChatbot.Agent/ErpAgentService.cs`
  - Verify: test cộng dồn token qua nhiều turn ra đúng số
- [ ] **T5 (P2, human: ~10min / CC: ~2min)** — Cli — `/reset` giữ stats; `/xxx` sai in hint không gọi LLM
  - Surfaced by: Code quality D3+D4
  - Files: `ErpChatbot.Cli/Program.cs`
  - Verify: test `/reset` rồi `/stats` vẫn ra số cũ; test `/xxx` không tốn 1 lượt gọi LLM
- [ ] **T6 (P2, human: ~5min / CC: ~1min)** — Infrastructure — Bỏ OpenAI provider khỏi Ngày 1
  - Surfaced by: Cross-model tension D9 — YAGNI
  - Files: `ErpChatbot.Infrastructure.csproj`, `appsettings.json`
  - Verify: `dotnet build` không còn package `OpenAI`/`Microsoft.Extensions.AI.OpenAI`

_Test review đã gộp vào T8, không tách riêng._

## GSTACK REVIEW REPORT

| Review | Trigger | Why | Runs | Status | Findings |
|--------|---------|-----|------|--------|----------|
| CEO Review | `/plan-ceo-review` | Scope & strategy | 0 | — | — |
| Codex Review | `/codex review` | Independent 2nd opinion | 0 | — | — |
| Eng Review | `/plan-eng-review` | Architecture & tests (required) | 1 | CLEAR | 10 findings (D1-D10), all resolved and folded into doc |
| Design Review | `/plan-design-review` | UI/UX gaps | 0 | — | — |
| DX Review | `/plan-devex-review` | Developer experience gaps | 0 | — | — |

- **CROSS-MODEL:** Codex CLI lỗi `config.toml` (invalid type trong `features`) ngay khi khởi động, không liên quan plan — fallback qua Claude subagent độc lập. Subagent tìm thêm 6 finding mới (D6-D10 + phần health-check trong D7) mà review chính bỏ sót, và 1 tension thật (D9: multi-provider có phải YAGNI violation) — user chọn rút gọn chỉ Ollama Ngày 1, đổi hướng so với quyết định ban đầu ở Step 0.
- **VERDICT:** ENG REVIEW CLEARED — ready to implement.

NO UNRESOLVED DECISIONS
