namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// The pure moderation decision logic: quota check -> score -> decide
    /// Published vs. Unpublished against AUTO_PUBLISH_MIN_SCORE -> always
    /// notify, regardless of outcome. Deliberately has no Cosmos/HTTP
    /// dependency of its own -- everything external is behind
    /// IModerationQuotaStore/IModerationScorer/IModerationNotifier, so this
    /// class (the actual interesting logic) is testable with plain fakes,
    /// same reasoning as ApiSecurity/ArticleWriteValidation being pulled out
    /// of Program.cs in the sibling API project.
    ///
    /// Error handling note: a IModerationScorer/IModerationNotifier failure
    /// is allowed to propagate out of ProcessAsync rather than being
    /// swallowed here -- the actual Cosmos-trigger Function (a later
    /// commit, once this is wired to a real Change Feed trigger) decides
    /// whether to catch/log and leave the comment Queued for a later run,
    /// or let the Functions host's own retry behavior handle it. Not
    /// decided yet, deliberately deferred to when that wiring exists.
    /// </summary>
    public sealed class CommentModerationProcessor
    {
        private readonly IModerationQuotaStore _quotaStore;
        private readonly IModerationScorer _scorer;
        private readonly IModerationNotifier _notifier;
        private readonly ModerationSettings _settings;

        public CommentModerationProcessor(
            IModerationQuotaStore quotaStore,
            IModerationScorer scorer,
            IModerationNotifier notifier,
            ModerationSettings settings)
        {
            _quotaStore = quotaStore;
            _scorer = scorer;
            _notifier = notifier;
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

            var score = await _scorer.ScoreAsync(comment.Text);
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
