using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vendor.FourKites.Smoke
{
    /// <summary>Tiny inline test harness. Same shape as previous smoke tests.</summary>
    internal static class TestHarness
    {
        private static readonly List<TestResult> _results = new List<TestResult>();

        public static void Run(string name, Action body)
        {
            try { body(); Pass(name); }
            catch (Exception ex) { Fail(name, ex); }
        }

        public static async Task RunAsync(string name, Func<Task> body)
        {
            try { await body().ConfigureAwait(false); Pass(name); }
            catch (Exception ex) { Fail(name, ex); }
        }

        private static void Pass(string name)
        {
            _results.Add(new TestResult(name, true, null));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[PASS] ");
            Console.ResetColor();
            Console.WriteLine(name);
        }

        private static void Fail(string name, Exception ex)
        {
            var msg = ex is AggregateException agg && agg.InnerException != null
                ? agg.InnerException.Message : ex.Message;
            _results.Add(new TestResult(name, false, msg));
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[FAIL] ");
            Console.ResetColor();
            Console.Write(name);
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($" — {msg}");
            Console.ResetColor();
        }

        public static int Summarize()
        {
            int passed = 0, failed = 0;
            foreach (var r in _results) { if (r.Passed) passed++; else failed++; }

            Console.WriteLine();
            Console.WriteLine(new string('─', 60));
            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ALL TESTS PASSED: {passed} / {passed + failed}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  FAILURES: {failed} of {passed + failed} ({passed} passed)");
            }
            Console.ResetColor();
            Console.WriteLine(new string('─', 60));
            return failed;
        }

        public static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void AssertEqual<T>(T expected, T actual, string label = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{label ?? "value"}: expected {Show(expected)}, got {Show(actual)}");
        }

        public static void AssertNotNull(object value, string label)
        {
            if (value is null) throw new Exception($"{label} was null");
        }

        public static void AssertNull(object value, string label)
        {
            if (value != null) throw new Exception($"{label} was not null: {Show(value)}");
        }

        public static void AssertContains(string haystack, string needle, string label)
        {
            if (haystack == null || !haystack.Contains(needle))
                throw new Exception($"{label}: expected to contain \"{needle}\", got {Show(haystack)}");
        }

        public static void AssertThrows<TException>(Action action, string label = null)
            where TException : Exception
        {
            try { action(); }
            catch (TException) { return; }
            catch (Exception ex)
            {
                throw new Exception(
                    $"{label ?? "action"}: expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
            }
            throw new Exception($"{label ?? "action"}: expected {typeof(TException).Name}, nothing was thrown");
        }

        private static string Show(object v)
            => v is null ? "<null>" : (v is string s ? $"\"{s}\"" : v.ToString());

        private sealed class TestResult
        {
            public string Name { get; }
            public bool Passed { get; }
            public string Error { get; }
            public TestResult(string name, bool passed, string error)
            { Name = name; Passed = passed; Error = error; }
        }
    }
}
