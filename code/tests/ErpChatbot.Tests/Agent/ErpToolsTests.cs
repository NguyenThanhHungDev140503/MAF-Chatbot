using ErpChatbot.Agent.Tools;
using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models.Rbac;
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

    [Fact]
    public void GetTools_WhenUserIsSales_ExcludesCustomerDebt()
    {
        IErpDataService erpService = new InMemoryErpDataService();
        var salesContext = new UserContext("sales_user");

        var tools = ErpTools.GetTools(erpService, salesContext);

        tools.Should().HaveCount(4);
        tools.Should().NotContain(t => t.Name == "get_customer_debt");
        tools.Should().Contain(t => t.Name == "get_stock");
        tools.Should().Contain(t => t.Name == "create_purchase_request");
    }

    [Fact]
    public void GetTools_WhenUserIsAccountant_ExcludesPurchaseRequest()
    {
        IErpDataService erpService = new InMemoryErpDataService();
        var accountantContext = new UserContext("accountant_user");

        var tools = ErpTools.GetTools(erpService, accountantContext);

        tools.Should().HaveCount(4);
        tools.Should().NotContain(t => t.Name == "create_purchase_request");
        tools.Should().Contain(t => t.Name == "get_customer_debt");
    }

    [Fact]
    public void GetTools_WhenUserIsAdmin_IncludesAll5Tools()
    {
        IErpDataService erpService = new InMemoryErpDataService();
        var adminContext = new UserContext("admin_user");

        var tools = ErpTools.GetTools(erpService, adminContext);

        tools.Should().HaveCount(5);
    }
}
