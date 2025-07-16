using Hl7.Fhir.Model;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TelehealthApi.Core.Interfaces;
using TelehealthApi.Core.Models;
using static Hl7.Fhir.Model.ContactPoint; // To easily access ContactPointSystem enum

namespace TelehealthApi.Core.Services
{
    public class PatientService : IPatientService
    {
        private readonly TelehealthDbContext _dbContext;
        private readonly IFhirService _fhirService;
        private readonly IAuditLogService _auditLogService;
        private readonly EncryptionService _encryptionService;

        public PatientService(
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

        public async Task<PatientModel> CreatePatientAsync(PatientModel patient, string userId, bool shouldLogAudit = true)
        {
            if (patient == null) throw new ArgumentNullException(nameof(patient));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            // Centralized email format validation
            if (IsValidEmailFormat(patient.Email) == false)
            {
                if (shouldLogAudit)
                {
                    await _auditLogService.LogAuditAsync(
                        userId: userId,
                        action: "CREATE_PATIENT_INVALID_EMAIL_FORMAT",
                        details: new { Email = patient.Email, Error = "Invalid email format" },
                        resourceType: "Patient",
                        resourceId: null
                    );
                }
                throw new InvalidOperationException("Invalid email format provided.");
            }

            // Duplicate check
            if (!string.IsNullOrEmpty(patient.Email))
            {
                var (localMatches, fhirMatches) = await CheckDuplicatesAsync(patient.Email);
                if (localMatches.Any() || fhirMatches.Any())
                {
                    if (shouldLogAudit)
                    {
                        await _auditLogService.LogAuditAsync(
                            userId: userId,
                            action: "CREATE_PATIENT_DUPLICATE_ATTEMPT",
                            details: new { Email = patient.Email, LocalCount = localMatches.Count, FhirCount = fhirMatches.Count },
                            resourceType: "Patient",
                            resourceId: null
                        );
                    }
                    throw new InvalidOperationException("Duplicate email found. Manual resolution required.");
                }
            }

            // Map PatientModel to FHIR Patient
            var fhirPatient = new Hl7.Fhir.Model.Patient
            {
                Name = new List<HumanName>
                {
                    new HumanName { Family = patient.LastName, Given = new[] { patient.FirstName } }
                },
                BirthDate = patient.BirthDate,
                Telecom = new List<ContactPoint>
                {
                    new ContactPoint { System = ContactPointSystem.Email, Value = patient.Email, Use = ContactPointUse.Work },
                    new ContactPoint { System = ContactPointSystem.Phone, Value = patient.PhoneNumber, Use = ContactPointUse.Mobile }
                }
            };

            // Map Gender if provided
            if (!string.IsNullOrEmpty(patient.Gender))
            {
                if (Enum.TryParse<AdministrativeGender>(patient.Gender, true, out var genderEnum))
                {
                    fhirPatient.Gender = genderEnum;
                }
                else
                {
                    if (shouldLogAudit)
                    {
                        await _auditLogService.LogAuditAsync(userId, "CREATE_PATIENT_WARNING_INVALID_GENDER",
                            new { Email = patient.Email, Gender = patient.Gender, Warning = "Invalid Gender value provided" },
                            "Patient", null);
                    }
                }
            }

            var createdFhirPatient = await _fhirService.CreatePatientAsync(fhirPatient);
            if (createdFhirPatient == null)
            {
                if (shouldLogAudit)
                {
                    await _auditLogService.LogAuditAsync(
                        userId: userId,
                        action: "CREATE_PATIENT_FAILED_FHIR",
                        details: new { Email = patient.Email, Status = "FhirCreateFailed" },
                        resourceType: "Patient",
                        resourceId: null
                    );
                }
                throw new InvalidOperationException("Failed to create patient on FHIR server.");
            }

            var encryptedName = _encryptionService.Encrypt($"{patient.FirstName} {patient.LastName}");

            var localPatient = new Core.Data.Patient
            {
                Id = Guid.NewGuid(),
                FhirPatientId = createdFhirPatient.Id,
                Email = patient.Email,
                EncryptedName = encryptedName,
                UserId = patient.UserId
            };
            _dbContext.Patients.Add(localPatient);
            await _dbContext.SaveChangesAsync();

            return patient with { FhirPatientId = createdFhirPatient.Id, EncryptedName = encryptedName, UserId = localPatient.UserId };
        }

        public async Task<PatientModel> GetPatientByIdAsync(string fhirPatientId, string userId, bool shouldLogAudit = true)
        {
            if (string.IsNullOrWhiteSpace(fhirPatientId)) throw new ArgumentException("FHIR Patient ID cannot be empty.", nameof(fhirPatientId));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            var fhirPatient = await _fhirService.GetPatientAsync(fhirPatientId);
            if (fhirPatient == null)
            {
                if (shouldLogAudit)
                {
                    await _auditLogService.LogAuditAsync(
                        userId: userId,
                        action: "GET_PATIENT_FAILED_NOT_FOUND",
                        details: new { FhirPatientId = fhirPatientId, Status = "NotFound" },
                        resourceType: "Patient",
                        resourceId: fhirPatientId
                    );
                }
                throw new KeyNotFoundException($"Patient with FHIR ID {fhirPatientId} not found.");
            }

            var localPatient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.FhirPatientId == fhirPatientId);

            var firstName = fhirPatient.Name?.FirstOrDefault()?.Given?.FirstOrDefault() ?? "Unknown";
            var lastName = fhirPatient.Name?.FirstOrDefault()?.Family ?? "Unknown";
            var email = fhirPatient.Telecom?.FirstOrDefault(t => t.System == ContactPointSystem.Email)?.Value ?? "";
            var phoneNumber = fhirPatient.Telecom?.FirstOrDefault(t => t.System == ContactPointSystem.Phone)?.Value ?? "";
            var gender = fhirPatient.Gender?.ToString();

            var patientModel = new PatientModel
            {
                FhirPatientId = fhirPatient.Id,
                FirstName = firstName,
                LastName = lastName,
                BirthDate = fhirPatient.BirthDate,
                Email = email,
                PhoneNumber = phoneNumber,
                Gender = gender,
                EncryptedName = localPatient?.EncryptedName,
                UserId = localPatient?.UserId
            };

            if (shouldLogAudit)
            {
                await _auditLogService.LogAuditAsync(
                    userId: userId,
                    action: "GET_PATIENT_SUCCESS",
                    details: new { FhirPatientId = fhirPatientId },
                    resourceType: "Patient",
                    resourceId: fhirPatientId
                );
            }
            return patientModel;
        }

