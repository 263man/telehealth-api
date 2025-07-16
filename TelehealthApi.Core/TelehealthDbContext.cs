using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TelehealthApi.Core.Data;
using TelehealthApi.Core.Interfaces;

namespace TelehealthApi.Core
{
    public class TelehealthDbContext : IdentityDbContext
    {
        public TelehealthDbContext(DbContextOptions<TelehealthDbContext> options) : base(options) { }

        public DbSet<Patient> Patients { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Patient>()
                .HasIndex(p => p.FhirPatientId)
                .IsUnique();
            builder.Entity<Patient>()
                .HasIndex(p => new { p.Email, p.FhirPatientId });

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
                entity.HasIndex(a => a.Timestamp);
            });
        }

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