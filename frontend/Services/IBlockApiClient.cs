namespace Chat.Client.Services;

// Chặn / bỏ chặn người dùng (hợp đồng mục 4.4). Cả hai đều trả 204 và lặp lại nhiều lần không sao.
public interface IBlockApiClient
{
    Task BlockAsync(Guid userId);     // PUT /api/blocks/{userId}
    Task UnblockAsync(Guid userId);   // DELETE /api/blocks/{userId}
}
