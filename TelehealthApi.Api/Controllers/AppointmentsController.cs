using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims; // Added for ClaimTypes.NameIdentifier
using TelehealthApi.Core.Interfaces;
using TelehealthApi.Core.Models;

namespace TelehealthApi.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AppointmentsController : ControllerBase
    {
        private readonly IAppointmentService _appointmentService;
        private readonly IAuditLogService _auditLogService;
        private readonly ILogger<AppointmentsController> _logger; // Added for controller-specific logging

        public AppointmentsController(
            IAppointmentService appointmentService,
            IAuditLogService auditLogService,
            ILogger<AppointmentsController> logger) // Injected ILogger
        {
            _appointmentService = appointmentService ?? throw new ArgumentNullException(nameof(appointmentService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger)); // Initialize logger
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)] // Added 500 for unexpected errors
        public async Task<IActionResult> CreateAppointment([FromBody] AppointmentModel appointment)
        {
            // Basic input validation
            if (appointment == null)
            {
                _logger.LogWarning("CreateAppointment: Received null appointment data.");
                return BadRequest("Appointment data is required.");
            }

            // Extract userId from claims for robust identification
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString(); // Capture client IP for audit log

            try
            {
                var createdAppointment = await _appointmentService.CreateAppointmentAsync(appointment, userId);

                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_CREATE_APPOINTMENT_SUCCESS", // More specific audit event
                    new { FhirAppointmentId = createdAppointment.FhirAppointmentId, PatientId = createdAppointment.PatientId, ClientIp = clientIp },
                    "Appointment",
                    createdAppointment.FhirAppointmentId);

                _logger.LogInformation("Appointment created successfully with FHIR ID: {FhirAppointmentId}", createdAppointment.FhirAppointmentId);
                return CreatedAtAction(nameof(GetAppointmentById), new { fhirAppointmentId = createdAppointment.FhirAppointmentId }, createdAppointment);
            }
            // Catch specific business logic exceptions from the service layer
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "CreateAppointment: Business logic error for user {UserId}. Message: {Message}", userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_CREATE_APPOINTMENT_FAILED_BUSINESS_LOGIC",
                    new { Error = ex.Message, PatientId = appointment.PatientId, ClientIp = clientIp },
                    "Appointment",
                    null);
                return BadRequest(new { Message = ex.Message }); // Return specific message
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "CreateAppointment: Patient not found for user {UserId}. Message: {Message}", userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_CREATE_APPOINTMENT_FAILED_PATIENT_NOT_FOUND",
                    new { Error = ex.Message, PatientId = appointment.PatientId, ClientIp = clientIp },
                    "Appointment",
                    null);
                return NotFound(new { Message = ex.Message }); // Return 404 if patient not found
            }
            // Catch any other unexpected exceptions
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateAppointment: An unexpected error occurred for user {UserId}. Message: {Message}", userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_CREATE_APPOINTMENT_FAILED_EXCEPTION",
                    new { Error = ex.Message, PatientId = appointment.PatientId, ClientIp = clientIp },
                    "Appointment",
                    null);
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while creating the appointment.");
            }
        }

        [HttpGet("{fhirAppointmentId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)] // Added 500
        public async Task<IActionResult> GetAppointmentById(string fhirAppointmentId)
        {
            if (string.IsNullOrWhiteSpace(fhirAppointmentId))
            {
                _logger.LogWarning("GetAppointmentById: Received empty FHIR Appointment ID.");
                return BadRequest("FHIR Appointment ID is required.");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            try
            {
                var appointment = await _appointmentService.GetAppointmentByIdAsync(fhirAppointmentId, userId);
                if (appointment == null) // Service might return null if not found
                {
                    _logger.LogWarning("GetAppointmentById: Appointment with FHIR ID {FhirAppointmentId} not found.", fhirAppointmentId);
                    await _auditLogService.LogAuditAsync(
                        userId,
                        "API_GET_APPOINTMENT_NOT_FOUND",
                        new { FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                        "Appointment",
                        fhirAppointmentId);
                    return NotFound($"Appointment with FHIR ID {fhirAppointmentId} not found.");
                }

                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_GET_APPOINTMENT_SUCCESS",
                    new { FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                    "Appointment",
                    fhirAppointmentId);

                _logger.LogInformation("Appointment with FHIR ID {FhirAppointmentId} retrieved successfully.", fhirAppointmentId);
                return Ok(appointment);
            }
            catch (KeyNotFoundException ex) // Catch KeyNotFoundException specifically from service
            {
                _logger.LogWarning(ex, "GetAppointmentById: Appointment with FHIR ID {FhirAppointmentId} not found. Message: {Message}", fhirAppointmentId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_GET_APPOINTMENT_NOT_FOUND_EXCEPTION", // Specific audit for exception-driven not found
                    new { FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp, Error = ex.Message },
                    "Appointment",
                    fhirAppointmentId);
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAppointmentById: An unexpected error occurred for FHIR ID {FhirAppointmentId} by user {UserId}. Message: {Message}", fhirAppointmentId, userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_GET_APPOINTMENT_FAILED_EXCEPTION",
                    new { Error = ex.Message, FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                    "Appointment",
                    fhirAppointmentId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while retrieving the appointment.");
            }
        }

        [HttpPut("{fhirAppointmentId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)] // Added 500
        public async Task<IActionResult> UpdateAppointment(string fhirAppointmentId, [FromBody] AppointmentModel appointment)
        {
            if (appointment == null || string.IsNullOrWhiteSpace(fhirAppointmentId) || appointment.FhirAppointmentId != fhirAppointmentId)
            {
                _logger.LogWarning("UpdateAppointment: Invalid appointment data or mismatched FHIR IDs. Route ID: {RouteId}, Body ID: {BodyId}", fhirAppointmentId, appointment?.FhirAppointmentId);
                return BadRequest("Invalid appointment data or mismatched FHIR IDs.");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            try
            {
                var updatedAppointment = await _appointmentService.UpdateAppointmentAsync(appointment, userId);

                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_UPDATE_APPOINTMENT_SUCCESS",
                    new { FhirAppointmentId = updatedAppointment.FhirAppointmentId, PatientId = updatedAppointment.PatientId, ClientIp = clientIp },
                    "Appointment",
                    updatedAppointment.FhirAppointmentId);

                _logger.LogInformation("Appointment with FHIR ID {FhirAppointmentId} updated successfully.", updatedAppointment.FhirAppointmentId);
                return Ok(updatedAppointment);
            }
            catch (InvalidOperationException ex) // Catch specific business logic exceptions
            {
                _logger.LogWarning(ex, "UpdateAppointment: Business logic error for FHIR ID {FhirAppointmentId} by user {UserId}. Message: {Message}", fhirAppointmentId, userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_UPDATE_APPOINTMENT_FAILED_BUSINESS_LOGIC",
                    new { Error = ex.Message, FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                    "Appointment",
                    fhirAppointmentId);
                return BadRequest(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "UpdateAppointment: Appointment with FHIR ID {FhirAppointmentId} not found. Message: {Message}", fhirAppointmentId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_UPDATE_APPOINTMENT_NOT_FOUND",
                    new { FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp, Error = ex.Message },
                    "Appointment",
                    fhirAppointmentId);
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateAppointment: An unexpected error occurred for FHIR ID {FhirAppointmentId} by user {UserId}. Message: {Message}", fhirAppointmentId, userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_UPDATE_APPOINTMENT_FAILED_EXCEPTION",
                    new { Error = ex.Message, FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                    "Appointment",
                    fhirAppointmentId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while updating the appointment.");
            }
        }

        [HttpDelete("{fhirAppointmentId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)] // Added 500
        public async Task<IActionResult> DeleteAppointment(string fhirAppointmentId)
        {
            if (string.IsNullOrWhiteSpace(fhirAppointmentId))
            {
                _logger.LogWarning("DeleteAppointment: Received empty FHIR Appointment ID.");
                return BadRequest("FHIR Appointment ID is required.");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "anonymous";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            try
            {
                var success = await _appointmentService.DeleteAppointmentAsync(fhirAppointmentId, userId);
                if (success == false) // Should ideally be handled by service throwing specific exceptions
                {
                    _logger.LogWarning("DeleteAppointment: Service indicated failure but didn't throw specific exception for FHIR ID {FhirAppointmentId}.", fhirAppointmentId);
                    await _auditLogService.LogAuditAsync(
                        userId,
                        "API_DELETE_APPOINTMENT_FAILED_SERVICE_INDICATION",
                        new { FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                        "Appointment",
                        fhirAppointmentId);
                    return BadRequest("Appointment could not be deleted due to an unknown service issue.");
                }

                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_DELETE_APPOINTMENT_SUCCESS",
                    new { FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                    "Appointment",
                    fhirAppointmentId);

                _logger.LogInformation("Appointment with FHIR ID {FhirAppointmentId} deleted successfully.", fhirAppointmentId);
                return Ok(new { Success = success, Message = "Appointment deleted successfully." });
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "DeleteAppointment: Appointment with FHIR ID {FhirAppointmentId} not found. Message: {Message}", fhirAppointmentId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_DELETE_APPOINTMENT_NOT_FOUND",
                    new { FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp, Error = ex.Message },
                    "Appointment",
                    fhirAppointmentId);
                return NotFound(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex) // Catch specific business logic exceptions (e.g., if service throws for specific reasons)
            {
                _logger.LogWarning(ex, "DeleteAppointment: Business logic error for FHIR ID {FhirAppointmentId} by user {UserId}. Message: {Message}", fhirAppointmentId, userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_DELETE_APPOINTMENT_FAILED_BUSINESS_LOGIC",
                    new { Error = ex.Message, FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                    "Appointment",
                    fhirAppointmentId);
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteAppointment: An unexpected error occurred for FHIR ID {FhirAppointmentId} by user {UserId}. Message: {Message}", fhirAppointmentId, userId, ex.Message);
                await _auditLogService.LogAuditAsync(
                    userId,
                    "API_DELETE_APPOINTMENT_FAILED_EXCEPTION",
                    new { Error = ex.Message, FhirAppointmentId = fhirAppointmentId, ClientIp = clientIp },
                    "Appointment",
                    fhirAppointmentId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred while deleting the appointment.");
            }
        }
    }
}
