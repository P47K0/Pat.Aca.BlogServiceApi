using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeModerationScorer : IModerationScorer
    {
        private readonly ModerationScore? _score;
        private readonly Exception? _exceptionToThrow;

        public FakeModerationScorer(ModerationScore score)
        {
            _score = score;
        }

        // For tests exercising CommentModerationProcessor's quota-release
        // behavior on a scoring failure -- see ReleaseAsync's doc comment
        // on IModerationQuotaStore for why that matters.
        public FakeModerationScorer(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public string? LastScoredText { get; private set; }

        public string? LastArticleSummary { get; private set; }

        public Task<ModerationScore> ScoreAsync(string commentText, string? articleSummary)
        {
            LastScoredText = commentText;
            LastArticleSummary = articleSummary;
            return _exceptionToThrow is not null
                ? Task.FromException<ModerationScore>(_exceptionToThrow)
                : Task.FromResult(_score!);
        }
    }
}
