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

                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.Slug).HasMaxLength(200).IsRequired();

                // Slug unique per tenant
                b.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();

                // Default: Draft
                b.Property(x => x.PageStatusId).HasDefaultValue(1);

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

                b.Property(x => x.TypeKey)
                    .HasMaxLength(100)
                    .IsRequired();

                // Optional: if you want ContentJson to be large, leave as nvarchar(max) by default.
                // b.Property(x => x.ContentJson).HasColumnType("nvarchar(max)");

                // Deterministic ordering per page (not necessarily required to be unique, but usually desired)
                // If you want to allow duplicate SortOrder values, change IsUnique(false).
                b.HasIndex(x => new { x.TenantId, x.PageId, x.SortOrder }).IsUnique();

                // Tenant-safe relationship:
                // PageSection(TenantId, PageId) -> Page(TenantId, Id)
                b.HasOne(x => x.Page)
                    .WithMany(p => p.Sections)
                    .HasForeignKey(x => new { x.TenantId, x.PageId })
                    .HasPrincipalKey(p => new { p.TenantId, p.Id })
                    .OnDelete(DeleteBehavior.Cascade);
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
    }
}
