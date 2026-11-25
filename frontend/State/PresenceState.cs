using Chat.Client.Services;

namespace Chat.Client.State;

public sealed class PresenceState(ApiRequestSender api)
{
    private Dictionary<Guid, FriendPresence> _friends = [];
    private Guid? _viewer;
    private int _revision;

    public event Action? Changed;

    public FriendPresence? Find(Guid userId) => _friends.GetValueOrDefault(userId);

    public bool SetViewer(Guid? viewer)
    {
        if (_viewer == viewer) return false;

        _viewer = viewer;
        _revision++;
        _friends.Clear();
        Changed?.Invoke();
        return true;
    }

    public async Task RefreshAsync()
    {
        if (_viewer is null) return;

        var revision = ++_revision;
        try
        {
            var friends = await api.SendAsync<FriendPresence[]>(HttpMethod.Post, "api/presence/heartbeat");
            if (revision != _revision) return;
            _friends = friends.ToDictionary(friend => friend.UserId);
        }
        catch (ApiException)
        {
            if (revision != _revision) return;
            _friends.Clear();
        }

        Changed?.Invoke();
    }

    public sealed record FriendPresence(Guid UserId, bool IsOnline, DateTimeOffset? LastSeenAt)
    {
        public string? Label
        {
            get
            {
                if (IsOnline) return "Đang hoạt động";
                if (LastSeenAt is not { } lastSeen) return null;

                var elapsed = DateTimeOffset.UtcNow - lastSeen;
                if (elapsed.TotalDays >= 1) return $"Hoạt động {(int)elapsed.TotalDays} ngày trước";
                if (elapsed.TotalHours >= 1) return $"Hoạt động {(int)elapsed.TotalHours} giờ trước";
                if (elapsed.TotalMinutes >= 1) return $"Hoạt động {(int)elapsed.TotalMinutes} phút trước";
                return "Vừa hoạt động";
            }
        }
    }
}
