using ErpChatbot.Agent.Services;
using FluentAssertions;

namespace ErpChatbot.Tests.Agent;

public class ConditionalApprovalTests
{
    [Theory]
    [InlineData(-5, true)]  // Số âm không bao giờ được auto-duyệt
    [InlineData(0, true)]   // Số 0 không bao giờ được auto-duyệt
    [InlineData(10, false)] // Số dương <= 50 được auto-duyệt
    [InlineData(50, false)] // Ngưỡng biên 50 được auto-duyệt
    [InlineData(51, true)]  // > 50 cần người dùng duyệt
    [InlineData(100, true)]
    public void ShouldRequireApproval_QuantityThresholdCheck(int quantity, bool expectedRequireApproval)
    {
        var args = new Dictionary<string, object?> { ["quantity"] = quantity };
        bool requiresApproval = ErpAgentService.EvaluateRequiresApproval("create_purchase_request", args);
        requiresApproval.Should().Be(expectedRequireApproval);
    }

    [Fact]
    public void ShouldRequireApproval_WhenCumulativeTurnQuantityExceedsThreshold()
    {
        // Lần gọi 1: mua 30 cái -> auto duyệt (30 <= 50)
        var args1 = new Dictionary<string, object?> { ["quantity"] = 30 };
        ErpAgentService.EvaluateRequiresApproval("create_purchase_request", args1, cumulativeTurnQty: 0)
            .Should().BeFalse();

        // Lần gọi 2 trong cùng lượt: mua tiếp 25 cái -> tổng 55 > 50 -> PHẢI hỏi duyệt!
        var args2 = new Dictionary<string, object?> { ["quantity"] = 25 };
        ErpAgentService.EvaluateRequiresApproval("create_purchase_request", args2, cumulativeTurnQty: 30)
            .Should().BeTrue();
    }
}
