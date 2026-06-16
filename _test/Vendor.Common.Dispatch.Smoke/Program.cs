using System;
using System.Threading.Tasks;

namespace Vendor.Common.Dispatch.Smoke
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Vendor.Common Dispatch Smoke Tests");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine($"Connection: {DbHelper.ConnectionString}");
            Console.WriteLine();

            try { DbHelper.VerifyConnectivityAsync().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("CANNOT CONNECT TO VendorAPI_FK:");
                Console.WriteLine($"  {ex.Message}");
                Console.ResetColor();
                return 1;
            }

            try
            {
                DbHelper.CleanupTestRowsAsync().GetAwaiter().GetResult();
                Console.WriteLine("Pre-test cleanup: complete (removed any leftover DISPATCH_SMOKE_* rows)");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Pre-test cleanup warning: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("─── A. Singleton lifecycle ───");
            SingletonLifecycleTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── B. Registry + config loading ───");
            RegistryTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── C. Dispatch happy paths ───");
            DispatchHappyPathTests.RegisterAll();
            Console.WriteLine();

            Console.WriteLine("─── D. Dispatch sad paths ───");
            DispatchSadPathTests.RegisterAll();
            Console.WriteLine();

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
