using System;
using System.Threading.Tasks;

namespace Vendor.Common.Persistence.Smoke
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Vendor.Common Persistence Smoke Tests");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine($"Connection: {DbHelper.ConnectionString}");
            Console.WriteLine();

            // Verify connectivity first — if this fails, all other tests would fail with
            // the same error. Print a friendly message instead.
            try
            {
                DbHelper.VerifyConnectivityAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("CANNOT CONNECT TO VendorAPI_FK:");
                Console.WriteLine($"  {ex.Message}");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Things to check:");
                Console.WriteLine("  1. Has the database been deployed? Run scripts in _deliverables\\07_SQL_Schema\\scripts\\");
                Console.WriteLine("  2. Is LocalDB running? Try: sqllocaldb info");
                Console.WriteLine("  3. Is the connection string in App.config correct?");
                return 1;
            }

            // Pre-run cleanup: remove leftover SMOKE_ rows from prior runs
            try
            {
                DbHelper.CleanupTestRowsAsync().GetAwaiter().GetResult();
                Console.WriteLine("Pre-test cleanup: complete (removed any leftover SMOKE_* rows)");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Pre-test cleanup warning: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("─── ClientProfileRepository tests ───");
            ClientProfileRepoTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── OutboundTransactionRepository tests ───");
            OutboundRepoTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── InboundCallbackRepository tests ───");
            InboundRepoTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── Stored procedure tests ───");
            ProcTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── WebhookCorrelator tests ───");
            WebhookCorrelatorTests.RegisterAll();
            Console.WriteLine();

            // Post-run cleanup
            try
            {
                DbHelper.CleanupTestRowsAsync().GetAwaiter().GetResult();
                Console.WriteLine("Post-test cleanup: complete");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Post-test cleanup warning: {ex.Message}");
                Console.ResetColor();
            }

            return TestHarness.Summarize();
        }
    }
}
