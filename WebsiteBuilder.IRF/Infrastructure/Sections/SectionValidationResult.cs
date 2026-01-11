namespace WebsiteBuilder.IRF.Infrastructure.Sections
{
    public sealed class SectionValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new();

        public static SectionValidationResult Success() => new();

        public static SectionValidationResult Fail(params string[] errors)
        {
            var r = new SectionValidationResult();
            r.Errors.AddRange(errors.Where(e => !string.IsNullOrWhiteSpace(e)));
            return r;
        }

        public void Add(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
                Errors.Add(error);
        }

        public void AddRange(IEnumerable<string> errors)
        {
            foreach (var e in errors)
                Add(e);
        }
    }
}
