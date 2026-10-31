namespace DontBlink.Services;

public static class ArchiveFilter
{
    public static List<ArchiveItem> Apply(IEnumerable<ArchiveItem> clips, string? camera = null, DateTime? day = null, DateTime? from = null, DateTime? to = null)
    {
        if (from?.Date > to?.Date) throw new ArgumentException("The start date must be on or before the end date.");
        return clips.Where(c => string.IsNullOrEmpty(camera) || c.Camera.Equals(camera, StringComparison.OrdinalIgnoreCase))
            .Where(c => !day.HasValue || c.CreatedUtc.ToLocalTime().Date == day.Value.Date)
            .Where(c => !from.HasValue || c.CreatedUtc.ToLocalTime().Date >= from.Value.Date)
            .Where(c => !to.HasValue || c.CreatedUtc.ToLocalTime().Date <= to.Value.Date)
            .OrderByDescending(c => c.CreatedUtc).ToList();
    }
}
