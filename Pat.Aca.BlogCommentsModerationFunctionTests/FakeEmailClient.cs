using Azure;
using Azure.Communication.Email;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    /// <summary>
    /// EmailClient's SendAsync is virtual and it has a parameterless
    /// constructor -- the standard Azure SDK pattern for exactly this kind
    /// of subclass-based test double, no mocking library needed.
    /// </summary>
    public sealed class FakeEmailClient : EmailClient
    {
        private readonly Exception? _throws;

        public EmailMessage? LastMessage { get; private set; }

        public FakeEmailClient()
        {
        }

        public FakeEmailClient(Exception throwsOnSend)
        {
            _throws = throwsOnSend;
        }

        public override Task<EmailSendOperation> SendAsync(WaitUntil waitUntil, EmailMessage message, CancellationToken cancellationToken = default)
        {
            LastMessage = message;

            if (_throws is not null)
            {
                throw _throws;
            }

            return Task.FromResult<EmailSendOperation>(null!);
        }
    }
}
