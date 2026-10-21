# Ngày 1 — Nền tảng Microsoft Agent Framework (MAF) cho Chatbot ERP (.NET 10) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng một Solution .NET 10 chuẩn mực tại `code/` hiện thực hóa trọn vẹn luồng Ngày 1 (chuyển đổi từ `IChatClient` thô sang `AIAgent` và `AgentSession` của Microsoft Agent Framework) với Clean Architecture, sẵn sàng mở rộng cho 7 giai đoạn trong lộ trình Chatbot ERP.

**Architecture:** Áp dụng Clean Architecture 4 tầng phân tách nghiêm ngặt: `Core` (trừu tượng & DTO), `Infrastructure` (Ollama provider & health check), `Agent` (MAF `AIAgent` & `AgentSession`, độc lập với Infrastructure), và `Cli` (Console Host & Composition Root). Toàn bộ luồng chat dùng streaming qua `IAsyncEnumerable<AgentResponseUpdate>`, theo dõi token qua `UsageContent`, kiểm thử bằng Fake `IChatClient` độc lập 100% với môi trường bên ngoài.

**Tech Stack:** .NET 10, C# 13, `Microsoft.Agents.AI` 1.24.0, `Microsoft.Extensions.AI` 10.10.0, `OllamaSharp` 5.5.0, `Microsoft.Extensions.Hosting` 10.0.0, `xunit` 2.9.3, `FluentAssertions` 8.0.1.

## Global Constraints

- Target Framework: `net10.0` cho toàn bộ các project.
- Package floor: `Microsoft.Agents.AI` 1.24.0, `Microsoft.Extensions.AI` 10.10.0, `OllamaSharp` 5.5.0.
- Ngày 1 chỉ hỗ trợ provider `ollama`; không thêm dependency `OpenAI` hay `Microsoft.Extensions.AI.OpenAI` theo nguyên tắc YAGNI.
- `ErpChatbot.Agent` chỉ phụ thuộc `ErpChatbot.Core`, KHÔNG phụ thuộc `ErpChatbot.Infrastructure`.
- Session ownership: `ErpAgentService` sở hữu `AgentSession` làm mutable state (1 instance = 1 conversation). Không bao giờ đăng ký Singleton.
- Health check: Timeout cứng 5s, gọi request thật xác nhận model đã nạp, phân biệt lỗi model-not-found và mất kết nối.
- Token tracking: Trích xuất qua `UsageContent` trong `AgentResponseUpdate.Contents`, không dùng pattern `ToChatResponse()` của Ex3.
- Error resilience: Mất kết nối hoặc Ctrl+C ngắt stream không được tự động reset session.

---

### Task 1: Solution Bootstrap & Core Models (`ErpChatbot.Core`)

**Files:**
- Create: `code/ErpChatbot.sln`
- Create: `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`
- Create: `code/src/ErpChatbot.Core/Models/LlmOptions.cs`
- Create: `code/src/ErpChatbot.Core/Models/ErpOptions.cs`
- Create: `code/src/ErpChatbot.Core/Models/SessionStats.cs`
- Create: `code/src/ErpChatbot.Core/Models/ChatTurnResponse.cs`
- Create: `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`
- Create: `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`
- Create: `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
- Create: `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`

**Interfaces:**
- Consumes: None (Root abstraction)
- Produces:
  - `IErpAgentService`: `IAsyncEnumerable<string> RunStreamingAsync(string userPrompt, CancellationToken cancellationToken = default)`, `Task ResetSessionAsync()`, `SessionStats GetStats()`
  - `IChatClientFactory`: `IChatClient CreateClient()`, `Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken cancellationToken = default)`
  - DTOs: `LlmOptions`, `OllamaOptions`, `ErpOptions`, `SessionStats`, `ChatTurnResponse`

- [ ] **Step 1: Khởi tạo Solution và các project bằng .NET CLI**

Run:
```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code
dotnet new sln -n ErpChatbot
dotnet new classlib -n ErpChatbot.Core -o src/ErpChatbot.Core -f net10.0
dotnet new xunit -n ErpChatbot.Tests -o tests/ErpChatbot.Tests -f net10.0
dotnet sln ErpChatbot.sln add src/ErpChatbot.Core/ErpChatbot.Core.csproj
dotnet sln ErpChatbot.sln add tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj
dotnet add tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj reference src/ErpChatbot.Core/ErpChatbot.Core.csproj
dotnet add src/ErpChatbot.Core/ErpChatbot.Core.csproj package Microsoft.Extensions.AI.Abstractions --version 10.10.0
dotnet add tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj package FluentAssertions --version 8.0.1
rm -f src/ErpChatbot.Core/Class1.cs tests/ErpChatbot.Tests/UnitTest1.cs
```

- [ ] **Step 2: Viết failing test cho Core Models và Options**

Tạo file `code/tests/ErpChatbot.Tests/Core/CoreModelsTests.cs`:
```csharp
using ErpChatbot.Core.Models;
using FluentAssertions;
using Xunit;

namespace ErpChatbot.Tests.Core;

