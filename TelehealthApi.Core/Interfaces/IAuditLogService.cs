namespace TelehealthApi.Core.Interfaces
{
    public interface IAuditLogService
    {
        Task LogAuditAsync(string userId, string action, object details, string? resourceType = null, string? resourceId = null);
    }

}