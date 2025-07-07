using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TelehealthApi.Core.Data;
using TelehealthApi.Core.Interfaces;

namespace TelehealthApi.Core
{
    // Inherit from IdentityDbContext to include ASP.NET Core Identity tables
    public class TelehealthDbContext : IdentityDbContext
    {
        public TelehealthDbContext(DbContextOptions<TelehealthDbContext> options) : base(options) { }

        // DbSet properties for our custom entities
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Appointment> Appointments { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        public class AuditLog
        {
            public Guid Id { get; set; }
            public string? UserId { get; set; }
            public string? Action { get; set; }
            public DateTime Timestamp { get; set; }
            public string? Details { get; set; }
            public string? ResourceType { get; set; }
            public string? ResourceId { get; set; }
        }

        // Result class for deletion operation feedback
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder); // Must call base implementation for IdentityDbContext
            builder.Entity<Patient>()
        .HasIndex(p => p.FhirPatientId) // Keep this unique
        .IsUnique();
            builder.Entity<Patient>()
                .HasIndex(p => new { p.Email, p.FhirPatientId }); // Composite index for email lookups

            // Configure relationship between Patient and Appointment
            builder.Entity<Appointment>()
                .HasOne(a => a.Patient)          // An Appointment has one Patient
                .WithMany(p => p.Appointments)   // A Patient can have many Appointments
                .HasForeignKey(a => a.PatientId) // Foreign key is PatientId in Appointment
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete if a patient is deleted

            builder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs", "telehealthkeps");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasDefaultValueSql("NEWID()").ValueGeneratedOnAdd();
                entity.Property(e => e.UserId).HasMaxLength(450).IsRequired(false);
                entity.Property(e => e.Action).IsRequired(false);
                entity.Property(e => e.Details).IsRequired(false);
                entity.Property(e => e.ResourceType).IsRequired(false);
                entity.Property(e => e.ResourceId).IsRequired(false);
                entity.Property(e => e.Timestamp).IsRequired();
                entity.HasIndex(a => a.Timestamp); // Index for timestamp-based queries
            });
        }

        /// <summary>
        /// Deletes all Patient records where UserId is NULL and logs each deletion for audit.
        /// </summary>
        /// <param name="userId">The ID of the user performing the deletion (e.g., "SystemCleanup")</param>
        /// <returns>A DeletionResult containing the outcome and count of deleted records</returns>
        public async Task<DeletionResult> DeletePatientsWithNullUserIdAsync(IAuditLogService auditLogService, string userId)
        {
            var recordsToDelete = Patients.Where(p => p.UserId == null).ToList();
            if (!recordsToDelete.Any())
            {
                return new DeletionResult { Success = true, Count = 0, Message = "No records with NULL UserId found." };
            }

            foreach (var patient in recordsToDelete)
            {
                await auditLogService.LogAuditAsync(userId, "DELETE_NULL_USERID",
                    new { PatientFhirId = patient.FhirPatientId, Status = "Deleted" },
                    "Patient", patient.FhirPatientId);
            }

            Patients.RemoveRange(recordsToDelete);
            var deletedCount = await SaveChangesAsync();
            return new DeletionResult { Success = true, Count = deletedCount, Message = $"Deleted {deletedCount} records." };
        }
        public class DeletionResult
        {
            public bool Success { get; set; }
            public int Count { get; set; }
            public string? Message { get; set; }
        }

    }
}
