namespace WebsiteBuilder.IRF.Infrastructure.Media
{
    public sealed class MediaCleanupResult
    {
        public int CandidatesFound { get; set; }
        public int RecordsDeleted { get; set; }
        public int FilesDeleted { get; set; }
        public int FileDeleteFailures { get; set; }
        public DateTime CutoffUtc { get; set; }
    }
}
