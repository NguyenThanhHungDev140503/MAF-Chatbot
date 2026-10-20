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
| **`ErpChatbot.Infrastructure`** | Class Library | - Triển khai `IChatClientFactory`.<br/>- Hỗ trợ tạo client cho Ollama (qua `OllamaSharp`) và OpenAI/Azure OpenAI (qua `OpenAIClient` + adapter `.AsIChatClient()`).<br/>- Health check endpoint LLM (báo lỗi thân thiện nếu Ollama chưa bật).<br/>- Đọc config phân tầng: `appsettings.json` + biến môi trường (`LLM_PROVIDER`, `LLM_MODEL`, `OLLAMA_ENDPOINT`, `OPENAI_API_KEY`). | `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, `OllamaSharp`, `OpenAI`, `Microsoft.Extensions.Configuration.Binder` |
| **`ErpChatbot.Agent`** | Class Library | - Đóng gói Agent của Microsoft Agent Framework (`Microsoft.Agents.AI`).<br/>- Triển khai `IErpAgentService` với `AIAgent` và `AgentSession`.<br/>- Quản lý streaming, theo dõi token usage.<br/>- Sẵn sàng nhận danh sách Tools (`AIFunction`) cho Ngày 2. | `Microsoft.Agents.AI` (1.24.0), `Microsoft.Extensions.AI`, `ErpChatbot.Core`, `ErpChatbot.Infrastructure` |
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
    },
    "OpenAI": {
      "Endpoint": "http://localhost:11434/v1",
      "ApiKey": "ollama"
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
- `LLM_PROVIDER`: Chọn nhà cung cấp (`ollama` hoặc `openai`).
- `LLM_MODEL`: Tên model (mặc định `llama3.2`).
- `OLLAMA_ENDPOINT`: Địa chỉ Ollama server (mặc định `http://localhost:11434`).
- `OPENAI_API_KEY`: API key cho OpenAI hoặc Ollama /v1 (mặc định `ollama`).
- `OPENAI_ENDPOINT`: Địa chỉ OpenAI-compatible endpoint.

### 3.2. Factory tạo `IChatClient` (`ErpChatbot.Infrastructure`)

- Interface `IChatClientFactory`:
  - `IChatClient CreateClient()`
  - `Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken ct)`
- Triển khai:
  - Khi `Provider == "ollama"`: Trả về `new OllamaApiClient(endpoint, model)` (tự triển khai `IChatClient`).
  - Khi `Provider == "openai"`: Trả về `new OpenAIClient(credential, clientOptions).GetChatClient(model).AsIChatClient()`.
  - Phương thức `CheckHealthAsync` kiểm tra socket/HTTP của endpoint và trả về trạng thái thân thiện.

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
  - Quản lý phiên: Duy trì biến `AgentSession? _session`. Khi `_session == null` hoặc khi người dùng gõ `/reset`, gọi `await _agent.CreateSessionAsync()`.
  - Stream phản hồi: Gọi `_agent.RunStreamingAsync(userPrompt, _session)`. Mỗi update được stream về caller, đồng thời trích xuất token usage và cập nhật `SessionStats`.

### 3.4. Giao diện CLI (`ErpChatbot.Cli`)

- Hiển thị banner khởi động đẹp mắt kèm thông số kết nối.
- Thực hiện health check lúc khởi động; nếu endpoint không khả dụng, in cảnh báo hướng dẫn rõ ràng.
- Vòng lặp nhận diện các lệnh:
  - `/reset`: Tạo `AgentSession` mới, thông báo đã xóa ngữ cảnh hội thoại.
  - `/stats`: Hiển thị số lượt chat và tổng số token đã dùng.
  - `/info`: Hiển thị cấu hình hiện tại và System Instructions.
  - `/exit`: Thoát ứng dụng an toàn.
- Bắt lỗi mất kết nối (`HttpRequestException`, `TimeoutException`) và hỗ trợ hủy stream qua `Ctrl+C`.

---

## 4. Kế hoạch Kiểm thử & Điểm neo Ngày 2–6

1. **Unit Testing (`ErpChatbot.Tests`)**:
   - Sử dụng Fake `IChatClient` để mô phỏng streaming phản hồi.
   - Kiểm tra `ErpAgentService`: Đảm bảo stream đầy đủ, `AgentSession` giữ ngữ cảnh và `ResetSessionAsync` làm mới phiên.
   - Kiểm tra `ChatClientFactory` nạp đúng cấu hình từ file và biến môi trường.
2. **Kế thừa xuyên suốt lộ trình**:
   - **Ngày 2**: Thêm project `ErpChatbot.Tools.Erp` hoặc danh sách `AIFunction` tra cứu kho, công nợ và inject vào Agent.
   - **Ngày 3**: Tách hoặc bọc các Tool thành MCP Server (`ModelContextProtocol.AspNetCore`).
   - **Ngày 4**: Thêm project Web API (`ErpChatbot.Api`) sử dụng chung `IErpAgentService` hoặc AG-UI SSE hosting (`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`).
   - **Ngày 5 & 6**: Bổ sung OpenTelemetry tracing, Guardrails và `ApprovalRequiredAIFunction` cho các thao tác ghi.
