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
    }
}
