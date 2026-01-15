namespace WebsiteBuilder.IRF.Infrastructure.Media;

public sealed class MediaCleanupResult
{
    public DateTime CutoffUtc { get; set; }

    public int CandidatesFound { get; set; }
    public int RecordsDeleted { get; set; }

    public int FilesDeleted { get; set; }
    public int FileDeleteFailures { get; set; }

    public long CandidateBytes { get; set; }
    public long? RunId { get; set; }

}
