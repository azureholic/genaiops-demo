using GenAIOps.Demo;

namespace GenAIOps.IntegrationTests;

public sealed class EndToEndDemoTests
{
    [Fact]
    public async Task Deterministic_demo_completes_all_phases_and_restores_v2()
    {
        DemoReport report = await DemoScenario.RunAsync();

        Assert.Equal(7, report.Phases.Count);
        Assert.Equal(Enumerable.Range(1, 7), report.Phases.Select(phase => phase.Number));
        Assert.Equal("v2", report.FinalProductionVersion);
        Assert.Contains("never live operational state", report.DataSource, StringComparison.Ordinal);
        Assert.Contains(
            report.Phases,
            phase => phase.Number == 6
                && phase.Evidence.Contains("gate rejected", StringComparison.Ordinal));
        Assert.Contains(
            report.Phases,
            phase => phase.Number == 7
                && phase.Evidence.Contains("incident fixture", StringComparison.Ordinal));
    }
}
