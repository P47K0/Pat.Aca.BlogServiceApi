using Microsoft.Azure.Functions.Worker;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Hourly: adds the article views counted at the edge to Cosmos
    /// viewCount (see ViewCountFlushProcessor). Hourly rather than more often
    /// because every flush is a Cosmos write per viewed article, and each one
    /// reaches the Articles Change Feed (list push, count push). At minute 10,
    /// away from the other timers on the hour.
    /// </summary>
    public sealed class ViewCountFlushFunction(ViewCountFlushProcessor processor)
    {
        [Function("ViewCountFlushFunction")]
        public async Task RunAsync([TimerTrigger("0 10 * * * *")] TimerInfo timer, CancellationToken cancellationToken)
        {
            await processor.FlushAsync(DateTime.UtcNow, cancellationToken);
        }
    }
}
