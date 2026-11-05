using Chat.Application.DTOs.Messages;
using Chat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chat.Database.Tests
{
    public class MessageServiceTests
    {
        [Fact]
        public async Task SendMessage_ShouldAssignAtomicSeqAndHandleIdempotency()
        {
            // Sử dụng cơ sở dữ liệu InMemory để test logic cấp Seq và Retry an toàn
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(options);
            var service = new MessageService(context);

            var conversationId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            // Thêm giả lập thành viên hội thoại để vượt qua bước check quyền (Yêu cầu 5)
            context.ConversationMembers.Add(new ConversationMember { ConversationId = conversationId, UserId = userId });
            await context.SaveChangesAsync();

            var request = new SendMessageRequest
            {
                ConversationId = conversationId,
                Content = "Test Message Concurrency",
                ClientMessageId = "client-retry-test-123"
            };

            // 1. Gửi tin nhắn lần đầu -> Kỳ vọng trả về 201 Created và Seq = 1
            var (result1, statusCode1) = await service.SendMessageAsync(userId, request);
            Assert.Equal(201, statusCode1);
            Assert.Equal(1, result1.Seq);

            // 2. Gửi lại với cùng ClientMessageId (Kiểm thử Retry / Idempotency - Yêu cầu 4) -> Kỳ vọng trả về 200 OK và giữ nguyên Seq
            var (result2, statusCode2) = await service.SendMessageAsync(userId, request);
            Assert.Equal(200, statusCode2);
            Assert.Equal(result1.Seq, result2.Seq);
        }
    }
}