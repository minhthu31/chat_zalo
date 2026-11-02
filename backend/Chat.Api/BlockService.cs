
using Chat.Application.Interfaces;
using Chat.Domain.Entities;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Services;

public class BlockService : IBlockService
{
    private readonly ChatDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public BlockService(
        ChatDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task BlockAsync(
        Guid blockerUserId,
        Guid blockedUserId)
    {
        if (blockerUserId == blockedUserId)
        {
            throw new InvalidOperationException(
                "SELF_BLOCK_NOT_ALLOWED");
        }

        var usersExist = await _userManager.Users
            .AnyAsync(user => user.Id == blockerUserId)
            && await _userManager.Users
            .AnyAsync(user => user.Id == blockedUserId);

        if (!usersExist)
        {
            throw new KeyNotFoundException("USER_NOT_FOUND");
        }

        var alreadyBlocked = await _context.UserBlocks
            .AnyAsync(block =>
                block.BlockerUserId == blockerUserId &&
                block.BlockedUserId == blockedUserId);

        if (alreadyBlocked)
        {
            return;
        }

        _context.UserBlocks.Add(new UserBlock
        {
            BlockerUserId = blockerUserId,
            BlockedUserId = blockedUserId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    public async Task UnblockAsync(
        Guid blockerUserId,
        Guid blockedUserId)
    {
        var block = await _context.UserBlocks
            .FirstOrDefaultAsync(item =>
                item.BlockerUserId == blockerUserId &&
                item.BlockedUserId == blockedUserId);

        if (block == null)
        {
            return;
        }

        _context.UserBlocks.Remove(block);

        await _context.SaveChangesAsync();
    }

    public async Task<bool> HasBlockedAsync(
        Guid blockerUserId,
        Guid blockedUserId)
    {
        return await _context.UserBlocks
            .AnyAsync(block =>
                block.BlockerUserId == blockerUserId &&
                block.BlockedUserId == blockedUserId);
    }

    public async Task<bool> IsBlockedAsync(
        Guid userId1,
        Guid userId2)
    {
        return await _context.UserBlocks
            .AnyAsync(block =>
                (block.BlockerUserId == userId1 &&
                 block.BlockedUserId == userId2)
                ||
                (block.BlockerUserId == userId2 &&
                 block.BlockedUserId == userId1));
    }
}
