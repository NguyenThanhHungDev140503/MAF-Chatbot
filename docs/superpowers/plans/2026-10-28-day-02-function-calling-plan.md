# Day 2: Function Calling, RBAC & Human-in-the-Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hiện thực hóa trọn vẹn Giai đoạn Ngày 2 (Function Calling) vào codebase `code/` của MAF Chatbot ERP, bao gồm bộ 4 tool đọc, 1 tool ghi có duyệt điều kiện, hệ thống phân quyền NIST RBAC chuẩn doanh nghiệp, luồng phản hồi Event Streaming và cấu hình middleware an toàn.

**Architecture:** Mở rộng Solution .NET 10 theo Clean Architecture: `Core` định nghĩa DTOs, Event stream, RBAC models và interfaces; `Infrastructure` triển khai `InMemoryErpDataService` và cấu hình an toàn cho `ChatClientFactory`; `Agent` định nghĩa 5 `AIFunction` tools, `RbacFunctionInvoker`, và `ErpAgentService` điều phối streaming cùng cơ chế resuming phê duyệt MAF; `Cli` làm Composition Root kết nối DI, hiển thị stream, cung cấp lệnh `/user`, `/whoami` và giao diện hỏi duyệt người dùng.

**Tech Stack:** .NET 10, `Microsoft.Agents.AI` 1.25.0, `Microsoft.Extensions.AI` 10.11.0, `OllamaSharp` 5.5.0, xUnit, FluentAssertions.

## Global Constraints
- Target Framework: `net10.0`.
- Dependency rule: `ErpChatbot.Agent` chỉ phụ thuộc `ErpChatbot.Core`, **tuyệt đối không phụ thuộc** `ErpChatbot.Infrastructure`.
- Composition root đặt tại `ErpChatbot.Cli`.
- Session ownership: Mỗi `ErpAgentService` quản lý một phiên `AgentSession` riêng biệt; `ResetSessionAsync` làm sạch ngữ cảnh nhưng **không** reset `SessionStats`.
- Security: `IncludeDetailedErrors = false`, `MaximumIterationsPerRequest = 5`, `MaximumConsecutiveErrorsPerRequest = 2`.
- Duyệt an toàn mặc định: Chỉ `y` / `Y` là chấp thuận duyệt; mọi ký tự khác là từ chối.
- Unit tests độc lập: Chạy 100% với Fake `IChatClient` và in-memory data, không yêu cầu Ollama server thật.

---

### Task 1: Package Upgrades & Core Domain Models (RBAC, DTOs, Event Models, Interfaces)

**Files:**
- Modify: `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`
- Modify: `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`
- Modify: `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`
- Create: `code/src/ErpChatbot.Core/Models/ErpDataModels.cs`
- Create: `code/src/ErpChatbot.Core/Models/AgentEvents.cs`
- Create: `code/src/ErpChatbot.Core/Models/Rbac/ErpPermissions.cs`
- Create: `code/src/ErpChatbot.Core/Models/Rbac/RbacModels.cs`
- Create: `code/src/ErpChatbot.Core/Interfaces/IUserContext.cs`
- Create: `code/src/ErpChatbot.Core/Interfaces/IErpDataService.cs`
- Modify: `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`
- Create: `code/tests/ErpChatbot.Tests/Core/UserContextTests.cs`
- Create: `code/tests/ErpChatbot.Tests/Core/ErpDataModelsTests.cs`

**Interfaces:**
- Produces:
  - `ErpPermissions` (`InventoryRead`, `OrdersRead`, `DebtRead`, `PurchaseRequestCreate`)
  - `User`, `Role`, `UserContext`, `IUserContext`
  - `StockInfo`, `OrderStatusInfo`, `CustomerDebtInfo`, `PurchaseRequestResult`
  - `AgentResponseEvent` (`AgentTextChunkEvent`, `AgentToolExecutionEvent`, `AgentApprovalRequiredEvent`, `AgentTurnCompletedEvent`)
  - `IErpDataService`
  - `IErpAgentService` (with Event stream and `Func<AgentApprovalRequiredEvent, Task<bool>>` callback)

- [ ] **Step 1: Write the failing unit tests for Core Models & UserContext**

Tạo file `code/tests/ErpChatbot.Tests/Core/UserContextTests.cs`:
```csharp
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models.Rbac;
using FluentAssertions;

namespace ErpChatbot.Tests.Core;

public class UserContextTests
{
    [Fact]
    public void SalesUser_ShouldHave_InventoryAndOrderAndPr_ButNotDebtPermission()
    {
        var context = new UserContext("sales_user");

        context.CurrentUser.Username.Should().Be("sales_user");
        context.HasPermission(ErpPermissions.InventoryRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.OrdersRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.PurchaseRequestCreate).Should().BeTrue();
        context.HasPermission(ErpPermissions.DebtRead).Should().BeFalse();
    }

    [Fact]
    public void AccountantUser_ShouldHave_DebtPermission()
    {
        var context = new UserContext("accountant_user");

        context.CurrentUser.Username.Should().Be("accountant_user");
        context.HasPermission(ErpPermissions.DebtRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.InventoryRead).Should().BeTrue();
    }

    [Fact]
    public void AdminUser_ShouldHave_AllPermissions()
    {
        var context = new UserContext("admin_user");

        context.HasPermission(ErpPermissions.InventoryRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.OrdersRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.DebtRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.PurchaseRequestCreate).Should().BeTrue();
    }

    [Fact]
    public void SwitchUser_ShouldUpdateCurrentUser_AndPermissions()
    {
        var context = new UserContext("sales_user");
        context.HasPermission(ErpPermissions.DebtRead).Should().BeFalse();

        context.SwitchUser("accountant_user");

        context.CurrentUser.Username.Should().Be("accountant_user");
        context.HasPermission(ErpPermissions.DebtRead).Should().BeTrue();
    }

    [Fact]
    public void SwitchUser_WithUnknownUser_ShouldThrowArgumentException()
    {
        var context = new UserContext("sales_user");
        var act = () => context.SwitchUser("non_existent_user");
        act.Should().Throw<ArgumentException>();
    }
}
```

Tạo file `code/tests/ErpChatbot.Tests/Core/ErpDataModelsTests.cs`:
```csharp
using ErpChatbot.Core.Models;
using FluentAssertions;

namespace ErpChatbot.Tests.Core;

public class ErpDataModelsTests
{
    [Fact]
    public void ErpDataModels_ShouldInitializeCorrectly()
    {
        var stock = new StockInfo("SP-001", "Bàn phím cơ K87", 124, "cái", "Kho Hà Nội");
        stock.ProductCode.Should().Be("SP-001");
        stock.Quantity.Should().Be(124);

        var order = new OrderStatusInfo("DH-1001", "Công ty An Phát", "Đang giao", "2026-10-23");
        order.Status.Should().Be("Đang giao");

        var debt = new CustomerDebtInfo("KH-001", "Công ty TNHH Ánh Dương", 45000000m, "VND", "2026-11-15");
        debt.DebtAmount.Should().Be(45000000m);

        var pr = new PurchaseRequestResult("PR-1234", "SP-003", 20, "Chờ duyệt");
        pr.RequestId.Should().Be("PR-1234");
    }

    [Fact]
    public void AgentResponseEvents_ShouldBeInstantiable()
    {
        AgentResponseEvent textEvt = new AgentTextChunkEvent("Xin chào");
        textEvt.Should().BeOfType<AgentTextChunkEvent>();

        AgentResponseEvent appEvt = new AgentApprovalRequiredEvent("create_pr", new Dictionary<string, object?> { ["qty"] = 10 }, "Duyệt mua");
        appEvt.Should().BeOfType<AgentApprovalRequiredEvent>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Core"`
