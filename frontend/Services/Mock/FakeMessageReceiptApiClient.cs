using Chat.Client.Models;

namespace Chat.Client.Services.Mock;

public class FakeMessageReceiptApiClient : IMessageReceiptApiClient
{
    private static readonly Guid Me = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");

    private readonly Dictionary<Guid, ReceiptDto> _mine = new();

    public async Task<ConversationReceiptsDto> GetAsync(Guid conversationId, IReadOnlyCollection<Guid>? messageIds = null)
    {
        await Task.Delay(150);
        var mine = _mine.GetValueOrDefault(conversationId) ?? new ReceiptDto { UserId = Me };
        return new ConversationReceiptsDto
        {
            ConversationId = conversationId,
            Items = new() { new ReceiptItemDto { UserId = Me, DisplayName = "Nguyen Van A", DeliveredSeq = mine.DeliveredSeq, ReadSeq = mine.ReadSeq } }
        };
    }

    public async Task<ReceiptDto> MarkDeliveredAsync(Guid conversationId, long seq)
    {
        await Task.Delay(100);
        var r = Get(conversationId, seq);
        r.DeliveredSeq = Math.Max(r.DeliveredSeq, seq); // mốc chỉ tăng
        return Copy(r);
    }

    public async Task<ReceiptDto> MarkReadAsync(Guid conversationId, long seq)
    {
        await Task.Delay(100);
        var r = Get(conversationId, seq);
        r.ReadSeq = Math.Max(r.ReadSeq, seq);
        r.DeliveredSeq = Math.Max(r.DeliveredSeq, r.ReadSeq); // read tự nâng delivered
        return Copy(r);
    }

    private ReceiptDto Get(Guid conversationId, long seq)
    {
        if (seq < 0)
            throw new ApiException(400, "INVALID_RECEIPT_SEQ", "Invalid seq");

        if (!_mine.TryGetValue(conversationId, out var r))
            _mine[conversationId] = r = new ReceiptDto { UserId = Me };

        return r;
    }

    private static ReceiptDto Copy(ReceiptDto r) => new() { UserId = r.UserId, DeliveredSeq = r.DeliveredSeq, ReadSeq = r.ReadSeq };
}
