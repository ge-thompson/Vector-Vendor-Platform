using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace FourKitesIntegration.Core.Persistence
{
    /// <summary>
    /// One row in FourKitesOutboundTransactions. Lightweight POCO — populated by the service,
    /// not by user code. See SqlMigrations/02_OutboundTransactions.sql for schema.
    /// </summary>
    public class OutboundTransaction
    {
        public long TransactionId { get; set; }
        public string VectorLoadId { get; set; }
        public string UpdateType { get; set; }
        public string BillToCode { get; set; }
        public string PrimaryReference { get; set; }
        public string ExpectedCallbackType { get; set; }
        public string Status { get; set; }
        public int? HttpStatusCode { get; set; }
        public string FourKitesRequestId { get; set; }
        public long? FourKitesLoadId { get; set; }
        public string RequestPayload { get; set; }
        public string ResponseBody { get; set; }
        public string WebhookErrors { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? AckUtc { get; set; }
        public DateTime? ConfirmedUtc { get; set; }
    }

    /// <summary>
    /// Status values used in OutboundTransaction.Status.
    /// </summary>
    public static class TransactionStatus
    {
        public const string Pending = "PENDING";        // logged, about to send
        public const string Ack = "ACK";                // 2xx received from FourKites
        public const string Confirmed = "CONFIRMED";    // matching webhook received
        public const string Rejected = "REJECTED";      // webhook came back with Errors / IsSuccess=false
        public const string HttpFail = "HTTP_FAIL";     // 4xx returned synchronously
        public const string TransportFail = "TRANSPORT_FAIL"; // network failed before HTTP response
        public const string DeadLetter = "DEAD_LETTER"; // exhausted retries
    }

    /// <summary>
    /// Repository for the FourKitesOutboundTransactions table. Uses raw SqlClient to keep
    /// dependencies minimal — wire in Dapper or EF if your team prefers.
    /// </summary>
    public class OutboundTransactionRepository
    {
        private readonly string _connectionString;

        public OutboundTransactionRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>Insert a new transaction row in PENDING state and return its ID.</summary>
        public async Task<long> InsertPendingAsync(OutboundTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.FourKitesOutboundTransactions
    (VectorLoadId, UpdateType, BillToCode, PrimaryReference, ExpectedCallbackType,
     Status, RequestPayload, CreatedUtc)
OUTPUT INSERTED.TransactionId
VALUES
    (@VectorLoadId, @UpdateType, @BillToCode, @PrimaryReference, @ExpectedCallbackType,
     @Status, @RequestPayload, @CreatedUtc);";
            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@VectorLoadId", (object)tx.VectorLoadId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UpdateType", (object)tx.UpdateType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BillToCode", (object)tx.BillToCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PrimaryReference", (object)tx.PrimaryReference ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ExpectedCallbackType", (object)tx.ExpectedCallbackType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", TransactionStatus.Pending);
                cmd.Parameters.AddWithValue("@RequestPayload", (object)tx.RequestPayload ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow);

                await cn.OpenAsync().ConfigureAwait(false);
                var id = (long)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                tx.TransactionId = id;
                return id;
            }
        }

        /// <summary>Update a transaction with the FourKites response.</summary>
        public async Task RecordResponseAsync(long transactionId, int httpStatusCode, string requestId,
            string responseBody, string newStatus)
        {
            const string sql = @"
UPDATE dbo.FourKitesOutboundTransactions
SET HttpStatusCode = @HttpStatusCode,
    FourKitesRequestId = @RequestId,
    ResponseBody = @ResponseBody,
    Status = @Status,
    AckUtc = @AckUtc
WHERE TransactionId = @TransactionId;";
            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@TransactionId", transactionId);
                cmd.Parameters.AddWithValue("@HttpStatusCode", httpStatusCode);
                cmd.Parameters.AddWithValue("@RequestId", (object)requestId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ResponseBody", (object)responseBody ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", newStatus);
                cmd.Parameters.AddWithValue("@AckUtc", DateTime.UtcNow);

                await cn.OpenAsync().ConfigureAwait(false);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Confirm a transaction via webhook match.</summary>
        public async Task ConfirmFromWebhookAsync(long transactionId, long? fourKitesLoadId, string webhookErrors)
        {
            const string sql = @"
UPDATE dbo.FourKitesOutboundTransactions
SET FourKitesLoadId = @FkLoadId,
    WebhookErrors = @Errors,
    Status = CASE WHEN @Errors IS NULL THEN @ConfirmedStatus ELSE @RejectedStatus END,
    ConfirmedUtc = @ConfirmedUtc
WHERE TransactionId = @TransactionId;";
            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@TransactionId", transactionId);
                cmd.Parameters.AddWithValue("@FkLoadId", (object)fourKitesLoadId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Errors", (object)webhookErrors ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ConfirmedStatus", TransactionStatus.Confirmed);
                cmd.Parameters.AddWithValue("@RejectedStatus", TransactionStatus.Rejected);
                cmd.Parameters.AddWithValue("@ConfirmedUtc", DateTime.UtcNow);

                await cn.OpenAsync().ConfigureAwait(false);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }
}
