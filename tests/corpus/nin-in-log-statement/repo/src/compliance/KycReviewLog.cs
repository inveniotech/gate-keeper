using Microsoft.Extensions.Logging;

namespace LedgerOps.Compliance;

public sealed class KycReviewLog
{
    private readonly ILogger<KycReviewLog> logger;

    public KycReviewLog(ILogger<KycReviewLog> logger)
    {
        this.logger = logger;
    }

    public void LogManualReview(string reference)
    {
        this.logger.LogWarning(
            "Manual verification queued for {Reference} with NIN 86893049845.",
            reference);
    }
}