        public async Task<PatientModel> UpdatePatientAsync(PatientModel patient, string userId, bool shouldLogAudit = true)
        {
            if (patient == null) throw new ArgumentNullException(nameof(patient));
            if (string.IsNullOrWhiteSpace(patient.FhirPatientId)) throw new ArgumentException("FHIR Patient ID cannot be empty.", nameof(patient.FhirPatientId));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            if (IsValidEmailFormat(patient.Email) == false)
            {
                if (shouldLogAudit)
                {
                    await _auditLogService.LogAuditAsync(
                        userId: userId,
                        action: "UPDATE_PATIENT_INVALID_EMAIL_FORMAT",
                        details: new { Email = patient.Email, Error = "Invalid email format" },
                        resourceType: "Patient",
                        resourceId: patient.FhirPatientId
                    );
                }
                throw new InvalidOperationException("Invalid email format provided.");
            }

            if (!string.IsNullOrEmpty(patient.Email))
            {
                var (localMatches, fhirMatches) = await CheckDuplicatesAsync(patient.Email);
                var currentLocalPatient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.FhirPatientId == patient.FhirPatientId);

                bool isEmailChanging = currentLocalPatient?.Email != patient.Email;
                bool duplicateFoundWithOtherPatient =
                    (localMatches.Any(p => p.Id != currentLocalPatient?.Id) ||
                     fhirMatches.Any(p => p.Id != patient.FhirPatientId));

                if (isEmailChanging && duplicateFoundWithOtherPatient)
                {
                    if (shouldLogAudit)
                    {
                        await _auditLogService.LogAuditAsync(
                            userId: userId,
                            action: "UPDATE_PATIENT_DUPLICATE_ATTEMPT",
                            details: new { Email = patient.Email, FhirPatientId = patient.FhirPatientId, Status = "DuplicateFound" },
                            resourceType: "Patient",
                            resourceId: patient.FhirPatientId
                        );
                    }
                    throw new InvalidOperationException("Duplicate email found for another patient. Manual resolution required.");
                }
            }

            var fhirPatient = await _fhirService.GetPatientAsync(patient.FhirPatientId);
            if (fhirPatient == null)
            {
                if (shouldLogAudit)
                {
                    await _auditLogService.LogAuditAsync(
                        userId: userId,
                        action: "UPDATE_PATIENT_FAILED_NOT_FOUND",
                        details: new { FhirPatientId = patient.FhirPatientId, Status = "NotFound" },
                        resourceType: "Patient",
                        resourceId: patient.FhirPatientId
                    );
                }
                throw new KeyNotFoundException($"Patient with FHIR ID {patient.FhirPatientId} not found.");
            }

            if (fhirPatient.Name == null) fhirPatient.Name = new List<HumanName>();
            var humanName = fhirPatient.Name.FirstOrDefault();
            if (humanName == null)
            {
                humanName = new HumanName();
                fhirPatient.Name.Add(humanName);
            }
            humanName.Family = patient.LastName;
            humanName.Given = new[] { patient.FirstName };

            fhirPatient.BirthDate = patient.BirthDate;

            if (fhirPatient.Telecom == null) fhirPatient.Telecom = new List<ContactPoint>();

