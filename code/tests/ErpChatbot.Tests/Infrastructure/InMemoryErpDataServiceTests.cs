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

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(-100)]
    public async Task CreatePurchaseRequestAsync_NegativeOrZeroQuantity_ThrowsArgumentOutOfRangeException(int invalidQty)
    {
        var act = async () => await _service.CreatePurchaseRequestAsync("SP-003", invalidQty);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Số lượng đề xuất mua hàng phải là số nguyên dương*");
    }
}
