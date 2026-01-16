using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Models;
using WebsiteBuilder.Models.Base;

namespace WebsiteBuilder.IRF.DataAccess
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options) { }

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<DomainMapping> DomainMappings => Set<DomainMapping>();
        public DbSet<Theme> Themes => Set<Theme>();
        public DbSet<Page> Pages => Set<Page>();
        public DbSet<PageSection> PageSections => Set<PageSection>();
        public DbSet<PageStatus> PageStatuses => Set<PageStatus>();
        public DbSet<PageRevision> PageRevisions => Set<PageRevision>();
        public DbSet<PageRevisionSection> PageRevisionSections => Set<PageRevisionSection>();
        public DbSet<SectionType> SectionTypes => Set<SectionType>();
        public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
        public DbSet<MediaCleanupRunLog> MediaCleanupRunLogs => Set<MediaCleanupRunLog>();
        public DbSet<MediaAlert> MediaAlerts => Set<MediaAlert>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureBaseModelConventions(modelBuilder);
            ConfigureTenant(modelBuilder);
            ConfigureDomainMappings(modelBuilder);
            ConfigureThemes(modelBuilder);
            ConfigurePages(modelBuilder);
            ConfigurePageSections(modelBuilder);
            ConfigurePageStatuses(modelBuilder);

            ConfigurePageRevisions(modelBuilder);
            ConfigurePageRevisionSections(modelBuilder);

            ConfigureMediaAssets(modelBuilder);
            ConfigureMediaCleanupRunLogs(modelBuilder);
            ConfigureMediaAlerts(modelBuilder);

            ConfigureSectionTypes(modelBuilder);

        }

        private static void ConfigureBaseModelConventions(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (!typeof(BaseModel).IsAssignableFrom(entityType.ClrType))
                    continue;

                var b = modelBuilder.Entity(entityType.ClrType);

                // Defaults (only applies if property exists on the entity)
                b.Property(nameof(BaseModel.IsActive)).HasDefaultValue(true);
                b.Property(nameof(BaseModel.IsDeleted)).HasDefaultValue(false);
                b.Property(nameof(BaseModel.CreatedAt)).HasDefaultValueSql("SYSUTCDATETIME()");

                // RowVersion (timestamp/rowversion)
                b.Property(nameof(BaseModel.RowVersion)).IsRowVersion();
            }
        }

        private static void ConfigureTenant(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Tenant>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedNever();

                b.Property(x => x.DisplayName).HasMaxLength(300).IsRequired();
                b.Property(x => x.Slug).HasMaxLength(200).IsRequired();
                b.HasIndex(x => x.Slug).IsUnique();

                // Avoid multiple cascade paths
                b.HasOne(x => x.ActiveTheme)
                    .WithMany()
                    .HasForeignKey(x => x.ActiveThemeId)
                    .OnDelete(DeleteBehavior.NoAction);

                b.HasMany(x => x.Themes)
                    .WithOne(t => t.Tenant)
                    .HasForeignKey(t => t.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasMany(x => x.DomainMappings)
                    .WithOne(dm => dm.Tenant)
                    .HasForeignKey(dm => dm.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasMany(x => x.Pages)
                    .WithOne(p => p.Tenant)
                    .HasForeignKey(p => p.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.HomePage)
                    .WithMany()
                    .HasForeignKey(x => x.HomePageId)
                    .OnDelete(DeleteBehavior.NoAction);
            });
        }

        private static void ConfigureDomainMappings(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DomainMapping>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.Host).HasMaxLength(510).IsRequired();
                b.HasIndex(x => x.Host).IsUnique();

                // One primary domain per tenant
                b.HasIndex(x => new { x.TenantId, x.IsPrimary })
                    .IsUnique()
                    .HasFilter("[IsPrimary] = 1");

                // Keep column name stable (optional)
                b.Property(x => x.SslModeId).HasColumnName("SslModeId");
            });
        }

        private static void ConfigureThemes(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Theme>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.Mode).HasMaxLength(50);

                b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(false);
            });
        }

        private static void ConfigurePages(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Page>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.Title)
                    .HasMaxLength(200)
                    .IsRequired();

                b.Property(x => x.Slug)
                    .HasMaxLength(200)
                    .IsRequired();

                // Slug unique per tenant
                b.HasIndex(x => new { x.TenantId, x.Slug })
                    .IsUnique();

                // Default: Draft
                b.Property(x => x.PageStatusId)
                    .HasDefaultValue(1);

                // ✅ Draft revision pointer (nullable)
                b.Property(x => x.DraftRevisionId)
                    .IsRequired(false);

                b.HasOne(x => x.DraftRevision)
                    .WithMany()
                    .HasForeignKey(x => x.DraftRevisionId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Published revision pointer (nullable)
                b.Property(x => x.PublishedRevisionId)
                    .IsRequired(false);

                b.HasOne(x => x.PublishedRevision)
                    .WithMany()
                    .HasForeignKey(x => x.PublishedRevisionId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Composite principal key used by PageSection composite FK (tenant-safe)
                b.HasAlternateKey(x => new { x.TenantId, x.Id });
            });
        }


        private static void ConfigurePageSections(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PageSection>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                // ----------------------------
                // Canonical fields (final)
                // ----------------------------

                // Canonical discriminator
                b.Property(x => x.SectionTypeId)
                    .IsRequired();

                // Canonical payload
                b.Property(x => x.SettingsJson)
                    .HasColumnType("nvarchar(max)")
                    .IsRequired(false);

                // Optional label
                b.Property(x => x.DisplayName)
                    .HasMaxLength(200)
                    .IsRequired(false);

                // Deterministic ordering per page
                b.HasIndex(x => new { x.TenantId, x.PageId, x.SortOrder }).IsUnique();

                // ----------------------------
                // Relationships
                // ----------------------------

                // Tenant-safe: PageSection(TenantId, PageId) -> Page(TenantId, Id)
                b.HasOne(x => x.Page)
                    .WithMany(p => p.Sections)
                    .HasForeignKey(x => new { x.TenantId, x.PageId })
                    .HasPrincipalKey(p => new { p.TenantId, p.Id })
                    .OnDelete(DeleteBehavior.Cascade);

                // SectionType FK
                b.HasOne(x => x.SectionType)
                    .WithMany()
                    .HasForeignKey(x => x.SectionTypeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }



        private static void ConfigurePageStatuses(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PageStatus>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedNever();

                b.Property(x => x.Name).HasMaxLength(50).IsRequired();
                b.Property(x => x.IsSystem).HasDefaultValue(true);

                b.HasIndex(x => x.Name).IsUnique();

                b.HasData(
                    new PageStatus { Id = 1, Name = "Draft", IsSystem = true },
                    new PageStatus { Id = 2, Name = "Published", IsSystem = true },
                    new PageStatus { Id = 3, Name = "Archived", IsSystem = true }
                );
            });
        }

        private static void ConfigurePageRevisions(ModelBuilder modelBuilder)
        {
            // PageRevision: unique version per tenant+page
            modelBuilder.Entity<PageRevision>(b =>
            {
                b.HasIndex(x => new { x.TenantId, x.PageId, x.VersionNumber })
                 .IsUnique();

                b.HasMany(x => x.Sections)
                 .WithOne(x => x.PageRevision!)
                 .HasForeignKey(x => x.PageRevisionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // PageRevisionSection: supporting index for lookups
            modelBuilder.Entity<PageRevisionSection>(b =>
            {
                b.HasIndex(x => new { x.TenantId, x.PageRevisionId });
            });
        }
        private static void ConfigurePageRevisionSections(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PageRevisionSection>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                b.Property(x => x.SortOrder)
                    .HasMaxLength(200)
                    .IsRequired();

                b.Property(x => x.SettingsJson)
                    .HasColumnType("nvarchar(max)")
                    .IsRequired(false);

                b.Property(x => x.SectionTypeId).IsRequired();

                // Deterministic ordering per revision
                b.HasIndex(x => new { x.TenantId, x.PageRevisionId, x.SortOrder }).IsUnique();

                // Tenant-safe: PageRevisionSection(TenantId, PageRevisionId) -> PageRevision(TenantId, Id)
                b.HasOne(x => x.PageRevision)
                    .WithMany(r => r.Sections)
                    .HasForeignKey(x => new { x.TenantId, x.PageRevisionId })
                    .HasPrincipalKey(r => new { r.TenantId, r.Id })
                    .OnDelete(DeleteBehavior.Cascade);

                // SectionType FK
                b.HasOne(x => x.SectionType)
                    .WithMany()
                    .HasForeignKey(x => x.SectionTypeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
        private static void ConfigureMediaAssets(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MediaAsset>(b =>
            {
                b.ToTable("MediaAssets");

                b.HasKey(x => x.Id);
                b.Property(x => x.Id).ValueGeneratedOnAdd();

                // Tenant scoping (required)
                b.Property(x => x.TenantId)
                    .IsRequired();

                b.Property(x => x.FileName)
                    .HasMaxLength(255)
                    .IsRequired();

                b.Property(x => x.ContentType)
                    .HasMaxLength(255)
                    .IsRequired();

                b.Property(x => x.SizeBytes)
                    .HasMaxLength(255);

                b.Property(x => x.StorageKey)
                    .HasMaxLength(500)
                    .IsRequired();

                // Thumbnail URL path (optional but recommended)
                b.Property(x => x.ThumbStorageKey)
                    .HasMaxLength(500);

                b.Property(x => x.Width)
                    .HasMaxLength(500);

                b.Property(x => x.Height)
                    .HasMaxLength(500);

                b.Property(x => x.AltText)
                    .HasMaxLength(1000);

                b.Property(x => x.CheckSum)
                    .HasMaxLength(64);

                // Indexes
                b.HasIndex(x => x.StorageKey);
                b.HasIndex(x => x.FileName);
                b.HasIndex(x => x.ContentType);

                // Tenant-aware indexes (important)
                b.HasIndex(x => new { x.TenantId, x.IsDeleted });
                b.HasIndex(x => new { x.TenantId, x.CheckSum }); // dedupe per tenant

                // Optional: fast thumb lookup / diagnostics
                b.HasIndex(x => x.ThumbStorageKey);
            });
        }
        private static void ConfigureMediaCleanupRunLogs(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MediaCleanupRunLog>(b =>
            {
                b.ToTable("MediaCleanupRunLogs");

                b.HasKey(x => x.Id);

                // Indexes for dashboard lookups / "latest run" queries
                b.HasIndex(x => new { x.TenantId, x.StartedAtUtc });
                b.HasIndex(x => new { x.TenantId, x.Status, x.StartedAtUtc });

                // TenantId MUST be Guid (uniqueidentifier)
                b.Property(x => x.TenantId)
                    .IsRequired();

                // RunType / Status should be required and constrained
                b.Property(x => x.RunType)
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasDefaultValue("Nightly");

                b.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(30)
                    .HasDefaultValue("Running");

                // UTC timestamps
                b.Property(x => x.StartedAtUtc)
                    .IsRequired()
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                b.Property(x => x.FinishedAtUtc)
                    .IsRequired(false);

                // Diagnostics
                b.Property(x => x.ErrorSummary)
                    .HasMaxLength(2000);

                b.Property(x => x.Notes)
                    .HasMaxLength(4000);

                // If your BaseModel conventions already configure these, you can remove this block.
                b.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                b.Property(x => x.IsDeleted)
                    .HasDefaultValue(false);

                b.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                b.Property(x => x.RowVersion)
                    .IsRowVersion();
            });
        }

        private static void ConfigureMediaAlerts(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MediaAlert>(b =>
            {
                b.ToTable("MediaAlerts");

                b.HasKey(x => x.Id);

                b.Property(x => x.Subject)
                    .HasMaxLength(200)
                    .IsRequired();

                b.Property(x => x.Message)
                    .HasMaxLength(4000)
                    .IsRequired();

                b.Property(x => x.Severity)
                    .HasMaxLength(20)
                    .IsRequired();

                // Optional link to a cleanup run
                b.HasIndex(x => new { x.TenantId, x.CreatedAt });
                b.HasIndex(x => x.MediaCleanupRunLogId);

                // If you want tenant scoping fast:
                b.HasIndex(x => new { x.TenantId, x.IsDeleted });
            });
        }

        private static void ConfigureSectionTypes(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SectionType>(entity =>
            {
                entity.ToTable("SectionTypes");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                // ✅ Key is required + bounded + unique (matches the DB rule we want)
                entity.Property(x => x.Key)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.HasIndex(x => x.Key)
                    .IsUnique();

                // BaseModel fields (consistent defaults)
                entity.Property(x => x.IsActive)
                    .HasDefaultValue(true);

                entity.Property(x => x.IsDeleted)
                    .HasDefaultValue(false);

                // Since BaseModel.CreatedAt is non-nullable, give it a DB default
                entity.Property(x => x.CreatedAt)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                // CreatedBy is non-nullable in your BaseModel, so the DB needs a default
                entity.Property(x => x.CreatedBy)
                    .HasDefaultValue(Guid.Empty);

                // Optional but useful index for your common filter
                entity.HasIndex(x => new { x.IsActive, x.IsDeleted });
            });
        }


    }
}