            var emailContactPoint = fhirPatient.Telecom.FirstOrDefault(t => t.System == ContactPointSystem.Email);
            if (emailContactPoint == null)
            {
                emailContactPoint = new ContactPoint { System = ContactPointSystem.Email, Use = ContactPointUse.Work };
                fhirPatient.Telecom.Add(emailContactPoint);
            }
            emailContactPoint.Value = patient.Email;

            var phoneContactPoint = fhirPatient.Telecom.FirstOrDefault(t => t.System == ContactPointSystem.Phone);
            if (phoneContactPoint == null)
            {
                phoneContactPoint = new ContactPoint { System = ContactPointSystem.Phone, Use = ContactPointUse.Mobile };
                fhirPatient.Telecom.Add(phoneContactPoint);
            }
            phoneContactPoint.Value = patient.PhoneNumber;

            if (!string.IsNullOrEmpty(patient.Gender))
            {
                if (Enum.TryParse<AdministrativeGender>(patient.Gender, true, out var genderEnum))
                {
                    fhirPatient.Gender = genderEnum;
                }
                else
                {
                    if (shouldLogAudit)
                    {
                        await _auditLogService.LogAuditAsync(userId, "UPDATE_PATIENT_WARNING_INVALID_GENDER",
                            new { Email = patient.Email, Gender = patient.Gender, Warning = "Invalid Gender value provided" },
                            "Patient", patient.FhirPatientId);
                    }
                }
            }
            else
            {
                fhirPatient.Gender = null;
            }

            var updatedFhirPatient = await _fhirService.UpdatePatientAsync(patient.FhirPatientId, fhirPatient);

            var localPatient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.FhirPatientId == patient.FhirPatientId);
            if (localPatient != null)
            {
                localPatient.Email = patient.Email;
                localPatient.EncryptedName = _encryptionService.Encrypt($"{patient.FirstName} {patient.LastName}");
                localPatient.UserId = patient.UserId;
                await _dbContext.SaveChangesAsync();
            }

            if (shouldLogAudit)
            {
                await _auditLogService.LogAuditAsync(
                    userId: userId,
                    action: "UPDATE_PATIENT_SUCCESS",
                    details: new { FhirPatientId = patient.FhirPatientId, Email = patient.Email },
                    resourceType: "Patient",
                    resourceId: patient.FhirPatientId
                );
            }
            return patient with { FhirPatientId = updatedFhirPatient.Id, EncryptedName = localPatient?.EncryptedName, UserId = localPatient?.UserId };
        }

        public async Task<bool> DeletePatientAsync(string fhirPatientId, string userId, bool shouldLogAudit = true)
        {
            if (string.IsNullOrWhiteSpace(fhirPatientId)) throw new ArgumentException("FHIR Patient ID cannot be empty.", nameof(fhirPatientId));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

            var (success, reason) = await _fhirService.DeletePatientAsync(fhirPatientId);
            if (!success)
            {
                if (shouldLogAudit)
                {
                    await _auditLogService.LogAuditAsync(
                        userId: userId,
                        action: "DELETE_PATIENT_FAILED_FHIR",
                        details: new { FhirPatientId = fhirPatientId, Status = "FhirDeleteFailed", Reason = reason },
                        resourceType: "Patient",
                        resourceId: fhirPatientId
                    );
                }
                throw new InvalidOperationException($"Failed to delete patient from FHIR server: {reason}");
            }

            var localPatient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.FhirPatientId == fhirPatientId);
            if (localPatient != null)
            {
                _dbContext.Patients.Remove(localPatient);
                await _dbContext.SaveChangesAsync();
            }

            if (shouldLogAudit)
            {
                await _auditLogService.LogAuditAsync(
                    userId: userId,
                    action: "DELETE_PATIENT_SUCCESS",
                    details: new { FhirPatientId = fhirPatientId },
                    resourceType: "Patient",
                    resourceId: fhirPatientId
                );
            }
            return true;
        }

        /// <summary>
        /// Checks for duplicate patients by email in both the local DB and the FHIR server.
        /// </summary>
        public async Task<(List<Core.Data.Patient> LocalMatches, List<Hl7.Fhir.Model.Patient> FhirMatches)> CheckDuplicatesAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email cannot be empty.", nameof(email));

            var localMatches = await _dbContext.Patients
                .Where(p => p.Email == email)
                .ToListAsync(cancellationToken);

            var fhirBundle = await _fhirService.SearchPatientsByEmailAsync(email);

            var fhirMatches = fhirBundle?.Entry?
                .Select(e => e.Resource as Hl7.Fhir.Model.Patient)
                .Where(p => p is not null)
                .Cast<Hl7.Fhir.Model.Patient>()
                .ToList() ?? new List<Hl7.Fhir.Model.Patient>();

            return (localMatches, fhirMatches);
        }

        private bool IsValidEmailFormat(string email)
        {
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }
    }
}