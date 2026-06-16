using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vendor.FourKites.Smoke
{
    /// <summary>
    /// HttpMessageHandler that records every request and returns canned responses.
    /// Used to test FourKitesClient + FourKitesAdapter without making real HTTP calls.
    ///
    /// Behavior modes:
    ///   - QueueResponse(): pushes a response onto a FIFO queue; next request gets it
    ///   - SetThrowException(): the next request throws (simulates network failure)
    ///   - SetDelay(): adds latency before responding (for timeout tests)
    ///
    /// After requests are sent, tests inspect:
    ///   - Requests: list of every HttpRequestMessage received
    ///   - SentBodies: the body strings sent (auto-captured)
    /// </summary>
    internal class MockHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
        public List<string> SentBodies { get; } = new List<string>();

        private readonly Queue<Func<HttpResponseMessage>> _responses = new Queue<Func<HttpResponseMessage>>();
        private Exception _exceptionToThrow;
        private TimeSpan _delay = TimeSpan.Zero;

        /// <summary>Queue a response builder. Called in order on subsequent requests.</summary>
        public void QueueResponse(HttpStatusCode statusCode, string body = "{}", string contentType = "application/json")
        {
            _responses.Enqueue(() =>
            {
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(body ?? "", System.Text.Encoding.UTF8, contentType)
                };
                return response;
            });
        }

        /// <summary>The next N requests will throw this exception (simulates network errors).</summary>
        public void SetThrowException(Exception ex)
        {
            _exceptionToThrow = ex;
        }

        /// <summary>Clear the throw-on-next-request setting (e.g., for testing retry recovery).</summary>
        public void ClearException() => _exceptionToThrow = null;

        public void SetDelay(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Capture the request and body BEFORE returning/throwing
            Requests.Add(request);
            string body = "";
            if (request.Content != null)
            {
                body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            SentBodies.Add(body);

            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }

            if (_exceptionToThrow != null)
            {
                throw _exceptionToThrow;
            }

            if (_responses.Count > 0)
            {
                return _responses.Dequeue()();
            }

            // Default: return 200 OK with empty body if no response queued
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
