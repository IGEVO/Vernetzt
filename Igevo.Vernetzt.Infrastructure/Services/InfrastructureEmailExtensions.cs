using Igevo.Vernetzt.Core.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Igevo.Vernetzt.Infrastructure.Services;

public static class InfrastructureEmailExtensions
{
    /// <summary>
    /// Registers <see cref="SmtpSender"/> as <see cref="ISmtpSender"/> and
    /// <see cref="EmailService"/> as <see cref="IEmailService"/> via a factory lambda so that
    /// internal types stay encapsulated in the Infrastructure assembly.
    /// Call this instead of registering <see cref="IEmailService"/> directly from the host project.
    /// </summary>
    public static IServiceCollection AddInfrastructureEmailSender(this IServiceCollection services)
    {
        // Singleton: the retry/circuit-breaker pipeline must keep its state across requests,
        // otherwise every scope starts with a closed breaker and it never opens.
        services.AddSingleton<ISmtpSender, SmtpSender>();
        services.AddScoped<IEmailService>(sp => new EmailService(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<EmailService>>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<ISmtpSender>()
        ));
        return services;
    }
}
