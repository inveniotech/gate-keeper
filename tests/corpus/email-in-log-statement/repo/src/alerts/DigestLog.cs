using Microsoft.Extensions.Logging;

namespace LedgerOps.Alerts;

public sealed class DigestLog
{
    private readonly ILogger<DigestLog> logger;

    public DigestLog(ILogger<DigestLog> logger)
    {
        this.logger = logger;
    }

    public void LogQueueSent(string batch)
    {
        this.logger.LogInformation(
            "Compliance digest queued for {Batch} to processor-alerts@ledger-ops.example.",
            batch);
    }
}
