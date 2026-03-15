namespace WebsiteBuilder.Models.Constants
{
    public static class DomainVerificationStatus
    {
        public const int Pending = 1;
        public const int Verified = 2;
        public const int Failed = 3;
        public const int Expired = 4;
    }

    public static class DomainVerificationMethod
    {
        public const int DnsTxt = 1;
    }

    public static class DomainSslMode
    {
        public const int None = 1;
        public const int External = 2;
        public const int Managed = 3;
    }
}