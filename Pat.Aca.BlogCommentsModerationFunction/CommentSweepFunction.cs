using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Runs StuckCommentSweeper on a schedule -- the periodic-sweep piece
    /// CommentModerationFunction's own doc comment flags as needed to
    /// recover a comment left Queued after a scoring failure or exhausted
    /// daily quota. Hourly is a deliberately simple, hardcoded schedule
    /// rather than a configurable app setting -- unlike the LLM
    /// prompt/model/quota/threshold (which the user explicitly wants to
    /// tune quickly), this is an operational reliability detail with no
    /// day-one tuning need; can be made configurable later if that
    /// changes.
    /// </summary>
    public sealed class CommentSweepFunction
    {
        private readonly StuckCommentSweeper _sweeper;
        private readonly ILogger<CommentSweepFunction> _logger;

        public CommentSweepFunction(StuckCommentSweeper sweeper, ILogger<CommentSweepFunction> logger)
        {
            _sweeper = sweeper;
            _logger = logger;
        }

        [Function("CommentSweepFunction")]
        public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timer)
        {
            var touchedCount = await _sweeper.SweepAsync();

            if (touchedCount > 0)
            {
                _logger.LogInformation(
                    "Comment sweep re-queued {Count} stuck comment(s) for another moderation attempt.",
                    touchedCount);
            }
        }
    }
}
