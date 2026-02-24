using System.Text.Json;

namespace WebsiteBuilder.IRF.Infrastructure.Sections;

public static class SectionJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static T Deserialize<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json))
            return new T();

        try
        {
            var obj = JsonSerializer.Deserialize<T>(json, Options);
            return obj ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public static string Serialize<T>(T obj)
        => JsonSerializer.Serialize(obj, Options);
}
