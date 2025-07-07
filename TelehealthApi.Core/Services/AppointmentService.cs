using Hl7.Fhir.Model;
using Microsoft.EntityFrameworkCore;
using TelehealthApi.Core.Interfaces;
using TelehealthApi.Core.Models;
using static Hl7.Fhir.Model.Appointment;

namespace TelehealthApi.Core.Services
{
    public class AppointmentService : IAppointmentService
    {
        private readonly TelehealthDbContext _dbContext;
        private readonly IFhirService _fhirService;
        private readonly IAuditLogService _auditLogService;
        private readonly EncryptionService _encryptionService;

        public AppointmentService(
            TelehealthDbContext dbContext,
            IFhirService fhirService,
            IAuditLogService auditLogService,
            EncryptionService encryptionService)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _fhirService = fhirService ?? throw new ArgumentNullException(nameof(fhirService));
            _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        }

        public async Task<AppointmentModel> CreateAppointmentAsync(AppointmentModel appointment, string userId)
        {
            // Validate input parameters
            if (appointment == null) throw new ArgumentNullException(nameof(appointment));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            // Ensure end time is after start time
            if (appointment.EndTime <= appointment.StartTime)
            {
                await _auditLogService.LogAuditAsync(
                    userId,
                    "CREATE_APPOINTMENT_INVALID_TIME_RANGE",
                    new { PatientId = appointment.PatientId, StartTime = appointment.StartTime, EndTime = appointment.EndTime, Error = "End time must be after start time" },
                    "Appointment",
                    null);
                throw new InvalidOperationException("End time must be after start time.");
            }

            // Check for scheduling conflicts in local DB and FHIR server
            var (localConflicts, fhirConflicts) = await CheckConflictsAsync(appointment.PatientId, appointment.StartTime, appointment.EndTime);
            if (localConflicts.Any() || fhirConflicts.Any())
            {
                await _auditLogService.LogAuditAsync(
                    userId,
                    "CREATE_APPOINTMENT_CONFLICT",
                    new { PatientId = appointment.PatientId, StartTime = appointment.StartTime, EndTime = appointment.EndTime, LocalCount = localConflicts.Count, FhirCount = fhirConflicts.Count },
                    "Appointment",
                    null);
                throw new InvalidOperationException("Scheduling conflict detected. Please choose a different time.");
            }

            // Retrieve local patient and ensure FHIR Patient ID exists
            var localPatient = await _dbContext.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == appointment.PatientId);

            if (localPatient == null || string.IsNullOrWhiteSpace(localPatient.FhirPatientId))
            {
                await _auditLogService.LogAuditAsync(
                    userId,
                    "CREATE_APPOINTMENT_FAILED_PATIENT_NOT_FOUND",
                    new { PatientId = appointment.PatientId, Error = "Local patient not found or missing FHIR ID" },
                    "Appointment",
                    null);
                throw new KeyNotFoundException($"Patient with local ID {appointment.PatientId} not found or has no associated FHIR ID.");
            }

            // Create FHIR appointment resource
            var fhirAppointment = new Hl7.Fhir.Model.Appointment
            {
                Status = Enum.Parse<Hl7.Fhir.Model.Appointment.AppointmentStatus>(appointment.Status.ToString(), true),
                Start = appointment.StartTime,
                End = appointment.EndTime,
                Description = appointment.Description,
                Participant = new List<ParticipantComponent>
                {
                    new ParticipantComponent
                    {
                        Actor = new ResourceReference($"Patient/{localPatient.FhirPatientId}"),
                        Status = Hl7.Fhir.Model.ParticipationStatus.Accepted
                    }
                }
            };

            // Save appointment to FHIR server
            var createdFhirAppointment = await _fhirService.CreateAppointmentAsync(fhirAppointment);

            // Create and save local appointment record
            var localAppointment = new TelehealthApi.Core.Data.Appointment
            {
                Id = Guid.NewGuid(),
                FhirAppointmentId = createdFhirAppointment.Id,
                PatientId = appointment.PatientId,
                StartTime = appointment.StartTime,
                EndTime = appointment.EndTime,
                Status = appointment.Status.ToString(),
                Description = string.IsNullOrEmpty(appointment.Description) ? null : _encryptionService.Encrypt(appointment.Description)
            };
            _dbContext.Appointments.Add(localAppointment);
            await _dbContext.SaveChangesAsync();

