using System;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Events;

namespace Vendor.Common.Persistence.Smoke
{
    internal static class OutboundRepoTests
    {
        public static void RegisterAll()
        {
            TestHarness.RunAsync("Outbound.InsertPendingAsync returns a valid TransactionId", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LoadAssignedEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("pending"),
                    SourceSystem = "Persistence.Smoke"
                };

                var txId = await repo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT")
                                     .ConfigureAwait(false);
                TestHarness.Assert(txId > 0, $"expected positive TransactionId, got {txId}");

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status")
                                           .ConfigureAwait(false);
                TestHarness.AssertEqual("PENDING", status, "Status");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Outbound.RecordOutcomeAsync — success path sets ACK + AckUtc", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LoadAssignedEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("success"),
                    SourceSystem = "Persistence.Smoke"
                };
                var txId = await repo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT")
                                     .ConfigureAwait(false);

                var result = VendorOperationResult.Succeeded(
                    httpStatusCode: 202,
                    vendorRequestId: "req-12345",
                    requestPayloadJson: "{\"foo\":\"bar\"}",
                    responseBodyJson: "{\"status\":\"accepted\"}",
                    duration: TimeSpan.FromMilliseconds(123));

                await repo.RecordOutcomeAsync(txId, result).ConfigureAwait(false);

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("ACK", status);

                var http = await DbHelper.ReadOutboundAsync<int>(txId, "HttpStatusCode");
                TestHarness.AssertEqual(202, http);

                var reqId = await DbHelper.ReadOutboundAsync<string>(txId, "VendorRequestId");
                TestHarness.AssertEqual("req-12345", reqId);

                var reqPayload = await DbHelper.ReadOutboundAsync<string>(txId, "RequestPayload");
                TestHarness.AssertContains(reqPayload, "foo", "RequestPayload");

                var dur = await DbHelper.ReadOutboundAsync<int>(txId, "DurationMs");
                TestHarness.AssertEqual(123, dur);

                // AckUtc should be set on success
                var ackUtc = await DbHelper.ReadOutboundAsync<DateTime>(txId, "AckUtc");
                TestHarness.Assert(ackUtc > DateTime.UtcNow.AddMinutes(-5), "AckUtc populated");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Outbound.RecordOutcomeAsync — HTTP fail path", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LoadStatusEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("httpfail"),
                    SourceSystem = "Persistence.Smoke",
                    StatusType = LoadStatusType.Exception,
                    StatusTimeUtc = DateTime.UtcNow
                };
                var txId = await repo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");

                var result = VendorOperationResult.Failed(
                    "Bad request from vendor", "Permanent", httpStatusCode: 400);

                await repo.RecordOutcomeAsync(txId, result);

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("HTTP_FAIL", status);

                var cat = await DbHelper.ReadOutboundAsync<string>(txId, "ErrorCategory");
                TestHarness.AssertEqual("Permanent", cat);

