using System;
using System.Configuration;
using System.Data.SqlClient;

namespace Vendor.FourKites.Smoke
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Vendor.FourKites Smoke Tests");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine();

            // Some tests need DB connectivity (webhook processor matching).
            // Others don't (config parsing, mapping, payload building, rate limiter,
            // client+adapter via mock handler). Verify DB if connection string is set,
            // but don't bail if it's not — the non-DB tests still run.
            var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
            if (!string.IsNullOrWhiteSpace(connStr))
            {
                try
                {
                    using (var cn = new SqlConnection(connStr))
                    {
                        cn.Open();
                    }
                    Console.WriteLine($"DB available: {connStr}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"WARNING: DB unreachable — webhook DB tests will fail.");
                    Console.WriteLine($"  {ex.Message}");
                    Console.ResetColor();
                }
            }
            Console.WriteLine();

            Console.WriteLine("─── FourKitesConfig parsing ───");
            ConfigTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── Mapping (LoadStatusMapper + PayloadBuilder) ───");
            MappingTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── InMemoryRateLimiter ───");
            RateLimiterTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── FourKitesClient (mock HTTP) ───");
            ClientTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── FourKitesAdapter (mock HTTP) ───");
            AdapterTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── Webhook validator + processor ───");
            WebhookTests.RegisterAll();
            Console.WriteLine();

            return TestHarness.Summarize();
        }
    }
}
