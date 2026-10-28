# Design Document: Ngày 2 — Tích hợp Function Calling, Phân quyền RBAC & Duyệt Phê duyệt (MAF Chatbot ERP)

- **Ngày tạo**: 2026-10-28
- **Tác giả**: Nguyen Thanh Hung & Antigravity
- **Dự án**: MAF-Chatbot ERP
- **Trạng thái**: Chờ người dùng nghiệm thu (Pending User Review)
- **Tài liệu căn cứ**: `tutorial/ngay-02/` (`Ngay-02-Function-calling.pdf`, `giai-thich-ngay-02-function-calling.md`, các bài mẫu Ex1–Ex4).

---

## 1. Bối cảnh & Mục tiêu

### 1.1. Bối cảnh từ Ngày 2
Trong Ngày 1, Chatbot ERP chỉ giao tiếp và trả lời *"Tôi chưa có dữ liệu ERP"*. Ngày 2 đưa chatbot bước sang giai đoạn có dữ liệu nghiệp vụ thật sự bằng kỹ thuật **Function Calling**:
- **Ex1**: Vòng lặp thủ công (`FunctionCallContent`, `FunctionResultContent`, `CallId`).
- **Ex2**: Tự động hóa qua middleware `UseFunctionInvocation()` và trích xuất `response.Messages`, token `Usage`.
- **Ex3**: Tích hợp nhiều tool đọc (`get_stock`, `get_order_status`, `list_low_stock`), cấu hình middleware an toàn (`MaximumIterationsPerRequest = 5`, `MaximumConsecutiveErrorsPerRequest = 2`, `IncludeDetailedErrors = false`, `FunctionInvoker` đo ms và log), trả lời streaming.
- **Ex4**: Nâng cấp lên Microsoft Agent Framework (MAF `ChatClientAgent`), bọc tool ghi (`create_purchase_request`) bằng `ApprovalRequiredAIFunction`, yêu cầu người dùng phê duyệt (`ToolApprovalRequestContent`, hỏi `y/n`) trước khi thực thi ghi dữ liệu.
- **Các bài tập mở rộng được chọn**:
  - Bổ sung tool tra cứu công nợ khách hàng `get_customer_debt`.
  - Phân quyền người dùng theo chuẩn **NIST RBAC** (Role-Based Access Control) ngăn chặn vai trò `Sales` tra cứu công nợ.
  - Phê duyệt có điều kiện (Conditional Approval): Tự động duyệt nếu số lượng mua `<= 50`, chỉ hỏi người dùng khi số lượng `> 50`.

### 1.2. Mục tiêu kiến trúc
1. **Bảo tồn Clean Architecture**:
   - `ErpChatbot.Core`: Chứa abstractions, models, event definitions, DTOs, interfaces. Không phụ thuộc thư viện AI bên ngoài (trừ `Microsoft.Extensions.AI.Abstractions`).
   - `ErpChatbot.Infrastructure`: Triển khai `IErpDataService` (`InMemoryErpDataService`) và `IChatClientFactory`.
   - `ErpChatbot.Agent`: Đóng gói `AIAgent`, định nghĩa các `AIFunction`, quản lý vòng đời hội thoại và điều phối event stream. **Agent hoàn toàn không phụ thuộc Infrastructure**.
   - `ErpChatbot.Cli`: Composition Root kết nối toàn bộ hệ thống qua Dependency Injection, hiển thị console stream và xử lý tương tác duyệt người dùng.
2. **Event-Driven Streaming**:
   - `IErpAgentService.RunStreamingAsync` phát ra luồng sự kiện `AgentResponseEvent` (`AgentTextChunkEvent`, `AgentToolExecutionEvent`, `AgentApprovalRequiredEvent`, `AgentTurnCompletedEvent`). Sẵn sàng 100% cho Web API / SignalR / SSE ở Giai đoạn 4.
3. **An toàn & Bảo mật**:
   - Chặn lặp vô hạn (tối đa 5 vòng lặp).
   - Ẩn lỗi nội bộ (`IncludeDetailedErrors = false`), tránh lộ schema/SQL ra mô hình.
   - RBAC chuẩn: Phân quyền theo Granular Permission (`Resource:Action`).
