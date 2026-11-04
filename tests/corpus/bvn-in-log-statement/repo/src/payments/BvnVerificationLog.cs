using Microsoft.Extensions.Logging;

namespace LedgerOps.Payments;

public sealed class BvnVerificationLog
{
    private readonly ILogger<BvnVerificationLog> logger;

    public BvnVerificationLog(ILogger<BvnVerificationLog> logger)
    {
        this.logger = logger;
    }

    public void LogVerified(string fullName)
    {
        this.logger.LogInformation(
            "Identity verified for {Name} with BVN 32826314843.",
            fullName);
    }
}
