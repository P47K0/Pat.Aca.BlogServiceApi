using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeModerationScorer : IModerationScorer
    {
        private readonly ModerationScore _score;

        public FakeModerationScorer(ModerationScore score)
        {
            _score = score;
        }

        public string? LastScoredText { get; private set; }

        public Task<ModerationScore> ScoreAsync(string commentText)
        {
            LastScoredText = commentText;
            return Task.FromResult(_score);
        }
    }
}