4. **Kiểm thử độc lập**:
   - Bộ unit tests với Fake `IChatClient` và in-memory data, kiểm thử 100% các nhánh RBAC, Conditional Approval, và Streaming Events mà không cần Ollama thật.

---

## 2. Kiến trúc Hệ thống & Phân tầng (.NET 10)

```mermaid
graph TD
    subgraph "ErpChatbot.Cli (Console App & Composition Root)"
        CLI[CliRunner / Program.cs]
        CLI_HANDLER[CliCommandHandler]
    end

    subgraph "ErpChatbot.Agent (MAF AIAgent & Tool Registrations)"
        AGENT_SVC[ErpAgentService]
        TOOLS[ErpTools]
        INVOKER[RbacFunctionInvoker]
    end

    subgraph "ErpChatbot.Core (Domain, Contracts & RBAC)"
        I_AGENT[IErpAgentService]
        I_DATA[IErpDataService]
        I_USER[IUserContext]
        MODELS[StockInfo / OrderInfo / DebtInfo / PurchaseRequest]
        EVENTS[AgentResponseEvent Stream]
        RBAC[User, Role, Permission]
    end

    subgraph "ErpChatbot.Infrastructure (Adapters & Mock ERP)"
        IN_MEMORY_ERP[InMemoryErpDataService]
        CHAT_FACTORY[ChatClientFactory]
    end

    CLI --> AGENT_SVC
    CLI --> CHAT_FACTORY
    CLI --> IN_MEMORY_ERP
    CLI --> CLI_HANDLER

    AGENT_SVC --> I_AGENT
    AGENT_SVC --> TOOLS
    TOOLS --> I_DATA
    TOOLS --> I_USER
    INVOKER --> I_USER

    IN_MEMORY_ERP --> I_DATA
    CHAT_FACTORY --> I_USER
    AGENT_SVC --> EVENTS
```

### 2.1. Nâng cấp Package Dependencies
- `Microsoft.Agents.AI`: Nâng cấp lên `1.25.0` (hỗ trợ đầy đủ `ApprovalRequiredAIFunction`, `ToolApprovalRequestContent`, `ToolApprovalResponseContent`).
- `Microsoft.Extensions.AI`: Nâng cấp lên `10.11.0`.
- `Microsoft.Extensions.AI.Abstractions`: `10.11.0`.

---

## 3. Thiết kế Chi tiết các Phân hệ

### 3.1. Hệ thống RBAC Chuẩn Doanh nghiệp (`ErpChatbot.Core/Models/Rbac/`)

Mô hình dựa trên tiêu chuẩn NIST RBAC với Granular Permissions:

#### Permissions (`Permissions.cs`):
```csharp
public static class ErpPermissions
{
    public const string InventoryRead = "Inventory:Read";
    public const string OrdersRead = "Orders:Read";
    public const string DebtRead = "Debt:Read";
    public const string PurchaseRequestCreate = "PurchaseRequest:Create";
}
```

#### Roles & Users:
- **`Role`**:
  - `Sales`: Có quyền `Inventory:Read`, `Orders:Read`, `PurchaseRequest:Create`. *(Không có quyền `Debt:Read`)*.
  - `Accountant`: Có quyền `Debt:Read`, `Inventory:Read`, `Orders:Read`.
  - `Admin`: Sở hữu toàn bộ quyền hạn.
- **`User`**:
  - `Id`, `Username`, `FullName`, `IReadOnlyList<Role> Roles`, `IReadOnlySet<string> EffectivePermissions`.
- **Mock Users**:
  - `sales_user` (Nguyễn Văn Bán - Nhân viên kinh doanh): Role `Sales`.
  - `accountant_user` (Trần Thị Kế Toán - Kế toán công nợ): Role `Accountant`.
  - `admin_user` (Lê Quản Trị - Quản trị viên hệ thống): Role `Admin`.

#### Ngữ cảnh người dùng (`IUserContext`):
```csharp
public interface IUserContext
{
    User CurrentUser { get; }
    void SwitchUser(string username);
    bool HasPermission(string permission);
}
```

---

### 3.2. Dữ liệu Nghiệp vụ & Dịch vụ ERP (`IErpDataService`)