                var msg = await DbHelper.ReadOutboundAsync<string>(txId, "ErrorMessage");
                TestHarness.AssertContains(msg, "Bad request", "ErrorMessage");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Outbound.RecordOutcomeAsync — transient fail", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("transient"),
                    SourceSystem = "Persistence.Smoke",
                    Latitude = "1.0", Longitude = "2.0", LocatedAtUtc = DateTime.UtcNow
                };
                var txId = await repo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");

                var result = VendorOperationResult.Failed(
                    new TimeoutException("network timeout"), "Transient");
                await repo.RecordOutcomeAsync(txId, result);

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("TRANSPORT_FAIL", status);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Outbound.RecordOutcomeAsync — rate limit", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("rate"),
                    SourceSystem = "Persistence.Smoke",
                    Latitude = "1.0", Longitude = "2.0", LocatedAtUtc = DateTime.UtcNow
                };
                var txId = await repo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");

                await repo.RecordOutcomeAsync(txId, VendorOperationResult.RateLimited());

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("RATE_LIMITED", status);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Outbound.InsertSkippedAsync — writes SKIPPED row", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new DocumentAvailableEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("skipped"),
                    SourceSystem = "Persistence.Smoke",
                    DocumentType = DocumentType.ProofOfDelivery,
                    FileName = "test.pdf"
                };

                await repo.InsertSkippedAsync(evt, "FourKites", "No active profile matched");

                var rowCount = await DbHelper.CountRowsAsync(
                    "VendorOutboundTransactions", evt.VectorLoadId);
                TestHarness.AssertEqual(1, rowCount, "one skipped row");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Outbound.UpdateStatusFromWebhookAsync flips ACK → CONFIRMED", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LoadCreatedEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("confirmed"),
                    SourceSystem = "Persistence.Smoke",
                    Mode = "TL"
                };
                var txId = await repo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await repo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));

                // Now simulate the webhook flipping it to CONFIRMED
                await repo.UpdateStatusFromWebhookAsync(
                    txId, "CONFIRMED", vendorLoadId: "FK-99999", webhookErrorsJson: null);

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("CONFIRMED", status);

                var vendorLoadId = await DbHelper.ReadOutboundAsync<string>(txId, "VendorLoadId");
                TestHarness.AssertEqual("FK-99999", vendorLoadId);

                var confirmedUtc = await DbHelper.ReadOutboundAsync<DateTime>(txId, "ConfirmedUtc");
                TestHarness.Assert(confirmedUtc > DateTime.UtcNow.AddMinutes(-5), "ConfirmedUtc set");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Outbound.UpdateStatusFromWebhookAsync is idempotent (only updates ACK/PENDING)", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LoadCreatedEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("idempotent"),
                    SourceSystem = "Persistence.Smoke",
                    Mode = "TL"
                };
                var txId = await repo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await repo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));

                // First webhook → CONFIRMED
                await repo.UpdateStatusFromWebhookAsync(txId, "CONFIRMED", "FK-FIRST", null);
                var firstConfirmedUtc = await DbHelper.ReadOutboundAsync<DateTime>(txId, "ConfirmedUtc");

                // Sleep briefly then send a "REJECTED" webhook — should be ignored because status is CONFIRMED
                await Task.Delay(50);
                await repo.UpdateStatusFromWebhookAsync(txId, "REJECTED", "FK-SECOND", "{\"err\":\"x\"}");

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("CONFIRMED", status); // not overwritten

                var vendorLoadId = await DbHelper.ReadOutboundAsync<string>(txId, "VendorLoadId");
                TestHarness.AssertEqual("FK-FIRST", vendorLoadId); // first wins
            }).GetAwaiter().GetResult();

            TestHarness.Run("Outbound.DeriveStatus mapping covers all VendorOperationResult shapes", () =>
            {
                TestHarness.AssertEqual("ACK", OutboundTransactionRepository.DeriveStatus(
                    VendorOperationResult.Succeeded(200)));
                TestHarness.AssertEqual("HTTP_FAIL", OutboundTransactionRepository.DeriveStatus(
                    VendorOperationResult.Failed("x", "Permanent", httpStatusCode: 400)));
                TestHarness.AssertEqual("TRANSPORT_FAIL", OutboundTransactionRepository.DeriveStatus(
                    VendorOperationResult.Failed(new Exception("net"), "Transient")));
                TestHarness.AssertEqual("RATE_LIMITED", OutboundTransactionRepository.DeriveStatus(
                    VendorOperationResult.RateLimited()));
                TestHarness.AssertEqual("SKIPPED", OutboundTransactionRepository.DeriveStatus(
                    VendorOperationResult.Skipped("nope")));
                TestHarness.AssertEqual("DEAD_LETTER", OutboundTransactionRepository.DeriveStatus(null));
            });

            TestHarness.RunAsync("Outbound.InsertDispatcherErrorAsync writes DEAD_LETTER row", async () =>
            {
                var repo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var evt = new LoadAssignedEvent
                {
                    VectorLoadId = DbHelper.NewTestLoadId("deadletter"),
                    SourceSystem = "Persistence.Smoke"
                };

                await repo.InsertDispatcherErrorAsync(evt, new InvalidOperationException("kaboom"));

                var rowCount = await DbHelper.CountRowsAsync(
                    "VendorOutboundTransactions", evt.VectorLoadId);
                TestHarness.AssertEqual(1, rowCount, "one dispatcher-error row");
            }).GetAwaiter().GetResult();
        }
    }
}
