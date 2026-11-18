using Chat.Application.DTOs.Receipts;

namespace Chat.Application.Interfaces;

// Implementations publish MessageReceiptChanged only after the receipt transaction commits.
public interface IReceiptEventPublisher
{
    Task PublishAsync(MessageReceiptChangedDto receipt, CancellationToken cancellationToken = default);
}