            // Log successful creation
            await _auditLogService.LogAuditAsync(
                userId,
                "CREATE_APPOINTMENT_SUCCESS",
                new { FhirAppointmentId = createdFhirAppointment.Id, PatientId = appointment.PatientId },
                "Appointment",
                createdFhirAppointment.Id);

            // Return the created appointment model with assigned IDs
            return appointment with { Id = localAppointment.Id, FhirAppointmentId = createdFhirAppointment.Id };
        }

        public async Task<AppointmentModel> GetAppointmentByIdAsync(string fhirAppointmentId, string userId)
        {
            // Validate input parameters
            if (string.IsNullOrWhiteSpace(fhirAppointmentId)) throw new ArgumentException("FHIR Appointment ID cannot be empty.", nameof(fhirAppointmentId));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            // Retrieve appointment from FHIR server
            var fhirAppointment = await _fhirService.GetAppointmentAsync(fhirAppointmentId);
            if (fhirAppointment == null)
            {
                // Log and throw if not found
                await _auditLogService.LogAuditAsync(
                    userId,
                    "GET_APPOINTMENT_FAILED_NOT_FOUND",
                    new { FhirAppointmentId = fhirAppointmentId, Status = "NotFound" },
                    "Appointment",
                    fhirAppointmentId);
                throw new KeyNotFoundException($"Appointment with FHIR ID {fhirAppointmentId} not found.");
            }

            // Retrieve local appointment record
            var localAppointment = await _dbContext.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.FhirAppointmentId == fhirAppointmentId);

            // Get patient ID and decrypt description if available
            var patientId = localAppointment?.PatientId ?? Guid.Empty;
            var description = localAppointment?.Description != null ? _encryptionService.Decrypt(localAppointment.Description) : null;

            // Map FHIR status to local enum
            var status = MapFhirStatusToLocal(fhirAppointment.Status);

            return new AppointmentModel
            {
                Id = localAppointment?.Id,
                FhirAppointmentId = fhirAppointment.Id,
                PatientId = patientId,
                StartTime = fhirAppointment.Start.GetValueOrDefault().DateTime,
                EndTime = fhirAppointment.End.GetValueOrDefault().DateTime,
                Status = status,
                Description = description
            };
        }
        public async Task<AppointmentModel> UpdateAppointmentAsync(AppointmentModel appointment, string userId)
        {
            if (appointment == null) throw new ArgumentNullException(nameof(appointment));
            if (string.IsNullOrWhiteSpace(appointment.FhirAppointmentId)) throw new ArgumentException("FHIR Appointment ID cannot be empty.", nameof(appointment.FhirAppointmentId));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            if (appointment.EndTime <= appointment.StartTime)
            {
                await _auditLogService.LogAuditAsync(
                    userId,
                    "UPDATE_APPOINTMENT_INVALID_TIME_RANGE",
                    new { FhirAppointmentId = appointment.FhirAppointmentId, StartTime = appointment.StartTime, EndTime = appointment.EndTime, Error = "End time must be after start time" },
                    "Appointment",
                    appointment.FhirAppointmentId);
                throw new InvalidOperationException("End time must be after start time.");
            }

            var fhirAppointment = await _fhirService.GetAppointmentAsync(appointment.FhirAppointmentId);
            if (fhirAppointment == null)
            {
                await _auditLogService.LogAuditAsync(
                    userId,
                    "UPDATE_APPOINTMENT_FAILED_NOT_FOUND",
                    new { FhirAppointmentId = appointment.FhirAppointmentId, Status = "NotFound" },
                    "Appointment",
                    appointment.FhirAppointmentId);
                throw new KeyNotFoundException($"Appointment with FHIR ID {appointment.FhirAppointmentId} not found.");
            }

            var currentLocalAppointment = await _dbContext.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.FhirAppointmentId == appointment.FhirAppointmentId);

            var (localConflicts, fhirConflicts) = await CheckConflictsAsync(
                appointment.PatientId,
                appointment.StartTime,
                appointment.EndTime,
                currentLocalAppointment?.Id,
                appointment.FhirAppointmentId);
            if ((localConflicts.Any(c => c.Id != (currentLocalAppointment?.Id)) || fhirConflicts.Any(c => c.Id != appointment.FhirAppointmentId)))
            {
                await _auditLogService.LogAuditAsync(
                    userId,
                    "UPDATE_APPOINTMENT_CONFLICT",
                    new { FhirAppointmentId = appointment.FhirAppointmentId, StartTime = appointment.StartTime, EndTime = appointment.EndTime, LocalCount = localConflicts.Count, FhirCount = fhirConflicts.Count },
                    "Appointment",
                    appointment.FhirAppointmentId);
                throw new InvalidOperationException("Scheduling conflict detected with another appointment.");
            }

            if (fhirAppointment.Participant == null) fhirAppointment.Participant = new List<ParticipantComponent>();
            var patientParticipant = fhirAppointment.Participant.FirstOrDefault(p => p.Actor?.Reference?.StartsWith("Patient/") == true);

            if (patientParticipant != null)
            {
                var updatedLocalPatient = await _dbContext.Patients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == appointment.PatientId);

                if (updatedLocalPatient == null || string.IsNullOrWhiteSpace(updatedLocalPatient.FhirPatientId))
                {
                    await _auditLogService.LogAuditAsync(
                        userId,
                        "UPDATE_APPOINTMENT_FAILED_PATIENT_NOT_FOUND",
                        new { PatientId = appointment.PatientId, Error = "Local patient not found or missing FHIR ID for update" },
                        "Appointment",
                        appointment.FhirAppointmentId);
                    throw new KeyNotFoundException($"Patient with local ID {appointment.PatientId} not found or has no associated FHIR ID for updating appointment.");
                }
                patientParticipant.Actor = new ResourceReference($"Patient/{updatedLocalPatient.FhirPatientId}");
            }
            else
            {
                var currentLocalPatient = await _dbContext.Patients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == appointment.PatientId);
                if (currentLocalPatient != null && !string.IsNullOrWhiteSpace(currentLocalPatient.FhirPatientId))
                {
                    fhirAppointment.Participant.Add(new ParticipantComponent
                    {
                        Actor = new ResourceReference($"Patient/{currentLocalPatient.FhirPatientId}"),
                        Status = Hl7.Fhir.Model.ParticipationStatus.Accepted
                    });
                }
            }

            fhirAppointment.Status = Enum.Parse<Hl7.Fhir.Model.Appointment.AppointmentStatus>(appointment.Status.ToString(), true);
            fhirAppointment.Start = appointment.StartTime;
            fhirAppointment.End = appointment.EndTime;
            fhirAppointment.Description = appointment.Description;

            var updatedFhirAppointment = await _fhirService.UpdateAppointmentAsync(appointment.FhirAppointmentId, fhirAppointment);

            var localAppointment = await _dbContext.Appointments
                .FirstOrDefaultAsync(a => a.FhirAppointmentId == appointment.FhirAppointmentId);
            if (localAppointment != null)
            {
                localAppointment.StartTime = appointment.StartTime;
                localAppointment.EndTime = appointment.EndTime;
                localAppointment.Status = appointment.Status.ToString();
                localAppointment.Description = string.IsNullOrEmpty(appointment.Description) ? null : _encryptionService.Encrypt(appointment.Description);
                localAppointment.PatientId = appointment.PatientId;
                await _dbContext.SaveChangesAsync();
            }

            await _auditLogService.LogAuditAsync(
                userId,
                "UPDATE_APPOINTMENT_SUCCESS",
                new { FhirAppointmentId = appointment.FhirAppointmentId, PatientId = appointment.PatientId },
                "Appointment",
                appointment.FhirAppointmentId);

            return appointment with { Id = localAppointment?.Id, FhirAppointmentId = updatedFhirAppointment.Id };
        }

        public async Task<bool> DeleteAppointmentAsync(string fhirAppointmentId, string userId)
        {
            if (string.IsNullOrWhiteSpace(fhirAppointmentId)) throw new ArgumentException("FHIR Appointment ID cannot be empty.", nameof(fhirAppointmentId));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            var (success, reason) = await _fhirService.DeleteAppointmentAsync(fhirAppointmentId);
            if (!success)
            {
                await _auditLogService.LogAuditAsync(
                    userId,
                    "DELETE_APPOINTMENT_FAILED_FHIR",
                    new { FhirAppointmentId = fhirAppointmentId, Status = "FhirDeleteFailed", Reason = reason },
                    "Appointment",
                    fhirAppointmentId);
                throw new InvalidOperationException($"Failed to delete appointment from FHIR server: {reason}");
            }

            var localAppointment = await _dbContext.Appointments
                .FirstOrDefaultAsync(a => a.FhirAppointmentId == fhirAppointmentId);
            if (localAppointment != null)
            {
                _dbContext.Appointments.Remove(localAppointment);
                await _dbContext.SaveChangesAsync();
            }

            await _auditLogService.LogAuditAsync(
                userId,
                "DELETE_APPOINTMENT_SUCCESS",
                new { FhirAppointmentId = fhirAppointmentId },
                "Appointment",
                fhirAppointmentId);

            return true;
        }

        // Checks and returns overlapping local and FHIR appointments for a patient within a specified time range, excluding optionally specified appointments.
        public async Task<(List<TelehealthApi.Core.Data.Appointment> LocalConflicts, List<Hl7.Fhir.Model.Appointment> FhirConflicts)> CheckConflictsAsync(
            Guid patientId,
            DateTime startTime,
            DateTime endTime,
            Guid? excludeLocalAppointmentId = null,
            string? excludeFhirAppointmentId = null,
            CancellationToken cancellationToken = default)
        {
            // Validate that the start time is before the end time
            if (startTime >= endTime) throw new ArgumentException("Start time must be before end time.", nameof(startTime));

            // Retrieve the local patient and ensure FHIR Patient ID exists
            var localPatient = await _dbContext.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken);

            // If patient not found or missing FHIR ID, return empty conflict lists
            if (localPatient == null || string.IsNullOrWhiteSpace(localPatient.FhirPatientId))
            {
                return (new List<Core.Data.Appointment>(), new List<Hl7.Fhir.Model.Appointment>());
            }

            // Find local appointment conflicts, excluding the specified appointment if provided
            var localConflicts = await _dbContext.Appointments
                .Where(a => a.PatientId == patientId &&
                            (a.StartTime < endTime && a.EndTime > startTime) &&
                            (!excludeLocalAppointmentId.HasValue || a.Id != excludeLocalAppointmentId.Value))
                .ToListAsync(cancellationToken);

            // Find FHIR appointment conflicts, excluding the specified FHIR appointment if provided
            var fhirBundle = await _fhirService.SearchAppointmentsByPatientAsync(localPatient.FhirPatientId);
            var fhirConflicts = fhirBundle?.Entry?
                .Select(e => e.Resource as Hl7.Fhir.Model.Appointment)
                .Where(a => a != null &&
                            a.Start.HasValue && a.End.HasValue &&
                            (a.Start.Value < endTime && a.End.Value > startTime) &&
                            (string.IsNullOrWhiteSpace(excludeFhirAppointmentId) || a.Id != excludeFhirAppointmentId))
                .Cast<Hl7.Fhir.Model.Appointment>()
                .ToList() ?? new List<Hl7.Fhir.Model.Appointment>();

            // Return both local and FHIR conflicts
            return (localConflicts, fhirConflicts);
        }

        //Helper method to map FHIR AppointmentStatus to local AppointmentStatus enum
        private TelehealthApi.Core.Enums.AppointmentStatus MapFhirStatusToLocal(Hl7.Fhir.Model.Appointment.AppointmentStatus? fhirStatus)
        {
            return fhirStatus switch
            {
                null => TelehealthApi.Core.Enums.AppointmentStatus.Unknown,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Booked => TelehealthApi.Core.Enums.AppointmentStatus.Booked,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Pending => TelehealthApi.Core.Enums.AppointmentStatus.Pending,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Proposed => TelehealthApi.Core.Enums.AppointmentStatus.Proposed,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Arrived => TelehealthApi.Core.Enums.AppointmentStatus.Arrived,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Fulfilled => TelehealthApi.Core.Enums.AppointmentStatus.Fulfilled,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Cancelled => TelehealthApi.Core.Enums.AppointmentStatus.Cancelled,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Noshow => TelehealthApi.Core.Enums.AppointmentStatus.Noshow,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.EnteredInError => TelehealthApi.Core.Enums.AppointmentStatus.EnteredInError,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.CheckedIn => TelehealthApi.Core.Enums.AppointmentStatus.CheckedIn,
                Hl7.Fhir.Model.Appointment.AppointmentStatus.Waitlist => TelehealthApi.Core.Enums.AppointmentStatus.Waitlist,
                _ => TelehealthApi.Core.Enums.AppointmentStatus.Unknown,
            };
        }

    }
}