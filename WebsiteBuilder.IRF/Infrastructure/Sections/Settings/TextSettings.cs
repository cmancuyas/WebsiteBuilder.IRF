namespace WebsiteBuilder.IRF.Infrastructure.Sections.Settings;

public sealed class TextSettings
{
    public string? Title { get; set; }
    public string? Body { get; set; } // allow markdown/html depending on your validator policy
    public string? Align { get; set; } // "left" | "center" | "right"
}
