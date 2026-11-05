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
        // Key: connectionId -> Value: (UserId, Dictionary chứa các ConversationId đã join)
        private readonly ConcurrentDictionary<string, (Guid UserId, ConcurrentDictionary<Guid, bool> Conversations)> _connections = new();

        public void AddSubscription(string connectionId, Guid userId, Guid conversationId)
        {
            var entry = _connections.GetOrAdd(connectionId, id => (userId, new ConcurrentDictionary<Guid, bool>()));
            // Join lại cùng conversation không tạo trùng subscription
            entry.Conversations.TryAdd(conversationId, true);
        }

        public void RemoveSubscription(string connectionId, Guid conversationId)
        {
            if (_connections.TryGetValue(connectionId, out var entry))
            {
                // Chỉ xóa subscription của conversation được yêu cầu (Join A rồi Leave B không ảnh hưởng A)
                entry.Conversations.TryRemove(conversationId, out _);
            }
        }

        public void RemoveConnection(string connectionId)
        {
            // Disconnect mới xóa toàn bộ subscription của connection
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

                // Kiểm tra user còn active trong DB VÀ connection này đã join conversation đó
                if (activeUserSet.Contains(userId) && conversations.ContainsKey(conversationId))
                {
                    result.Add(kvp.Key);
                }
            }

            return result;
        }
    }
}