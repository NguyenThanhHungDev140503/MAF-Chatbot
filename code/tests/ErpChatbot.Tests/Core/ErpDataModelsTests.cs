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
