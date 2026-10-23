# ErpChatbot — Microsoft Agent Framework (.NET 10)

Hệ thống Chatbot Trợ lý ERP được xây dựng theo kiến trúc Clean Architecture, chuẩn hóa trên nền tảng **Microsoft Agent Framework (MAF)** và .NET 10. Dự án được thiết kế để mở rộng xuyên suốt 7 giai đoạn trong lộ trình Chatbot ERP.

## Cấu trúc Solution

- **`src/ErpChatbot.Core`**: Chứa Domain Models, cấu hình (`LlmOptions`, `ErpOptions`), Interfaces (`IErpAgentService`, `IChatClientFactory`). Không phụ thuộc thư viện AI bên thứ 3.
- **`src/ErpChatbot.Infrastructure`**: Triển khai `ChatClientFactory` (Ollama provider, health check với timeout 30s).
- **`src/ErpChatbot.Agent`**: Bọc `IChatClient` thành MAF `AIAgent`, tự động quản lý ngữ cảnh qua `AgentSession`, stream phản hồi và trích xuất token qua `UsageContent`. Độc lập hoàn toàn với Infrastructure.
- **`src/ErpChatbot.Cli`**: Console App tương tác, Composition Root, nạp cấu hình và xử lý các lệnh điều khiển (`/reset`, `/stats`, `/info`, `/exit`).
- **`tests/ErpChatbot.Tests`**: Bộ Unit Tests toàn diện với 28 test cases, kiểm thử độc lập hoàn toàn thông qua Fake `IChatClient`.

## Phiên bản Thư viện (Pinned Dependencies)

- **`Microsoft.Agents.AI`**: `1.24.0`
- **`Microsoft.Extensions.AI`**: `10.10.0`
- **`OllamaSharp`**: `5.5.0`
- **`Microsoft.Extensions.Hosting`**: `10.0.0`

## Hướng dẫn chạy

### 1. Chạy Unit Tests
Toàn bộ unit test sử dụng in-memory test doubles (fake clients), không yêu cầu dịch vụ ngoài, đạt tỷ lệ đạt 100%:
```bash
dotnet test
```

> **Lưu ý kiểm thử trực tiếp (Live Verification):** Automated tests dùng test doubles mô phỏng đầy đủ hành vi. Nếu muốn chạy kiểm thử đa lượt trực tiếp với LLM thật qua CLI, cần khởi động sẵn Ollama daemon (`ollama serve` và `ollama pull llama3.2`).

### 2. Chạy Chatbot CLI
Yêu cầu bật sẵn Ollama:
```bash
ollama serve
ollama pull llama3.2
dotnet run --project src/ErpChatbot.Cli
```

### 3. Các biến môi trường hỗ trợ
- `LLM_PROVIDER`: `ollama` (mặc định)
- `LLM_MODEL`: `llama3.2`
- `OLLAMA_ENDPOINT`: `http://localhost:11434`
