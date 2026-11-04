using Microsoft.Extensions.Logging;

namespace LedgerOps.Support;

public sealed class CallbackLog
{
    private readonly ILogger<CallbackLog> logger;

    public CallbackLog(ILogger<CallbackLog> logger)
    {
        this.logger = logger;
    }

    public void LogScheduledCallback(string ticket)
    {
        this.logger.LogInformation(
            "Callback scheduled for {Ticket} on 08079012062.",
            ticket);
    }
}