#### Models/DTOs (`ErpChatbot.Core/Models/ErpDataModels.cs`):
- `StockInfo(string ProductCode, string ProductName, int Quantity, string Unit, string Location = "Kho Hà Nội")`
- `OrderStatusInfo(string OrderCode, string Customer, string Status, string Eta)`
- `CustomerDebtInfo(string CustomerCode, string CustomerName, decimal DebtAmount, string Currency = "VND", string DueDate = "")`
- `PurchaseRequestResult(string RequestId, string ProductCode, int Quantity, string Status)`

#### Hợp đồng dịch vụ (`IErpDataService.cs`):
```csharp
public interface IErpDataService
{
    Task<StockInfo> GetStockAsync(string productCode, CancellationToken ct = default);
    Task<OrderStatusInfo> GetOrderStatusAsync(string orderCode, CancellationToken ct = default);
    Task<IReadOnlyList<StockInfo>> ListLowStockAsync(int threshold, CancellationToken ct = default);
    Task<CustomerDebtInfo> GetCustomerDebtAsync(string customerCode, CancellationToken ct = default);
    Task<PurchaseRequestResult> CreatePurchaseRequestAsync(string productCode, int quantity, CancellationToken ct = default);
}
```

#### Cài đặt trong bộ nhớ (`InMemoryErpDataService.cs`):
- Dữ liệu tồn kho:
  - `SP-001`: Bàn phím cơ K87 (124 cái)
  - `SP-002`: Chuột không dây M3 (8 cái)
  - `SP-003`: Màn hình 27 inch Q27 (3 chiếc)
  - `SP-004`: Cáp HDMI 2m (560 sợi)
- Dữ liệu đơn hàng:
  - `DH-1001`: Công ty An Phát (Đang giao, 2026-10-23)
  - `DH-1002`: Cửa hàng Minh Long (Chờ xuất kho, 2026-10-25)
- Dữ liệu công nợ:
  - `KH-001`: Công ty TNHH Ánh Dương (45.000.000 VND, Hạn: 2026-11-15)
  - `KH-002`: Đại lý Hoàng Gia (120.500.000 VND, Quá hạn: 2026-10-30)
  - `KH-003`: Doanh nghiệp Song Toàn (0 VND)
- Đề xuất mua hàng: Sinh mã ngẫu nhiên `PR-xxxx`, trạng thái *"Chờ phê duyệt của trưởng phòng mua hàng"*.

---

### 3.3. Định nghĩa Tools trong MAF (`ErpChatbot.Agent/Tools/ErpTools.cs`)

Định nghĩa 5 tools rõ ràng, có `[Description]` chi tiết và tên tường minh:
1. `get_stock(productCode)`: Tra cứu số lượng tồn kho sản phẩm (Quyền: `Inventory:Read`).
2. `get_order_status(orderCode)`: Tra cứu trạng thái đơn bán hàng (Quyền: `Orders:Read`).
3. `list_low_stock(threshold)`: Liệt kê các sản phẩm có tồn kho thấp hơn ngưỡng (Quyền: `Inventory:Read`).
4. `get_customer_debt(customerCode)`: Tra cứu công nợ khách hàng (Quyền: `Debt:Read`).
5. `create_purchase_request(productCode, quantity)`: Tạo đề xuất mua hàng (Quyền: `PurchaseRequestCreate`, bọc bằng `ApprovalRequiredAIFunction`).

---

### 3.4. Hệ thống Sự kiện Stream (`AgentResponseEvent`)

```csharp
public abstract record AgentResponseEvent;

public record AgentTextChunkEvent(string Text) : AgentResponseEvent;

public record AgentToolExecutionEvent(
    string ToolName, 
    string ArgumentsSummary, 
    long ElapsedMs, 
    bool Success, 
    string? Error = null) : AgentResponseEvent;

public record AgentApprovalRequiredEvent(
    string ToolName, 
    IReadOnlyDictionary<string, object?> Arguments, 
    string Summary) : AgentResponseEvent;

public record AgentTurnCompletedEvent(
    long InputTokens, 
    long OutputTokens, 
    long ElapsedMs) : AgentResponseEvent;
```

---

