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
