namespace GenAIOps.Domain.Prompts;

public sealed record PromptValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
