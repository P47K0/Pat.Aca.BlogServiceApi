using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The Cosmos DB Change Feed trigger that ties everything together:
    /// picks up newly-Queued comments, runs them through
    /// CommentModerationProcessor, and patches the decided status/score
    /// back via CommentStatusWriter.
    ///
    /// The Change Feed delivers every change in the Comments container,
    /// not just new comments -- the daily quota counter document
    /// (CosmosModerationQuotaStore) and this Function's own status-patch
    /// writes onto already-moderated comments both flow through it too.
    /// Both are filtered out by the Status == Queued check below: the
    /// quota document never has that field, and an already-moderated
    /// comment has already moved off it.
    ///
    /// Connection is the app setting name the Functions host resolves
    /// this trigger's own Cosmos binding from -- either a full connection
    /// string (CosmosDb = "AccountEndpoint=...;AccountKey=...;", the shape
    /// the local Cosmos DB Emulator uses) or, in production, an
    /// identity-based connection via CosmosDb__accountEndpoint +
    /// CosmosDb__credential = "managedidentity" app settings. This is
    /// Azure Functions' own binding infrastructure, config-driven rather
    /// than something this class's code directly constructs -- unlike
    /// every other Cosmos-facing class in this project, it can't be
    /// exercised without a real deployment or a local Functions Core
    /// Tools + Cosmos DB Emulator run, so it's the one piece of this whole
    /// feature that most needs a real environment to confirm, not just a
    /// clean build.
    ///
    /// Deliberately does NOT retry a scoring failure or a
    /// quota-exhausted comment by re-touching its document to force
    /// another Change Feed delivery -- that would create a tight
    /// immediate-retry loop for as long as the underlying cause persists
    /// (Cloudflare down, or quota exhausted for the rest of the day),
    /// wasting Cosmos RU/Function executions the whole time. Both cases
    /// just log and leave the comment at Queued; recovering a
    /// stuck-Queued comment once conditions improve is left to the
    /// separate periodic-sweep piece (StuckCommentSweeper/
    /// CommentSweepFunction, a Timer-triggered Function that re-touches
    /// anything still Queued past a grace period) rather than done here.
    /// </summary>
    public sealed class CommentModerationFunction
    {
        private readonly CommentModerationProcessor _processor;
        private readonly CommentStatusWriter _statusWriter;
        private readonly ILogger<CommentModerationFunction> _logger;

        public CommentModerationFunction(
            CommentModerationProcessor processor,
            CommentStatusWriter statusWriter,
            ILogger<CommentModerationFunction> logger)
        {
            _processor = processor;
            _statusWriter = statusWriter;
            _logger = logger;
        }

        [Function("CommentModerationFunction")]
        public async Task RunAsync(
            [CosmosDBTrigger(
                databaseName: "%CosmosDb:Database%",
                containerName: "Comments",
                Connection = "CosmosDb",
                LeaseContainerName = "CommentsLeases",
                CreateLeaseContainerIfNotExists = false)]
            IReadOnlyList<CommentChangeFeedDocument> changes)
        {
            foreach (var change in changes)
            {
                if (change.Status != ModerationCommentStatus.Queued)
                {
                    continue;
                }

                var queuedComment = new QueuedComment(change.Id, change.ArticleSlug, change.AuthorName, change.Text, change.Email);

                ModerationResult? result;
                try
                {
                    result = await _processor.ProcessAsync(queuedComment);
                }
                catch (ModerationScoringException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Moderation scoring failed for comment {CommentId} on article {ArticleSlug} -- leaving it Queued. The periodic sweep will recover it on its next run.",
                        change.Id,
                        change.ArticleSlug);
                    continue;
                }

                if (result is null)
                {
                    _logger.LogInformation(
                        "Daily moderation quota exhausted -- comment {CommentId} on article {ArticleSlug} stays Queued until the periodic sweep recovers it, on or after the next UTC day.",
                        change.Id,
                        change.ArticleSlug);
                    continue;
                }

                await _statusWriter.ApplyResultAsync(change.ArticleSlug, change.Id, result);
            }
        }
    }
}
