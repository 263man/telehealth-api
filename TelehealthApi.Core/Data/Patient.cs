using System.ComponentModel.DataAnnotations;

namespace TelehealthApi.Core.Data
{
    public class Patient
    {
        [Key]
        public Guid Id { get; set; } // Local unique identifier for our patient record

        // Foreign key to the FHIR Patient resource on the FHIR server
        // This links our local authentication/minimal data to the richer FHIR data
        public string FhirPatientId { get; set; } = string.Empty; // Initialized to empty string for non-nullable property

        [Required]
        public string Email { get; set; } = string.Empty;

        // Encrypted patient name for local storage, adhering to PHI best practices
        [Required]
        public string EncryptedName { get; set; } = string.Empty;

        // Link to the ASP.NET Core Identity user (optional, but good for direct linkage)
        // This is how we connect our Patient entity to the IdentityUser for login
        public string? UserId { get; set; } // Nullable because a patient might not be linked to an IdentityUser initially
    }
}
