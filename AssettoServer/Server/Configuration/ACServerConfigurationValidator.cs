using FluentValidation;
using System.Linq;

namespace AssettoServer.Server.Configuration;

public class ACServerConfigurationValidator : AbstractValidator<ACServerConfiguration>
{
    public ACServerConfigurationValidator()
    {
        RuleFor(cfg => cfg.Extra).ChildRules(extra =>
        {
            extra.RuleFor(x => x.UseSteamAuth)
                .NotEqual(true)
                .When(x => x.EnableACProSupport)
                .WithMessage("Can't use SteamAuth with ACPro support enabled");
            extra.RuleFor(x => x.ValidateDlcOwnership).NotNull();
            extra.RuleFor(x => x.NetworkBindAddress)
                .Must(PrivateNetworkAddress.IsValid)
                .WithMessage("NetworkBindAddress must be an IPv4 address");
            extra.RuleFor(x => x.NetworkBindAddress)
                .Must(PrivateNetworkAddress.IsPrivateIpv4)
                .When(x => x.AiParams.Behavior == AssettoServer.Server.Configuration.Extra.AiBehaviorMode.Race)
                .WithMessage("Race bot V1 is LAN-only; NetworkBindAddress must be a private IPv4 address");
            extra.RuleFor(x => x.ServerDescription).NotNull();
            extra.RuleFor(x => x.RainTrackGripReductionPercent).InclusiveBetween(0, 0.5);
            extra.RuleFor(x => x.IgnoreConfigurationErrors).NotNull();
            extra.RuleFor(x => x.UserGroups).NotNull();
            extra.RuleFor(x => x.BlacklistUserGroup).NotEmpty();
            extra.RuleFor(x => x.WhitelistUserGroup).NotEmpty();
            extra.RuleFor(x => x.AdminUserGroup).NotEmpty();
            extra.RuleFor(x => x.VoteKickMinimumConnectedPlayers).GreaterThanOrEqualTo((ushort)3);

            extra.RuleFor(x => x.AiParams).ChildRules(aiParams =>
            {
                aiParams.RuleFor(ai => ai.MinSpawnDistancePoints).LessThanOrEqualTo(ai => ai.MaxSpawnDistancePoints);
                aiParams.RuleFor(ai => ai.MinAiSafetyDistanceMeters).LessThanOrEqualTo(ai => ai.MaxAiSafetyDistanceMeters);
                aiParams.RuleFor(ai => ai.MinSpawnProtectionTimeSeconds).LessThanOrEqualTo(ai => ai.MaxSpawnProtectionTimeSeconds);
                aiParams.RuleFor(ai => ai.MinCollisionStopTimeSeconds).LessThanOrEqualTo(ai => ai.MaxCollisionStopTimeSeconds);
                aiParams.RuleFor(ai => ai.MaxSpeedVariationPercent).InclusiveBetween(0, 1);
                aiParams.RuleFor(ai => ai.DefaultAcceleration).GreaterThan(0);
                aiParams.RuleFor(ai => ai.DefaultDeceleration).GreaterThan(0);
                aiParams.RuleFor(ai => ai.NamePrefix).NotNull();
                aiParams.RuleFor(ai => ai.Race).NotNull();
                aiParams.RuleFor(ai => ai.Race.Difficulty).InclusiveBetween(0, 1);
                aiParams.RuleFor(ai => ai.Race.Aggression).InclusiveBetween(0, 1);
                aiParams.RuleFor(ai => ai.Race.StartSplinePointId).GreaterThanOrEqualTo(0);
                aiParams.RuleFor(ai => ai.Race.GridSpacingMeters).GreaterThan(0);
                aiParams.RuleFor(ai => ai.Race.UpdateHz).InclusiveBetween(10, 120);
                aiParams.RuleFor(ai => ai.HideAiCars)
                    .Equal(false)
                    .When(ai => ai.Behavior == AssettoServer.Server.Configuration.Extra.AiBehaviorMode.Race)
                    .WithMessage("Race bots must remain visible to clients; set HideAiCars to false");
                aiParams.RuleFor(ai => ai.HourlyTrafficDensity)
                    .Null()
                    .When(ai => ai.Behavior == AssettoServer.Server.Configuration.Extra.AiBehaviorMode.Race)
                    .WithMessage("HourlyTrafficDensity is a traffic-only setting and cannot be combined with race bots");
                aiParams.RuleFor(ai => ai.IgnoreObstaclesAfterSeconds).GreaterThanOrEqualTo(0);
                aiParams.RuleFor(ai => ai.HourlyTrafficDensity)
                    .Must(htd => htd?.Count == 24)
                    .When(ai => ai.HourlyTrafficDensity != null)
                    .WithMessage("HourlyTrafficDensity must have exactly 24 entries");
                aiParams.RuleFor(ai => ai.CarSpecificOverrides).NotNull();
                aiParams.RuleFor(ai => ai.AiBehaviorUpdateIntervalHz).GreaterThan(0);
                aiParams.RuleFor(ai => ai.LaneCountSpecificOverrides).NotNull();
                aiParams.RuleForEach(ai => ai.LaneCountSpecificOverrides).ChildRules(overrides =>
                {
                    overrides.RuleFor(o => o.Key).GreaterThan(0);
                    overrides.RuleFor(o => o.Value.MinAiSafetyDistanceMeters).LessThanOrEqualTo(o => o.Value.MaxAiSafetyDistanceMeters);
                });
            });
        });

        RuleFor(cfg => cfg.Server).ChildRules(server =>
        {
            server.RuleFor(s => s.Track).NotEmpty();
            server.RuleFor(s => s.TrackConfig).NotNull();
            server.RuleFor(s => s.FuelConsumptionRate).GreaterThanOrEqualTo(0);
            server.RuleFor(s => s.MechanicalDamageRate).GreaterThanOrEqualTo(0);
            server.RuleFor(s => s.TyreConsumptionRate).GreaterThanOrEqualTo(0);
            server.RuleFor(s => s.InvertedGridPositions).GreaterThanOrEqualTo((short)0);
            server.RuleFor(s => s.LegalTyres).NotNull();
            server.RuleFor(s => s.WelcomeMessagePath).NotNull();
            server.RuleFor(s => s.TimeOfDayMultiplier).GreaterThanOrEqualTo(0);
            server.RuleFor(s => s.KickQuorum).InclusiveBetween((ushort)0, (ushort)90);
            server.RuleFor(s => s.VotingQuorum).InclusiveBetween((ushort)0, (ushort)100);
            server.RuleFor(s => s.QualifyMaxWait).GreaterThanOrEqualTo((ushort)100);
            server.RuleFor(s => s.ResultScreenTime).GreaterThanOrEqualTo(1);

            server.RuleForEach(s => s.Weathers).ChildRules(weather =>
            {
                weather.RuleFor(w => w.BaseTemperatureAmbient).GreaterThanOrEqualTo(0);
                weather.RuleFor(w => w.WindBaseSpeedMin).LessThanOrEqualTo(w => w.WindBaseSpeedMax);
            });

            server.RuleFor(s => s.DynamicTrack).NotNull().ChildRules(dynTrack =>
            {
                dynTrack.RuleFor(d => d.StartGrip).InclusiveBetween(0, 1);
                dynTrack.RuleFor(d => d.SessionTransfer).InclusiveBetween(0, 1);
            });
        });

        RuleFor(cfg => cfg.EntryList).ChildRules(entryList =>
        {
            entryList.RuleForEach(el => el.Cars).ChildRules(car =>
            {
                car.RuleFor(c => c.Model).NotNull();
                car.RuleFor(c => c.Guid).NotNull();
                car.RuleFor(c => c.Restrictor).InclusiveBetween(0, 400);
                car.RuleFor(c => c.Ballast).GreaterThanOrEqualTo(0);
            });
        });

        RuleFor(cfg => cfg.Server.Race)
            .Must(race => race?.IsOpen == Kunos.IsOpenMode.Open)
            .When(cfg => cfg.Extra.AiParams is
                { Behavior: AssettoServer.Server.Configuration.Extra.AiBehaviorMode.Race, Race.AllowMidRaceBotTakeover: true })
            .WithMessage("Mid-race bot takeover requires RACE IS_OPEN=1 so Content Manager clients can join");
        RuleFor(cfg => cfg.EntryList.Cars)
            .Must(cars => cars.Any(car => car.AiMode == AiMode.Auto))
            .When(cfg => cfg.Extra.AiParams is
                { Behavior: AssettoServer.Server.Configuration.Extra.AiBehaviorMode.Race, Race.AllowMidRaceBotTakeover: true })
            .WithMessage("Mid-race bot takeover requires at least one AI=auto entry-list slot");
    }
}
