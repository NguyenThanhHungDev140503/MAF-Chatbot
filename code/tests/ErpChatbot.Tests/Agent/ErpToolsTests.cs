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
