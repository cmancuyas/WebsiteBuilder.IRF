namespace WebsiteBuilder.IRF.Infrastructure.Pages
{
    public sealed class PublishResult
    {
        public bool Success { get; init; }
        public int? PublishedRevisionId { get; init; }
        public List<string> Errors { get; init; } = new();

        public static PublishResult Ok(int revisionId) =>
            new() { Success = true, PublishedRevisionId = revisionId };

        public static PublishResult Fail(params string[] errors) =>
            new()
            {
                Success = false,
                Errors = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToList()
            };
    }
}
