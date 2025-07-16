using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using TelehealthApi.Core.Interfaces;

namespace TelehealthApi.Core.Services
{
    public class FhirService : IFhirService
    {
        private readonly FhirClient _fhirClient;
        private readonly ILogger<FhirService> _logger;

        public FhirService(FhirClient fhirClient, ILogger<FhirService> logger)
        {
            _fhirClient = fhirClient;
            _logger = logger;
        }

        // --- Patient-related methods (UNCHANGED from previous versions) ---
        public async Task<Patient?> GetPatientAsync(string id)
        {
            try
            {
                var patient = await _fhirClient.ReadAsync<Patient>($"Patient/{id}");
                _logger.LogInformation("Successfully retrieved patient {PatientId}", id);
                return patient;
            }
            catch (FhirOperationException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound || ex.Status == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogWarning("Patient {PatientId} not found or has been deleted", id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting patient {PatientId}: {Message}", id, ex.Message);
                throw;
            }
        }

        public async Task<Patient> CreatePatientAsync(Patient patient)
        {
            var createdPatient = await _fhirClient.CreateAsync(patient);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            _logger.LogInformation("Successfully created patient with ID {PatientId}", createdPatient.Id);
#pragma warning restore CS8602
            return createdPatient;
        }

        public async Task<Patient> UpdatePatientAsync(string id, Patient patient)
        {
            try
            {
                patient.Id = id; // Ensure the patient object has the ID set for the update operation
                var updatedPatient = await _fhirClient.UpdateAsync(patient);
                if (updatedPatient == null)
                {
                    throw new InvalidOperationException($"Update operation returned null for patient with ID {id}.");
                }
                _logger.LogInformation("Successfully updated patient {PatientId}", id);
                return updatedPatient;
            }
            catch (FhirOperationException ex)
            {
                _logger.LogError(ex, "Error updating patient {PatientId}: {Message}", id, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating patient {PatientId}: {Message}", id, ex.Message);
                throw;
            }
        }

        public async Task<(bool Success, string Reason)> DeletePatientAsync(string id)
        {
            try
            {
                var patient = await _fhirClient.ReadAsync<Patient>($"Patient/{id}");
                if (patient == null)
                {
                    _logger.LogWarning("Patient {PatientId} not found before deletion attempt", id);
                    return (false, "Patient not found");
                }

                await _fhirClient.DeleteAsync($"Patient/{id}");
                _logger.LogInformation("Successfully deleted patient {PatientId}", id);
                return (true, "Deletion successful");
            }
            catch (FhirOperationException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Attempted to delete non-existent patient {PatientId}", id);
                return (false, "Patient not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting patient {PatientId}: {Message}", id, ex.Message);
                return (false, $"Deletion failed: {ex.Message}");
            }
        }

        public async Task<Bundle?> SearchPatientsByEmailAsync(string email)
        {
            try
            {
                var result = await _fhirClient.SearchAsync<Patient>(new[] { $"telecom={email}" });
                if (result?.Entry == null || result.Entry.Count == 0)
                {
                    _logger.LogInformation("No existing patients found for email: {Email}", email);
                    return null;
                }
                _logger.LogInformation("Potential duplicate email detected for: {Email}", email);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching patients by email {Email}", email);
                throw;
            }
        }
    }
}
