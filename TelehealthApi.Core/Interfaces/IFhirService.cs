using Hl7.Fhir.Model;

namespace TelehealthApi.Core.Interfaces
{
    public interface IFhirService
    {
        // Patient-related methods
        Task<Patient?> GetPatientAsync(string id);
        Task<Patient> CreatePatientAsync(Patient patient);
        Task<Patient> UpdatePatientAsync(string id, Patient patient);
        Task<(bool Success, string Reason)> DeletePatientAsync(string id);
        Task<Bundle?> SearchPatientsByEmailAsync(string email);

        // Appointment-related methods
        Task<Appointment> CreateAppointmentAsync(Appointment appointment);
        Task<Appointment?> GetAppointmentAsync(string id);
        Task<Appointment> UpdateAppointmentAsync(string id, Appointment appointment);
        Task<(bool Success, string Reason)> DeleteAppointmentAsync(string id);
        Task<Bundle?> SearchAppointmentsByPatientAsync(string patientFhirId);
    }
}
