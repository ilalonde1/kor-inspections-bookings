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
        public DbSet<ProjectAccess> ProjectAccessEntries => Set<ProjectAccess>();

        // NEW: Domain-scoped project profile data
        public DbSet<ProjectDefault> ProjectDefaults => Set<ProjectDefault>();
        public DbSet<ProjectContact> ProjectContacts => Set<ProjectContact>();

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

                entity.HasIndex(b => b.ContactEmail);

                entity.Property(b => b.Status)
                      .HasMaxLength(30)
                      .HasDefaultValue("Unassigned");

                entity.Property(b => b.CreatedUtc)
                      .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.Property(b => b.CancelToken)
                      .HasDefaultValueSql("NEWID()");
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

                entity.Property(i => i.DailyMax)
                      .HasDefaultValue(8);

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
            });

            // ----------------------------
            // ProjectAccess (legacy / PIN)
            // ----------------------------
            modelBuilder.Entity<ProjectAccess>(entity =>
            {
                entity.ToTable("ProjectAccess");
                entity.HasKey(pa => pa.Id);

                entity.HasIndex(pa => pa.ProjectNumber)
                      .IsUnique();

                entity.Property(pa => pa.ProjectNumber)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(pa => pa.PinHash)
                      .IsRequired();

                entity.Property(pa => pa.IsEnabled)
                      .HasDefaultValue(true);

                entity.Property(pa => pa.CreatedUtc)
                      .HasDefaultValueSql("SYSUTCDATETIME()");
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

        }
    }
}
