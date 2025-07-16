using TelehealthApi.Core.Models;

namespace TelehealthApi.Core.Interfaces
{
    public interface IPatientService
    {
        Task<PatientModel> CreatePatientAsync(PatientModel patient, string userId, bool shouldLogAudit = true);
        Task<PatientModel> GetPatientByIdAsync(string fhirPatientId, string userId, bool shouldLogAudit = true);
        Task<PatientModel> UpdatePatientAsync(PatientModel patient, string userId, bool shouldLogAudit = true);
        Task<bool> DeletePatientAsync(string fhirPatientId, string userId, bool shouldLogAudit = true);
        Task<(List<Core.Data.Patient> LocalMatches, List<Hl7.Fhir.Model.Patient> FhirMatches)> CheckDuplicatesAsync(string email, CancellationToken cancellationToken = default);
    }
}