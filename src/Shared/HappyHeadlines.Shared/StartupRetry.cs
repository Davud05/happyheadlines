using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Shared;

public static class StartupRetry
{
    /// <summary>Retries a startup step (e.g. creating a schema) while dependencies are still booting.</summary>
    public static async Task RunAsync(Func<Task> action, ILogger logger, string description, int attempts = 20)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning("{Description} failed (attempt {Attempt}/{Attempts}): {Error}",
                    description, attempt, attempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
