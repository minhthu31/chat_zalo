using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Chat.Api.Services;

public sealed class PasswordRecovery(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<PasswordRecovery> logger) : BackgroundService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendDelay = TimeSpan.FromMinutes(1);
    private const int MaxAttempts = 5;

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 4096 });
    private readonly object gate = new();
    private readonly Channel<Delivery> queue = Channel.CreateBounded<Delivery>(64);

    private sealed record Delivery(string Email, string Key);
    private sealed record Challenge(Guid UserId, string Token, byte[] Digest, int AttemptsLeft, DateTimeOffset Expires);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["PasswordRecovery:Smtp:Host"]) &&
        MailAddress.TryCreate(configuration["PasswordRecovery:Smtp:From"] ?? "", out _) &&
        configuration.GetValue("PasswordRecovery:Smtp:Port", 587) is >= 1 and <= 65535 &&
        (string.IsNullOrWhiteSpace(configuration["PasswordRecovery:Smtp:Username"]) ||
         !string.IsNullOrWhiteSpace(configuration["PasswordRecovery:Smtp:Password"]));

    public bool Enqueue(string email, string key)
    {
        lock (gate)
        {
            if (cache.TryGetValue((key, "resend"), out _))
                return true;

            if (!queue.Writer.TryWrite(new Delivery(email, key)))
                return false;

            cache.Set((key, "resend"), true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ResendDelay,
                Size = 1
            });
            return true;
        }
    }

    public (Guid UserId, string Token)? Consume(string key, string code)
    {
        lock (gate)
        {
            if (!cache.TryGetValue<Challenge>((key, "code"), out var challenge) || challenge is null)
                return null;

            if (CryptographicOperations.FixedTimeEquals(challenge.Digest, Hash(code)))
            {
                cache.Remove((key, "code"));
                return (challenge.UserId, challenge.Token);
            }

            if (challenge.AttemptsLeft == 1)
                cache.Remove((key, "code"));
            else
                Store(key, challenge with { AttemptsLeft = challenge.AttemptsLeft - 1 });

            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await DeliverAsync(request, timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Delivery errors must never log an email address, code or SMTP credentials.
                logger.LogWarning("Password recovery delivery failed ({ErrorType}).", ex.GetType().Name);
            }
        }
    }

    private async Task DeliverAsync(Delivery request, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(request.Email);
        if (string.IsNullOrWhiteSpace(user?.Email)) return;

        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var challenge = new Challenge(user.Id, token, Hash(code), MaxAttempts,
            DateTimeOffset.UtcNow.Add(CodeLifetime));

        using var mail = new MailMessage(configuration["PasswordRecovery:Smtp:From"]!, user.Email,
            "Chat Zalo - Mã đặt lại mật khẩu",
            $"Mã đặt lại mật khẩu của bạn là {code}. Mã có hiệu lực trong 10 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.");
        using var smtp = new SmtpClient(configuration["PasswordRecovery:Smtp:Host"]!,
            configuration.GetValue("PasswordRecovery:Smtp:Port", 587))
        {
            EnableSsl = true
        };

        var username = configuration["PasswordRecovery:Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(username))
            smtp.Credentials = new NetworkCredential(username, configuration["PasswordRecovery:Smtp:Password"]);

        lock (gate) Store(request.Key, challenge);
        try
        {
            await smtp.SendMailAsync(mail, ct);
        }
        catch
        {
            lock (gate) cache.Remove((request.Key, "code"));
            throw;
        }
    }

    private void Store(string key, Challenge challenge) =>
        cache.Set((key, "code"), challenge, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = challenge.Expires,
            Size = 1
        });

    private static byte[] Hash(string code) => SHA256.HashData(Encoding.ASCII.GetBytes(code));

    public override void Dispose()
    {
        base.Dispose();
        cache.Dispose();
    }
}
