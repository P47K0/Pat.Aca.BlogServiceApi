namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Sends the review-alert email for one scored comment. The real
    /// implementation (a later commit) uses Azure Communication Services,
    /// reusing the same resource as the user's other project,
    /// YoutubeChannelGuard; kept behind this interface so the processor's
    /// "always notify, regardless of outcome" behavior is testable without
    /// a real ACS connection.
    /// </summary>
    public interface IModerationNotifier
    {
        Task NotifyAsync(ModerationNotification notification);
    }
}
