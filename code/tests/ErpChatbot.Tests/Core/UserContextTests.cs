using ErpChatbot.Core.Interfaces;
using ErpChatbot.Core.Models.Rbac;
using FluentAssertions;

namespace ErpChatbot.Tests.Core;

public class UserContextTests
{
    [Fact]
    public void SalesUser_ShouldHave_InventoryAndOrderAndPr_ButNotDebtPermission()
    {
        var context = new UserContext("sales_user");

        context.CurrentUser.Username.Should().Be("sales_user");
        context.HasPermission(ErpPermissions.InventoryRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.OrdersRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.PurchaseRequestCreate).Should().BeTrue();
        context.HasPermission(ErpPermissions.DebtRead).Should().BeFalse();
    }

    [Fact]
    public void AccountantUser_ShouldHave_DebtPermission()
    {
        var context = new UserContext("accountant_user");

        context.CurrentUser.Username.Should().Be("accountant_user");
        context.HasPermission(ErpPermissions.DebtRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.InventoryRead).Should().BeTrue();
    }

    [Fact]
    public void AdminUser_ShouldHave_AllPermissions()
    {
        var context = new UserContext("admin_user");

        context.HasPermission(ErpPermissions.InventoryRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.OrdersRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.DebtRead).Should().BeTrue();
        context.HasPermission(ErpPermissions.PurchaseRequestCreate).Should().BeTrue();
    }

    [Fact]
    public void SwitchUser_ShouldUpdateCurrentUser_AndPermissions()
    {
        var context = new UserContext("sales_user");
        context.HasPermission(ErpPermissions.DebtRead).Should().BeFalse();

        context.SwitchUser("accountant_user");

        context.CurrentUser.Username.Should().Be("accountant_user");
        context.HasPermission(ErpPermissions.DebtRead).Should().BeTrue();
    }

    [Fact]
    public void SwitchUser_WithUnknownUser_ShouldThrowArgumentException()
    {
        var context = new UserContext("sales_user");
        var act = () => context.SwitchUser("non_existent_user");
        act.Should().Throw<ArgumentException>();
    }
}
