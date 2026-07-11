namespace Trion.Desktop.Services;

/// <summary>Result of submitting a bug report to Phoenix.</summary>
public sealed record BugReportResult(bool Success, string? Error);

public interface IBugReportService
{
    /// <summary>
    /// Submits a bug report to Phoenix. <paramref name="category"/> must be one of
    /// "repack", "trion", or "other". <paramref name="repackSlug"/> is only meaningful
    /// when <paramref name="category"/> is "repack" and may be null/empty otherwise.
    /// The current Trion Desktop version is attached automatically.
    /// </summary>
    Task<BugReportResult> SubmitAsync(
        string category,
        string? repackSlug,
        string description,
        string content,
        CancellationToken ct = default);
}
