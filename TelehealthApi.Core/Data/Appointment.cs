using System.ComponentModel.DataAnnotations;

namespace TelehealthApi.Core.Data
{
    public class Appointment
    {
        [Key]
        public Guid Id { get; set; }

        // Foreign key to the FHIR Appointment resource
        public string FhirAppointmentId { get; set; } = string.Empty;

        // Foreign key to our local Patient entity
        [Required]
        public Guid PatientId { get; set; }

        // Navigation property for the related Patient
        public Patient Patient { get; set; } = default!; // EF Core will populate this

        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        // Store the status as a string in the database, but we'll use our enum for mapping
        // This allows flexibility if the DB needs to store string values
        [Required]
        public string Status { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
