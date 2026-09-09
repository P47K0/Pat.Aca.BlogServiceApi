using System.Text;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Real IModerationNotifier implementation: sends the review-alert
    /// email via Azure Communication Services. Unlike
    /// CloudflareWorkersAiScorer's infra failures (which propagate --
    /// there's no moderation result to fail safe to if the model never
    /// ran), an email-send failure here is deliberately best-effort: by the
    /// time NotifyAsync is called, CommentModerationProcessor has already
    /// decided the comment's status, so losing the email would otherwise
    /// throw away a perfectly good moderation decision over a
    /// notification-only side effect. Same "side effect that isn't the
    /// primary operation should never turn success into failure" precedent
    /// as the sibling API project's view-count increment
    /// (Program.cs's GET /articles/{slug} handler) -- caught and logged
    /// here, not rethrown, so a caller never needs its own try/catch around
    /// this to get that behavior.
    /// </summary>
    public sealed class AcsModerationNotifier : IModerationNotifier
    {
        private readonly EmailClient _emailClient;
        private readonly AcsEmailSettings _settings;
        private readonly ILogger<AcsModerationNotifier> _logger;

        public AcsModerationNotifier(EmailClient emailClient, AcsEmailSettings settings, ILogger<AcsModerationNotifier> logger)
        {
            _emailClient = emailClient;
            _settings = settings;
            _logger = logger;
        }

        public async Task NotifyAsync(ModerationNotification notification)
        {
            try
            {
                var message = BuildMessage(notification);
                await _emailClient.SendAsync(WaitUntil.Completed, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send the moderation review email for comment {CommentId} on article {ArticleSlug} -- the comment's status was still decided/persisted, only the notification failed.",
                    notification.CommentId,
                    notification.ArticleSlug);
            }
        }

        /// <summary>
        /// Public specifically so the email's actual content (the part
        /// worth getting right and testing) is directly unit-testable
        /// without a real ACS connection.
        /// </summary>
        public EmailMessage BuildMessage(ModerationNotification notification)
        {
            var subject = notification.AutoPublished
                ? $"Comment auto-published on \"{notification.ArticleSlug}\" (score {notification.Score}/5)"
                : $"New comment awaiting review on \"{notification.ArticleSlug}\" (score {notification.Score}/5)";

            var body = new StringBuilder()
                .AppendLine(notification.AutoPublished
                    ? "This comment scored high enough to auto-publish. Flag it if the LLM got it wrong."
                    : "This comment is awaiting your review -- it will not appear on the site until you publish it.")
                .AppendLine()
                .AppendLine($"Article: {notification.ArticleSlug}")
                .AppendLine($"Author: {notification.AuthorName}");

            if (!string.IsNullOrEmpty(notification.Email))
            {
                body.AppendLine($"Email: {notification.Email}");
            }

            body
                .AppendLine($"Score: {notification.Score}/5 -- {notification.Reason}")
                .AppendLine()
                .AppendLine("Comment:")
                .AppendLine(notification.CommentText)
                .AppendLine()
                .AppendLine($"Comment id: {notification.CommentId}");

            var content = new EmailContent(subject) { PlainText = body.ToString() };
            var recipients = new EmailRecipients(new[] { new EmailAddress(_settings.RecipientEmail) });
            return new EmailMessage(_settings.SenderAddress, recipients, content);
        }
    }
}
