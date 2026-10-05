using Rroms.Web.Contracts;

namespace Rroms.Web.Services;

/// <summary>
/// Temporary implementation of <see cref="INotificationService"/> that only writes to the log.
/// </summary>

public class LoggingNotificationService : INotificationService
{
    private readonly ILogger<LoggingNotificationService> _logger;

    /// <summary>Receives a logger from dependency injection.</summary>
    public LoggingNotificationService(ILogger<LoggingNotificationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
    {
        _logger.LogInformation(
            "[Notification placeholder] To user {UserId} ({Type}): {Title} - {Message}",
            userId, type, title, message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
    {
        _logger.LogInformation(
            "[Notification placeholder] To role {RoleName} ({Type}): {Title} - {Message}",
            roleName, type, title, message);
        return Task.CompletedTask;
    }
}