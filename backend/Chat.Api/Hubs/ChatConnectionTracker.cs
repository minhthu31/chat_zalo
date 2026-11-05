using System.Collections.Concurrent;

namespace chat_zalo.Hubs
{
    public interface IChatConnectionTracker
    {
        void AddConnection(string connectionId, Guid userId, Guid conversationId);
        void RemoveConnection(string connectionId);
        IReadOnlyCollection<string> GetConnectionsForConversation(Guid conversationId, IEnumerable<Guid> activeUserIds);
    }

    public class ChatConnectionTracker : IChatConnectionTracker
    {
        private readonly ConcurrentDictionary<string, (Guid UserId, Guid ConversationId)> _connections = new();

        public void AddConnection(string connectionId, Guid userId, Guid conversationId)
        {
            _connections[connectionId] = (userId, conversationId);
        }

        public void RemoveConnection(string connectionId)
        {
            _connections.TryRemove(connectionId, out _);
        }

        public IReadOnlyCollection<string> GetConnectionsForConversation(Guid conversationId, IEnumerable<Guid> activeUserIds)
        {
            var activeUserSet = new HashSet<Guid>(activeUserIds);
            var result = new List<string>();

            foreach (var kvp in _connections)
            {
                if (kvp.Value.ConversationId == conversationId && activeUserSet.Contains(kvp.Value.UserId))
                {
                    result.Add(kvp.Key);
                }
            }

            return result;
        }
    }
}