public class CoreModelsTests
{
    [Fact]
    public void LlmOptions_ShouldHaveDefaultValues_MatchingDay01Spec()
    {
        var options = new LlmOptions();

        options.Provider.Should().Be("ollama");
        options.Model.Should().Be("llama3.2");
        options.Temperature.Should().Be(0.2f);
        options.MaxOutputTokens.Should().Be(500);
        options.Ollama.Endpoint.Should().Be("http://localhost:11434");
    }

    [Fact]
    public void ErpOptions_ShouldHaveDefaultPersona()
    {
        var options = new ErpOptions();

        options.AssistantName.Should().Be("Trợ lý ERP");
        options.Instructions.Should().Contain("Bạn là 'Trợ lý ERP' của công ty");
        options.Instructions.Should().Contain("không được bịa số");
    }

    [Fact]
    public void SessionStats_ShouldAccumulateCorrectly()
    {
        var stats = new SessionStats();
        stats.RecordTurn(inputTokens: 10, outputTokens: 25);
        stats.RecordTurn(inputTokens: 20, outputTokens: 35);

        stats.TurnCount.Should().Be(2);
        stats.TotalInputTokens.Should().Be(30);
        stats.TotalOutputTokens.Should().Be(60);
        stats.TotalTokens.Should().Be(90);
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận test FAIL (chưa có code triển khai)**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: Build compilation FAIL với lỗi không tìm thấy namespace/class `ErpChatbot.Core.Models`.

- [ ] **Step 4: Triển khai Models và Interfaces trong `ErpChatbot.Core`**

Tạo file `code/src/ErpChatbot.Core/Models/LlmOptions.cs`:
```csharp
namespace ErpChatbot.Core.Models;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public float Temperature { get; set; } = 0.2f;
    public int MaxOutputTokens { get; set; } = 500;
    public OllamaOptions Ollama { get; set; } = new();
}

public class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";
}
```

Tạo file `code/src/ErpChatbot.Core/Models/ErpOptions.cs`:
```csharp
namespace ErpChatbot.Core.Models;

public class ErpOptions
{
    public const string SectionName = "Erp";

    public string AssistantName { get; set; } = "Trợ lý ERP";
    public string CompanyName { get; set; } = "Doanh nghiệp Demo";
    public string Instructions { get; set; } =
        "Bạn là 'Trợ lý ERP' của công ty. Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng. " +
        "Bạn CHƯA được kết nối dữ liệu ERP. Nếu người dùng hỏi số liệu cụ thể (tồn kho, đơn hàng, công nợ...), " +
        "hãy nói rõ là bạn chưa có quyền truy cập dữ liệu, tuyệt đối không được bịa số.";
}
```

Tạo file `code/src/ErpChatbot.Core/Models/SessionStats.cs`:
```csharp
namespace ErpChatbot.Core.Models;

public class SessionStats
{
    public int TurnCount { get; private set; }
    public long TotalInputTokens { get; private set; }
    public long TotalOutputTokens { get; private set; }
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public void RecordTurn(long inputTokens, long outputTokens)
    {
        TurnCount++;
        TotalInputTokens += inputTokens;
        TotalOutputTokens += outputTokens;
    }
}
```

Tạo file `code/src/ErpChatbot.Core/Models/ChatTurnResponse.cs`:
```csharp
namespace ErpChatbot.Core.Models;

public record ChatTurnResponse(
    string Content,
    long InputTokens,
    long OutputTokens,
    TimeSpan Duration,
    string Provider,
    string Model
);
```

Tạo file `code/src/ErpChatbot.Core/Interfaces/IChatClientFactory.cs`:
```csharp
using Microsoft.Extensions.AI;

namespace ErpChatbot.Core.Interfaces;

public interface IChatClientFactory
{
    IChatClient CreateClient();
    Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken cancellationToken = default);
}
```

Tạo file `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`:
```csharp
using ErpChatbot.Core.Models;

namespace ErpChatbot.Core.Interfaces;

/// <summary>
/// Quản lý vòng đời hội thoại của Trợ lý ERP sử dụng Microsoft Agent Framework (MAF).
/// <para>
/// <b>LƯU Ý SỞ HỮU TRẠNG THÁI (Session Ownership):</b><br/>
/// Mỗi instance của implementation giữ một phiên <c>AgentSession</c> và <c>SessionStats</c> làm mutable field riêng.
/// Giả định: <b>1 instance = 1 phiên hội thoại của 1 người dùng</b>.<br/>
/// Ở Giai đoạn 4 (Web API / Multi-tenant), BẮT BUỘC đăng ký service này theo Scoped hoặc Keyed DI per-connection/user.
/// TUYỆT ĐỐI KHÔNG đăng ký làm Singleton trong môi trường Web để tránh rò rỉ dữ liệu giữa các người dùng.
/// </para>
/// </summary>
public interface IErpAgentService
{
    IAsyncEnumerable<string> RunStreamingAsync(string userPrompt, CancellationToken cancellationToken = default);
    Task ResetSessionAsync();
    SessionStats GetStats();
}
```

- [ ] **Step 5: Chạy test để xác nhận test PASS**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: 3 passed, 0 failed.

- [ ] **Step 6: Commit Task 1**

```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot
git add code/ErpChatbot.sln code/src/ErpChatbot.Core/ code/tests/ErpChatbot.Tests/
git commit -m "feat(core): bootstrap solution and implement core models and interfaces"
```

---

### Task 2: Infrastructure Layer (`ErpChatbot.Infrastructure`)

**Files:**
- Create: `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`
- Create: `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`
- Create: `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`
- Modify: `code/ErpChatbot.sln`
- Modify: `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`

**Interfaces:**
- Consumes:
  - `ErpChatbot.Core.Interfaces.IChatClientFactory`
  - `ErpChatbot.Core.Models.LlmOptions`
- Produces:
  - `ChatClientFactory`: Khởi tạo `OllamaApiClient` (implement `IChatClient`). Throw `InvalidOperationException` cho provider không phải `ollama`. Thực hiện `CheckHealthAsync` với timeout 5s, gọi request thật xác nhận model tồn tại, phân biệt thông điệp lỗi.

- [ ] **Step 1: Khởi tạo project `ErpChatbot.Infrastructure` và thêm dependencies**

Run:
```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code
dotnet new classlib -n ErpChatbot.Infrastructure -o src/ErpChatbot.Infrastructure -f net10.0
dotnet sln ErpChatbot.sln add src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj
dotnet add src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj reference src/ErpChatbot.Core/ErpChatbot.Core.csproj
dotnet add src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj package OllamaSharp --version 5.5.0
dotnet add src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj package Microsoft.Extensions.AI --version 10.10.0
dotnet add src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj package Microsoft.Extensions.Options --version 10.0.0
dotnet add tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj reference src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj
rm -f src/ErpChatbot.Infrastructure/Class1.cs
```

- [ ] **Step 2: Viết 6 test cases cho `ChatClientFactoryTests`**

Tạo file `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`:
```csharp
using ErpChatbot.Core.Models;
using ErpChatbot.Infrastructure.Factories;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Xunit;

namespace ErpChatbot.Tests.Infrastructure;

public class ChatClientFactoryTests
{
    private class FakeChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _responder;

        public FakeChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> responder)
        {
            _responder = responder;
        }

        public ChatClientMetadata Metadata => new("fake");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _responder(chatMessages, options, cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public void CreateClient_WithOllamaProvider_ReturnsOllamaApiClient()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2" });
        var factory = new ChatClientFactory(options);

        var client = factory.CreateClient();

        client.Should().NotBeNull();
        client.Should().BeOfType<OllamaApiClient>();
    }

    [Fact]
    public void CreateClient_WithUnsupportedProvider_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new LlmOptions { Provider = "openai" });
        var factory = new ChatClientFactory(options);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LLM_PROVIDER không hợp lệ*");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenModelReady_ReturnsHealthy()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2" });
        var fakeClient = new FakeChatClient((msgs, opt, ct) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        var factory = new ChatClientFactory(options, _ => fakeClient);

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeTrue();
        message.Should().Contain("sẵn sàng");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenModelMissing_ReturnsModelNotFoundMessage()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "non-existent-model" });
        var fakeClient = new FakeChatClient((msgs, opt, ct) =>
            throw new InvalidOperationException("model 'non-existent-model' not found, try pulling it first"));

        var factory = new ChatClientFactory(options, _ => fakeClient);

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeFalse();
        message.Should().Contain("không tìm thấy").And.Contain("ollama pull");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTimeoutExceeded_ReturnsTimeoutMessage()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2" });
        var fakeClient = new FakeChatClient(async (msgs, opt, ct) =>
        {
            await Task.Delay(10000, ct); // Giả lập treo quá 5s
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "late"));
        });

        var factory = new ChatClientFactory(options, _ => fakeClient, timeoutMs: 50); // Timeout nhanh cho test

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeFalse();
        message.Should().Contain("Không phản hồi");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointUnreachable_ReturnsConnectionErrorMessage()
    {
        var options = Options.Create(new LlmOptions { Provider = "ollama", Model = "llama3.2" });
        var fakeClient = new FakeChatClient((msgs, opt, ct) =>
            throw new HttpRequestException("Connection refused"));

        var factory = new ChatClientFactory(options, _ => fakeClient);

        var (isHealthy, message) = await factory.CheckHealthAsync();

        isHealthy.Should().BeFalse();
        message.Should().Contain("Không thể kết nối tới Ollama");
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận test FAIL (chưa có `ChatClientFactory`)**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: Build compilation FAIL với lỗi không tìm thấy `ChatClientFactory`.

- [ ] **Step 4: Triển khai `ChatClientFactory`**

Tạo file `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`:
```csharp
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace ErpChatbot.Infrastructure.Factories;

public class ChatClientFactory : IChatClientFactory
{
    private readonly LlmOptions _options;
    private readonly Func<LlmOptions, IChatClient>? _clientOverride;
    private readonly int _timeoutMs;

    public ChatClientFactory(IOptions<LlmOptions> options)
        : this(options, null, 5000)
    {
    }

    // Constructor phục vụ Dependency Injection và Unit Testing (inject mock/timeout)
    public ChatClientFactory(
        IOptions<LlmOptions> options,
        Func<LlmOptions, IChatClient>? clientOverride = null,
        int timeoutMs = 5000)
    {
        _options = options.Value;
        _clientOverride = clientOverride;
        _timeoutMs = timeoutMs;
    }

    public IChatClient CreateClient()
    {
        if (_clientOverride != null)
        {
            return _clientOverride(_options);
        }

        return _options.Provider.ToLowerInvariant() switch
        {
            "ollama" => new OllamaApiClient(new Uri(_options.Ollama.Endpoint), _options.Model),
            _ => throw new InvalidOperationException(
                $"LLM_PROVIDER không hợp lệ: '{_options.Provider}'. Ngày 1 chỉ hỗ trợ 'ollama'.")
        };
    }

    public async Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(_timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var client = CreateClient();
            // Gửi một tin nhắn kiểm tra cực ngắn để xác thực cả Endpoint và sự tồn tại của Model
            var testMessage = new ChatMessage(ChatRole.User, "ping");
            var testOptions = new ChatOptions { MaxOutputTokens = 1 };

            await client.GetResponseAsync([testMessage], testOptions, linkedCts.Token);
            return (true, $"Ollama và model '{_options.Model}' đã sẵn sàng kết nối.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return (false, $"Không phản hồi sau {_timeoutMs / 1000}s. Vui lòng kiểm tra xem Ollama có bị treo không.");
        }
        catch (Exception ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                                  ex.Message.Contains("pull", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Model '{_options.Model}' không tìm thấy trên Ollama. Vui lòng chạy lệnh: 'ollama pull {_options.Model}'.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Không thể kết nối tới Ollama tại '{_options.Ollama.Endpoint}'. Chi tiết: {ex.Message}. Vui lòng bật Ollama: 'ollama serve'.");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kiểm tra kết nối LLM: {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Chạy test để xác nhận tất cả 9 tests PASS**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: 9 passed, 0 failed.

- [ ] **Step 6: Commit Task 2**

```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot
git add code/src/ErpChatbot.Infrastructure/ code/tests/ErpChatbot.Tests/
git commit -m "feat(infrastructure): implement ChatClientFactory with Ollama and robust health checks"
```

---

### Task 3: Agent Layer (`ErpChatbot.Agent`)

**Files:**
- Create: `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`
- Create: `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`
- Create: `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`
- Modify: `code/ErpChatbot.sln`
- Modify: `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`

**Interfaces:**
- Consumes:
  - `ErpChatbot.Core.Interfaces.IErpAgentService`
  - `ErpChatbot.Core.Models.ErpOptions`
  - `ErpChatbot.Core.Models.SessionStats`
  - `Microsoft.Extensions.AI.IChatClient` (được inject từ bên ngoài, KHÔNG phụ thuộc Infrastructure!)
- Produces:
  - `ErpAgentService`: Triển khai `IErpAgentService` sử dụng `AIAgent` và `AgentSession` của `Microsoft.Agents.AI`. Quản lý streaming, bóc tách token từ `UsageContent`, phục hồi khi stream lỗi hoặc cancel mà không reset session.

- [ ] **Step 1: Khởi tạo project `ErpChatbot.Agent`**

Run:
```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code
dotnet new classlib -n ErpChatbot.Agent -o src/ErpChatbot.Agent -f net10.0
dotnet sln ErpChatbot.sln add src/ErpChatbot.Agent/ErpChatbot.Agent.csproj
dotnet add src/ErpChatbot.Agent/ErpChatbot.Agent.csproj reference src/ErpChatbot.Core/ErpChatbot.Core.csproj
dotnet add src/ErpChatbot.Agent/ErpChatbot.Agent.csproj package Microsoft.Agents.AI --version 1.24.0
dotnet add src/ErpChatbot.Agent/ErpChatbot.Agent.csproj package Microsoft.Extensions.AI --version 10.10.0
dotnet add src/ErpChatbot.Agent/ErpChatbot.Agent.csproj package Microsoft.Extensions.Options --version 10.0.0
dotnet add tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj reference src/ErpChatbot.Agent/ErpChatbot.Agent.csproj
rm -f src/ErpChatbot.Agent/Class1.cs
```

- [ ] **Step 2: Viết 6 test cases cho `ErpAgentServiceTests`**

Tạo file `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`:
```csharp
using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Services;
using ErpChatbot.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace ErpChatbot.Tests.Agent;

public class ErpAgentServiceTests
{
    private class FakeStreamingChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, IAsyncEnumerable<ChatResponseUpdate>> _streamGenerator;
        public List<List<ChatMessage>> RecordedHistory { get; } = [];

        public FakeStreamingChatClient(Func<IEnumerable<ChatMessage>, IAsyncEnumerable<ChatResponseUpdate>> streamGenerator)
        {
            _streamGenerator = streamGenerator;
        }

        public ChatClientMetadata Metadata => new("fake-agent");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RecordedHistory.Add(chatMessages.ToList());
            await foreach (var update in _streamGenerator(chatMessages).WithCancellation(cancellationToken))
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task RunStreamingAsync_YieldsStreamUpdatesInOrder()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunks(["Xin ", "chào ", "bạn!"]));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        var chunks = new List<string>();
        await foreach (var chunk in service.RunStreamingAsync("Chào"))
        {
            chunks.Add(chunk);
        }

        string.Join("", chunks).Should().Be("Xin chào bạn!");
    }

    [Fact]
    public async Task RunStreamingAsync_RetainsContextAcrossConsecutiveTurns()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunks(["OK"]));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Tôi tên Hùng ở kho")) { }
        await foreach (var _ in service.RunStreamingAsync("Tôi ở phòng nào?")) { }

        fakeClient.RecordedHistory.Should().HaveCount(2);
        var secondTurnMessages = fakeClient.RecordedHistory[1];
        secondTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Tôi tên Hùng ở kho"));
        secondTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Tôi ở phòng nào?"));
    }

    [Fact]
    public async Task RunStreamingAsync_AccumulatesTokensViaUsageContent()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunksWithUsage(["Xin ", "chào!"], inTokens: 15, outTokens: 5));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Lượt 1")) { }
        await foreach (var _ in service.RunStreamingAsync("Lượt 2")) { }

        var stats = service.GetStats();
        stats.TurnCount.Should().Be(2);
        stats.TotalInputTokens.Should().Be(30);
        stats.TotalOutputTokens.Should().Be(10);
    }

    [Fact]
    public async Task ResetSessionAsync_CreatesNewSession_RetainsSessionStats()
    {
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunksWithUsage(["OK"], 10, 5));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Lượt 1")) { }
        await service.ResetSessionAsync();
        await foreach (var _ in service.RunStreamingAsync("Lượt 2 sau reset")) { }

        var stats = service.GetStats();
        stats.TurnCount.Should().Be(2);
        stats.TotalInputTokens.Should().Be(20);

        // Turn 2 không được chứa context của Turn 1
        var secondTurnMessages = fakeClient.RecordedHistory[1];
        secondTurnMessages.Should().NotContain(m => m.Text != null && m.Text.Contains("Lượt 1"));
    }

    [Fact]
    public async Task RunStreamingAsync_WhenClientThrowsException_DoesNotResetSession()
    {
        bool shouldThrow = false;
        var fakeClient = new FakeStreamingChatClient(msgs =>
        {
            if (shouldThrow) throw new HttpRequestException("Mạng chập chờn");
            return CreateChunks(["Được rồi"]);
        });
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        // Turn 1 thành công
        await foreach (var _ in service.RunStreamingAsync("Ghi nhớ: mã kho là K1")) { }

        // Turn 2 lỗi mạng
        shouldThrow = true;
        var act = async () =>
        {
            await foreach (var _ in service.RunStreamingAsync("Gặp lỗi nè")) { }
        };
        await act.Should().ThrowAsync<HttpRequestException>();

        // Turn 3 thành công lại -> Ngữ cảnh Turn 1 vẫn còn, session không bị reset nhầm
        shouldThrow = false;
        await foreach (var _ in service.RunStreamingAsync("Mã kho là gì?")) { }

        var lastTurnMessages = fakeClient.RecordedHistory.Last();
        lastTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Ghi nhớ: mã kho là K1"));
    }

    [Fact]
    public async Task RunStreamingAsync_WhenCanceled_DoesNotResetSession()
    {
        var cts = new CancellationTokenSource();
        var fakeClient = new FakeStreamingChatClient(msgs => CreateChunks(["Đang trả lời..."]));
        var erpOptions = Options.Create(new ErpOptions());
        var service = new ErpAgentService(fakeClient, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Nhớ chữ X")) { }

        // Cancel lượt 2
        cts.Cancel();
        var act = async () =>
        {
            await foreach (var _ in service.RunStreamingAsync("Câu bị cancel", cts.Token)) { }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Lượt 3: context vẫn còn
        await foreach (var _ in service.RunStreamingAsync("Nhắc lại chữ X")) { }

        var lastTurnMessages = fakeClient.RecordedHistory.Last();
        lastTurnMessages.Should().Contain(m => m.Text != null && m.Text.Contains("Nhớ chữ X"));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateChunks(string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return new ChatResponseUpdate { Text = chunk };
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateChunksWithUsage(string[] chunks, long inTokens, long outTokens)
    {
        foreach (var chunk in chunks)
        {
            yield return new ChatResponseUpdate { Text = chunk };
            await Task.Yield();
        }

        var updateWithUsage = new ChatResponseUpdate();
        updateWithUsage.Contents.Add(new UsageContent(new UsageDetails
        {
            InputTokenCount = inTokens,
            OutputTokenCount = outTokens
        }));
        yield return updateWithUsage;
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận test FAIL (chưa có `ErpAgentService`)**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: Build compilation FAIL với lỗi không tìm thấy `ErpAgentService`.

- [ ] **Step 4: Triển khai `ErpAgentService` trong `ErpChatbot.Agent`**

Tạo file `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`:
```csharp
using System.Runtime.CompilerServices;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ErpChatbot.Agent.Services;

public class ErpAgentService : IErpAgentService
{
    private readonly AIAgent _agent;
    private readonly SessionStats _stats = new();
    private AgentSession? _session;

    public ErpAgentService(IChatClient chatClient, IOptions<ErpOptions> erpOptions)
    {
        var options = erpOptions.Value;

        // Bọc IChatClient thành AIAgent của Microsoft Agent Framework
        _agent = chatClient.AsAIAgent(
            instructions: options.Instructions,
            name: options.AssistantName
        );
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            yield break;
        }

        // Tự khởi tạo session nếu là lượt đầu tiên
        _session ??= await _agent.CreateSessionAsync(cancellationToken);

        long turnInputTokens = 0;
        long turnOutputTokens = 0;

        // Gọi RunStreamingAsync của AIAgent. Trả về IAsyncEnumerable<AgentResponseUpdate>
        var responseStream = _agent.RunStreamingAsync(userPrompt, _session, options: null, cancellationToken: cancellationToken);

        await using var enumerator = responseStream.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            AgentResponseUpdate update;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
                update = enumerator.Current;
            }
            catch
            {
                // Khi xảy ra Exception hoặc CancelKeyPress giữa chừng:
                // KHÔNG reset _session! Để nguyên session để người dùng không bị mất ngữ cảnh đã trao đổi trước đó.
                throw;
            }

            // Trích xuất text để stream ra UI
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }

            // Trích xuất số token qua UsageContent trong Contents
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent && usageContent.Details != null)
                    {
                        turnInputTokens += usageContent.Details.InputTokenCount ?? 0;
                        turnOutputTokens += usageContent.Details.OutputTokenCount ?? 0;
                    }
                }
            }
        }

