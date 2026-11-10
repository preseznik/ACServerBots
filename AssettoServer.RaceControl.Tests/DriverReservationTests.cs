using AssettoServer.RaceControl.Core.Configuration;
using AssettoServer.RaceControl.Core.Models;
using AssettoServer.RaceControl.Core.Validation;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class DriverReservationTests
{
    private const string Alice = "76561198000000001";
    private const string Bob = "76561198000000002";

    [Test]
    public void SteamIds_ParseAcceptsCommonSeparatorsAndDropsDuplicates()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SteamIds.Parse($" {Alice};{Bob}, {Alice}\n"), Is.EqualTo(new[] { Alice, Bob }));
            Assert.That(SteamIds.Parse(null), Is.Empty);
            Assert.That(SteamIds.Format([Alice, Bob]), Is.EqualTo($"{Alice}; {Bob}"));
            Assert.That(SteamIds.IsValid(Alice), Is.True);
            Assert.That(SteamIds.IsValid("12345"), Is.False);
            Assert.That(SteamIds.IsValid("7656119800000000x"), Is.False);
        });
    }

    [Test]
    public void Render_TeamCarIsReservedHumanOnlyAndGraceIsStaged()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset(3);
        preset.Grid[1].DriverSteamIds = [Alice, Bob];
        preset.Sessions.DriverSwapGraceMinutes = 5;

        var rendered = new ServerConfigurationRenderer().Render(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.EntryList.Get("CAR_1", "GUID"), Is.EqualTo($"{Alice};{Bob}"));
            Assert.That(rendered.EntryList.Get("CAR_1", "AI"), Is.EqualTo("none"));
            Assert.That(rendered.EntryList.Get("CAR_0", "GUID"), Is.Empty);
            Assert.That(rendered.EntryList.Get("CAR_0", "AI"), Is.EqualTo("auto"));
            Assert.That(rendered.ExtraConfiguration, Does.Contain("DriverSwapGraceSeconds: 300"));
            Assert.That(preset.Grid[1].Mode, Is.EqualTo(SlotMode.Auto), "the saved slot mode is kept");
        });
    }

    [Test]
    public void Render_GraceIsATopLevelKeyOutsideTheNestedAiBlock()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset();

        var lines = new ServerConfigurationRenderer().Render(preset, factory.Scan()).ExtraConfiguration
            .Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        int grace = lines.FindIndex(line => line.StartsWith("DriverSwapGraceSeconds:", StringComparison.Ordinal));
        int aiParams = lines.IndexOf("AiParams:");

        Assert.That(grace, Is.GreaterThanOrEqualTo(0).And.LessThan(aiParams));
    }

    [Test]
    public void Validate_RejectsMalformedSteamIdsAndReportsTeamCars()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset(3);
        preset.Grid[0].DriverSteamIds = [Alice, Bob];
        preset.Grid[1].DriverSteamIds = ["not-a-steam-id"];

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Error && message.Message.Contains("not-a-steam-id", StringComparison.Ordinal)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Information && message.Message.Contains("driver changes within 5 min", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Validate_WarnsWhenTeamCarsHaveNoDriverChangeTimeOrShareADriver()
    {
        using var factory = new TestContentFactory();
        factory.CreateInstallation();
        var preset = factory.CreatePreset(3);
        preset.Grid[0].DriverSteamIds = [Alice, Bob];
        preset.Grid[1].DriverSteamIds = [Bob];
        preset.Sessions.DriverSwapGraceMinutes = 0;

        var result = new RaceControlValidator().Validate(preset, factory.Scan());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Warning && message.Message.Contains("driver change time is 0", StringComparison.Ordinal)));
            Assert.That(result.Messages, Has.Some.Matches<ValidationMessage>(message =>
                message.Severity == ValidationSeverity.Warning && message.Message.Contains(Bob, StringComparison.Ordinal)));
        });
    }
}