Expected: FAIL (Compile error do các type chưa tồn tại).

- [ ] **Step 3: Update csproj packages and implement Core types**

Cập nhật `code/src/ErpChatbot.Core/ErpChatbot.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.11.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
  </ItemGroup>
</Project>
```

Cập nhật `code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\ErpChatbot.Core\ErpChatbot.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.11.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
    <PackageReference Include="OllamaSharp" Version="5.5.0" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

Cập nhật `code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\ErpChatbot.Core\ErpChatbot.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.25.0" />
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.11.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

Tạo file `code/src/ErpChatbot.Core/Models/ErpDataModels.cs`:
```csharp
namespace ErpChatbot.Core.Models;

public record StockInfo(
    string ProductCode,
    string ProductName,
    int Quantity,
    string Unit,
    string Location = "Kho Hà Nội");

public record OrderStatusInfo(
    string OrderCode,
    string Customer,
    string Status,
    string Eta);

public record CustomerDebtInfo(
    string CustomerCode,
    string CustomerName,
    decimal DebtAmount,
    string Currency = "VND",
    string DueDate = "");

public record PurchaseRequestResult(
    string RequestId,
    string ProductCode,
    int Quantity,
    string Status);
```

Tạo file `code/src/ErpChatbot.Core/Models/AgentEvents.cs`:
```csharp
namespace ErpChatbot.Core.Models;

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

Tạo file `code/src/ErpChatbot.Core/Models/Rbac/ErpPermissions.cs`:
```csharp
namespace ErpChatbot.Core.Models.Rbac;

public static class ErpPermissions
{
    public const string InventoryRead = "Inventory:Read";
    public const string OrdersRead = "Orders:Read";
    public const string DebtRead = "Debt:Read";
    public const string PurchaseRequestCreate = "PurchaseRequest:Create";
}
```

Tạo file `code/src/ErpChatbot.Core/Models/Rbac/RbacModels.cs`:
```csharp
namespace ErpChatbot.Core.Models.Rbac;

public record Role(string Name, string Description, IReadOnlySet<string> Permissions);

public record User(
    string Id,
    string Username,
    string FullName,
    IReadOnlyList<Role> Roles)
{
    public IReadOnlySet<string> EffectivePermissions { get; } =
        Roles.SelectMany(r => r.Permissions).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
```

Tạo file `code/src/ErpChatbot.Core/Interfaces/IUserContext.cs`:
```csharp
using ErpChatbot.Core.Models.Rbac;

namespace ErpChatbot.Core.Interfaces;

public interface IUserContext
{
    User CurrentUser { get; }
    void SwitchUser(string username);
    bool HasPermission(string permission);
}
```

Tạo file `code/src/ErpChatbot.Core/Models/Rbac/UserContext.cs`:
```csharp
using ErpChatbot.Core.Interfaces;

namespace ErpChatbot.Core.Models.Rbac;

public class UserContext : IUserContext
{
    private static readonly Role RoleSales = new(
        "Sales",
        "Nhân viên kinh doanh",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ErpPermissions.InventoryRead,
            ErpPermissions.OrdersRead,
            ErpPermissions.PurchaseRequestCreate
        });

    private static readonly Role RoleAccountant = new(
        "Accountant",
        "Kế toán công nợ",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ErpPermissions.DebtRead,
            ErpPermissions.InventoryRead,
            ErpPermissions.OrdersRead
        });

    private static readonly Role RoleAdmin = new(
        "Admin",
        "Quản trị hệ thống",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ErpPermissions.InventoryRead,
            ErpPermissions.OrdersRead,
            ErpPermissions.DebtRead,
            ErpPermissions.PurchaseRequestCreate
        });

    public static readonly IReadOnlyDictionary<string, User> MockUsers = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase)
    {
        ["sales_user"] = new("USR-01", "sales_user", "Nguyễn Văn Bán", [RoleSales]),
        ["accountant_user"] = new("USR-02", "accountant_user", "Trần Thị Kế Toán", [RoleAccountant]),
        ["admin_user"] = new("USR-03", "admin_user", "Lê Quản Trị", [RoleAdmin])
    };

    private User _currentUser;

    public UserContext(string initialUsername = "sales_user")
    {
        if (!MockUsers.TryGetValue(initialUsername, out var user))
        {
            throw new ArgumentException($"User '{initialUsername}' không tồn tại trong danh sách mẫu.", nameof(initialUsername));
        }
        _currentUser = user;
    }

    public User CurrentUser => _currentUser;

    public void SwitchUser(string username)
    {
        if (!MockUsers.TryGetValue(username, out var user))
        {
            throw new ArgumentException($"User '{username}' không tồn tại trong danh sách mẫu.", nameof(username));
        }
        _currentUser = user;
    }

    public bool HasPermission(string permission) =>
        _currentUser.EffectivePermissions.Contains(permission);
}
```

Tạo file `code/src/ErpChatbot.Core/Interfaces/IErpDataService.cs`:
```csharp
using ErpChatbot.Core.Models;

namespace ErpChatbot.Core.Interfaces;

public interface IErpDataService
{
    Task<StockInfo> GetStockAsync(string productCode, CancellationToken ct = default);
    Task<OrderStatusInfo> GetOrderStatusAsync(string orderCode, CancellationToken ct = default);
    Task<IReadOnlyList<StockInfo>> ListLowStockAsync(int threshold, CancellationToken ct = default);
    Task<CustomerDebtInfo> GetCustomerDebtAsync(string customerCode, CancellationToken ct = default);
    Task<PurchaseRequestResult> CreatePurchaseRequestAsync(string productCode, int quantity, CancellationToken ct = default);
}
```

Cập nhật `code/src/ErpChatbot.Core/Interfaces/IErpAgentService.cs`:
```csharp
using ErpChatbot.Core.Models;

namespace ErpChatbot.Core.Interfaces;

public interface IErpAgentService
{
    IAsyncEnumerable<AgentResponseEvent> RunStreamingAsync(
        string userPrompt,
        Func<AgentApprovalRequiredEvent, Task<bool>>? approvalHandler = null,
        CancellationToken cancellationToken = default);

