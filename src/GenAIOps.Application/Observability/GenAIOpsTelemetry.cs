using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GenAIOps.Application.Observability;

public static class GenAIOpsTelemetry
{
    public const string ActivitySourceName = "GenAIOps.Application";
    public const string MeterName = "GenAIOps.Application";

    public const string OperationTag = "genaiops.operation";
    public const string OutcomeTag = "genaiops.outcome";
    public const string PromptVersionTag = "genaiops.prompt.version";
    public const string LifecycleTag = "genaiops.lifecycle";
    public const string DispositionTag = "genaiops.disposition";
    public const string RouteTypeTag = "genaiops.route.type";
    public const string GateResultTag = "genaiops.gate.result";

    public static readonly IReadOnlySet<string> AllowedTags = new HashSet<string>(
        [
            OperationTag,
            OutcomeTag,
            PromptVersionTag,
            LifecycleTag,
            DispositionTag,
            RouteTypeTag,
            GateResultTag,
        ],
        StringComparer.Ordinal);

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "genaiops.operations",
        description: "Completed GenAIOps business operations.");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "genaiops.operation.duration",
        unit: "ms",
        description: "GenAIOps business operation duration.");
    private static readonly Counter<long> Samples = Meter.CreateCounter<long>(
        "genaiops.evaluation.samples",
        description: "Evaluation samples included in metric snapshots.");

    public static TelemetryOperation StartOperation(
        string activityName,
        string operation,
        string? promptVersion = null,
        ActivityContext? parentContext = null)
    {
        Activity? activity = parentContext.HasValue
            ? Source.StartActivity(activityName, ActivityKind.Internal, parentContext.Value)
            : Source.StartActivity(activityName, ActivityKind.Internal);
        activity?.SetTag(OperationTag, operation);
        if (!string.IsNullOrWhiteSpace(promptVersion))
        {
            activity?.SetTag(PromptVersionTag, promptVersion);
        }

        return new TelemetryOperation(activity, operation, promptVersion);
    }

    public static (string? TraceParent, string? TraceState) CapturePropagationContext()
    {
        Activity? current = Activity.Current;
        return current is null
            ? (null, null)
            : (current.Id, current.TraceStateString);
    }

    public static ActivityContext? ParsePropagationContext(
        string? traceParent,
        string? traceState) =>
        ActivityContext.TryParse(traceParent, traceState, isRemote: true, out ActivityContext context)
            ? context
            : null;

    public static void RecordSamples(string promptVersion, int count, string outcome)
    {
        TagList tags = default;
        tags.Add(OperationTag, "aggregation");
        tags.Add(OutcomeTag, outcome);
        tags.Add(PromptVersionTag, promptVersion);
        Samples.Add(count, tags);
    }

    public sealed class TelemetryOperation : IDisposable
    {
        private readonly Activity? activity;
        private readonly string operation;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private string? promptVersion;
        private bool completed;

        internal TelemetryOperation(
            Activity? activity,
            string operation,
            string? promptVersion)
        {
            this.activity = activity;
            this.operation = operation;
            this.promptVersion = promptVersion;
        }

        public Activity? Activity => activity;

        public void SetPromptVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            promptVersion = value;
            activity?.SetTag(PromptVersionTag, value);
        }

        public void Complete(
            string outcome,
            string? lifecycle = null,
            string? disposition = null,
            string? routeType = null,
            string? gateResult = null)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            stopwatch.Stop();
            TagList tags = default;
            AddTag(tags: ref tags, activity, OperationTag, operation);
            AddTag(tags: ref tags, activity, OutcomeTag, outcome);
            AddTag(tags: ref tags, activity, PromptVersionTag, promptVersion);
            AddTag(tags: ref tags, activity, LifecycleTag, lifecycle);
            AddTag(tags: ref tags, activity, DispositionTag, disposition);
            AddTag(tags: ref tags, activity, RouteTypeTag, routeType);
            AddTag(tags: ref tags, activity, GateResultTag, gateResult);
            Operations.Add(1, tags);
            Duration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
            activity?.SetStatus(IsFailure(outcome) ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }

        public void Dispose()
        {
            Complete("failure");
            activity?.Dispose();
        }

        private static bool IsFailure(string outcome) =>
            outcome is "failure" or "cancelled" or "rejected" or "poisoned";

        private static void AddTag(
            ref TagList tags,
            Activity? activity,
            string name,
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            tags.Add(name, value);
            activity?.SetTag(name, value);
        }
    }
}
