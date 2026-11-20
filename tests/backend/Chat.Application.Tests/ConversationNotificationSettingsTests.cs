namespace Chat.Application.Tests;

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Chat.Api.Controllers;
using Chat.Application.DTOs.Notifications;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class ConversationNotificationSettingsTests
{
    private static ChatDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ChatDbContext(options);
    }

    private static ApplicationUser MakeUser(string suffix) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"notif_{suffix}",
        NormalizedUserName = $"NOTIF_{suffix.ToUpperInvariant()}",
        DisplayName = $"User {suffix}"
    };

    private static ConversationNotificationSettingsController CreateController(
        IConversationSettingsService service,
        Guid? userId)
    {
        var controller = new ConversationNotificationSettingsController(service);
        var identity = userId.HasValue
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }, "TestAuth")
            : new ClaimsIdentity();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private static async Task<IActionResult?> ExecuteJsonValidationFilterAsync(string rawJson)
    {
        var filter = new ValidateUpdateNotificationSettingsJsonAttribute();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(rawJson));

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());

        var executingContext = new ResourceExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());

        var executed = false;
        await filter.OnResourceExecutionAsync(executingContext, () =>
        {
            executed = true;
            return Task.FromResult(new ResourceExecutedContext(actionContext, new List<IFilterMetadata>()));
        });

        return executed ? null : executingContext.Result;
    }

    // -----------------------------------------------------------------------
    // Test 1: Giá trị mặc định
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Get_WhenNoSettingExists_ReturnsDefaultFalseWithoutCreatingRecord()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var userA = MakeUser("a");
        var userB = MakeUser("b");
        var conversationId = Guid.NewGuid();

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            db.Users.AddRange(userA, userB);
            db.Conversations.Add(new Conversation { Id = conversationId, CreatedByUserId = userA.Id, Kind = "Direct" });
            db.DirectConversations.Add(new DirectConversation { ConversationId = conversationId, UserLowId = userA.Id, UserHighId = userB.Id });
            db.ConversationMembers.AddRange(
                new ConversationMember { ConversationId = conversationId, UserId = userA.Id, LeftAt = null },
                new ConversationMember { ConversationId = conversationId, UserId = userB.Id, LeftAt = null });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationSettingsService(db);
            var controller = CreateController(service, userA.Id);

            var result = await controller.Get(conversationId);
            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<NotificationSettingsDto>(ok.Value);

            Assert.False(dto.IsMuted);
            Assert.Equal(0, await db.ConversationNotificationSettings.CountAsync());
        }
    }

    // -----------------------------------------------------------------------
    // Test 2: Lưu riêng từng tài khoản
    // -----------------------------------------------------------------------
    [Fact]
    public async Task PutAndGet_TwoMembersInSameConversation_StoreIndependentSettingsPerAccount()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var userA = MakeUser("a");
        var userB = MakeUser("b");
        var conversationId = Guid.NewGuid();

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            db.Users.AddRange(userA, userB);
            db.Conversations.Add(new Conversation { Id = conversationId, CreatedByUserId = userA.Id, Kind = "Direct" });
            db.DirectConversations.Add(new DirectConversation { ConversationId = conversationId, UserLowId = userA.Id, UserHighId = userB.Id });
            db.ConversationMembers.AddRange(
                new ConversationMember { ConversationId = conversationId, UserId = userA.Id, LeftAt = null },
                new ConversationMember { ConversationId = conversationId, UserId = userB.Id, LeftAt = null });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationSettingsService(db);

            // A PUT isMuted = true
            var updatedA = await service.UpdateAsync(userA.Id, conversationId, new UpdateNotificationSettingsRequest { IsMuted = true });
            Assert.True(updatedA.IsMuted);

            // B GET vẫn nhận isMuted = false
            var currentB = await service.GetAsync(userB.Id, conversationId);
            Assert.False(currentB.IsMuted);

            // B PUT isMuted = false
            var updatedB = await service.UpdateAsync(userB.Id, conversationId, new UpdateNotificationSettingsRequest { IsMuted = false });
            Assert.False(updatedB.IsMuted);

            // A GET vẫn nhận isMuted = true
            var currentA = await service.GetAsync(userA.Id, conversationId);
            Assert.True(currentA.IsMuted);
        }
    }

    // -----------------------------------------------------------------------
    // Test 3: Nhiều phiên đăng nhập / nhiều thiết bị cùng một tài khoản
    // -----------------------------------------------------------------------
    [Fact]
    public async Task PutAndGet_MultipleSessionsSameAccount_SynchronizeSettingsViaDatabase()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var userA = MakeUser("a");
        var userB = MakeUser("b");
        var conversationId = Guid.NewGuid();

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            db.Users.AddRange(userA, userB);
            db.Conversations.Add(new Conversation { Id = conversationId, CreatedByUserId = userA.Id, Kind = "Direct" });
            db.DirectConversations.Add(new DirectConversation { ConversationId = conversationId, UserLowId = userA.Id, UserHighId = userB.Id });
            db.ConversationMembers.AddRange(
                new ConversationMember { ConversationId = conversationId, UserId = userA.Id, LeftAt = null },
                new ConversationMember { ConversationId = conversationId, UserId = userB.Id, LeftAt = null });
            await db.SaveChangesAsync();
        }

        // Session 1 PUT isMuted = true
        await using (var dbSession1 = CreateInMemoryDbContext(dbName))
        {
            var ctrlSession1 = CreateController(new ConversationSettingsService(dbSession1), userA.Id);
            var ok = Assert.IsType<OkObjectResult>(
                await ctrlSession1.Update(conversationId, new UpdateNotificationSettingsRequest { IsMuted = true }));
            Assert.True(Assert.IsType<NotificationSettingsDto>(ok.Value).IsMuted);
        }

        // Session 2 GET nhận isMuted = true, sau đó PUT isMuted = false
        await using (var dbSession2 = CreateInMemoryDbContext(dbName))
        {
            var ctrlSession2 = CreateController(new ConversationSettingsService(dbSession2), userA.Id);
            var getOk = Assert.IsType<OkObjectResult>(await ctrlSession2.Get(conversationId));
            Assert.True(Assert.IsType<NotificationSettingsDto>(getOk.Value).IsMuted);

            var putOk = Assert.IsType<OkObjectResult>(
                await ctrlSession2.Update(conversationId, new UpdateNotificationSettingsRequest { IsMuted = false }));
            Assert.False(Assert.IsType<NotificationSettingsDto>(putOk.Value).IsMuted);
        }

        // Session 1 GET nhận isMuted = false, tổng số bản ghi trong DB vẫn là 1
        await using (var dbSession1Reload = CreateInMemoryDbContext(dbName))
        {
            var ctrlSession1Reload = CreateController(new ConversationSettingsService(dbSession1Reload), userA.Id);
            var getOk = Assert.IsType<OkObjectResult>(await ctrlSession1Reload.Get(conversationId));
            Assert.False(Assert.IsType<NotificationSettingsDto>(getOk.Value).IsMuted);
            Assert.Equal(1, await dbSession1Reload.ConversationNotificationSettings.CountAsync(
                s => s.ConversationId == conversationId && s.UserId == userA.Id));
        }
    }

    // -----------------------------------------------------------------------
    // Test 4: CSRF (ValidateAntiforgeryJsonAttribute)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Put_CsrfValidation_RejectsMissingOrInvalidTokenAndAllowsValidToken()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        Microsoft.AspNetCore.DataProtection.DataProtectionBuilderExtensions.UseEphemeralDataProtectionProvider(
            services.AddDataProtection());
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "Chat.Antiforgery";
        });
        await using var provider = services.BuildServiceProvider();
        var antiforgery = provider.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var filter = new Chat.Api.Filters.ValidateAntiforgeryJsonAttribute();

        // 1. Missing CSRF token -> 400 INVALID_CSRF_TOKEN
        {
            var httpContext = new DefaultHttpContext { RequestServices = provider };
            httpContext.Request.Method = HttpMethods.Put;
            var authContext = new AuthorizationFilterContext(
                new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);
            var objResult = Assert.IsType<ObjectResult>(authContext.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, objResult.StatusCode);
            var err = Assert.IsType<Chat.Application.DTOs.Auth.AuthErrorResponse>(objResult.Value);
            Assert.Equal("INVALID_CSRF_TOKEN", err.Code);
        }

        // 2. Invalid CSRF token -> 400 INVALID_CSRF_TOKEN
        {
            var tokenContext = new DefaultHttpContext { RequestServices = provider };
            _ = antiforgery.GetAndStoreTokens(tokenContext);
            var cookieHeader = tokenContext.Response.Headers.SetCookie.Single()!.Split(';')[0];

            var httpContext = new DefaultHttpContext { RequestServices = provider };
            httpContext.Request.Method = HttpMethods.Put;
            httpContext.Request.Headers.Cookie = cookieHeader;
            httpContext.Request.Headers["X-CSRF-TOKEN"] = "invalid-token";
            var authContext = new AuthorizationFilterContext(
                new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);
            var objResult = Assert.IsType<ObjectResult>(authContext.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, objResult.StatusCode);
            var err = Assert.IsType<Chat.Application.DTOs.Auth.AuthErrorResponse>(objResult.Value);
            Assert.Equal("INVALID_CSRF_TOKEN", err.Code);
        }

        // 3. Valid CSRF token -> Allowed (Result remains null)
        {
            var tokenContext = new DefaultHttpContext { RequestServices = provider };
            var tokens = antiforgery.GetAndStoreTokens(tokenContext);
            var cookieHeader = tokenContext.Response.Headers.SetCookie.Single()!.Split(';')[0];

            var httpContext = new DefaultHttpContext { RequestServices = provider };
            httpContext.Request.Method = HttpMethods.Put;
            httpContext.Request.Headers.Cookie = cookieHeader;
            httpContext.Request.Headers["X-CSRF-TOKEN"] = tokens.RequestToken!;
            var authContext = new AuthorizationFilterContext(
                new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>());

            await filter.OnAuthorizationAsync(authContext);
            Assert.Null(authContext.Result);
        }
    }

    // -----------------------------------------------------------------------
    // Test 5: Quyền Direct Conversation & 404 NOT_FOUND
    // -----------------------------------------------------------------------
    [Fact]
    public async Task DirectConversation_PermissionsAndNotFound_FollowContract()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var member = MakeUser("member");
        var peer = MakeUser("peer");
        var outsider = MakeUser("outsider");
        var leftMember = MakeUser("left");
        var directId = Guid.NewGuid();
        var directWithLeftId = Guid.NewGuid();
        var nonExistentId = Guid.NewGuid();

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            db.Users.AddRange(member, peer, outsider, leftMember);
            db.Conversations.AddRange(
                new Conversation { Id = directId, CreatedByUserId = member.Id, Kind = "Direct" },
                new Conversation { Id = directWithLeftId, CreatedByUserId = member.Id, Kind = "Direct" });
            db.DirectConversations.AddRange(
                new DirectConversation { ConversationId = directId, UserLowId = member.Id, UserHighId = peer.Id },
                new DirectConversation { ConversationId = directWithLeftId, UserLowId = member.Id, UserHighId = leftMember.Id });
            db.ConversationMembers.AddRange(
                new ConversationMember { ConversationId = directId, UserId = member.Id, LeftAt = null },
                new ConversationMember { ConversationId = directId, UserId = peer.Id, LeftAt = null },
                new ConversationMember { ConversationId = directWithLeftId, UserId = member.Id, LeftAt = null },
                new ConversationMember { ConversationId = directWithLeftId, UserId = leftMember.Id, LeftAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationSettingsService(db);

            // Thành viên hợp lệ GET & PUT -> 200
            Assert.False((await service.GetAsync(member.Id, directId)).IsMuted);
            Assert.True((await service.UpdateAsync(member.Id, directId, new UpdateNotificationSettingsRequest { IsMuted = true })).IsMuted);

            // Người ngoài Direct Conversation -> 403 CONVERSATION_ACCESS_DENIED
            var exOutsiderGet = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() => service.GetAsync(outsider.Id, directId));
            Assert.Equal(403, exOutsiderGet.StatusCode);
            Assert.Equal("CONVERSATION_ACCESS_DENIED", exOutsiderGet.Code);

            var exOutsiderPut = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() =>
                service.UpdateAsync(outsider.Id, directId, new UpdateNotificationSettingsRequest { IsMuted = false }));
            Assert.Equal(403, exOutsiderPut.StatusCode);
            Assert.Equal("CONVERSATION_ACCESS_DENIED", exOutsiderPut.Code);

            // Người đã mất quyền thành viên (LeftAt != null) -> 403 CONVERSATION_ACCESS_DENIED
            var exLeftPut = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() =>
                service.UpdateAsync(leftMember.Id, directWithLeftId, new UpdateNotificationSettingsRequest { IsMuted = true }));
            Assert.Equal(403, exLeftPut.StatusCode);
            Assert.Equal("CONVERSATION_ACCESS_DENIED", exLeftPut.Code);

            // Hội thoại không tồn tại -> 404 NOT_FOUND
            var exNotFoundGet = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() => service.GetAsync(member.Id, nonExistentId));
            Assert.Equal(404, exNotFoundGet.StatusCode);
            Assert.Equal("NOT_FOUND", exNotFoundGet.Code);

            var exNotFoundPut = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() =>
                service.UpdateAsync(member.Id, nonExistentId, new UpdateNotificationSettingsRequest { IsMuted = true }));
            Assert.Equal(404, exNotFoundPut.StatusCode);
            Assert.Equal("NOT_FOUND", exNotFoundPut.Code);
        }
    }

    // -----------------------------------------------------------------------
    // Test 6: Quyền Group Conversation & 404 NOT_FOUND
    // -----------------------------------------------------------------------
    [Fact]
    public async Task GroupConversation_PermissionsAndNotFound_FollowContract()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var admin = MakeUser("admin");
        var member = MakeUser("member");
        var leftMember = MakeUser("left");
        var outsider = MakeUser("outsider");
        var groupId = Guid.NewGuid();
        var disbandedGroupId = Guid.NewGuid();
        var nonExistentGroupId = Guid.NewGuid();

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            db.Users.AddRange(admin, member, leftMember, outsider);
            db.Conversations.AddRange(
                new Conversation { Id = groupId, CreatedByUserId = admin.Id, Kind = "Group" },
                new Conversation { Id = disbandedGroupId, CreatedByUserId = admin.Id, Kind = "Group" });
            db.GroupConversations.AddRange(
                new GroupConversation { ConversationId = groupId, Name = "Active Group", DeletedAt = null },
                new GroupConversation { ConversationId = disbandedGroupId, Name = "Disbanded Group", DeletedAt = DateTimeOffset.UtcNow });
            db.ConversationMembers.AddRange(
                new ConversationMember { ConversationId = groupId, UserId = admin.Id, Role = "Admin", LeftAt = null },
                new ConversationMember { ConversationId = groupId, UserId = member.Id, Role = "Member", LeftAt = null },
                new ConversationMember { ConversationId = groupId, UserId = leftMember.Id, Role = "Member", LeftAt = DateTimeOffset.UtcNow },
                new ConversationMember { ConversationId = disbandedGroupId, UserId = admin.Id, Role = "Admin", LeftAt = null });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateInMemoryDbContext(dbName))
        {
            var service = new ConversationSettingsService(db);

            // Thành viên Group hợp lệ được GET và PUT
            Assert.False((await service.GetAsync(member.Id, groupId)).IsMuted);
            Assert.True((await service.UpdateAsync(member.Id, groupId, new UpdateNotificationSettingsRequest { IsMuted = true })).IsMuted);
            Assert.True((await service.GetAsync(member.Id, groupId)).IsMuted);

            // Người ngoài Group -> 403 GROUP_ACCESS_DENIED
            var exOutsiderGet = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() => service.GetAsync(outsider.Id, groupId));
            Assert.Equal(403, exOutsiderGet.StatusCode);
            Assert.Equal("GROUP_ACCESS_DENIED", exOutsiderGet.Code);

            var exOutsiderPut = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() =>
                service.UpdateAsync(outsider.Id, groupId, new UpdateNotificationSettingsRequest { IsMuted = true }));
            Assert.Equal(403, exOutsiderPut.StatusCode);
            Assert.Equal("GROUP_ACCESS_DENIED", exOutsiderPut.Code);

            // Người đã rời Group -> 403 GROUP_ACCESS_DENIED
            var exLeftGet = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() => service.GetAsync(leftMember.Id, groupId));
            Assert.Equal(403, exLeftGet.StatusCode);
            Assert.Equal("GROUP_ACCESS_DENIED", exLeftGet.Code);

            var exLeftPut = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() =>
                service.UpdateAsync(leftMember.Id, groupId, new UpdateNotificationSettingsRequest { IsMuted = true }));
            Assert.Equal(403, exLeftPut.StatusCode);
            Assert.Equal("GROUP_ACCESS_DENIED", exLeftPut.Code);

            // Nhóm đã giải tán -> 403 GROUP_ACCESS_DENIED
            var exDisbanded = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() => service.GetAsync(admin.Id, disbandedGroupId));
            Assert.Equal(403, exDisbanded.StatusCode);
            Assert.Equal("GROUP_ACCESS_DENIED", exDisbanded.Code);

            // Group không tồn tại -> 404 NOT_FOUND
            var exMissing = await Assert.ThrowsAsync<ConversationSettingsServiceException>(() => service.GetAsync(member.Id, nonExistentGroupId));
            Assert.Equal(404, exMissing.StatusCode);
            Assert.Equal("NOT_FOUND", exMissing.Code);
        }
    }

    // -----------------------------------------------------------------------
    // Test 7: Validation cho PUT isMuted
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"other\":true}")]
    [InlineData("{\"isMuted\":null}")]
    [InlineData("{\"isMuted\":\"true\"}")]
    [InlineData("{\"isMuted\":\"false\"}")]
    [InlineData("{\"isMuted\":1}")]
    [InlineData("{\"isMuted\":0}")]
    [InlineData("{\"isMuted\":[]}")]
    [InlineData("{\"isMuted\":{}}")]
    [InlineData("{")]
    [InlineData("")]
    public async Task PutValidation_InvalidPayloads_Return400ValidationErrorAndPreserveExistingSettings(string rawPayload)
    {
        var filterResult = await ExecuteJsonValidationFilterAsync(rawPayload);
        var badRequest = Assert.IsType<BadRequestObjectResult>(filterResult);
        Assert.Equal(400, badRequest.StatusCode);

        var codeProp = badRequest.Value?.GetType().GetProperty("code")?.GetValue(badRequest.Value)?.ToString();
        Assert.Equal("VALIDATION_ERROR", codeProp);

        if (!string.IsNullOrWhiteSpace(rawPayload))
        {
            Assert.ThrowsAny<JsonException>(() =>
                JsonSerializer.Deserialize<UpdateNotificationSettingsRequest>(
                    rawPayload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        }
    }

    [Theory]
    [InlineData("{\"isMuted\":true}", true)]
    [InlineData("{\"isMuted\":false}", false)]
    public async Task PutValidation_ValidPayloads_PassFilterAndDeserializeExactBoolean(string rawPayload, bool expected)
    {
        var filterResult = await ExecuteJsonValidationFilterAsync(rawPayload);
        Assert.Null(filterResult);

        var dto = JsonSerializer.Deserialize<UpdateNotificationSettingsRequest>(
            rawPayload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal(expected, dto!.IsMuted);
    }
}
