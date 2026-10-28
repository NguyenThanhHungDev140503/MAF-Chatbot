using ErpChatbot.Core.Models.Rbac;

namespace ErpChatbot.Core.Interfaces;

public interface IUserContext
{
    User CurrentUser { get; }
    void SwitchUser(string username);
    bool HasPermission(string permission);
}
