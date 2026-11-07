using Chat.Client.Models;
using Chat.Client.Services;
using Xunit;

namespace Chat.Client.Tests;

public sealed class ConversationDiscoveryTests
{
    [Fact]
    public async Task DiscoversConversationCreatedAfterInitialEmptyListAndSubscribes()
    {
        var api = new Conversations();
        var joined = new HashSet<Guid>();
        var discovery = new ConversationDiscovery(api, id => { joined.Add(id); return Task.CompletedTask; });
        Assert.Empty(await discovery.RefreshAsync());
        var conversation = new ConversationDto(Guid.NewGuid(), "Direct", 1);
        api.Items.Add(conversation);
        Assert.Equal(conversation, Assert.Single(await discovery.RefreshAsync()));
        Assert.Contains(conversation.Id, joined);
    }

    [Fact]
    public async Task SubscriptionFailureDoesNotHideRoomsOrSkipOtherSubscriptionsAndIsRetried()
    {
        var api = new Conversations();
        var first = new ConversationDto(Guid.NewGuid(), "Direct", 1);
        var second = new ConversationDto(Guid.NewGuid(), "Direct", 2);
        api.Items.AddRange([first, second]);
        var fail = true;
        var joined = new List<Guid>();
        var errors = new List<Exception>();
        var discovery = new ConversationDiscovery(api, id =>
        {
            if (id == first.Id && fail) throw new InvalidOperationException("temporary join failure");
            joined.Add(id);
            return Task.CompletedTask;
        });
        discovery.SubscriptionFailed += ex => { errors.Add(ex); return Task.CompletedTask; };
        Assert.Equal(2, (await discovery.RefreshAsync()).Count);
        Assert.Contains(second.Id, joined);
        Assert.Single(errors);
        fail = false;
        await discovery.RefreshAsync();
        Assert.Contains(first.Id, joined);
    }

    [Fact]
    public async Task CancellationDiscardsLateResultAndDoesNotSubscribeAfterNavigation()
    {
        var api = new Conversations { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var joined = new List<Guid>();
        var discovery = new ConversationDiscovery(api, id => { joined.Add(id); return Task.CompletedTask; });
        using var lifetime = new CancellationTokenSource();
        var loading = discovery.RefreshAsync(lifetime.Token);
        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        api.Pending.SetResult([new(Guid.NewGuid(), "Direct", 1)]);
        Assert.Empty(joined);
    }

    private sealed class Conversations : IChatConversations
    {
        public List<ConversationDto> Items { get; } = [];
        public TaskCompletionSource<List<ConversationDto>>? Pending;
        public Task<List<ConversationDto>> GetAsync() => Pending?.Task ?? Task.FromResult(Items.ToList());
    }
}
