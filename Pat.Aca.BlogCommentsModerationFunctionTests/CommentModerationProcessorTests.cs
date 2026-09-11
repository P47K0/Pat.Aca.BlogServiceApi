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
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            var result = await processor.ProcessAsync(SampleComment());

            Assert.Null(result);
        }

        [Fact]
        public async Task ProcessAsync_does_not_score_or_notify_when_quota_exhausted()
        {
            var quotaStore = new FakeModerationQuotaStore(hasQuotaRemaining: false);
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Null(scorer.LastScoredText);
            Assert.Empty(notifier.Notifications);
        }

        [Fact]
        public async Task ProcessAsync_does_not_look_up_article_context_when_quota_exhausted()
        {
            var quotaStore = new FakeModerationQuotaStore(hasQuotaRemaining: false);
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider("A summary.");
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Null(articleContext.LastRequestedSlug);
        }

        [Fact]
        public async Task ProcessAsync_unpublishes_when_score_is_below_the_auto_publish_threshold()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(3, "Borderline."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider();
            var settings = new ModerationSettings { AutoPublishMinScore = 4 };
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, settings);

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
            var articleContext = new FakeArticleContextProvider();
            var settings = new ModerationSettings { AutoPublishMinScore = 4 };
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, settings);

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
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

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
            var articleContext = new FakeArticleContextProvider();
            var settings = new ModerationSettings { AutoPublishMinScore = 4 };
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, settings);

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
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            var notification = Assert.Single(notifier.Notifications);
            Assert.False(notification.AutoPublished);
            Assert.Equal("Looks spammy.", notification.Reason);
        }

        [Fact]
        public async Task ProcessAsync_releases_the_quota_slot_when_scoring_fails()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScoringException("Cloudflare Workers AI returned 401 Unauthorized."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await Assert.ThrowsAsync<ModerationScoringException>(() => processor.ProcessAsync(SampleComment()));

            Assert.Equal(1, quotaStore.CallCount);
            Assert.Equal(1, quotaStore.ReleaseCallCount);
        }

        [Fact]
        public async Task ProcessAsync_does_not_release_the_quota_slot_when_scoring_succeeds()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Equal(0, quotaStore.ReleaseCallCount);
        }

        [Fact]
        public async Task ProcessAsync_does_not_notify_when_scoring_fails()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScoringException("Failed to reach Cloudflare Workers AI."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await Assert.ThrowsAsync<ModerationScoringException>(() => processor.ProcessAsync(SampleComment()));

            Assert.Empty(notifier.Notifications);
        }

        [Fact]
        public async Task ProcessAsync_passes_the_comment_text_to_the_scorer()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Equal("A perfectly nice comment.", scorer.LastScoredText);
        }

        [Fact]
        public async Task ProcessAsync_passes_the_looked_up_article_summary_to_the_scorer()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider("How the blog's comment system works.");
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Equal("How the blog's comment system works.", scorer.LastArticleSummary);
        }

        [Fact]
        public async Task ProcessAsync_looks_up_article_context_by_the_comments_article_slug()
        {
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(5, "Fine."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider("A summary.");
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            await processor.ProcessAsync(SampleComment());

            Assert.Equal("first-article", articleContext.LastRequestedSlug);
        }

        [Fact]
        public async Task ProcessAsync_still_scores_when_article_context_lookup_returns_null()
        {
            // FakeArticleContextProvider's default (no summary passed)
            // mirrors CosmosArticleContextProvider's real best-effort
            // failure mode -- a missing/failed lookup must never block
            // moderation, only fall back to scoring on the comment text
            // alone (see IArticleContextProvider's own doc comment).
            var quotaStore = new FakeModerationQuotaStore();
            var scorer = new FakeModerationScorer(new ModerationScore(4, "Fine, no context needed."));
            var notifier = new FakeModerationNotifier();
            var articleContext = new FakeArticleContextProvider();
            var processor = new CommentModerationProcessor(quotaStore, scorer, notifier, articleContext, new ModerationSettings());

            var result = await processor.ProcessAsync(SampleComment());

            Assert.NotNull(result);
            Assert.Null(scorer.LastArticleSummary);
        }
    }
}
