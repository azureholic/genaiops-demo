using System.Diagnostics;
using System.Text.Json;

namespace GenAIOps.Demo;

internal static class DemoProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && !args.SequenceEqual(["--json"], StringComparer.Ordinal))
        {
            Console.Error.WriteLine("Usage: dotnet run --project src\\Demo [--json]");
            return 2;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            DemoReport report = await DemoScenario.RunAsync();
            stopwatch.Stop();
            report = report with { ElapsedMilliseconds = stopwatch.ElapsedMilliseconds };
            if (args.Contains("--json", StringComparer.Ordinal))
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        report,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(
                    "GenAIOps deterministic demo (isolated in-memory fixture; no live state)");
                foreach (DemoPhase phase in report.Phases)
                {
                    Console.WriteLine(
                        $"[{phase.Number}/7] PASS {phase.Name}: {phase.Evidence}");
                }

                Console.WriteLine(
                    $"SMOKE DEMO PASS: 7/7 phases in {report.ElapsedMilliseconds} ms; "
                    + $"production={report.FinalProductionVersion}");
            }

            return 0;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Console.Error.WriteLine(
                $"SMOKE DEMO FAIL after {stopwatch.ElapsedMilliseconds} ms: {exception.Message}");
            return 1;
        }
    }
}