        // Cập nhật thống kê token sau khi stream hoàn tất trọn vẹn
        _stats.RecordTurn(turnInputTokens, turnOutputTokens);
    }

    public async Task ResetSessionAsync()
    {
        // Tạo một session mới hoàn toàn, dọn dẹp ngữ cảnh cũ
        _session = await _agent.CreateSessionAsync();
        // LƯU Ý: Không reset _stats vì stats thống kê toàn bộ phiên làm việc của người dùng
    }

    public SessionStats GetStats() => _stats;
}
```

- [ ] **Step 5: Chạy test để xác nhận tất cả 15 tests PASS**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: 15 passed, 0 failed.

- [ ] **Step 6: Commit Task 3**

```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot
git add code/src/ErpChatbot.Agent/ code/tests/ErpChatbot.Tests/
git commit -m "feat(agent): implement ErpAgentService with MAF AIAgent and AgentSession streaming"
```

---

### Task 4: CLI Application & Interactive Composition Root (`ErpChatbot.Cli`)

**Files:**
- Create: `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj`
- Create: `code/src/ErpChatbot.Cli/appsettings.json`
- Create: `code/src/ErpChatbot.Cli/Program.cs`
- Create: `code/src/ErpChatbot.Cli/CliRunner.cs`
- Create: `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`
- Modify: `code/ErpChatbot.sln`
- Modify: `code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`

**Interfaces:**
- Consumes:
  - `ErpChatbot.Core` (Models, Interfaces)
  - `ErpChatbot.Infrastructure` (`ChatClientFactory`)
  - `ErpChatbot.Agent` (`ErpAgentService`)
- Produces:
  - CLI binary (`ErpChatbot.Cli`): Composition root, nạp config, health check, vòng lặp nhận lệnh `/reset`, `/stats`, `/info`, `/exit`, stream kết quả và bắt lỗi gracefully.

- [ ] **Step 1: Khởi tạo project `ErpChatbot.Cli`**

Run:
```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code
dotnet new console -n ErpChatbot.Cli -o src/ErpChatbot.Cli -f net10.0
dotnet sln ErpChatbot.sln add src/ErpChatbot.Cli/ErpChatbot.Cli.csproj
dotnet add src/ErpChatbot.Cli/ErpChatbot.Cli.csproj reference src/ErpChatbot.Core/ErpChatbot.Core.csproj
dotnet add src/ErpChatbot.Cli/ErpChatbot.Cli.csproj reference src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj
dotnet add src/ErpChatbot.Cli/ErpChatbot.Cli.csproj reference src/ErpChatbot.Agent/ErpChatbot.Agent.csproj
dotnet add src/ErpChatbot.Cli/ErpChatbot.Cli.csproj package Microsoft.Extensions.Hosting --version 10.0.0
dotnet add tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj reference src/ErpChatbot.Cli/ErpChatbot.Cli.csproj
```

- [ ] **Step 2: Viết test cho CLI input & command parser logic**

Tạo file `code/tests/ErpChatbot.Tests/Cli/CliCommandTests.cs`:
```csharp
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using FluentAssertions;
using Xunit;

