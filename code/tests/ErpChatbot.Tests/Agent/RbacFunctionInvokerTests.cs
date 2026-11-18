using ErpChatbot.Agent.Security;
using ErpChatbot.Agent.Tools;
using ErpChatbot.Core.Models.Rbac;
using ErpChatbot.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task Invoker_WhenToolThrows_RethrowsOriginalAndLogsItAtError()
    {
        var userContext = new UserContext("sales_user");
        var logger = new CapturingLogger();
        var expected = new InvalidOperationException("SELECT * FROM Items WHERE Code='X'");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext, logger);

        var func = AIFunctionFactory.Create(() => ThrowFor(expected), name: "get_stock");
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

        Func<Task> act = async () => await invoker(ctx, CancellationToken.None);
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && ReferenceEquals(e.Exception, expected));
    }

    [Fact]
    public async Task Invoker_WhenToolThrowsAndNoLogger_RethrowsOriginalException()
    {
        var userContext = new UserContext("sales_user");
        var expected = new InvalidOperationException("SELECT * FROM Items WHERE Code='X'");
        var invoker = RbacFunctionInvoker.CreateInvoker(userContext);

        var func = AIFunctionFactory.Create(() => ThrowFor(expected), name: "get_stock");
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

        Func<Task> act = async () => await invoker(ctx, CancellationToken.None);
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    [Theory]
    [InlineData("sales_user", "get_stock", true)]
    [InlineData("sales_user", "list_low_stock", true)]
    [InlineData("sales_user", "get_order_status", true)]
    [InlineData("sales_user", "get_customer_debt", false)]
    [InlineData("sales_user", "create_purchase_request", true)]
    [InlineData("accountant_user", "get_stock", true)]
    [InlineData("accountant_user", "list_low_stock", true)]
    [InlineData("accountant_user", "get_order_status", true)]
    [InlineData("accountant_user", "get_customer_debt", true)]
    [InlineData("accountant_user", "create_purchase_request", false)]
    [InlineData("admin_user", "get_stock", true)]
    [InlineData("admin_user", "list_low_stock", true)]
    [InlineData("admin_user", "get_order_status", true)]
    [InlineData("admin_user", "get_customer_debt", true)]
    [InlineData("admin_user", "create_purchase_request", true)]
    public async Task TC01_Invoker_PermissionMatrix_AllowsOrDeniesPerRole(string username, string toolName, bool allowed)
    {
        var invoker = RbacFunctionInvoker.CreateInvoker(new UserContext(username));
        var func = AIFunctionFactory.Create(() => "ok", name: toolName);
        var ctx = new FunctionInvocationContext { Function = func, Arguments = new AIFunctionArguments() };

        var result = await invoker(ctx, CancellationToken.None);

        if (allowed)
        {
            result?.ToString().Should().Be("ok");
        }
        else
        {
            var err = result.Should().BeOfType<ToolSecurityError>().Subject;
            err.Error.Should().Be("PermissionDenied");
            err.Message.Should().Contain(RbacFunctionInvoker.ToolPermissions[toolName]);
        }
    }

    [Fact]
    public void TC03_ToolPermissions_KeysMatchRegisteredToolNames()
    {
        var names = ErpTools.GetTools(new InMemoryErpDataService()).Select(t => t.Name);

        names.Should().BeEquivalentTo(RbacFunctionInvoker.ToolPermissions.Keys);
    }

    private static string ThrowFor(Exception ex) => throw ex;
}

internal sealed class CapturingLogger : ILogger
{
    public List<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, exception));
    }
}
