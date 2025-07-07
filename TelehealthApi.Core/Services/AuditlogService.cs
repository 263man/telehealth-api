using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TelehealthApi.Core.Interfaces;

namespace TelehealthApi.Core.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly IDbContextFactory<TelehealthDbContext> _contextFactory;

        public AuditLogService(IDbContextFactory<TelehealthDbContext> contextFactory)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task LogAuditAsync(string userId, string action, object details, string? resourceType = null, string? resourceId = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var auditLog = new TelehealthDbContext.AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                Timestamp = DateTime.UtcNow,
                Details = JsonSerializer.Serialize(details),
                ResourceType = resourceType ?? string.Empty,
                ResourceId = resourceId ?? string.Empty
            };

            context.AuditLogs.Add(auditLog);
            await context.SaveChangesAsync();
        }
    }
}