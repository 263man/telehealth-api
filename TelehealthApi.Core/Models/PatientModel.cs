using System.ComponentModel.DataAnnotations;

namespace TelehealthApi.Core.Models
{
    public record PatientModel
    {
        // Primary identifier for the patient in the FHIR server.
        // It can be null for new patients before creation.
        public string? FhirPatientId { get; init; }

        [Required]
        public string FirstName { get; init; } = "";

        [Required]
        public string LastName { get; init; } = "";

        // Storing as string in YYYY-MM-DD format for consistency with FHIR API interactions
        // and simplified input validation.
        [Required]
        [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "BirthDate must be in YYYY-MM-DD format.")]
        public string BirthDate { get; init; } = "";

        // Gender, typically represented as a code in FHIR (e.g., 'male', 'female', 'other', 'unknown').
        public string? Gender { get; init; }

        [Required]
        [EmailAddress]
        public string Email { get; init; } = "";

        [Required]
        public string PhoneNumber { get; init; } = "";

        // Links to the local ASP.NET Core Identity user. Nullable if the patient isn't yet linked
        // to an application user, or if they are created by an administrator.
        public string? UserId { get; init; }

        // Encrypted version of the patient's full name, handled by the service for local storage.
        public string? EncryptedName { get; init; }
    }
}