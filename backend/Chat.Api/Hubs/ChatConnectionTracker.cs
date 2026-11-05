using System.Collections.Concurrent;

namespace chat_zalo.Hubs
{
    public interface IChatConnectionTracker
    {
        void AddSubscription(string connectionId, Guid userId, Guid conversationId);
        void RemoveSubscription(string connectionId, Guid conversationId);
        void RemoveConnection(string connectionId);
        IReadOnlyCollection<string> GetActiveConnectionsForConversation(Guid conversationId, IEnumerable<Guid> activeUserIds);
    }

    public class ChatConnectionTracker : IChatConnectionTracker
    {
        private readonly ConcurrentDictionary<string, (Guid UserId, ConcurrentDictionary<Guid, bool> Conversations)> _connections = new();

        public void AddSubscription(string connectionId, Guid userId, Guid conversationId)
        {
            var entry = _connections.GetOrAdd(connectionId, id => (userId, new ConcurrentDictionary<Guid, bool>()));
            entry.Conversations.TryAdd(conversationId, true);
        }

        public void RemoveSubscription(string connectionId, Guid conversationId)
        {
            if (_connections.TryGetValue(connectionId, out var entry))
            {
                entry.Conversations.TryRemove(conversationId, out _);
            }
        }

        public void RemoveConnection(string connectionId)
        {
            _connections.TryRemove(connectionId, out _);
        }

        public IReadOnlyCollection<string> GetActiveConnectionsForConversation(Guid conversationId, IEnumerable<Guid> activeUserIds)
        {
            var activeUserSet = new HashSet<Guid>(activeUserIds);
            var result = new List<string>();

            foreach (var kvp in _connections)
            {
                var userId = kvp.Value.UserId;
                var conversations = kvp.Value.Conversations;

                if (activeUserSet.Contains(userId) && conversations.ContainsKey(conversationId))
                {
                    result.Add(kvp.Key);
                }
            }

            return result;
        }
    }
}