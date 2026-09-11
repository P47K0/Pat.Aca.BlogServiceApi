namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The pure moderation decision logic: quota check -> look up article
    /// context -> score -> decide Published vs. Unpublished against
    /// AUTO_PUBLISH_MIN_SCORE -> always notify, regardless of outcome.
    /// Deliberately has no Cosmos/HTTP dependency of its own -- everything
    /// external is behind IModerationQuotaStore/IModerationScorer/
    /// IModerationNotifier/IArticleContextProvider, so this class (the
    /// actual interesting logic) is testable with plain fakes, same
    /// reasoning as ApiSecurity/ArticleWriteValidation being pulled out of
    /// Program.cs in the sibling API project.
    ///
    /// The quota slot is claimed before scoring (so a comment never even
    /// reaches the scorer once today's quota is genuinely exhausted), but
    /// given back via IModerationQuotaStore.ReleaseAsync if the scoring
    /// call itself fails -- a slot is only meant to represent a real,
    /// successful moderation, not a wasted attempt.
    ///
    /// Error handling note: a IModerationScorer/IModerationNotifier failure
    /// is allowed to propagate out of ProcessAsync (after the quota release
    /// above) rather than being swallowed here -- the actual Cosmos-trigger
    /// Function decides whether to catch/log and leave the comment Queued
    /// for a later run, or let the Functions host's own retry behavior
    /// handle it.
    /// </summary>
    public sealed class CommentModerationProcessor
    {
        private readonly IModerationQuotaStore _quotaStore;
        private readonly IModerationScorer _scorer;
        private readonly IModerationNotifier _notifier;
        private readonly IArticleContextProvider _articleContextProvider;
        private readonly ModerationSettings _settings;

        public CommentModerationProcessor(
            IModerationQuotaStore quotaStore,
            IModerationScorer scorer,
            IModerationNotifier notifier,
            IArticleContextProvider articleContextProvider,
            ModerationSettings settings)
        {
            _quotaStore = quotaStore;
            _scorer = scorer;
            _notifier = notifier;
            _articleContextProvider = articleContextProvider;
            _settings = settings;
        }

        /// <summary>
        /// Processes one queued comment. Returns null if today's quota is
        /// already exhausted (comment stays Queued, un-scored, un-notified)
        /// -- otherwise always scores, always notifies, and returns the
        /// status/score the caller should patch back onto the Cosmos
        /// document.
        /// </summary>
        public async Task<ModerationResult?> ProcessAsync(QueuedComment comment)
        {
            if (!await _quotaStore.TryConsumeAsync())
            {
                return null;
            }

            // Best-effort -- never throws, falls back to null (scoring on
            // the comment text alone) on any failure. Fetched only after
            // the quota check above succeeds, so an already-exhausted day
            // doesn't pay for a lookup that's about to be discarded anyway.
            var articleSummary = await _articleContextProvider.GetArticleSummaryAsync(comment.ArticleSlug);

            ModerationScore score;
            try
            {
                score = await _scorer.ScoreAsync(comment.Text, articleSummary);
            }
            catch
            {
                // The quota slot claimed above was for an attempt that
                // never actually moderated anything -- give it back before
                // letting the failure propagate (see
                // IModerationQuotaStore.ReleaseAsync's own doc comment for
                // why this matters: a real production incident on
                // 2026-09-10 had a persistent scoring failure silently
                // exhaust the entire day's quota with zero comments ever
                // actually scored).
                await _quotaStore.ReleaseAsync();
                throw;
            }

            var autoPublished = score.Score >= _settings.AutoPublishMinScore;
            var status = autoPublished ? ModerationCommentStatus.Published : ModerationCommentStatus.Unpublished;

            await _notifier.NotifyAsync(new ModerationNotification(
                comment.Id,
                comment.ArticleSlug,
                comment.AuthorName,
                comment.Text,
                comment.Email,
                score.Score,
                score.Reason,
                autoPublished));

            return new ModerationResult(status, score.Score);
        }
    }
}
