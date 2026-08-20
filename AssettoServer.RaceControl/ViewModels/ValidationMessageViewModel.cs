using AssettoServer.RaceControl.Core.Validation;

namespace AssettoServer.RaceControl.ViewModels;

public sealed record ValidationMessageViewModel(ValidationSeverity Severity, string Field, string Message)
{
    public string Symbol => Severity switch
    {
        ValidationSeverity.Error => "✕",
        ValidationSeverity.Warning => "!",
        _ => "i",
    };
}