### 3.5. Quy trình Điều phối & Duyệt Phê duyệt (`ErpAgentService.cs`)

```mermaid
sequenceDiagram
    autonumber
    actor User as Người dùng CLI
    participant Service as ErpAgentService
    participant Agent as AIAgent (MAF)
    participant Invoker as FunctionInvoker (RBAC)
    participant Erp as InMemoryErpDataService

    User->>Service: RunStreamingAsync(prompt, approvalHandler)
    Service->>Agent: RunStreamingAsync(prompt, session)
    
    alt Tool đọc (get_stock / get_order_status / get_customer_debt)
        Agent->>Invoker: Gọi tool qua middleware
        Invoker->>Invoker: Kiểm tra IUserContext.HasPermission
        alt Thiếu quyền (sales tra nợ)
            Invoker-->>Agent: Ném UnauthorizedAccessException
            Agent-->>Service: Stream giải thích không có quyền
        else Đủ quyền
            Invoker->>Erp: Thực thi truy vấn
            Erp-->>Invoker: Trả kết quả DTO
            Invoker-->>Agent: Trả JSON kết quả
            Agent-->>Service: Stream câu trả lời chứa số liệu thật
        end
    else Tool ghi (create_purchase_request)
        Agent-->>Service: Phát hiện ToolApprovalRequestContent
        alt Số lượng <= 50 (Tự động duyệt)
            Service->>Service: request.CreateResponse(true)
            Service->>Agent: RunStreamingAsync([resumeMessage], session)
            Agent->>Invoker: Gọi hàm ghi vào ERP
            Invoker->>Erp: Tạo phiếu PR-xxxx
            Erp-->>Invoker: PurchaseRequestResult
            Agent-->>Service: Stream câu trả lời tạo thành công
        else Số lượng > 50 (Cần duyệt)
            Service->>User: approvalHandler(AgentApprovalRequiredEvent)
            User-->>Service: Phản hồi duyệt (y/n)
            Service->>Agent: RunStreamingAsync([CreateResponse(ok)], session)
            alt ok = true
                Agent->>Invoker: Gọi hàm ghi vào ERP
                Agent-->>Service: Stream thông báo đã tạo đề xuất
            else ok = false
                Agent-->>Service: Stream thông báo đề xuất bị từ chối
            end
        end
    end
    Service-->>User: AgentTurnCompletedEvent
```

---

### 3.6. Cấu hình Middleware An toàn (`ChatClientFactory.cs`)
```csharp
new ChatClientBuilder(innerClient)
    .UseFunctionInvocation(configure: f =>
    {
        f.MaximumIterationsPerRequest = 5;          // Chặn loop vô hạn
        f.MaximumConsecutiveErrorsPerRequest = 2;   // Ngắt nếu lỗi liên tiếp 2 lần
        f.IncludeDetailedErrors = false;            // Ẩn lỗi SQL/stack trace
        f.AllowConcurrentInvocation = false;        // Chạy tuần tự

        f.FunctionInvoker = async (ctx, ct) =>
        {
            var sw = Stopwatch.StartNew();
            // 1. Kiểm tra quyền hạn RBAC
            string toolName = ctx.Function.Name;
            string requiredPermission = GetRequiredPermission(toolName);
            if (!_userContext.HasPermission(requiredPermission))
            {
                throw new UnauthorizedAccessException(
                    $"Tài khoản '{_userContext.CurrentUser.Username}' (vai trò: {string.Join(", ", _userContext.CurrentUser.Roles.Select(r => r.Name))}) " +
                    $"không có quyền '{requiredPermission}' để thực thi tool '{toolName}'.");
            }

            // 2. Thực thi hàm
            try
            {
                var result = await ctx.Function.InvokeAsync(ctx.Arguments, ct);
                sw.Stop();
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                throw;
            }
        };
    })
    .Build();
```

---

### 3.7. Trải nghiệm & Lệnh CLI (`ErpChatbot.Cli`)
- **Lệnh điều khiển phiên**:
  - `/user <username>`: Chuyển đổi giữa `sales_user`, `accountant_user`, `admin_user`.
  - `/whoami`: Hiển thị thông tin người dùng đang đăng nhập, các Role và Permissions.
  - `/reset`: Bắt đầu phiên hội thoại mới, dọn sạch session nhưng bảo lưu tổng token.
  - `/stats`: Hiển thị số lượt chat và token sử dụng.
  - `/info`: Xem cấu hình hệ thống và danh sách lệnh.
  - `/exit`: Thoát chương trình.
