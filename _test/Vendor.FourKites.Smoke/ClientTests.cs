using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vendor.FourKites.Smoke
{
    internal static class ClientTests
    {
        public static void RegisterAll()
        {
            TestHarness.RunAsync("Client. Happy path returns Success", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.Accepted,
                    @"{""requestId"":""req-xyz"",""fourKitesLoadId"":""FK-001""}");

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostJsonAsync(
                        "https://api.fourkites.com/v1/loads",
                        "test-key",
                        @"{""loadNumber"":""LOAD1""}",
                        CancellationToken.None);

                    TestHarness.Assert(response.IsSuccess, "should be success");
                    TestHarness.AssertEqual(202, response.HttpStatusCode.Value, "status");
                    TestHarness.AssertEqual("req-xyz", response.VendorRequestId, "VendorRequestId extracted");
                    TestHarness.AssertEqual("FK-001", response.VendorLoadId, "VendorLoadId extracted");
                }
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. Sets correct headers on outgoing request", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.OK);

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    await client.PostJsonAsync(
                        "https://api.fourkites.com/v1/loads",
                        "my-api-key-123",
                        @"{""foo"":""bar""}",
                        CancellationToken.None);
                }

                TestHarness.AssertEqual(1, mock.Requests.Count, "exactly 1 request sent");
                var req = mock.Requests[0];

                TestHarness.AssertEqual(HttpMethod.Post, req.Method, "POST method");
                TestHarness.AssertEqual("https://api.fourkites.com/v1/loads", req.RequestUri.ToString());

                TestHarness.Assert(req.Headers.Contains("X-FK-API-Key"), "X-FK-API-Key header present");
                var apiKeyValues = req.Headers.GetValues("X-FK-API-Key");
                foreach (var v in apiKeyValues)
                {
                    TestHarness.AssertEqual("my-api-key-123", v, "X-FK-API-Key value");
                    break;
                }

                TestHarness.AssertEqual(@"{""foo"":""bar""}", mock.SentBodies[0], "body sent verbatim");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. 4xx returns Permanent failure, no retry", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.BadRequest, @"{""error"":""bad payload""}");

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostJsonAsync(
                        "https://api.fourkites.com/v1/loads", "k", "{}", CancellationToken.None);

                    TestHarness.Assert(!response.IsSuccess, "should be failure");
                    TestHarness.AssertEqual(400, response.HttpStatusCode.Value);
                    TestHarness.AssertEqual("Permanent", response.ErrorCategory);
                }

                TestHarness.AssertEqual(1, mock.Requests.Count,
                    "4xx should NOT trigger retry — exactly 1 attempt");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. 429 returns RateLimit category", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse((HttpStatusCode)429, @"{""error"":""rate limited""}");

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostJsonAsync(
                        "https://api.fourkites.com/v1/loads", "k", "{}", CancellationToken.None);

                    TestHarness.Assert(!response.IsSuccess, "429 should be failure");
                    TestHarness.AssertEqual("RateLimit", response.ErrorCategory);
                }
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. 5xx triggers retry up to 3 attempts", async () =>
            {
                var mock = new MockHttpMessageHandler();
                // Queue three 503 responses
                mock.QueueResponse(HttpStatusCode.ServiceUnavailable);
                mock.QueueResponse(HttpStatusCode.ServiceUnavailable);
                mock.QueueResponse(HttpStatusCode.ServiceUnavailable);

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(10)))
                {
                    var response = await client.PostJsonAsync(
                        "https://api.fourkites.com/v1/loads", "k", "{}", CancellationToken.None);

                    TestHarness.Assert(!response.IsSuccess, "all 3 attempts 5xx should fail");
                    TestHarness.AssertEqual("Transient", response.ErrorCategory);
                }

                TestHarness.AssertEqual(3, mock.Requests.Count,
                    "3 attempts (initial + 2 retries) for 5xx");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. 5xx then success: retry recovers", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.ServiceUnavailable);  // attempt 1 fails
                mock.QueueResponse(HttpStatusCode.OK,
                    @"{""requestId"":""req-recovered""}");                 // attempt 2 succeeds

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(10)))
                {
                    var response = await client.PostJsonAsync(
                        "https://api.fourkites.com/v1/loads", "k", "{}", CancellationToken.None);

                    TestHarness.Assert(response.IsSuccess, "should recover via retry");
                    TestHarness.AssertEqual(200, response.HttpStatusCode.Value);
                    TestHarness.AssertEqual("req-recovered", response.VendorRequestId);
                }

                TestHarness.AssertEqual(2, mock.Requests.Count, "2 attempts (initial fail + retry success)");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. Network exception returns Transient failure (no throw)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.SetThrowException(new HttpRequestException("simulated network error"));

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostJsonAsync(
                        "https://api.fourkites.com/v1/loads", "k", "{}", CancellationToken.None);

                    TestHarness.Assert(!response.IsSuccess, "should be failure");
                    TestHarness.AssertEqual("Transient", response.ErrorCategory);
                    TestHarness.AssertContains(response.ErrorMessage, "simulated network error",
                        "error message preserved");
                }

                TestHarness.AssertEqual(3, mock.Requests.Count,
                    "network errors should retry — 3 attempts");
            }).GetAwaiter().GetResult();
        }
    }
}
