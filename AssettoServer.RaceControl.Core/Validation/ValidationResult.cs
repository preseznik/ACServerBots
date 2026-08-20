namespace AssettoServer.RaceControl.Core.Validation;

public enum ValidationSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record ValidationMessage(ValidationSeverity Severity, string Field, string Message);

public sealed class ValidationResult
{
    public ValidationResult(IEnumerable<ValidationMessage> messages) => Messages = messages.ToArray();

    public IReadOnlyList<ValidationMessage> Messages { get; }
    public bool IsValid => Messages.All(message => message.Severity != ValidationSeverity.Error);
    public int ErrorCount => Messages.Count(message => message.Severity == ValidationSeverity.Error);
    public int WarningCount => Messages.Count(message => message.Severity == ValidationSeverity.Warning);
}