namespace ErpChatbot.Tests.Cli;

public class CliCommandTests
{
    private class FakeAgentService : IErpAgentService
    {
        public int ResetCalls { get; private set; }
        public int PromptCalls { get; private set; }
        public SessionStats Stats { get; } = new();

        public async IAsyncEnumerable<string> RunStreamingAsync(string userPrompt, CancellationToken cancellationToken = default)
        {
            PromptCalls++;
            yield return "Phản hồi";
            await Task.Yield();
        }

        public Task ResetSessionAsync()
        {
            ResetCalls++;
            return Task.CompletedTask;
        }

        public SessionStats GetStats() => Stats;
    }

    [Fact]
    public async Task ProcessInput_WithResetCommand_ResetsSessionAndDoesNotCallLlm()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/reset", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.ResetCalls.Should().Be(1);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithStatsCommand_ShowsStatsAndDoesNotCallLlm()
    {
        var service = new FakeAgentService();
        service.Stats.RecordTurn(50, 20);
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/stats", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithInfoCommand_DisplaysConfiguration()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/info", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithInvalidSlashCommand_ShowsHintWithoutCallingLlm()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("/unknown", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        service.PromptCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessInput_WithEmptyOrWhitespace_IsIgnoredWithoutCallingLlm()
    {
        var service = new FakeAgentService();
        var handler = new CliCommandHandler(service, new LlmOptions(), new ErpOptions());

        var result = await handler.HandleInputAsync("   ", CancellationToken.None);

        result.Should().Be(CliInputResult.Ignored);
        service.PromptCalls.Should().Be(0);
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận test FAIL (chưa có `CliCommandHandler`)**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: Build compilation FAIL với lỗi không tìm thấy `CliCommandHandler`.

- [ ] **Step 4: Triển khai `CliCommandHandler`, `appsettings.json` và `Program.cs`**

Tạo file `code/src/ErpChatbot.Cli/CliRunner.cs`:
```csharp
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;

namespace ErpChatbot.Cli;

public enum CliInputResult
{
    Handled,
    Exit,
    Ignored,
    SendToLlm
}

public class CliCommandHandler
{
    private readonly IErpAgentService _agentService;
    private readonly LlmOptions _llmOptions;
    private readonly ErpOptions _erpOptions;

    public CliCommandHandler(IErpAgentService agentService, LlmOptions llmOptions, ErpOptions erpOptions)
    {
        _agentService = agentService;
        _llmOptions = llmOptions;
        _erpOptions = erpOptions;
    }

    public async Task<CliInputResult> HandleInputAsync(string input, CancellationToken cancellationToken)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return CliInputResult.Ignored;
        }

        if (trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            return CliInputResult.Exit;
        }

        if (trimmed.Equals("/reset", StringComparison.OrdinalIgnoreCase))
        {
            await _agentService.ResetSessionAsync();
            Console.WriteLine("\n[HỆ THỐNG] Đã xoá lịch sử phiên chat. Agent đã bắt đầu một phiên mới.\n");
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/stats", StringComparison.OrdinalIgnoreCase))
        {
            var stats = _agentService.GetStats();
            Console.WriteLine($"\n--- THỐNG KÊ PHIÊN CHAT ---");
            Console.WriteLine($"Số lượt trò chuyện: {stats.TurnCount}");
            Console.WriteLine($"Tổng Token vào (Input):  {stats.TotalInputTokens}");
            Console.WriteLine($"Tổng Token ra (Output): {stats.TotalOutputTokens}");
            Console.WriteLine($"Tổng Token đã dùng:     {stats.TotalTokens}\n");
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/info", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"\n--- THÔNG TIN HỆ THỐNG ---");
            Console.WriteLine($"Trợ lý:    {_erpOptions.AssistantName} ({_erpOptions.CompanyName})");
            Console.WriteLine($"Provider:  {_llmOptions.Provider}");
            Console.WriteLine($"Model:     {_llmOptions.Model}");
            Console.WriteLine($"Endpoint:  {_llmOptions.Ollama.Endpoint}");
            Console.WriteLine($"Lệnh sẵn sàng: /reset, /stats, /info, /exit\n");
            return CliInputResult.Handled;
        }

        if (trimmed.StartsWith("/"))
        {
            Console.WriteLine($"\n[CẢNH BÁO] Lệnh '{trimmed}' không hợp lệ. Gõ /info hoặc /help để xem danh sách lệnh.\n");
            return CliInputResult.Handled;
        }

        return CliInputResult.SendToLlm;
    }
}
```

Tạo file `code/src/ErpChatbot.Cli/appsettings.json`:
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

Cập nhật `code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj` để copy file cấu hình khi build:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ErpChatbot.Core\ErpChatbot.Core.csproj" />
    <ProjectReference Include="..\ErpChatbot.Infrastructure\ErpChatbot.Infrastructure.csproj" />
    <ProjectReference Include="..\ErpChatbot.Agent\ErpChatbot.Agent.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

Tạo file `code/src/ErpChatbot.Cli/Program.cs`:
```csharp
using System.Diagnostics;
using System.Text;
using ErpChatbot.Agent.Services;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Infrastructure.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// Cấu hình phân tầng: appsettings.json + Ghi đè bằng biến môi trường (theo chuẩn Ex3)
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<ErpOptions>(builder.Configuration.GetSection(ErpOptions.SectionName));

// Ghi đè biến môi trường trực tiếp theo quy ước tài liệu Ngày 1
var envProvider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
var envModel = Environment.GetEnvironmentVariable("LLM_MODEL");
var envEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT");

builder.Services.PostConfigure<LlmOptions>(opts =>
{
    if (!string.IsNullOrWhiteSpace(envProvider)) opts.Provider = envProvider;
    if (!string.IsNullOrWhiteSpace(envModel)) opts.Model = envModel;
    if (!string.IsNullOrWhiteSpace(envEndpoint)) opts.Ollama.Endpoint = envEndpoint;
});

// COMPOSITION ROOT:
// Đăng ký Factory và tạo IChatClient qua Factory, sau đó inject vào Agent
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IChatClientFactory>().CreateClient());

// Đăng ký ErpAgentService (1 instance per conversation)
builder.Services.AddScoped<IErpAgentService, ErpAgentService>();
builder.Services.AddScoped<CliCommandHandler>();

using var host = builder.Build();

var factory = host.Services.GetRequiredService<IChatClientFactory>();
var llmOptions = host.Services.GetRequiredService<IOptions<LlmOptions>>().Value;
var erpOptions = host.Services.GetRequiredService<IOptions<ErpOptions>>().Value;
var agentService = host.Services.GetRequiredService<IErpAgentService>();
var commandHandler = host.Services.GetRequiredService<CliCommandHandler>();

// 1. In Banner ứng dụng
Console.WriteLine("===============================================================");
Console.WriteLine($"   {erpOptions.AssistantName} — Microsoft Agent Framework (.NET 10)");
Console.WriteLine($"   Provider: {llmOptions.Provider} | Model: {llmOptions.Model}");
Console.WriteLine($"   Endpoint: {llmOptions.Ollama.Endpoint}");
Console.WriteLine("===============================================================");
Console.WriteLine("Gõ /info để xem trợ giúp, /reset để xoá lịch sử, /exit để thoát.\n");

// 2. Health Check kiểm tra kết nối tới mô hình
Console.Write("Đang kiểm tra kết nối LLM... ");
var (isHealthy, healthMessage) = await factory.CheckHealthAsync();
if (isHealthy)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[SẴN SÀNG] {healthMessage}\n");
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n[CẢNH BÁO KẾT NỐI] {healthMessage}\n");
    Console.ResetColor();
}

// 3. Vòng lặp Chat chính
while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Bạn: ");
    Console.ResetColor();

    string? rawInput = Console.ReadLine();
    if (rawInput == null) break;

    using var turnCts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) =>
    {
        e.Cancel = true; // Không tắt app, chỉ cancel lượt stream hiện tại
        turnCts.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        var inputResult = await commandHandler.HandleInputAsync(rawInput, turnCts.Token);
        if (inputResult == CliInputResult.Exit)
        {
            Console.WriteLine("Tạm biệt!");
            break;
        }
        if (inputResult != CliInputResult.SendToLlm)
        {
            continue;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Trợ lý: ");
        Console.ResetColor();

        var stopwatch = Stopwatch.StartNew();
        var prevTurnStats = agentService.GetStats();
        long prevTotalTokens = prevTurnStats.TotalTokens;

        await foreach (var chunk in agentService.RunStreamingAsync(rawInput, turnCts.Token))
        {
            Console.Write(chunk);
        }
        stopwatch.Stop();
        Console.WriteLine();

        var currentStats = agentService.GetStats();
        long turnTokens = currentStats.TotalTokens - prevTotalTokens;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [Thời gian: {stopwatch.ElapsedMilliseconds}ms | Token tích luỹ: {currentStats.TotalTokens}]");
        Console.ResetColor();
        Console.WriteLine();
    }
    catch (OperationCanceledException)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n(đã hủy phản hồi)");
        Console.ResetColor();
    }
    catch (HttpRequestException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[Mất kết nối tới Ollama: {ex.Message}. Thử lại hoặc gõ /reset]");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[Lỗi xử lý: {ex.Message}]");
        Console.ResetColor();
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}
```

- [ ] **Step 5: Chạy toàn bộ 20 Unit Tests để đảm bảo 100% test PASS**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/tests/ErpChatbot.Tests/ErpChatbot.Tests.csproj`
Expected: 20 passed, 0 failed.

- [ ] **Step 6: Build Solution để kiểm tra biên dịch trọn vẹn**

Run: `dotnet build /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/ErpChatbot.sln`
Expected: Build succeeded. 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Commit Task 4**

```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot
git add code/src/ErpChatbot.Cli/ code/tests/ErpChatbot.Tests/
git commit -m "feat(cli): implement interactive console host, composition root and command handler"
```

---

### Task 5: End-to-End Verification & Verification Report

**Files:**
- Test verification: Run `dotnet test`
- Verification check: Run `dotnet run --project code/src/ErpChatbot.Cli` (Health check test)
- Create: `code/README.md`

- [ ] **Step 1: Viết tài liệu README.md hướng dẫn chạy và kiến trúc của `code/`**

Tạo file `code/README.md`:
```markdown
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
```

- [ ] **Step 2: Chạy kiểm thử tự động toàn bộ Solution**

Run: `dotnet test /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/ErpChatbot.sln`
Expected: 20 passed, 0 failed.

- [ ] **Step 3: Chạy thử ứng dụng CLI để xác nhận banner và health check hoạt động đúng**

Run:
```bash
timeout 5s dotnet run --project /home/nguyen-thanh-hung/Documents/MAF-Chatbot/code/src/ErpChatbot.Cli/ErpChatbot.Cli.csproj || true
```
Expected: Banner in ra đầy đủ, health check báo trạng thái (hoặc cảnh báo kết nối nếu Ollama chưa chạy), không bị crash exception.

- [ ] **Step 4: Commit hoàn tất Task 5**

```bash
cd /home/nguyen-thanh-hung/Documents/MAF-Chatbot
git add code/README.md
git commit -m "docs: add code README and complete Day 1 MAF ERP Chatbot verification"
```
