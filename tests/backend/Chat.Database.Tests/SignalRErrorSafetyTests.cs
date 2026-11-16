using System.Collections.Concurrent;
using System.Reflection;
using Chat.Application.Interfaces;
using chat_zalo.Hubs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Database.Tests;

public sealed partial class ChatHubSignalRTests
{
    public class FailingPermissionsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException("INTERNAL_DATABASE_DETAIL_SHOULD_NOT_REACH_CLIENT");
    }

    private sealed class HubErrorLogger : ILogger<ChatHub>
    {
        public ConcurrentQueue<Exception> Exceptions { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Error && exception is not null) Exceptions.Enqueue(exception);
        }
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void DetailedSignalRErrors_AreEnabledOnlyInDevelopment(string environment, bool expected)
    {
        using var factory = CreateFactory().WithWebHostBuilder(b => b.UseEnvironment(environment));
        var options = factory.Services.GetRequiredService<IOptions<HubOptions>>().Value;
        Assert.Equal(expected, options.EnableDetailedErrors);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task JoinFailure_LogsException_ButReturnsOnlySafeMessage(string environment)
    {
        await using var seed = await TestDatabase.CreateAsync();
        var user = await seed.AddUserAsync();
        var group = await seed.AddGroupConversationAsync(user, (user, null));
        var logger = new HubErrorLogger();
        using var factory = CreateFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGroupPermissionService>();
                services.AddScoped<IGroupPermissionService>(_ =>
                    DispatchProxy.Create<IGroupPermissionService, FailingPermissionsProxy>());
                services.RemoveAll<ILogger<ChatHub>>();
                services.AddSingleton<ILogger<ChatHub>>(logger);
            });
        });
        await using var connection = CreateConnection(factory, user);
        await connection.StartAsync();
        var error = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinConversation", group.ToString("D")));
        Assert.Contains("Không thể tham gia hội thoại. Vui lòng thử lại sau.", error.Message);
        Assert.DoesNotContain("INTERNAL_DATABASE_DETAIL", error.Message);
        Assert.Contains(logger.Exceptions, ex =>
            ex.Message == "INTERNAL_DATABASE_DETAIL_SHOULD_NOT_REACH_CLIENT");
    }
}
