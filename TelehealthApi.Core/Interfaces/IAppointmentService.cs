using TelehealthApi.Core.Models;

namespace TelehealthApi.Core.Interfaces
{
    public interface IAppointmentService
    {
        /// <summary>
        /// Creates a new appointment and synchronizes it with the FHIR server.
        /// </summary>
        /// <param name="appointment">The appointment details to create.</param>
        /// <param name="userId">The ID of the user performing the action.</param>
        /// <returns>The created appointment model with assigned IDs.</returns>
        Task<AppointmentModel> CreateAppointmentAsync(AppointmentModel appointment, string userId);

        /// <summary>
        /// Retrieves an appointment by its FHIR Appointment ID.
        /// </summary>
        /// <param name="fhirAppointmentId">The FHIR Appointment ID.</param>
        /// <param name="userId">The ID of the user performing the action.</param>
        /// <returns>The appointment model if found, otherwise throws an exception.</returns>
        Task<AppointmentModel> GetAppointmentByIdAsync(string fhirAppointmentId, string userId);

        /// <summary>
        /// Updates an existing appointment and synchronizes with the FHIR server.
        /// </summary>
        /// <param name="appointment">The updated appointment details.</param>
        /// <param name="userId">The ID of the user performing the action.</param>
        /// <returns>The updated appointment model.</returns>
        Task<AppointmentModel> UpdateAppointmentAsync(AppointmentModel appointment, string userId);

        /// <summary>
        /// Deletes an appointment from both the local database and FHIR server.
        /// </summary>
        /// <param name="fhirAppointmentId">The FHIR Appointment ID to delete.</param>
        /// <param name="userId">The ID of the user performing the action.</param>
        /// <returns>True if deletion succeeds, otherwise throws an exception.</returns>
        Task<bool> DeleteAppointmentAsync(string fhirAppointmentId, string userId);

        /// <summary>
        /// Checks for scheduling conflicts or duplicates based on time and patient.
        /// </summary>
        /// <param name="patientId">The ID of the patient.</param>
        /// <param name="startTime">The proposed start time.</param>
        /// <param name="endTime">The proposed end time.</param>
        /// <param name="excludeLocalAppointmentId">Optional: Local ID of the appointment to exclude from conflict checks (for updates).</param>
        /// <param name="excludeFhirAppointmentId">Optional: FHIR ID of the appointment to exclude from conflict checks (for updates).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A tuple of conflicting local and FHIR appointments.</returns>
        Task<(List<TelehealthApi.Core.Data.Appointment> LocalConflicts, List<Hl7.Fhir.Model.Appointment> FhirConflicts)> CheckConflictsAsync(
            Guid patientId, DateTime startTime, DateTime endTime, Guid? excludeLocalAppointmentId = null, string? excludeFhirAppointmentId = null, CancellationToken cancellationToken = default);
    }
}
