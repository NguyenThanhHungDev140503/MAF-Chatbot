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
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

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
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

        var result = await invoker(ctx, CancellationToken.None);
        result?.ToString().Should().Be("ok debt");
    }

    [Fact]
    public async Task Invoker_WhenSalesCallsGetStock_ExecutesSuccessfully()
    {
        var userContext = new UserContext("sales_user");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext);

        var func = AIFunctionFactory.Create(() => "stock 100", name: "get_stock");
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

        var result = await invoker(ctx, CancellationToken.None);
        result?.ToString().Should().Be("stock 100");
    }
}
