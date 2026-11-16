using System.Security.Claims;
using Chat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Chat.Api.Authorization;

public enum GroupAction { View, Send, Admin }

/// <summary>Requirement dùng với IAuthorizationService.AuthorizeAsync(User, new GroupResource(id), requirement).</summary>
public sealed class GroupPermissionRequirement : IAuthorizationRequirement
{
    public GroupAction Action { get; }
    public GroupPermissionRequirement(GroupAction action) => Action = action;
}

public sealed record GroupResource(Guid GroupId);

public sealed class GroupPermissionHandler : AuthorizationHandler<GroupPermissionRequirement, GroupResource>
{
    private readonly IGroupPermissionService _permissions;

    public GroupPermissionHandler(IGroupPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, GroupPermissionRequirement requirement, GroupResource resource)
    {
        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId)) return;

        var ok = requirement.Action switch
        {
            GroupAction.View => (await _permissions.CanViewMessagesAsync(resource.GroupId, userId)).Allowed,
            GroupAction.Send => (await _permissions.CanSendMessageAsync(resource.GroupId, userId)).Allowed,
            GroupAction.Admin => await _permissions.IsAdminAsync(resource.GroupId, userId),
            _ => false
        };

        if (ok) context.Succeed(requirement);
    }
}

public static class GroupAuthorizationExtensions
{
    /// <summary>Gọi trong Program.cs: builder.Services.AddGroupPermissions();</summary>
    public static IServiceCollection AddGroupPermissions(this IServiceCollection services)
    {
        services.AddScoped<IGroupMembershipQuery, Chat.Infrastructure.Repositories.GroupMembershipQuery>();
        services.AddScoped<IGroupPermissionService, Chat.Application.Services.GroupPermissionService>();
        services.AddScoped<IAuthorizationHandler, GroupPermissionHandler>();
        return services;
    }
}
