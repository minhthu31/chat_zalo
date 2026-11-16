using Chat.Client.Models;
using Chat.Client.Services;
using Chat.Client.State;
using Xunit;

namespace Chat.Client.Tests;

public sealed class ChatSessionTests
{
    [Fact]
    public async Task InitialLoadShowsLatestTwentyAndOlderLoadsBackward()
    {
        var api = new FakeMessages();
        api.Add(45);
        var session = new ChatSession(api);
        await session.SyncAsync(api.Id);
        var state = session.State.For(api.Id);
        Assert.Equal(Enumerable.Range(26, 20).Select(i => (long)i), state.Messages.Select(m => m.Seq));
        await session.LoadOlderAsync(api.Id);
        Assert.Equal(6, state.FirstSeq);
        await session.LoadOlderAsync(api.Id);
        Assert.Equal(45, state.Messages.Count);
        Assert.False(state.HasOlder);
        Assert.Equal(45, state.SyncedSeq);
    }

    [Fact]
    public async Task ReconnectDrainsMoreThanHundredAndRepeatedSyncDoesNotDuplicate()
    {
        var api = new FakeMessages();
        api.Add(20);
        var session = new ChatSession(api);
        await session.SyncAsync(api.Id);
        api.Add(251);
        await session.SyncAsync(api.Id);
        await session.SyncAsync(api.Id);
        Assert.Equal(271, session.State.For(api.Id).SyncedSeq);
        Assert.Equal(271, session.State.For(api.Id).Messages.Count);
    }

    [Fact]
    public async Task SwitchingDuringRequestKeepsMessagesInOriginalConversation()
    {
        var api = new FakeMessages();
        api.Add(3);
        api.DelayLatest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ChatSession(api);
        session.State.SelectedId = api.Id;
        var loading = session.SyncAsync(api.Id);
        var other = Guid.NewGuid();
        session.State.SelectedId = other;
        api.DelayLatest.SetResult(api.Items.ToList());
        await loading;
        Assert.Empty(session.State.Active!.Messages);
        Assert.Equal(3, session.State.For(api.Id).Messages.Count);
    }

    [Fact]
    public async Task ResetDiscardsLateResponseAfterLogout()
    {
        var api = new FakeMessages();
        api.Add(3);
        api.DelayLatest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new ChatSession(api);
        var loading = session.SyncAsync(api.Id);
        session.Reset();
        api.DelayLatest.SetResult(api.Items.ToList());
        await loading;
        Assert.Empty(session.State.For(api.Id).Messages);
        Assert.False(session.State.For(api.Id).Initialized);
    }

    [Fact]
    public async Task LostSendResponseRetainsExactBodyAndIdOnRetry()
    {
        var api = new FakeMessages();
        api.Add(1);
        var session = new ChatSession(api);
        await session.SyncAsync(api.Id);
        var state = session.State.For(api.Id);
        state.Draft = "  Nội dung giữ nguyên\n";
        api.FailSendOnce = true;
        await Assert.ThrowsAsync<ApiException>(() => session.SendAsync(api.Id));
        Assert.Equal("  Nội dung giữ nguyên\n", state.Draft);
        var pending = state.Pending;
        await session.SendAsync(api.Id);
        Assert.Equal(pending, api.Requests.Last());
        Assert.Equal(2, state.Messages.Count);
        Assert.Equal("", state.Draft);
        Assert.Null(state.Pending);
    }

    [Fact]
    public async Task SendResponseDoesNotSkipUnseenMessagesBeforeIt()
    {
        var api = new FakeMessages();
        api.Add(1);
        var session = new ChatSession(api);
        await session.SyncAsync(api.Id);
        api.Add(3);
        session.State.For(api.Id).Draft = "new";
        await session.SendAsync(api.Id);
        Assert.Equal(Enumerable.Range(1, 5).Select(i => (long)i), session.State.For(api.Id).Messages.Select(m => m.Seq));
    }

    [Fact]
    public void MergeDeduplicatesIdAndSequenceAndRejectsOtherConversation()
    {
        var api = new FakeMessages();
        api.Add(1);
        var state = new ConversationState();
        var first = api.Items[0];
        state.Merge(api.Id, [first, first, first with { Id = Guid.NewGuid() },
            first with { Seq = 2 }, first with { Id = Guid.NewGuid(), Seq = 3, ConversationId = Guid.NewGuid() }]);
        Assert.Single(state.Messages);
    }

    private sealed class FakeMessages : IChatMessages
    {
        public Guid Id { get; } = Guid.NewGuid();
        public List<MessageResponse> Items { get; } = [];
        public List<SendMessageRequest> Requests { get; } = [];
        public TaskCompletionSource<List<MessageResponse>>? DelayLatest;
        public bool FailSendOnce;
        private readonly Dictionary<Guid, MessageResponse> sent = [];
        public void Add(int count)
        {
            for (var i = 0; i < count; i++) Items.Add(new(Guid.NewGuid(), Id, Guid.NewGuid(), Items.Count + 1, "body", DateTimeOffset.UtcNow));
        }
        public Task<List<MessageResponse>> GetLatestAsync(Guid id) => DelayLatest?.Task ?? Task.FromResult(Items.TakeLast(20).ToList());
        public Task<List<MessageResponse>> GetOlderAsync(Guid id, long beforeSeq) => Task.FromResult(Items.Where(m => m.Seq < beforeSeq).TakeLast(20).ToList());
        public Task<List<MessageResponse>> GetNewAsync(Guid id, long afterSeq) => Task.FromResult(Items.Where(m => m.Seq > afterSeq).Take(100).ToList());
        public Task<MessageResponse> SendAsync(Guid id, SendMessageRequest request)
        {
            Requests.Add(request);
            if (!sent.TryGetValue(request.ClientMessageId, out var message))
            {
                message = new(Guid.NewGuid(), id, Guid.NewGuid(), Items.Count + 1, request.Body, DateTimeOffset.UtcNow);
                Items.Add(message);
                sent.Add(request.ClientMessageId, message);
            }
            if (FailSendOnce) { FailSendOnce = false; throw new ApiException(0, "NETWORK_ERROR", "lost response"); }
            return Task.FromResult(message);
        }
    }
}
