using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Chat.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Chat.Api.Services;

public sealed class PasswordRecovery(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    IHttpClientFactory httpClients,
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

    private IConfigurationSection Gmail => configuration.GetSection("PasswordRecovery:Gmail");

    private bool UseGmail => Gmail.GetValue<bool>("Enabled");

    public bool IsConfigured => UseGmail
        ? MailAddress.TryCreate(Gmail["From"], out _) &&
          !string.IsNullOrWhiteSpace(Gmail["ClientId"]) &&
          !string.IsNullOrWhiteSpace(Gmail["ClientSecret"]) &&
          !string.IsNullOrWhiteSpace(Gmail["RefreshToken"])
        : !string.IsNullOrWhiteSpace(configuration["PasswordRecovery:Smtp:Host"]) &&
          MailAddress.TryCreate(configuration["PasswordRecovery:Smtp:From"], out _) &&
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

        const string subject = "Chat Zalo - Mã đặt lại mật khẩu";
        var body = $"Mã đặt lại mật khẩu của bạn là {code}. Mã có hiệu lực trong 10 phút. Nếu bạn không yêu cầu, hãy bỏ qua email này.";

        lock (gate) Store(request.Key, challenge);
        try
        {
            if (UseGmail)
                await SendWithGmailAsync(user.Email, subject, body, ct);
            else
                await SendWithSmtpAsync(user.Email, subject, body, ct);
        }
        catch
        {
            lock (gate) cache.Remove((request.Key, "code"));
            throw;
        }
    }

    private async Task SendWithSmtpAsync(string email, string subject, string body, CancellationToken ct)
    {
        using var mail = new MailMessage(configuration["PasswordRecovery:Smtp:From"]!, email, subject, body);
        using var smtp = new SmtpClient(configuration["PasswordRecovery:Smtp:Host"]!,
            configuration.GetValue("PasswordRecovery:Smtp:Port", 587))
        {
            EnableSsl = true
        };

        var username = configuration["PasswordRecovery:Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(username))
            smtp.Credentials = new NetworkCredential(username, configuration["PasswordRecovery:Smtp:Password"]);

        await smtp.SendMailAsync(mail, ct);
    }

    private async Task SendWithGmailAsync(string email, string subject, string body, CancellationToken ct)
    {
        var gmail = Gmail;
        using var client = httpClients.CreateClient("Gmail");
        using var refresh = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = gmail["ClientId"]!,
            ["client_secret"] = gmail["ClientSecret"]!,
            ["refresh_token"] = gmail["RefreshToken"]!,
            ["grant_type"] = "refresh_token"
        });
        using var token = await client.PostAsync("https://oauth2.googleapis.com/token", refresh, ct);
        token.EnsureSuccessStatusCode();
        using var tokenJson = await token.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty Gmail OAuth response.");

        var from = new MailAddress(gmail["From"]!).Address;
        var to = new MailAddress(email).Address;
        var encodedSubject = Convert.ToBase64String(Encoding.UTF8.GetBytes(subject));
        var encodedBody = Convert.ToBase64String(Encoding.UTF8.GetBytes(body), Base64FormattingOptions.InsertLineBreaks);
        var mime = $"From: {from}\r\nTo: {to}\r\nSubject: =?UTF-8?B?{encodedSubject}?=\r\n" +
            "MIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\n" +
            $"Content-Transfer-Encoding: base64\r\n\r\n{encodedBody}\r\n";
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(mime))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var send = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        send.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenJson.RootElement.GetProperty("access_token").GetString());
        send.Content = JsonContent.Create(new { raw });
        using var response = await client.SendAsync(send, ct);
        response.EnsureSuccessStatusCode();
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
