using System.ComponentModel.DataAnnotations;
using TelehealthApi.Core.Enums;

namespace TelehealthApi.Core.Models
{
    public record AppointmentModel
    {
        // Local unique identifier for the appointment in the local database.
        // Nullable for creation, assigned by the service.
        public Guid? Id { get; init; }

        // FHIR Appointment ID for synchronization with the FHIR server.
        // Nullable for creation requests (assigned by FHIR server), required for updates/gets.
        public string? FhirAppointmentId { get; init; }

        // Foreign key to the Patient entity in the local database
        [Required(ErrorMessage = "Patient ID is required.")]
        public Guid PatientId { get; init; } // Changed to init

        // Start time of the appointment
        [Required(ErrorMessage = "Start time is required.")]
        public DateTime StartTime { get; init; } // Changed to init

        // End time of the appointment
        [Required(ErrorMessage = "End time is required.")]

        public DateTime EndTime { get; init; } // Changed to init

        // Status is often required for an appointment
        public required AppointmentStatus Status { get; set; }

        // Optional description or notes for the appointment
        public string? Description { get; init; } // Changed to init
    }
}
