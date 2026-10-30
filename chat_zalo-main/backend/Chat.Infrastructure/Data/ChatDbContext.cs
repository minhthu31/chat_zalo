namespace Chat.Infrastructure.Data;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Chat.Domain.Entities;
using Chat.Infrastructure.Identity;

public class ChatDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
    {
    }

    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);
    }
}

