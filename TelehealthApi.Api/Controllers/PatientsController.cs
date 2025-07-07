using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using TelehealthApi.Core.Interfaces;
using TelehealthApi.Core.Models;

namespace TelehealthApi.Api.Controllers
{
    [Authorize] // Requires authentication for all actions in this controller
    [ApiController]
    [Route("api/[controller]")] // Sets the base route to /api/Patients
    public class PatientsController : ControllerBase
    {
        private readonly ILogger<PatientsController> _logger;
        private readonly IAuditLogService _auditLogService;
        private readonly IPatientService _patientService;

        public PatientsController(
            ILogger<PatientsController> logger,
             IAuditLogService auditLogService, IPatientService patientService)
        {
            _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public class PatientCreateRequest
        {
            [Required] public string FirstName { get; set; } = "";
            [Required] public string LastName { get; set; } = "";
            [Required][RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "BirthDate must be in YYYY-MM-DD format.")] public string BirthDate { get; set; } = "";
            public string Email { get; set; } = "";
            public string? Gender { get; set; }
            [Required]
            public string PhoneNumber { get; set; } = "";

        }
        public class PatientUpdateRequest : PatientCreateRequest
        {
            [Required] public string FhirPatientId { get; set; } = "";
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPatient(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "Anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogInformation("Attempting to get patient {FhirPatientId} by user {UserId} from IP {ClientIp}", id, userId, clientIp);

            try
            {
                var patient = await _patientService.GetPatientByIdAsync(id, userId);

                await _auditLogService.LogAuditAsync(userId, "API_GET_PATIENT_SUCCESS",
                    new { FhirPatientId = id, ClientIp = clientIp }, "Patient", id);

                return Ok(patient);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Get patient failed: {Message}", ex.Message);
                await _auditLogService.LogAuditAsync(userId, "API_GET_PATIENT_FAILED_NOT_FOUND",
                    new { FhirPatientId = id, ClientIp = clientIp, Error = ex.Message }, "Patient", id);
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting patient {FhirPatientId} by user {UserId} from IP {ClientIp}", id, userId, clientIp);
                await _auditLogService.LogAuditAsync(userId, "API_GET_PATIENT_FAILED_EXCEPTION",
                    new { FhirPatientId = id, ClientIp = clientIp, Error = ex.Message }, "Patient", id);
                return StatusCode(500, "An unexpected error occurred while retrieving the patient.");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePatient([FromBody] PatientCreateRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "Anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogInformation("Creating patient for email {Email} by user {UserId} from IP {ClientIp}", request.Email, userId, clientIp);

            try
            {
                var patientModel = new PatientModel
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    BirthDate = request.BirthDate,
                    Email = request.Email,
                    Gender = request.Gender,
                    PhoneNumber = request.PhoneNumber,
                    UserId = userId
                };
                var createdPatient = await _patientService.CreatePatientAsync(patientModel, userId);
                await _auditLogService.LogAuditAsync(userId, "API_CREATE_PATIENT_SUCCESS", new { FhirPatientId = createdPatient.FhirPatientId, Email = createdPatient.Email, ClientIp = clientIp }, "Patient", createdPatient.FhirPatientId);
                return CreatedAtAction(nameof(GetPatient), new { id = createdPatient.FhirPatientId }, createdPatient);
            }
            catch (InvalidOperationException ex)
            {   // This now catches both duplicate email AND invalid email format from the service
                _logger.LogWarning(ex, "Failed to create patient: {Message}", ex.Message);
                await _auditLogService.LogAuditAsync(userId, "API_CREATE_PATIENT_FAILED_BUSINESS_LOGIC", new { Email = request.Email, ClientIp = clientIp, Error = ex.Message }, "Patient", null);
                return Conflict(new { Message = ex.Message });
                //Consistently returns 409 for business logic errors
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating patient for email {Email}", request.Email);
                await _auditLogService.LogAuditAsync(userId, "API_CREATE_PATIENT_ERROR", new { Email = request.Email, ClientIp = clientIp, Error = ex.Message }, "Patient", null);
                return StatusCode(500, new { Message = "An unexpected error occurred." });
            }
        }

        [HttpPut("{fhirPatientId}")]
        public async Task<IActionResult> UpdatePatient(string fhirPatientId, [FromBody] PatientUpdateRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (fhirPatientId != request.FhirPatientId)
            {
                return BadRequest("FHIR Patient ID in URL does not match ID in request body.");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "Anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogInformation("Attempting to update patient {FhirPatientId} by user {UserId} from IP {ClientIp}", fhirPatientId, userId, clientIp);

            try
            {
                var patientModel = new PatientModel
                {
                    FhirPatientId = request.FhirPatientId,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    BirthDate = request.BirthDate,
                    Gender = request.Gender, // Added: Map Gender from request
                    Email = request.Email,
                    PhoneNumber = request.PhoneNumber, // Added: Map PhoneNumber from request
                    UserId = userId // Assign the authenticated user's ID
                };

                var updatedPatient = await _patientService.UpdatePatientAsync(patientModel, userId);

                await _auditLogService.LogAuditAsync(userId, "API_UPDATE_PATIENT_SUCCESS",
                    new { FhirPatientId = updatedPatient.FhirPatientId, Email = updatedPatient.Email, ClientIp = clientIp }, "Patient", updatedPatient.FhirPatientId);

                return Ok(updatedPatient);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Update patient failed due to business logic: {Message}", ex.Message);
                await _auditLogService.LogAuditAsync(userId, "API_UPDATE_PATIENT_FAILED_BUSINESS_LOGIC",
                    new { FhirPatientId = fhirPatientId, ClientIp = clientIp, Error = ex.Message }, "Patient", fhirPatientId);
                return Conflict(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Update patient failed: {Message}", ex.Message);
                await _auditLogService.LogAuditAsync(userId, "API_UPDATE_PATIENT_FAILED_NOT_FOUND",
                    new { FhirPatientId = fhirPatientId, ClientIp = clientIp, Error = ex.Message }, "Patient", fhirPatientId);
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating patient {FhirPatientId} by user {UserId} from IP {ClientIp}", fhirPatientId, userId, clientIp);
                await _auditLogService.LogAuditAsync(userId, "API_UPDATE_PATIENT_FAILED_EXCEPTION",
                    new { FhirPatientId = fhirPatientId, ClientIp = clientIp, Error = ex.Message }, "Patient", fhirPatientId);
                return StatusCode(500, "An unexpected error occurred while updating the patient.");
            }
        }

        [HttpDelete("{fhirPatientid}")]
        public async Task<IActionResult> DeletePatient(string fhirPatientId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "Anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogInformation("Attempting to delete patient {FhirPatientId} by user {UserId} from IP {ClientIp}", fhirPatientId, userId, clientIp);

            try
            {
                await _patientService.DeletePatientAsync(fhirPatientId, userId);

                await _auditLogService.LogAuditAsync(userId, "API_DELETE_PATIENT_SUCCESS",
                    new { FhirPatientId = fhirPatientId, ClientIp = clientIp }, "Patient", fhirPatientId);

                return NoContent(); // 204 No Content for successful deletion
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Delete patient failed due to business logic: {Message}", ex.Message);
                await _auditLogService.LogAuditAsync(userId, "API_DELETE_PATIENT_FAILED_BUSINESS_LOGIC",
                    new { FhirPatientId = fhirPatientId, ClientIp = clientIp, Error = ex.Message }, "Patient", fhirPatientId);
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Delete patient failed: {Message}", ex.Message);
                await _auditLogService.LogAuditAsync(userId, "API_DELETE_PATIENT_FAILED_NOT_FOUND",
                    new { FhirPatientId = fhirPatientId, ClientIp = clientIp, Error = ex.Message }, "Patient", fhirPatientId);
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting patient {FhirPatientId} by user {UserId} from IP {ClientIp}", fhirPatientId, userId, clientIp);
                await _auditLogService.LogAuditAsync(userId, "API_DELETE_PATIENT_FAILED_EXCEPTION",
                    new { FhirPatientId = fhirPatientId, ClientIp = clientIp, Error = ex.Message }, "Patient", fhirPatientId);
                return StatusCode(500, "An unexpected error occurred while deleting the patient.");
            }
        }

    }
}