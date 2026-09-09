using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class CommentModerationProcessorTests
    {
        private static QueuedComment SampleComment(string? email = null) =>
            new("comment-1", "first-article", "Alice", "A perfectly nice comment.", email);

        [Fact]
        public async Task ProcessAsync_returns_null_when_quota_exhausted()
        {
            var quotaStore = new FakeModerationQuotaStore(hasQuotaRemaining: false);
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, new ModerationSettings());

            var result = await processor.ProcessAsync(SampleComment());

            Assert.Null(result);
        }

        [Fact]
        public async Task ProcessAsync_does_not_score_or_notify_when_quota_exhausted()
        {
            var quotaStore = new FakeModerationQuotaStore(hasQuotaRemaining: false);
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Null(scorer.LastScoredText);
            Assert.Empty(notifier.Notifications);
        }

        [Fact]
        public async Task ProcessAsync_unpublishes_when_score_is_below_the_auto_publish_threshold()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(3, "Borderline."));
            var notifier = new FakeModerationNotifier();
            var settings = new ModerationSettings { AutoPublishMinScore = 4 };
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, settings);

            var result = await processor.ProcessAsync(SampleComment());

            Assert.NotNull(result);
            Assert.Equal(ModerationCommentStatus.Unpublished, result!.Status);
            Assert.Equal(3, result.Score);
        }

        [Fact]
        public async Task ProcessAsync_publishes_when_score_meets_the_auto_publish_threshold()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(4, "Looks fine."));
            var notifier = new FakeModerationNotifier();
            var settings = new ModerationSettings { AutoPublishMinScore = 4 };
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, settings);

            var result = await processor.ProcessAsync(SampleComment());

            Assert.NotNull(result);
            Assert.Equal(ModerationCommentStatus.Published, result!.Status);
        }

        [Fact]
        public async Task ProcessAsync_stays_unpublished_by_default_even_for_a_perfect_score()
        {
            // Default ModerationSettings.AutoPublishMinScore (6) sits above
            // the max possible score (5) -- "human always in the middle"
            // expressed with zero extra logic, not a separate flag.
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Perfectly fine."));
            var notifier = new FakeModerationNotifier();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, new ModerationSettings());

            var result = await processor.ProcessAsync(SampleComment());

            Assert.NotNull(result);
            Assert.Equal(ModerationCommentStatus.Unpublished, result!.Status);
        }

        [Fact]
        public async Task ProcessAsync_always_notifies_even_when_auto_published()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Great comment."));
            var notifier = new FakeModerationNotifier();
            var settings = new ModerationSettings { AutoPublishMinScore = 4 };
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, settings);

            await processor.ProcessAsync(SampleComment(email: "alice@example.com"));

            var notification = Assert.Single(notifier.Notifications);
            Assert.True(notification.AutoPublished);
            Assert.Equal("comment-1", notification.CommentId);
            Assert.Equal("alice@example.com", notification.Email);
            Assert.Equal(5, notification.Score);
        }

        [Fact]
        public async Task ProcessAsync_notification_reflects_manual_review_when_not_auto_published()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(2, "Looks spammy."));
            var notifier = new FakeModerationNotifier();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            var notification = Assert.Single(notifier.Notifications);
            Assert.False(notification.AutoPublished);
            Assert.Equal("Looks spammy.", notification.Reason);
        }

        [Fact]
        public async Task ProcessAsync_passes_the_comment_text_to_the_scorer()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Equal("A perfectly nice comment.", scorer.LastScoredText);
        }
    }
}