- **Tương tác Phê duyệt**:
  Khi phát sinh `AgentApprovalRequiredEvent`:
  ```
     [cần duyệt] create_purchase_request(productCode=SP-003, quantity=100). Đồng ý? (y/n): 
  ```
  Nhập `y` / `Y` -> duyệt; bất kỳ ký tự nào khác -> từ chối an toàn.

---

## 4. Chiến lược Kiểm thử (Testing Strategy)

Toàn bộ test suites sử dụng xUnit, FluentAssertions, và Fake `IChatClient` (không cần Ollama thật):

| Test Suite | File Kiểm thử | Trọng tâm kiểm thử |
|---|---|---|
| **Core RBAC** | `Tests/Core/UserContextTests.cs` | - Kiểm tra danh sách permissions của `Sales`, `Accountant`, `Admin`.<br/>- Kiểm tra hàm `SwitchUser` và `HasPermission`. |
| **Core DTOs** | `Tests/Core/ErpDataModelsTests.cs` | - Khởi tạo và tính toàn vẹn của DTOs và Event records. |
| **Infrastructure ERP** | `Tests/Infrastructure/InMemoryErpDataServiceTests.cs` | - Tra tồn kho SP-001 (124 cái), lỗi SP-999.<br/>- Tra đơn DH-1001.<br/>- Lọc hàng sắp hết threshold = 10.<br/>- Tra công nợ KH-001, KH-002.<br/>- Tạo PR-xxxx thành công. |
| **Agent Tools** | `Tests/Agent/ErpToolsTests.cs` | - Metadata của 5 tools (Name, Description, Parameters schema).<br/>- Tool ghi được bọc bởi `ApprovalRequiredAIFunction`. |
| **RBAC Security** | `Tests/Agent/RbacFunctionInvokerTests.cs` | - `sales_user` gọi `get_customer_debt` -> ném `UnauthorizedAccessException`.<br/>- `accountant_user` gọi `get_customer_debt` -> thành công. |
| **Conditional Approval** | `Tests/Agent/ConditionalApprovalTests.cs` | - Quantity <= 50 -> Tự động duyệt.<br/>- Quantity > 50 -> Phát sinh event hỏi duyệt. |
| **Agent Service Streaming** | `Tests/Agent/ErpAgentServiceTests.cs` | - Phát các event chunk và event hoàn tất lượt.<br/>- `ResetSessionAsync` không làm mất `SessionStats`. |
| **CLI Commands** | `Tests/Cli/CliCommandHandlerTests.cs` | - `/user`, `/whoami`, `/reset`, `/stats`, `/info`. |

---

## 5. Ranh giới Phạm vi (Scope Boundaries)

### Thuộc phạm vi Ngày 2 (IN SCOPE):
- Tích hợp 4 tool đọc (`get_stock`, `get_order_status`, `list_low_stock`, `get_customer_debt`) và 1 tool ghi (`create_purchase_request`).
- Hệ thống phân quyền RBAC chuẩn (User, Role, Granular Permission).
- Phê duyệt ghi có điều kiện (quantity > 50) và luồng Event-driven tương tác duyệt trên CLI.
- Cấu hình an toàn middleware (5 vòng lặp, ẩn lỗi nhạy cảm, log thời gian ms).
- Nâng cấp `Microsoft.Agents.AI` lên `1.25.0` và `Microsoft.Extensions.AI` lên `10.11.0`.

### Không thuộc phạm vi Ngày 2 (OUT OF SCOPE / Lùi lại):
- Kết nối cơ sở dữ liệu thật hoặc API ERP .NET Framework legacy (Dành cho Ngày 3).
- Giao thức MCP (Model Context Protocol) Server (Dành cho Ngày 3).
- Web Chat AG-UI / ASP.NET Core API hosting (Dành cho Ngày 4).
- Observability / OpenTelemetry / RAG tài liệu (Dành cho Ngày 5).
