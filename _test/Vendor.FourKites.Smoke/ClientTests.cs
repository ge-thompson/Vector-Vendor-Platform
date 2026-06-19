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
                    @"{""requestId"":""req-xyz"",""loadId"":420645495}");

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PostJsonAsync(
                        "https://api-staging.fourkites.com/api/v1/tracking",
                        "test-key",
                        @"{""load"":{""loadNumber"":""LOAD1""}}",
                        CancellationToken.None);

                    TestHarness.Assert(response.IsSuccess, "should be success");
                    TestHarness.AssertEqual(202, response.HttpStatusCode.Value, "status");
                    TestHarness.AssertEqual("req-xyz", response.VendorRequestId, "VendorRequestId extracted");
                    TestHarness.AssertEqual("420645495", response.VendorLoadId,
                        "VendorLoadId extracted from 'loadId' (FK actual field name)");
                }
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. Sets correct headers on outgoing request", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.OK);

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    await client.PostJsonAsync(
                        "https://api-staging.fourkites.com/api/v1/tracking",
                        "my-api-key-123",
                        @"{""foo"":""bar""}",
                        CancellationToken.None);
                }

                TestHarness.AssertEqual(1, mock.Requests.Count, "exactly 1 request sent");
                var req = mock.Requests[0];

                TestHarness.AssertEqual(HttpMethod.Post, req.Method, "POST method");
                TestHarness.AssertEqual("https://api-staging.fourkites.com/api/v1/tracking", req.RequestUri.ToString());

                // FK spec: header is lowercase 'apikey', raw key, no prefix
                TestHarness.Assert(req.Headers.Contains("apikey"), "'apikey' header present (lowercase)");
                var apiKeyValues = req.Headers.GetValues("apikey");
                foreach (var v in apiKeyValues)
                {
                    TestHarness.AssertEqual("my-api-key-123", v, "raw apikey value");
                    break;
                }

                // Pre-rewrite header is gone
                TestHarness.Assert(!req.Headers.Contains("X-FK-API-Key"),
                    "no X-FK-API-Key (the old pre-rewrite name)");

                TestHarness.AssertEqual(@"{""foo"":""bar""}", mock.SentBodies[0], "body sent verbatim");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Client. PATCH method works (used for FK Load Update)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.OK,
                    @"{""message"":""Load updated successfully"",""requestId"":""upd-1"",""statusCode"":200}");

                using (var client = new FourKitesClient(mock, TimeSpan.FromSeconds(5)))
                {
                    var response = await client.PatchJsonAsync(
                        "https://api-staging.fourkites.com/api/v1/tracking/420645495",
                        "test-key",
                        @"{""simpleUpdate"":false,""load"":{}}",
                        CancellationToken.None);

                    TestHarness.Assert(response.IsSuccess, "PATCH success");
                    TestHarness.AssertEqual("upd-1", response.VendorRequestId);
                }

                var req = mock.Requests[0];
                TestHarness.AssertEqual("PATCH", req.Method.Method, "PATCH verb");
                TestHarness.AssertContains(req.RequestUri.ToString(), "/420645495",
                    "FK loadId in URL path");
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
