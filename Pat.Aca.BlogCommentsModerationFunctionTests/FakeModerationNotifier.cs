using Pat.Aca.BlogCommentsModerationFunction;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public sealed class FakeModerationNotifier : IModerationNotifier
    {
        public List<ModerationNotification> Notifications { get; } = new();

        public Task NotifyAsync(ModerationNotification notification)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
