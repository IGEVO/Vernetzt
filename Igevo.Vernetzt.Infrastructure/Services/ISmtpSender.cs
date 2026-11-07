using System.Net.Mail;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Abstracts the physical SMTP send operation so that <see cref="EmailService"/> can be
/// unit-tested independently of a real SMTP server.
/// </summary>
internal interface ISmtpSender
{
    Task SendAsync(MailMessage message, CancellationToken cancellationToken = default);
}
