using System.ComponentModel.DataAnnotations;

namespace TelehealthApi.Core.Data
{
    public class AuditLog
    {
        [Key]
        public Guid Id { get; set; }

        // Link to the ASP.NET Core Identity user who performed the action
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Action { get; set; } = string.Empty; // e.g., "PatientAccessed", "AppointmentCreated"

        [Required]
        public DateTime Timestamp { get; set; }

        public string? Details { get; set; } // Optional additional details about the action
    }
}
