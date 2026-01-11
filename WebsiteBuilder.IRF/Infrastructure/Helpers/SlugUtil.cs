namespace WebsiteBuilder.IRF.Infrastructure.Helpers
{
    public static class SlugUtil
    {
        public static string Normalize(string? slug)
        {
            slug ??= string.Empty;
            slug = slug.Trim();

            // remove leading/trailing slashes
            slug = slug.Trim('/');

            // lowercase
            slug = slug.ToLowerInvariant();

            return slug;
        }
    }
}