    Task ResetSessionAsync();
    SessionStats GetStats();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Core"`
Expected: PASS (Tất cả test Core đều đạt).

- [ ] **Step 5: Commit**

```bash
git add code/src/ErpChatbot.Core code/src/ErpChatbot.Infrastructure/ErpChatbot.Infrastructure.csproj code/src/ErpChatbot.Agent/ErpChatbot.Agent.csproj code/tests/ErpChatbot.Tests/Core
git commit -m "feat(core): upgrade packages to M.A.AI 1.25.0, add RBAC, DTOs, Event stream models"
```

---

### Task 2: Infrastructure In-Memory ERP Data Service

**Files:**
- Create: `code/src/ErpChatbot.Infrastructure/Services/InMemoryErpDataService.cs`
- Create: `code/tests/ErpChatbot.Tests/Infrastructure/InMemoryErpDataServiceTests.cs`

**Interfaces:**
- Consumes: `IErpDataService`, `StockInfo`, `OrderStatusInfo`, `CustomerDebtInfo`, `PurchaseRequestResult`
- Produces: `InMemoryErpDataService`

- [ ] **Step 1: Write the failing tests for InMemoryErpDataService**

Tạo file `code/tests/ErpChatbot.Tests/Infrastructure/InMemoryErpDataServiceTests.cs`:
```csharp
using ErpChatbot.Infrastructure.Services;
using FluentAssertions;

namespace ErpChatbot.Tests.Infrastructure;

public class InMemoryErpDataServiceTests
{
    private readonly InMemoryErpDataService _service = new();

    [Fact]
    public async Task GetStockAsync_ExistingProduct_ReturnsStockInfo()
    {
        var stock = await _service.GetStockAsync("SP-001");
        stock.ProductCode.Should().Be("SP-001");
        stock.ProductName.Should().Be("Bàn phím cơ K87");
        stock.Quantity.Should().Be(124);
        stock.Unit.Should().Be("cái");
    }

    [Fact]
    public async Task GetStockAsync_UnknownProduct_ThrowsKeyNotFoundException()
    {
        var act = async () => await _service.GetStockAsync("SP-999");
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*SP-999*");
    }

    [Fact]
    public async Task GetOrderStatusAsync_ExistingOrder_ReturnsOrderInfo()
    {
        var order = await _service.GetOrderStatusAsync("DH-1001");
        order.OrderCode.Should().Be("DH-1001");
        order.Customer.Should().Be("Công ty An Phát");
        order.Status.Should().Be("Đang giao");
        order.Eta.Should().Be("2026-10-23");
    }

    [Fact]
    public async Task GetOrderStatusAsync_UnknownOrder_ReturnsNotFoundStatus()
    {
        var order = await _service.GetOrderStatusAsync("DH-9999");
        order.Status.Should().Be("Không tìm thấy");
    }

    [Fact]
    public async Task ListLowStockAsync_Threshold10_ReturnsItemsUnderThreshold()
    {
        var items = await _service.ListLowStockAsync(10);
        items.Should().Contain(i => i.ProductCode == "SP-002" && i.Quantity == 8);
        items.Should().Contain(i => i.ProductCode == "SP-003" && i.Quantity == 3);
        items.Should().NotContain(i => i.ProductCode == "SP-001");
    }

    [Fact]
    public async Task GetCustomerDebtAsync_ExistingCustomer_ReturnsDebtInfo()
    {
        var debt = await _service.GetCustomerDebtAsync("KH-001");
        debt.CustomerCode.Should().Be("KH-001");
        debt.CustomerName.Should().Be("Công ty TNHH Ánh Dương");
        debt.DebtAmount.Should().Be(45000000m);
    }

    [Fact]
    public async Task CreatePurchaseRequestAsync_ValidInput_CreatesRequestWithPrPrefix()
    {
        var pr = await _service.CreatePurchaseRequestAsync("SP-003", 25);
        pr.RequestId.Should().StartWith("PR-");
        pr.ProductCode.Should().Be("SP-003");
        pr.Quantity.Should().Be(25);
        pr.Status.Should().Contain("Chờ phê duyệt");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Infrastructure.InMemoryErpDataServiceTests"`
Expected: FAIL (Type not found).

- [ ] **Step 3: Implement InMemoryErpDataService**

Tạo file `code/src/ErpChatbot.Infrastructure/Services/InMemoryErpDataService.cs`:
```csharp
using System.Collections.Concurrent;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;

namespace ErpChatbot.Infrastructure.Services;

public class InMemoryErpDataService : IErpDataService
{
    private static readonly Dictionary<string, (string Name, int Qty, string Unit, string Location)> Stock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SP-001"] = ("Bàn phím cơ K87", 124, "cái", "Kho Hà Nội"),
        ["SP-002"] = ("Chuột không dây M3", 8, "cái", "Kho Hà Nội"),
        ["SP-003"] = ("Màn hình 27 inch Q27", 3, "chiếc", "Kho Hà Nội"),
        ["SP-004"] = ("Cáp HDMI 2m", 560, "sợi", "Kho Hà Nội")
    };

    private static readonly Dictionary<string, (string Customer, string Status, string Eta)> Orders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DH-1001"] = ("Công ty An Phát", "Đang giao", "2026-10-23"),
        ["DH-1002"] = ("Cửa hàng Minh Long", "Chờ xuất kho", "2026-10-25")
    };

    private static readonly Dictionary<string, (string Name, decimal Debt, string Currency, string DueDate)> Debts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KH-001"] = ("Công ty TNHH Ánh Dương", 45000000m, "VND", "2026-11-15"),
        ["KH-002"] = ("Đại lý Hoàng Gia", 120500000m, "VND", "2026-10-30"),
        ["KH-003"] = ("Doanh nghiệp Song Toàn", 0m, "VND", "")
    };

    private readonly ConcurrentBag<PurchaseRequestResult> _purchaseRequests = [];

    public Task<StockInfo> GetStockAsync(string productCode, CancellationToken ct = default)
    {
        string code = productCode.Trim().ToUpperInvariant();
        if (!Stock.TryGetValue(code, out var s))
        {
            throw new KeyNotFoundException($"Không có sản phẩm {code} trong danh mục Items ERP (SELECT ... WHERE Code='{code}')");
        }

        return Task.FromResult(new StockInfo(code, s.Name, s.Qty, s.Unit, s.Location));
    }

    public Task<OrderStatusInfo> GetOrderStatusAsync(string orderCode, CancellationToken ct = default)
    {
        string code = orderCode.Trim().ToUpperInvariant();
        return Task.FromResult(Orders.TryGetValue(code, out var o)
            ? new OrderStatusInfo(code, o.Customer, o.Status, o.Eta)
            : new OrderStatusInfo(code, "", "Không tìm thấy", ""));
    }

    public Task<IReadOnlyList<StockInfo>> ListLowStockAsync(int threshold, CancellationToken ct = default)
    {
        var result = Stock
            .Where(kv => kv.Value.Qty < threshold)
            .Select(kv => new StockInfo(kv.Key, kv.Value.Name, kv.Value.Qty, kv.Value.Unit, kv.Value.Location))
            .ToList();

        return Task.FromResult<IReadOnlyList<StockInfo>>(result);
    }

    public Task<CustomerDebtInfo> GetCustomerDebtAsync(string customerCode, CancellationToken ct = default)
    {
        string code = customerCode.Trim().ToUpperInvariant();
        if (!Debts.TryGetValue(code, out var d))
        {
            throw new KeyNotFoundException($"Không tìm thấy thông tin công nợ của khách hàng mã '{code}'.");
        }

        return Task.FromResult(new CustomerDebtInfo(code, d.Name, d.Debt, d.Currency, d.DueDate));
    }

    public Task<PurchaseRequestResult> CreatePurchaseRequestAsync(string productCode, int quantity, CancellationToken ct = default)
    {
        string id = $"PR-{Random.Shared.Next(1000, 9999)}";
        var result = new PurchaseRequestResult(id, productCode.Trim().ToUpperInvariant(), quantity, "Chờ phê duyệt của trưởng phòng mua hàng");
        _purchaseRequests.Add(result);
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Infrastructure.InMemoryErpDataServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add code/src/ErpChatbot.Infrastructure/Services/InMemoryErpDataService.cs code/tests/ErpChatbot.Tests/Infrastructure/InMemoryErpDataServiceTests.cs
git commit -m "feat(infra): implement InMemoryErpDataService for Stock, Orders, Debts, and PR"
```

---

### Task 3: Agent Tools & RBAC Protection (`ErpTools` & `RbacFunctionInvoker`)

**Files:**
- Create: `code/src/ErpChatbot.Agent/Tools/ErpTools.cs`
- Create: `code/src/ErpChatbot.Agent/Security/RbacFunctionInvoker.cs`
- Create: `code/tests/ErpChatbot.Tests/Agent/ErpToolsTests.cs`
- Create: `code/tests/ErpChatbot.Tests/Agent/RbacFunctionInvokerTests.cs`

**Interfaces:**
- Consumes: `IErpDataService`, `IUserContext`, `ErpPermissions`
- Produces: `ErpTools.GetTools(IErpDataService)`, `RbacFunctionInvoker.CreateInvoker(IUserContext, Action<AgentToolExecutionEvent>?)`

- [ ] **Step 1: Write failing tests for ErpTools and RbacFunctionInvoker**

Tạo file `code/tests/ErpChatbot.Tests/Agent/ErpToolsTests.cs`:
```csharp
using ErpChatbot.Agent.Tools;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FluentAssertions;

namespace ErpChatbot.Tests.Agent;

public class ErpToolsTests
{
    [Fact]
    public void GetTools_ShouldReturn5Tools_WithProperNamesAndApproval()
    {
        IErpDataService erpService = new InMemoryErpDataService();
        var tools = ErpTools.GetTools(erpService);

        tools.Should().HaveCount(5);

        tools.Should().Contain(t => t.Name == "get_stock");
        tools.Should().Contain(t => t.Name == "get_order_status");
        tools.Should().Contain(t => t.Name == "list_low_stock");
        tools.Should().Contain(t => t.Name == "get_customer_debt");
        tools.Should().Contain(t => t.Name == "create_purchase_request");

        var prTool = tools.First(t => t.Name == "create_purchase_request");
        prTool.Should().BeOfType<ApprovalRequiredAIFunction>();
    }
}
```

Tạo file `code/tests/ErpChatbot.Tests/Agent/RbacFunctionInvokerTests.cs`:
```csharp
using ErpChatbot.Agent.Security;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Extensions.AI;
using FluentAssertions;

namespace ErpChatbot.Tests.Agent;

public class RbacFunctionInvokerTests
{
    [Fact]
    public async Task Invoker_WhenSalesCallsCustomerDebt_ThrowsUnauthorizedAccessException()
    {
        var userContext = new UserContext("sales_user");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext);

        var func = AIFunctionFactory.Create(() => "debt info", name: "get_customer_debt");
        var ctx = new FunctionInvocationContext(func, new AIFunctionArguments());

        var act = async () => await invoker(ctx, CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*sales_user*không có quyền*Debt:Read*");
    }

    [Fact]
    public async Task Invoker_WhenAccountantCallsCustomerDebt_ExecutesSuccessfully()
    {
        var userContext = new UserContext("accountant_user");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext);

        var func = AIFunctionFactory.Create(() => "ok debt", name: "get_customer_debt");
        var ctx = new FunctionInvocationContext(func, new AIFunctionArguments());

        var result = await invoker(ctx, CancellationToken.None);
        result.Should().Be("ok debt");
    }

    [Fact]
    public async Task Invoker_WhenSalesCallsGetStock_ExecutesSuccessfully()
    {
        var userContext = new UserContext("sales_user");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext);

        var func = AIFunctionFactory.Create(() => "stock 100", name: "get_stock");
        var ctx = new FunctionInvocationContext(func, new AIFunctionArguments());

        var result = await invoker(ctx, CancellationToken.None);
        result.Should().Be("stock 100");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Agent.ErpToolsTests|FullyQualifiedName~ErpChatbot.Tests.Agent.RbacFunctionInvokerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement ErpTools and RbacFunctionInvoker**

Tạo file `code/src/ErpChatbot.Agent/Tools/ErpTools.cs`:
```csharp
using System.ComponentModel;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ErpChatbot.Agent.Tools;

public static class ErpTools
{
    public static IReadOnlyList<AITool> GetTools(IErpDataService erpService)
    {
        var getStock = AIFunctionFactory.Create(
            [Description("Tra số lượng tồn kho hiện tại và vị trí kho của một mã sản phẩm trong ERP.")]
            async ([Description("Mã sản phẩm, dạng SP-xxx (ví dụ: SP-001)")] string productCode, CancellationToken ct) =>
                await erpService.GetStockAsync(productCode, ct),
            name: "get_stock");

        var getOrderStatus = AIFunctionFactory.Create(
            [Description("Xem trạng thái và ngày giao dự kiến của một đơn bán hàng trong ERP.")]
            async ([Description("Mã đơn hàng, dạng DH-xxxx (ví dụ: DH-1001)")] string orderCode, CancellationToken ct) =>
                await erpService.GetOrderStatusAsync(orderCode, ct),
            name: "get_order_status");

        var listLowStock = AIFunctionFactory.Create(
            [Description("Liệt kê danh sách các sản phẩm có số lượng tồn kho thấp hơn ngưỡng cho trước.")]
            async ([Description("Ngưỡng số lượng tồn kho, ví dụ 10")] int threshold, CancellationToken ct) =>
                await erpService.ListLowStockAsync(threshold, ct),
            name: "list_low_stock");

        var getCustomerDebt = AIFunctionFactory.Create(
            [Description("Tra cứu thông tin công nợ hiện tại và hạn thanh toán của khách hàng trong ERP.")]
            async ([Description("Mã khách hàng, dạng KH-xxx (ví dụ: KH-001)")] string customerCode, CancellationToken ct) =>
                await erpService.GetCustomerDebtAsync(customerCode, ct),
            name: "get_customer_debt");

        var createPr = AIFunctionFactory.Create(
            [Description("Tạo phiếu đề xuất mua hàng (Purchase Request) mới trong ERP. Thao tác này là thao tác GHI dữ liệu.")]
            async (
                [Description("Mã sản phẩm cần mua, dạng SP-xxx")] string productCode,
                [Description("Số lượng cần mua (số nguyên dương)")] int quantity,
                CancellationToken ct) =>
                await erpService.CreatePurchaseRequestAsync(productCode, quantity, ct),
            name: "create_purchase_request");

        return [
            getStock,
            getOrderStatus,
            listLowStock,
            getCustomerDebt,
            new ApprovalRequiredAIFunction(createPr)
        ];
    }
}
```

Tạo file `code/src/ErpChatbot.Agent/Security/RbacFunctionInvoker.cs`:
```csharp
using System.Diagnostics;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Extensions.AI;

namespace ErpChatbot.Agent.Security;

public static class RbacFunctionInvoker
{
    public static readonly IReadOnlyDictionary<string, string> ToolPermissions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["get_stock"] = ErpPermissions.InventoryRead,
        ["list_low_stock"] = ErpPermissions.InventoryRead,
        ["get_order_status"] = ErpPermissions.OrdersRead,
        ["get_customer_debt"] = ErpPermissions.DebtRead,
        ["create_purchase_request"] = ErpPermissions.PurchaseRequestCreate
    };

