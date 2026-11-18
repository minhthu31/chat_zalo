# Sprint 3 - DI Backend / Frontend

API/DTO: [API_CONTRACT.md](API_CONTRACT.md).
Database: [DATABASE_RULES.md](../database/DATABASE_RULES.md).

**DI Backend — `backend/Chat.Api/Program.cs`**

```csharp
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IAttachmentMessageService, AttachmentMessageService>();
builder.Services.AddScoped<IConversationSettingsService, ConversationSettingsService>();
builder.Services.AddScoped<IMessageReceiptService, MessageReceiptService>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
```

Giữ các service Messages, Blocks, GroupPermission và connection tracker hiện có. Interface ở Application; implementation truy cập DB/storage ở Infrastructure.

**DI Frontend — `frontend/Program.cs`**

```csharp
builder.Services.AddScoped<IProfileApiClient, ProfileApiClient>();
builder.Services.AddScoped<IAttachmentApiClient, AttachmentApiClient>();
builder.Services.AddScoped<IConversationSettingsApiClient, ConversationSettingsApiClient>();
builder.Services.AddScoped<IMessageReceiptApiClient, MessageReceiptApiClient>();
```

Dùng chung `HttpClient`, `ApiRequestSender`, AuthState và realtime client hiện có.
