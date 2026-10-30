namespace ByteShelf.Server.Jobs.Models
{
    /// <summary>
    /// The return value of a callback executed by <see cref="Services.IJobRunRecorder.RunAsync{TSummary}"/>.
    /// Tells the recorder whether to mark the run as success or failure, and what structured summary
    /// (if any) to persist. The recorder serialises <see cref="Summary"/> with <see cref="System.Text.Json.JsonSerializer"/>
    /// using web defaults, so a plain record/POCO is sufficient.
    /// </summary>
    /// <param name="Success">Whether the run completed successfully (true) or with a logical failure (false).</param>
    /// <param name="Summary">Optional structured summary serialised into <c>result_summary</c>.</param>
    /// <param name="ErrorMessage">Optional message persisted to <c>error_message</c> when <paramref name="Success"/> is false.</param>
    /// <typeparam name="TSummary">The summary's static type.</typeparam>
    public record JobRunOutcome<TSummary>(bool Success, TSummary? Summary, string? ErrorMessage);
}