    public static Func<FunctionInvocationContext, CancellationToken, Task<object?>> CreateInvoker(
        IUserContext userContext,
        Action<AgentToolExecutionEvent>? onExecuted = null)
    {
        return async (ctx, ct) =>
        {
            string toolName = ctx.Function.Name;
            var sw = Stopwatch.StartNew();

            if (ToolPermissions.TryGetValue(toolName, out var requiredPermission))
            {
                if (!userContext.HasPermission(requiredPermission))
                {
                    string rolesStr = string.Join(", ", userContext.CurrentUser.Roles.Select(r => r.Name));
                    string errorMsg = $"Người dùng '{userContext.CurrentUser.Username}' (vai trò: {rolesStr}) không có quyền '{requiredPermission}' để thực thi tool '{toolName}'.";
                    onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, false, errorMsg));
                    throw new UnauthorizedAccessException(errorMsg);
                }
            }

            try
            {
                var result = await ctx.Function.InvokeAsync(ctx.Arguments, ct);
                sw.Stop();
                onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, true));
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                onExecuted?.Invoke(new AgentToolExecutionEvent(toolName, SummarizeArgs(ctx.Arguments), sw.ElapsedMilliseconds, false, ex.Message));
                throw;
            }
        };
    }

    private static string SummarizeArgs(AIFunctionArguments? args)
    {
        if (args == null || args.Count == 0) return "";
        return string.Join(", ", args.Select(a => $"{a.Key}={a.Value}"));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Agent.ErpToolsTests|FullyQualifiedName~ErpChatbot.Tests.Agent.RbacFunctionInvokerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add code/src/ErpChatbot.Agent/Tools code/src/ErpChatbot.Agent/Security code/tests/ErpChatbot.Tests/Agent
git commit -m "feat(agent): add ErpTools definitions and RbacFunctionInvoker"
```

---

### Task 4: Agent Service - Conditional Approval & Event-Driven Streaming

**Files:**
- Modify: `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`
- Create: `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`
- Create: `code/tests/ErpChatbot.Tests/Agent/ConditionalApprovalTests.cs`

**Interfaces:**
- Consumes: `IChatClient`, `IErpDataService`, `IUserContext`, `IOptions<ErpOptions>`
- Implements: `IErpAgentService` (`RunStreamingAsync`, `ResetSessionAsync`, `GetStats`)

- [ ] **Step 1: Write failing tests for ErpAgentService with Fake Chat Client**

Tạo file `code/tests/ErpChatbot.Tests/Agent/ErpAgentServiceTests.cs`:
```csharp
using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Services;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using FluentAssertions;

namespace ErpChatbot.Tests.Agent;

public class ErpAgentServiceTests
{
    private class FakeChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("FakeChatClient");
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var responseMsg = new ChatMessage(ChatRole.Assistant, "Phản hồi giả lập");
            return Task.FromResult(new ChatResponse([responseMsg]) { Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 } });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Xin ") { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 1 })] };
            yield return new ChatResponseUpdate(ChatRole.Assistant, "chào!") { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 0, OutputTokenCount = 1 })] };
        }
    }

    [Fact]
    public async Task RunStreamingAsync_YieldsTextChunkEvents_AndTurnCompletedEvent()
    {
        var fakeClient = new FakeChatClient();
        var erpService = new InMemoryErpDataService();
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", Instructions = "Hướng dẫn" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        var events = new List<AgentResponseEvent>();
        await foreach (var evt in service.RunStreamingAsync("Chào bạn"))
        {
            events.Add(evt);
        }

        events.Should().Contain(e => e is AgentTextChunkEvent && ((AgentTextChunkEvent)e).Text == "Xin ");
        events.Should().Contain(e => e is AgentTextChunkEvent && ((AgentTextChunkEvent)e).Text == "chào!");
        events.Should().Contain(e => e is AgentTurnCompletedEvent);

        var stats = service.GetStats();
        stats.TurnCount.Should().Be(1);
    }

    [Fact]
    public async Task ResetSessionAsync_ClearsSession_RetainsStats()
    {
        var fakeClient = new FakeChatClient();
        var erpService = new InMemoryErpDataService();
        var userContext = new UserContext("sales_user");
        var erpOptions = Options.Create(new ErpOptions { AssistantName = "Trợ lý ERP", Instructions = "Hướng dẫn" });

        var service = new ErpAgentService(fakeClient, erpService, userContext, erpOptions);

        await foreach (var _ in service.RunStreamingAsync("Lượt 1")) { }
        service.GetStats().TurnCount.Should().Be(1);

        await service.ResetSessionAsync();
        service.GetStats().TurnCount.Should().Be(1);
    }
}
```

Tạo file `code/tests/ErpChatbot.Tests/Agent/ConditionalApprovalTests.cs`:
```csharp
using ErpChatbot.Agent.Services;
using FluentAssertions;

