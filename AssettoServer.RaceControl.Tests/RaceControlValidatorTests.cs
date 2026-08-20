using AssettoServer.RaceControl.Core.Validation;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class RaceControlValidatorTests
{
    [Test]
    public void Validate_GridAbovePitCapacityIsWarningNotBlocker()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(2, true, "car_one");
        var preset = factory.CreatePreset(4);

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Warning && message.Message.Contains("first 2", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Validate_BotsDisabledDoNotRequireColliderOrFastLane()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation(4, false, "car_one");
        File.Delete(Path.Combine(factory.AcRoot, "content", "cars", "car_one", "collider.kn5"));
        var preset = factory.CreatePreset(2, false);

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_RejectsPublicListener()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();
        preset.Network.BindAddress = "8.8.8.8";

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Messages, Has.Some.Property("Field").EqualTo("Network"));
    }
}
