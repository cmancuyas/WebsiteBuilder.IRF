using Microsoft.EntityFrameworkCore;
using WebsiteBuilder.Models;

namespace WebsiteBuilder.IRF.DataAccess
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {
            ChangeTracker.LazyLoadingEnabled = false;
        }

        // ===== DbSets (core) =====
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantStatus> TenantStatuses => Set<TenantStatus>();
        public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
        public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

        public DbSet<Theme> Themes => Set<Theme>();
        public DbSet<DefaultTheme> DefaultThemes => Set<DefaultTheme>();

        public DbSet<DomainMapping> DomainMappings => Set<DomainMapping>();

        public DbSet<Page> Pages => Set<Page>();
        public DbSet<PageSection> PageSections => Set<PageSection>();

        public DbSet<NavigationMenuItem> NavigationMenuItems => Set<NavigationMenuItem>();
        // public DbSet<NavigationMenu> NavigationMenus => Set<NavigationMenu>(); // if exists

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureBaseModel(modelBuilder);
            ApplySoftDeleteQueryFilter(modelBuilder);

            // ===== Constraints / indexes =====

            // Tenant slug must be unique (subdomain routing)
            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.Slug)
                .IsUnique();

            // Domain host must be unique platform-wide
            modelBuilder.Entity<DomainMapping>()
                .HasIndex(d => d.Host)
                .IsUnique();

            // Only one primary domain per tenant (SQL Server filtered unique index)
            modelBuilder.Entity<DomainMapping>()
                .HasIndex(d => new { d.TenantId, d.IsPrimary })
                .IsUnique()
                .HasFilter("[IsPrimary] = 1 AND [IsDeleted] = 0");

            // Page slug must be unique within a tenant
            modelBuilder.Entity<Page>()
                .HasIndex(p => new { p.TenantId, p.Slug })
                .IsUnique();

            // ===== Relationships (minimal but important) =====

            // TenantStatus is reference data
            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.TenantStatus)
                .WithMany()
                .HasForeignKey(t => t.TenantStatusId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant -> ActiveTheme (optional)
            modelBuilder.Entity<Tenant>()
                .HasOne(t => t.ActiveTheme)
                .WithMany()
                .HasForeignKey(t => t.ActiveThemeId)
                .OnDelete(DeleteBehavior.SetNull);

            // DomainMapping belongs to tenant
            modelBuilder.Entity<DomainMapping>()
                .HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Theme belongs to tenant
            modelBuilder.Entity<Theme>()
                .HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(t => t.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Page belongs to tenant
            modelBuilder.Entity<Page>()
                .HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(p => p.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        public override int SaveChanges()
        {
            ApplyAuditAndSoftDeleteRules();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditAndSoftDeleteRules();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyAuditAndSoftDeleteRules()
        {
            var utcNow = DateTime.UtcNow;

            foreach (var entry in ChangeTracker.Entries<BaseModel>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedAt = utcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = utcNow;
                }
                else if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = utcNow;
                }
            }
        }

        private static void ConfigureBaseModel(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(BaseModel).IsAssignableFrom(entityType.ClrType))
                {
                    var rowVersionProp = entityType.FindProperty(nameof(BaseModel.RowVersion));
                    if (rowVersionProp != null)
                    {
                        rowVersionProp.IsConcurrencyToken = true;
                        rowVersionProp.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
                    }
                }
            }
        }

        private static void ApplySoftDeleteQueryFilter(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (!typeof(BaseModel).IsAssignableFrom(entityType.ClrType))
                    continue;

                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var isDeletedProperty = System.Linq.Expressions.Expression.Call(
                    typeof(EF),
                    nameof(EF.Property),
                    new[] { typeof(bool) },
                    parameter,
                    System.Linq.Expressions.Expression.Constant(nameof(BaseModel.IsDeleted)));

                var compare = System.Linq.Expressions.Expression.Equal(
                    isDeletedProperty,
                    System.Linq.Expressions.Expression.Constant(false));

                var lambda = System.Linq.Expressions.Expression.Lambda(compare, parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }
}
