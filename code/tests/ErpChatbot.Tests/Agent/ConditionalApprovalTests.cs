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