namespace ErpChatbot.Tests.Agent;

public class ConditionalApprovalTests
{
    [Theory]
    [InlineData(10, false)]
    [InlineData(50, false)]
    [InlineData(51, true)]
    [InlineData(100, true)]
    public void ShouldRequireApproval_QuantityThresholdCheck(int quantity, bool expectedRequireApproval)
    {
        var args = new Dictionary<string, object?> { ["quantity"] = quantity };
        bool requiresApproval = ErpAgentService.EvaluateRequiresApproval("create_purchase_request", args);
        requiresApproval.Should().Be(expectedRequireApproval);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Agent.ErpAgentServiceTests|FullyQualifiedName~ErpChatbot.Tests.Agent.ConditionalApprovalTests"`
Expected: FAIL.

- [ ] **Step 3: Implement ErpAgentService with Conditional Approval & Resuming Stream**

Cập nhật file `code/src/ErpChatbot.Agent/Services/ErpAgentService.cs`:
```csharp
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ErpChatbot.Agent.Security;
using ErpChatbot.Agent.Tools;
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

    public ErpAgentService(
        IChatClient chatClient,
        IErpDataService erpDataService,
        IUserContext userContext,
        IOptions<ErpOptions> erpOptions)
    {
        var options = erpOptions.Value;
        var tools = ErpTools.GetTools(erpDataService);

        _agent = chatClient.AsAIAgent(
            instructions: options.Instructions,
            name: options.AssistantName,
            tools: tools
        );
    }

    public static bool EvaluateRequiresApproval(string toolName, IReadOnlyDictionary<string, object?>? arguments)
    {
        if (toolName.Equals("create_purchase_request", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments != null && arguments.TryGetValue("quantity", out var qtyObj))
            {
                if (int.TryParse(qtyObj?.ToString(), out int qty))
                {
                    return qty > 50;
                }
            }
        }
        return true;
    }

    public async IAsyncEnumerable<AgentResponseEvent> RunStreamingAsync(
        string userPrompt,
        Func<AgentApprovalRequiredEvent, Task<bool>>? approvalHandler = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            yield break;
        }

        _session ??= await _agent.CreateSessionAsync(cancellationToken);

        long turnInputTokens = 0;
        long turnOutputTokens = 0;
        var turnSw = Stopwatch.StartNew();

        object currentInput = userPrompt;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var responseStream = currentInput switch
            {
                string text => _agent.RunStreamingAsync(text, _session, options: null, cancellationToken: cancellationToken),
                ChatMessage chatMsg => _agent.RunStreamingAsync([chatMsg], _session, options: null, cancellationToken: cancellationToken),
                _ => throw new InvalidOperationException($"Input type không được hỗ trợ: {currentInput.GetType()}")
            };

            List<ToolApprovalRequestContent> pendingApprovalRequests = [];

            await foreach (var update in responseStream.WithCancellation(cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return new AgentTextChunkEvent(update.Text);
                }

                if (update.Contents != null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is UsageContent usageContent && usageContent.Details != null)
                        {
                            turnInputTokens += usageContent.Details.InputTokenCount ?? 0;
                            turnOutputTokens += usageContent.Details.OutputTokenCount ?? 0;
                        }
                        else if (content is ToolApprovalRequestContent approvalReq)
                        {
                            pendingApprovalRequests.Add(approvalReq);
                        }
                    }
                }
            }

            if (pendingApprovalRequests.Count == 0)
            {
                break;
            }

            // Xử lý các yêu cầu phê duyệt
            List<AIContent> approvalResponses = [];
            foreach (var req in pendingApprovalRequests)
            {
                var toolCall = req.ToolCall as FunctionCallContent;
                string toolName = toolCall?.Name ?? "unknown_tool";
                var argsDict = toolCall?.Arguments?.ToDictionary(k => k.Key, v => (object?)v.Value) 
                               ?? new Dictionary<string, object?>();

                bool requiresApproval = EvaluateRequiresApproval(toolName, argsDict);
                bool approved;

                if (!requiresApproval)
                {
                    // Tự động duyệt vì số lượng <= 50
                    approved = true;
                    yield return new AgentToolExecutionEvent(toolName, $"Tự động phê duyệt (số lượng <= 50)", 0, true);
                }
                else
                {
                    // Cần người dùng phê duyệt
                    var approvalEvent = new AgentApprovalRequiredEvent(toolName, argsDict, $"Yêu cầu phê duyệt thực thi '{toolName}'");
                    yield return approvalEvent;

                    approved = approvalHandler != null && await approvalHandler(approvalEvent);
                }

                approvalResponses.Add(req.CreateResponse(approved));
            }

            currentInput = new ChatMessage(ChatRole.User, approvalResponses);
        }

        turnSw.Stop();
        _stats.RecordTurn(turnInputTokens, turnOutputTokens);
        yield return new AgentTurnCompletedEvent(turnInputTokens, turnOutputTokens, turnSw.ElapsedMilliseconds);
    }

    public async Task ResetSessionAsync()
    {
        _session = await _agent.CreateSessionAsync();
    }

    public SessionStats GetStats() => _stats;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Agent.ErpAgentServiceTests|FullyQualifiedName~ErpChatbot.Tests.Agent.ConditionalApprovalTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add code/src/ErpChatbot.Agent/Services/ErpAgentService.cs code/tests/ErpChatbot.Tests/Agent
git commit -m "feat(agent): implement ErpAgentService with event streaming and conditional tool approval"
```

---

### Task 5: Infrastructure ChatClientFactory Safe Middleware Configuration

**Files:**
- Modify: `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`
- Modify: `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs`

**Interfaces:**
- Consumes: `LlmOptions`, `IUserContext`
- Implements: `IChatClientFactory` (`CreateClient`, `CheckHealthAsync`) with `UseFunctionInvocation` safe configuration.

- [ ] **Step 1: Write/Update unit test for ChatClientFactory to verify safe middleware**

Cập nhật `code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs` bổ sung test kiểm tra việc tạo client kèm `FunctionInvokingChatClient`:
```csharp
[Fact]
public void CreateClient_ShouldConfigure_FunctionInvocationMiddleware()
{
    var options = Options.Create(new LlmOptions
    {
        Provider = "ollama",
        Model = "llama3.2",
        Ollama = new OllamaEndpointOptions { Endpoint = "http://localhost:11434" }
    });

    var userContext = new UserContext("sales_user");
    var factory = new ChatClientFactory(options, userContext);
    var client = factory.CreateClient();

    client.Should().NotBeNull();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Infrastructure.ChatClientFactoryTests"`
Expected: FAIL (Constructor signature mismatch).

- [ ] **Step 3: Update ChatClientFactory with safe UseFunctionInvocation and RbacFunctionInvoker**

Cập nhật file `code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs`:
```csharp
using ErpChatbot.Agent.Security;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Exceptions;

namespace ErpChatbot.Infrastructure.Factories;

public class ChatClientFactory : IChatClientFactory
{
    private readonly LlmOptions _options;
    private readonly IUserContext? _userContext;
    private readonly Func<LlmOptions, IChatClient>? _clientOverride;
    private readonly int _timeoutMs;

    public ChatClientFactory(IOptions<LlmOptions> options, IUserContext? userContext = null)
        : this(options, userContext, null, 30000)
    {
    }

    public ChatClientFactory(
        IOptions<LlmOptions> options,
        IUserContext? userContext = null,
        Func<LlmOptions, IChatClient>? clientOverride = null,
        int timeoutMs = 30000)
    {
        _options = options.Value;
        _userContext = userContext;
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
            "ollama" => new ChatClientBuilder(CreateOllamaClient())
                .ConfigureOptions(opt =>
                {
                    opt.Temperature ??= _options.Temperature;
                    opt.MaxOutputTokens ??= _options.MaxOutputTokens;
                })
                .UseFunctionInvocation(configure: f =>
                {
                    f.MaximumIterationsPerRequest = 5;          // Chặn vòng lặp vô hạn theo chuẩn Ex3
                    f.MaximumConsecutiveErrorsPerRequest = 2;   // Ngắt nếu lỗi 2 lần liên tiếp
                    f.IncludeDetailedErrors = false;            // Không lộ SQL/stack trace cho LLM
                    f.AllowConcurrentInvocation = false;        // Thực thi tuần tự

                    if (_userContext != null)
                    {
                        f.FunctionInvoker = RbacFunctionInvoker.CreateInvoker(_userContext);
                    }
                })
                .Build(),
            _ => throw new InvalidOperationException(
                $"LLM_PROVIDER không hợp lệ: '{_options.Provider}'. Chỉ hỗ trợ 'ollama'.")
        };
    }

    private OllamaApiClient CreateOllamaClient()
    {
        var ollama = _options.Ollama;
        if (string.IsNullOrWhiteSpace(ollama.AccessClientId) || string.IsNullOrWhiteSpace(ollama.AccessClientSecret))
        {
            return new OllamaApiClient(new Uri(ollama.Endpoint), _options.Model);
        }

        var http = new HttpClient { BaseAddress = new Uri(ollama.Endpoint) };
        http.DefaultRequestHeaders.Add("CF-Access-Client-Id", ollama.AccessClientId);
        http.DefaultRequestHeaders.Add("CF-Access-Client-Secret", ollama.AccessClientSecret);
        return new OllamaApiClient(http, _options.Model);
    }

    public async Task<(bool IsHealthy, string Message)> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(_timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var client = CreateClient();
            var testMessage = new ChatMessage(ChatRole.User, "ping");
            var testOptions = new ChatOptions { MaxOutputTokens = 1 };

            await client.GetResponseAsync([testMessage], testOptions, linkedCts.Token);
            return (true, $"Ollama và model '{_options.Model}' đã sẵn sàng kết nối.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return (false, $"Không phản hồi sau {_timeoutMs / 1000}s. Vui lòng kiểm tra xem Ollama có bị treo không.");
        }
        catch (OllamaException ex) when (ex.Message.Contains("model", StringComparison.OrdinalIgnoreCase) &&
                                         ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
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

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Infrastructure.ChatClientFactoryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add code/src/ErpChatbot.Infrastructure/Factories/ChatClientFactory.cs code/tests/ErpChatbot.Tests/Infrastructure/ChatClientFactoryTests.cs
git commit -m "feat(infra): configure safe UseFunctionInvocation middleware and health checks"
```

---

### Task 6: CLI Integration, New Commands (`/user`, `/whoami`) & Approval UI

**Files:**
- Modify: `code/src/ErpChatbot.Cli/appsettings.json`
- Modify: `code/src/ErpChatbot.Cli/CliRunner.cs`
- Modify: `code/src/ErpChatbot.Cli/Program.cs`
- Create: `code/tests/ErpChatbot.Tests/Cli/CliCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IUserContext`, `IErpAgentService`, `IErpDataService`, `LlmOptions`, `ErpOptions`
- Implements: CLI Commands (`/user`, `/whoami`, `/reset`, `/stats`, `/info`, `/exit`), Interactive Approval (`y/n`), Event streaming printer.

- [ ] **Step 1: Write failing tests for CliCommandHandler new commands**

Tạo file `code/tests/ErpChatbot.Tests/Cli/CliCommandHandlerTests.cs`:
```csharp
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using FluentAssertions;
using NSubstitute;

namespace ErpChatbot.Tests.Cli;

public class CliCommandHandlerTests
{
    private readonly IErpAgentService _agentService = Substitute.For<IErpAgentService>();
    private readonly IUserContext _userContext = new UserContext("sales_user");
    private readonly LlmOptions _llmOptions = new() { Provider = "ollama", Model = "llama3.2" };
    private readonly ErpOptions _erpOptions = new() { AssistantName = "Trợ lý ERP" };

    [Fact]
    public async Task HandleInputAsync_UserCommand_SwitchesUser()
    {
        var handler = new CliCommandHandler(_agentService, _userContext, _llmOptions, _erpOptions);

        var result = await handler.HandleInputAsync("/user accountant_user", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
        _userContext.CurrentUser.Username.Should().Be("accountant_user");
    }

    [Fact]
    public async Task HandleInputAsync_WhoamiCommand_PrintsUserInfo()
    {
        var handler = new CliCommandHandler(_agentService, _userContext, _llmOptions, _erpOptions);

        var result = await handler.HandleInputAsync("/whoami", CancellationToken.None);

        result.Should().Be(CliInputResult.Handled);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Cli.CliCommandHandlerTests"`
Expected: FAIL.

- [ ] **Step 3: Update appsettings.json, CliRunner.cs, and Program.cs**

Cập nhật `code/src/ErpChatbot.Cli/appsettings.json`:
```json
{
  "Llm": {
    "Provider": "ollama",
    "Model": "llama3.2:3b",
    "Temperature": 0.1,
    "MaxOutputTokens": 600,
    "Ollama": {
      "Endpoint": "http://localhost:11434"
    }
  },
  "Erp": {
    "AssistantName": "Trợ lý ERP",
    "CompanyName": "Doanh nghiệp Demo",
    "Instructions": "Bạn là 'Trợ lý ERP' của công ty. Trả lời bằng tiếng Việt, ngắn gọn. Luôn dùng tool để lấy số liệu tồn kho, đơn hàng, công nợ hoặc tạo đề xuất mua hàng. Tuyệt đối không bao giờ tự bịa số. Nếu tool báo lỗi, thiếu quyền truy cập hoặc không tìm thấy dữ liệu, hãy giải thích rõ ràng và lịch sự cho người dùng."
  }
}
```

Cập nhật `code/src/ErpChatbot.Cli/CliRunner.cs`:
```csharp
using System.Diagnostics;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Extensions.Options;

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
    private readonly IUserContext _userContext;
    private readonly LlmOptions _llmOptions;
    private readonly ErpOptions _erpOptions;

    public CliCommandHandler(
        IErpAgentService agentService,
        IUserContext userContext,
        IOptions<LlmOptions> llmOptions,
        IOptions<ErpOptions> erpOptions)
        : this(agentService, userContext, llmOptions.Value, erpOptions.Value)
    {
    }

    public CliCommandHandler(
        IErpAgentService agentService,
        IUserContext userContext,
        LlmOptions llmOptions,
        ErpOptions erpOptions)
    {
        _agentService = agentService;
        _userContext = userContext;
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

        if (trimmed.StartsWith("/user", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/role", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                Console.WriteLine("\n[HƯỚNG DẪN] Cú pháp: /user <tên_user> (ví dụ: /user sales_user, /user accountant_user, /user admin_user)\n");
                return CliInputResult.Handled;
            }

            string targetUser = parts[1];
            try
            {
                _userContext.SwitchUser(targetUser);
                var u = _userContext.CurrentUser;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[NGƯỜI DÙNG] Đã chuyển sang tài khoản: {u.Username} ({u.FullName})");
                Console.WriteLine($"  Vai trò: {string.Join(", ", u.Roles.Select(r => r.Name))}");
                Console.WriteLine($"  Quyền hạn: {string.Join(", ", u.EffectivePermissions)}\n");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[LỖI] {ex.Message}\n");
                Console.ResetColor();
            }
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/whoami", StringComparison.OrdinalIgnoreCase))
        {
            var u = _userContext.CurrentUser;
            Console.WriteLine($"\n--- THÔNG TIN TÀI KHOẢN ĐANG ĐĂNG NHẬP ---");
            Console.WriteLine($"Mã:       {u.Id}");
            Console.WriteLine($"Tên user: {u.Username}");
            Console.WriteLine($"Họ tên:   {u.FullName}");
            Console.WriteLine($"Vai trò:  {string.Join(", ", u.Roles.Select(r => $"{r.Name} ({r.Description})"))}");
            Console.WriteLine($"Quyền:    {string.Join(", ", u.EffectivePermissions)}\n");
            return CliInputResult.Handled;
        }

        if (trimmed.Equals("/info", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            var u = _userContext.CurrentUser;
            Console.WriteLine($"\n--- THÔNG TIN HỆ THỐNG ---");
            Console.WriteLine($"Trợ lý:    {_erpOptions.AssistantName} ({_erpOptions.CompanyName})");
            Console.WriteLine($"Tài khoản: {u.Username} (Vai trò: {string.Join(", ", u.Roles.Select(r => r.Name))})");
            Console.WriteLine($"Provider:  {_llmOptions.Provider} | Model: {_llmOptions.Model}");
            Console.WriteLine($"Endpoint:  {_llmOptions.Ollama.Endpoint}");
            Console.WriteLine($"Lệnh: /user <tên>, /whoami, /reset, /stats, /info, /exit\n");
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

public class CliChatLoop
{
    private readonly IErpAgentService _agentService;
    private readonly CliCommandHandler _commandHandler;

    public CliChatLoop(IErpAgentService agentService, CliCommandHandler commandHandler)
    {
        _agentService = agentService;
        _commandHandler = commandHandler;
    }

    public static void PrintHealth(bool isHealthy, string message)
    {
        Console.ForegroundColor = isHealthy ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine(isHealthy ? $"[SẴN SÀNG] {message}\n" : $"\n[CẢNH BÁO KẾT NỐI] {message}\n");
        Console.ResetColor();
    }

    public async Task<bool> ProcessLineAsync(string rawInput, CancellationToken userCancellation)
    {
        try
        {
            var inputResult = await _commandHandler.HandleInputAsync(rawInput, userCancellation);
            if (inputResult == CliInputResult.Exit)
            {
                Console.WriteLine("Tạm biệt!");
                return false;
            }
            if (inputResult != CliInputResult.SendToLlm)
            {
                return true;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("Trợ lý: ");
            Console.ResetColor();

            long turnTokens = 0;
            long elapsedMs = 0;

            await foreach (var evt in _agentService.RunStreamingAsync(rawInput, HandleApprovalRequestAsync, userCancellation))
            {
                switch (evt)
                {
                    case AgentTextChunkEvent chunk:
                        Console.Write(chunk.Text);
                        break;
                    case AgentToolExecutionEvent toolEvt:
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(toolEvt.Success 
                            ? $"\n   [tool] {toolEvt.ToolName}({toolEvt.ArgumentsSummary}) -> ok ({toolEvt.ElapsedMs}ms)" 
                            : $"\n   [tool] {toolEvt.ToolName} -> LỖI: {toolEvt.Error}");
                        Console.ResetColor();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("Trợ lý: ");
                        Console.ResetColor();
                        break;
                    case AgentTurnCompletedEvent completed:
                        turnTokens = completed.InputTokens + completed.OutputTokens;
                        elapsedMs = completed.ElapsedMs;
                        break;
                }
            }

            Console.WriteLine();
            var currentStats = _agentService.GetStats();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [Thời gian: {elapsedMs}ms | Token lượt này: {turnTokens} | Token tích luỹ: {currentStats.TotalTokens}]");
            Console.ResetColor();
            Console.WriteLine();
        }
        catch (OperationCanceledException) when (userCancellation.IsCancellationRequested)
        {
            WriteLine(ConsoleColor.Yellow, "\n(đã hủy phản hồi)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            WriteLine(ConsoleColor.Red, $"\n[Mất kết nối tới Ollama: {ex.Message}. Thử lại hoặc gõ /reset]");
        }
        catch (Exception ex)
        {
            WriteLine(ConsoleColor.Red, $"\n[Lỗi xử lý: {ex.Message}]");
        }

        return true;
    }

    private static Task<bool> HandleApprovalRequestAsync(AgentApprovalRequiredEvent req)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        string argsStr = string.Join(", ", req.Arguments.Select(a => $"{a.Key}={a.Value}"));
        Console.Write($"\n   [cần duyệt] {req.ToolName}({argsStr}). Đồng ý? (y/n): ");
        Console.ResetColor();

        string? response = Console.ReadLine()?.Trim().ToLowerInvariant();
        bool approved = response == "y";

        Console.ForegroundColor = approved ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(approved ? "   -> Đã duyệt thực thi." : "   -> Đã từ chối thực thi.");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Trợ lý: ");
        Console.ResetColor();

        return Task.FromResult(approved);
    }

    private static void WriteLine(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
```

Cập nhật `code/src/ErpChatbot.Cli/Program.cs`:
```csharp
using System.Text;
using ErpChatbot.Agent.Services;
using ErpChatbot.Cli;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Factories;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<ErpOptions>(builder.Configuration.GetSection(ErpOptions.SectionName));

var envProvider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
var envModel = Environment.GetEnvironmentVariable("LLM_MODEL");
var envEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT");
var envUser = Environment.GetEnvironmentVariable("ERP_USER") ?? "sales_user";

builder.Services.PostConfigure<LlmOptions>(opts =>
{
    if (!string.IsNullOrWhiteSpace(envProvider)) opts.Provider = envProvider;
    if (!string.IsNullOrWhiteSpace(envModel)) opts.Model = envModel;
    if (!string.IsNullOrWhiteSpace(envEndpoint)) opts.Ollama.Endpoint = envEndpoint;
});

// COMPOSITION ROOT
builder.Services.AddSingleton<IUserContext>(new UserContext(envUser));
builder.Services.AddSingleton<IErpDataService, InMemoryErpDataService>();
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IChatClientFactory>().CreateClient());

builder.Services.AddScoped<IErpAgentService, ErpAgentService>();
builder.Services.AddScoped<CliCommandHandler>();

using var host = builder.Build();

using var scope = host.Services.CreateScope();
var factory = scope.ServiceProvider.GetRequiredService<IChatClientFactory>();
var llmOptions = scope.ServiceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
var erpOptions = scope.ServiceProvider.GetRequiredService<IOptions<ErpOptions>>().Value;
var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();
var agentService = scope.ServiceProvider.GetRequiredService<IErpAgentService>();
var commandHandler = scope.ServiceProvider.GetRequiredService<CliCommandHandler>();

Console.WriteLine("===============================================================");
Console.WriteLine($"   {erpOptions.AssistantName} — Microsoft Agent Framework (.NET 10)");
Console.WriteLine($"   Người dùng: {userContext.CurrentUser.Username} ({string.Join(", ", userContext.CurrentUser.Roles.Select(r => r.Name))})");
Console.WriteLine($"   Provider: {llmOptions.Provider} | Model: {llmOptions.Model}");
Console.WriteLine($"   Endpoint: {llmOptions.Ollama.Endpoint}");
Console.WriteLine("===============================================================");
Console.WriteLine("Gõ /whoami để xem quyền, /user <tên> để đổi user, /reset để xoá session, /exit để thoát.\n");

Console.Write("Đang kiểm tra kết nối LLM (tối đa 30s nếu model đang nạp)... ");
var (isHealthy, healthMessage) = await factory.CheckHealthAsync();
CliChatLoop.PrintHealth(isHealthy, healthMessage);

var chatLoop = new CliChatLoop(agentService, commandHandler);
while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"[{userContext.CurrentUser.Username}] Bạn: ");
    Console.ResetColor();

    string? rawInput = Console.ReadLine();
    if (rawInput == null) break;

    using var turnCts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) =>
    {
        e.Cancel = true;
        turnCts.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        if (!await chatLoop.ProcessLineAsync(rawInput, turnCts.Token)) break;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ErpChatbot.Tests.Cli.CliCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add code/src/ErpChatbot.Cli code/tests/ErpChatbot.Tests/Cli
git commit -m "feat(cli): add /user, /whoami commands, approval UI, and updated instructions"
```

---

### Task 7: Full Test Suite Verification & Verification Runbook

**Files:**
- Test: All tests in `code/tests/ErpChatbot.Tests/`
- Create: `code/docs/superpowers/plans/2026-10-28-day-02-verification-runbook.md`

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: PASS (Tất cả test các tầng Core, Infrastructure, Agent, Cli đều vượt qua).

- [ ] **Step 2: Verify code build with release mode**

Run: `dotnet build -c Release`
Expected: Build succeeded với 0 Error(s), 0 Warning(s).

- [ ] **Step 3: Write Verification Runbook document**

Ghi lại kịch bản kiểm thử thực tế và các lệnh xác thực vào file `code/docs/superpowers/plans/2026-10-28-day-02-verification-runbook.md`.

- [ ] **Step 4: Commit**

```bash
git add code/docs/superpowers/plans
git commit -m "docs: add Day 2 verification runbook"
```
