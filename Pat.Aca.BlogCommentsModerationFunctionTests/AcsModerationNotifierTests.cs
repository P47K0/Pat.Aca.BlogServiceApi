using Microsoft.Extensions.Logging.Abstractions;
using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class AcsModerationNotifierTests
    {
        private static AcsEmailSettings SampleSettings() => new()
        {
            ConnectionString = "endpoint=https://example.communication.azure.com/;accesskey=fake",
            SenderAddress = "DoNotReply@example.azurecomm.net",
            RecipientEmail = "patrick@example.com"
        };

        private static ModerationNotification SampleNotification(bool autoPublished = false, string? email = null) =>
            new("comment-1", "first-article", "Alice", "A test comment.", email, 3, "Borderline.", autoPublished);

        [Fact]
        public void BuildMessage_addresses_from_sender_to_recipient()
        {
            var notifier = new AcsModerationNotifier(new FakeEmailClient(), SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            var message = notifier.BuildMessage(SampleNotification());

            Assert.Equal("DoNotReply@example.azurecomm.net", message.SenderAddress);
            Assert.Contains(message.Recipients.To, a => a.Address == "patrick@example.com");
        }

        [Fact]
        public void BuildMessage_subject_reflects_awaiting_review()
        {
            var notifier = new AcsModerationNotifier(new FakeEmailClient(), SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            var message = notifier.BuildMessage(SampleNotification(autoPublished: false));

            Assert.Contains("awaiting review", message.Content.Subject, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildMessage_subject_reflects_auto_published()
        {
            var notifier = new AcsModerationNotifier(new FakeEmailClient(), SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            var message = notifier.BuildMessage(SampleNotification(autoPublished: true));

            Assert.Contains("auto-published", message.Content.Subject, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildMessage_body_includes_author_score_reason_and_comment_text()
        {
            var notifier = new AcsModerationNotifier(new FakeEmailClient(), SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            var message = notifier.BuildMessage(SampleNotification());

            Assert.Contains("Alice", message.Content.PlainText);
            Assert.Contains("3/5", message.Content.PlainText);
            Assert.Contains("Borderline.", message.Content.PlainText);
            Assert.Contains("A test comment.", message.Content.PlainText);
            Assert.Contains("comment-1", message.Content.PlainText);
        }

        [Fact]
        public void BuildMessage_body_omits_the_email_line_when_no_email_was_provided()
        {
            var notifier = new AcsModerationNotifier(new FakeEmailClient(), SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            var message = notifier.BuildMessage(SampleNotification(email: null));

            Assert.DoesNotContain("Email:", message.Content.PlainText);
        }

        [Fact]
        public void BuildMessage_body_includes_the_email_when_provided()
        {
            var notifier = new AcsModerationNotifier(new FakeEmailClient(), SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            var message = notifier.BuildMessage(SampleNotification(email: "alice@example.com"));

            Assert.Contains("alice@example.com", message.Content.PlainText);
        }

        [Fact]
        public async Task NotifyAsync_sends_via_the_email_client()
        {
            var fakeClient = new FakeEmailClient();
            var notifier = new AcsModerationNotifier(fakeClient, SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            await notifier.NotifyAsync(SampleNotification());

            Assert.NotNull(fakeClient.LastMessage);
        }

        [Fact]
        public async Task NotifyAsync_swallows_a_send_failure_instead_of_throwing()
        {
            // The whole point of this design: a failed notification must
            // never turn an already-decided moderation outcome into a
            // failure for the caller -- see this class's own doc comment.
            var fakeClient = new FakeEmailClient(new InvalidOperationException("ACS is down"));
            var notifier = new AcsModerationNotifier(fakeClient, SampleSettings(), NullLogger<AcsModerationNotifier>.Instance);

            var exception = await Record.ExceptionAsync(() => notifier.NotifyAsync(SampleNotification()));

            Assert.Null(exception);
        }
    }
}
