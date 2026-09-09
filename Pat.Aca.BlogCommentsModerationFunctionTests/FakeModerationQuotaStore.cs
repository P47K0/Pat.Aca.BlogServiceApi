using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeModerationQuotaStore : IModerationQuotaStore
    {
        private readonly bool _hasQuotaRemaining;

        public FakeModerationQuotaStore(bool hasQuotaRemaining = true)
        {
            _hasQuotaRemaining = hasQuotaRemaining;
        }

        public int CallCount { get; private set; }

        public Task<bool> TryConsumeAsync()
        {
            CallCount++;
            return Task.FromResult(_hasQuotaRemaining);
        }
    }
}
