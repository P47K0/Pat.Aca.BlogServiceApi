using System.Net;
using System.Text;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    /// <summary>
    /// A DelegatingHandler stand-in for a real network call -- lets tests
    /// construct an HttpClient that returns a canned response (or throws,
    /// for the "can't reach the service at all" case) without any real
    /// HTTP traffic.
    /// </summary>
    public sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly Exception? _throws;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public FakeHttpMessageHandler(Exception throws)
        {
            _throws = throws;
            _statusCode = HttpStatusCode.OK;
            _responseBody = string.Empty;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_throws is not null)
            {
                throw _throws;
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
