using System;
using System.Collections.Generic;

namespace Vendor.Common.Smoke
{
    /// <summary>
    /// Tiny inline test harness. No xUnit dependency — keeps the test app simple.
    /// Each test is a delegate; runner catches exceptions and reports pass/fail.
    /// </summary>
    internal static class TestHarness
    {
        private static readonly List<TestResult> _results = new List<TestResult>();

        public static void Run(string name, Action body)
        {
            try
            {
                body();
                _results.Add(new TestResult(name, true, null));
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[PASS] ");
                Console.ResetColor();
                Console.WriteLine(name);
            }
            catch (Exception ex)
            {
                _results.Add(new TestResult(name, false, ex.Message));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("[FAIL] ");
                Console.ResetColor();
                Console.Write(name);
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($" — {ex.Message}");
                Console.ResetColor();
            }
        }

        public static int Summarize()
        {
            int passed = 0, failed = 0;
            foreach (var r in _results)
            {
                if (r.Passed) passed++; else failed++;
            }

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
                throw new Exception(
                    $"{label ?? "value"}: expected {Show(expected)}, got {Show(actual)}");
        }

        public static void AssertNotNull(object value, string label)
        {
            if (value is null) throw new Exception($"{label} was null");
        }

        public static void AssertNull(object value, string label)
        {
            if (value != null) throw new Exception($"{label} was not null: {Show(value)}");
        }

        private static string Show(object v)
            => v is null ? "<null>" : (v is string s ? $"\"{s}\"" : v.ToString());

        private sealed class TestResult
        {
            public string Name { get; }
            public bool Passed { get; }
            public string Error { get; }
            public TestResult(string name, bool passed, string error)
            {
                Name = name; Passed = passed; Error = error;
            }
        }
    }
}
