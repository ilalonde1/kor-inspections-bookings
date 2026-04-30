using Kor.Inspections.App.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Kor.Inspections.App.Data
{
    public class InspectionsContext : DbContext
    {
        public InspectionsContext(DbContextOptions<InspectionsContext> options)
            : base(options)
        {
        }

        // Existing tables
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<BookingAction> BookingActions => Set<BookingAction>();
        public DbSet<Inspector> Inspectors => Set<Inspector>();
        // NEW: Domain-scoped project profile data
        public DbSet<ProjectDefault> ProjectDefaults => Set<ProjectDefault>();
        public DbSet<ProjectContact> ProjectContacts => Set<ProjectContact>();

        // Persistent OTP verification state (replaces in-memory cache).
        public DbSet<ProjectVerification> ProjectVerifications => Set<ProjectVerification>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ----------------------------
            // Booking
            // ----------------------------
            modelBuilder.Entity<Booking>(entity =>
            {
                entity.ToTable("Bookings");
                entity.HasKey(b => b.BookingId);

                entity.Property(b => b.ProjectNumber)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(b => b.ProjectAddress)
                      .HasMaxLength(255);

                entity.Property(b => b.ContactName)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(b => b.ContactPhone)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(b => b.ContactEmail)
                      .HasMaxLength(120)
                      .IsRequired();

                entity.Property(b => b.RouteOrder);

                entity.Property(b => b.ProjectNumberDisplay)
                      .HasMaxLength(50);

                entity.Property(b => b.ProjectName)
                      .HasMaxLength(200);

                entity.HasIndex(b => b.ContactEmail);
                entity.HasIndex(b => b.ProjectNumber);
                entity.HasIndex(b => b.Status);
                entity.HasIndex(b => b.StartUtc);
                entity.HasIndex(b => new { b.ProjectNumber, b.StartUtc });
                entity.HasIndex(b => new { b.ProjectNumber, b.ContactEmail, b.StartUtc })
                      .IsUnique()
                      .HasFilter("[Status] != 'Cancelled'")
                      .HasDatabaseName("IX_Bookings_NoDuplicateActiveSlot");

                entity.Property(b => b.Status)
                      .HasMaxLength(30)
                      .HasDefaultValue(BookingStatus.Unassigned);

                entity.Property(b => b.CreatedUtc)
                      .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.Property(b => b.RowVersion)
                      .IsRowVersion()
                      .IsConcurrencyToken();

                entity.Property(b => b.CancelToken)
                      .HasDefaultValueSql("NEWID()");

                entity.HasIndex(b => b.CancelToken)
                      .IsUnique()
                      .HasDatabaseName("IX_Bookings_CancelToken_Unique");
            });

            // ----------------------------
            // Inspector
            // ----------------------------
            modelBuilder.Entity<Inspector>(entity =>
            {
                entity.ToTable("Inspectors");
                entity.HasKey(i => i.InspectorId);

                entity.Property(i => i.DisplayName)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(i => i.Email)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.HasIndex(i => i.Email)
                      .IsUnique()
                      .HasDatabaseName("IX_Inspectors_UniqueEmail");

                entity.Property(i => i.Enabled)
                      .HasDefaultValue(true);
            });

            // ----------------------------
            // BookingAction
            // ----------------------------
            modelBuilder.Entity<BookingAction>(entity =>
            {
                entity.ToTable("BookingActions");
                entity.HasKey(a => a.ActionId);

                entity.HasOne<Booking>()
                      .WithMany()
                      .HasForeignKey(a => a.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ==================================================
            // NEW: ProjectDefaults (per Project + EmailDomain)
            // ==================================================
            modelBuilder.Entity<ProjectDefault>(entity =>
            {
                entity.ToTable("ProjectDefaults");
                entity.HasKey(pd => pd.Id);

                entity.HasIndex(pd => new { pd.ProjectNumber, pd.EmailDomain })
                      .IsUnique();

                entity.Property(pd => pd.ProjectNumber)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(pd => pd.EmailDomain)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.Property(pd => pd.DefaultAddress)
                      .HasMaxLength(255);

                entity.Property(pd => pd.UpdatedUtc)
                      .HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // ==================================================
            // NEW: ProjectContacts (many per Project + Domain)
            // ==================================================
            modelBuilder.Entity<ProjectContact>(entity =>
            {
                entity.ToTable("ProjectContacts");
                entity.HasKey(pc => pc.ContactId);

                // UNIQUE CONTACT PER PROJECT + DOMAIN
                entity.HasIndex(pc => new
                {
                    pc.ProjectNumber,
                    pc.EmailDomain,
                    pc.ContactEmail
                })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

                entity.Property(pc => pc.ProjectNumber)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(pc => pc.EmailDomain)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.Property(pc => pc.ContactName)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(pc => pc.ContactPhone)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(pc => pc.ContactEmail)
                      .HasMaxLength(120)
                      .IsRequired();

                entity.Property(pc => pc.IsDeleted)
                      .HasDefaultValue(false);

                entity.Property(pc => pc.UpdatedUtc)
                      .HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // ==================================================
            // ProjectVerifications (OTP state — one row per project+email)
            // ==================================================
            modelBuilder.Entity<ProjectVerification>(entity =>
            {
                entity.ToTable("ProjectVerifications");
                entity.HasKey(pv => pv.Id);

                entity.HasIndex(pv => new { pv.ProjectNumber, pv.Email })
                      .IsUnique()
                      .HasDatabaseName("IX_ProjectVerifications_ProjectEmail");

                // Supports cheap filtering of expired rows for lookups and cleanup.
                entity.HasIndex(pv => pv.ExpiresUtc)
                      .HasDatabaseName("IX_ProjectVerifications_ExpiresUtc");

                entity.Property(pv => pv.ProjectNumber)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(pv => pv.Email)
                      .HasMaxLength(200)
                      .IsRequired();

                entity.Property(pv => pv.Code)
                      .HasMaxLength(10)
                      .IsRequired();

                entity.Property(pv => pv.Verified)
                      .HasDefaultValue(false);

                entity.Property(pv => pv.FailedAttempts)
                      .HasDefaultValue(0);

                entity.Property(pv => pv.CreatedUtc)
                      .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.Property(pv => pv.UpdatedUtc)
                      .HasDefaultValueSql("SYSUTCDATETIME()");
            });

        }
    }
}
