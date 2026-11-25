using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Recruitment.Api.Services.Auth;

public sealed class PasswordResetOptions
{
    public int TokenLifetimeMinutes { get; set; } = 15;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpEnableSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
}

public interface IPasswordResetEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string email, string token, CancellationToken cancellationToken);
}

public sealed class SmtpPasswordResetEmailSender(IOptions<PasswordResetOptions> options)
    : IPasswordResetEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.SmtpHost)
        && MailAddress.TryCreate(options.Value.FromEmail, out _);

    public async Task SendAsync(string email, string token, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.SmtpEnableSsl,
            Credentials = string.IsNullOrWhiteSpace(settings.SmtpUsername) ? null
                : new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword)
        };
        using var message = new MailMessage(settings.FromEmail, email)
        {
            Subject = "Recruitment – Đặt lại mật khẩu",
            Body = $"Bạn vừa yêu cầu đặt lại mật khẩu. Nhập mã sau vào trang Đặt lại mật khẩu: \n\n{token}\n\nMã chỉ dùng một lần trong {settings.TokenLifetimeMinutes} phút kể từ lúc yêu cầu. Nếu không phải bạn yêu cầu, hãy bỏ qua email này.",
            IsBodyHtml = false
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}
