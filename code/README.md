# ErpChatbot — Microsoft Agent Framework (.NET 10)

Hệ thống Chatbot Trợ lý ERP được xây dựng theo kiến trúc Clean Architecture, chuẩn hóa trên nền tảng **Microsoft Agent Framework (MAF)** và .NET 10. Dự án được thiết kế để mở rộng xuyên suốt 7 giai đoạn trong lộ trình Chatbot ERP.

## Cấu trúc Solution

- **`src/ErpChatbot.Core`**: Chứa Domain Models, cấu hình (`LlmOptions`, `ErpOptions`), Interfaces (`IErpAgentService`, `IChatClientFactory`). Không phụ thuộc thư viện AI bên thứ 3.
- **`src/ErpChatbot.Infrastructure`**: Triển khai `ChatClientFactory` (Ollama provider, health check với timeout 5s).
- **`src/ErpChatbot.Agent`**: Bọc `IChatClient` thành MAF `AIAgent`, tự động quản lý ngữ cảnh qua `AgentSession`, stream phản hồi và trích xuất token qua `UsageContent`. Độc lập hoàn toàn với Infrastructure.
- **`src/ErpChatbot.Cli`**: Console App tương tác, Composition Root, nạp cấu hình và xử lý các lệnh điều khiển (`/reset`, `/stats`, `/info`, `/exit`).
- **`tests/ErpChatbot.Tests`**: Bộ Unit Tests toàn diện với 20 test cases, kiểm thử độc lập hoàn toàn thông qua Fake `IChatClient`.

## Hướng dẫn chạy

### 1. Chạy Unit Tests
```bash
dotnet test
```

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
