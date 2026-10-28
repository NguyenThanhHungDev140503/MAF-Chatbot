using ErpChatbot.Agent.Security;
using ErpChatbot.Core.Models.Rbac;
using Microsoft.Extensions.AI;
using FluentAssertions;

namespace ErpChatbot.Tests.Agent;

public class RbacFunctionInvokerTests
{
    [Fact]
    public async Task Invoker_WhenSalesCallsCustomerDebt_ReturnsStructuredPermissionDeniedError()
    {
        var userContext = new UserContext("sales_user");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext);

        var func = AIFunctionFactory.Create(() => "debt info", name: "get_customer_debt");
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

        var result = await invoker(ctx, CancellationToken.None);
        result.Should().BeOfType<ToolSecurityError>();
        var err = (ToolSecurityError)result!;
        err.Error.Should().Be("PermissionDenied");
        err.Message.Should().Contain("sales_user").And.Contain("Debt:Read");
    }

    [Fact]
    public async Task Invoker_WhenCallingUnregisteredTool_ReturnsFailSecureError()
    {
        var userContext = new UserContext("admin_user");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext);

        var func = AIFunctionFactory.Create(() => "unregistered", name: "delete_database");
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

        var result = await invoker(ctx, CancellationToken.None);
        result.Should().BeOfType<ToolSecurityError>();
        var err = (ToolSecurityError)result!;
        err.Error.Should().Be("PermissionDenied");
        err.Message.Should().Contain("Default Deny / Fail-Secure");
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
