using System;

namespace WebsiteBuilder.IRF.Infrastructure.Tenancy
{
    public sealed class TenantResolutionResult
    {
        // Required tenant identity
        public Guid TenantId { get; init; }

        // Compatibility fields (your TenantResolver currently sets these)
        public string Slug { get; init; } = string.Empty;
        public bool MatchedByCustomDomain { get; init; }
        public string MatchedHost { get; init; } = string.Empty;

        // Optional helpers (convenience aliases)
        public string TenantSlug => Slug;

        public TenantMatchType MatchType =>
            MatchedByCustomDomain ? TenantMatchType.CustomDomain : TenantMatchType.Subdomain;

        // Optional: you can start filling these later without changing middleware contracts
        public int? DomainMappingId { get; init; }
        public bool? IsPrimaryDomain { get; init; }
        public int? VerificationStatusId { get; init; }
        public int? VerificationMethodId { get; init; }
        public int? SslModeId { get; init; }
        public DateTime? DomainActivatedAt { get; init; }

        // Optional: tenant lifecycle info (if you want to enforce gating in middleware later)
        public int? TenantStatusId { get; init; }
        public bool? IsTenantActive { get; init; }
        public DateTime? PublishedAt { get; init; }
    }

    public enum TenantMatchType
    {
        None = 0,
        CustomDomain = 1,
        Subdomain = 2
    }
}
