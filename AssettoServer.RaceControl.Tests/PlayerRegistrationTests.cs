using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using AssettoServer.RaceControl.Core.Infrastructure;
using AssettoServer.RaceControl.Core.Players;
using AssettoServer.RaceControl.Core.Web;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class PlayerRegistrationTests
{
    private const string Alice = "76561198000000001";
    private const string Bob = "76561198000000002";
    private const string Carla = "76561198000000003";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);
    private static readonly string[] EventCars = ["bmw_m3_e30", "ks_mazda_mx5_cup"];

    private string _root = null!;
    private RaceControlPaths _paths = null!;

    [SetUp]
    public void CreateDataRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"registrations-{Guid.NewGuid():N}");
        _paths = new RaceControlPaths(_root);
        _paths.EnsureCreated();
    }

    [TearDown]
    public void DeleteDataRoot()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    // ---- Registration store ----

    [Test]
    public void Store_RegistersUpdatesPersistsAndWithdraws()
    {
        var store = new PlayerRegistrationStore(_paths);
        var eventId = Guid.NewGuid();
        var changes = new List<Guid>();
        store.Changed += (_, id) => changes.Add(id);

        var first = store.Register(eventId, Alice, new("Alice", "Night Owls", "bmw_m3_e30"), EventCars, Now);
        var update = store.Register(eventId, Alice, new("  Alice [NO]  ", "", ""), EventCars, Now.AddMinutes(5));
        var reloaded = new PlayerRegistrationStore(_paths).List(eventId).Single();
        bool removed = store.Remove(eventId, Alice);

        Assert.Multiple(() =>
        {
            Assert.That(first.Accepted, Is.True);
            Assert.That(update.Accepted, Is.True);
            Assert.That(reloaded.DisplayName, Is.EqualTo("Alice NO"), "brackets would break entry_list.ini");
            Assert.That(reloaded.TeamName, Is.Empty);
            Assert.That(reloaded.RegisteredAt, Is.EqualTo(Now), "updating keeps the original sign-up time");
            Assert.That(reloaded.UpdatedAt, Is.EqualTo(Now.AddMinutes(5)));
            Assert.That(removed, Is.True);
            Assert.That(store.List(eventId), Is.Empty);
            Assert.That(changes, Has.Count.EqualTo(3).And.All.EqualTo(eventId));
        });
    }

    [Test]
    public void Store_RejectsBadInput()
    {
        var store = new PlayerRegistrationStore(_paths);
        var eventId = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(store.Register(eventId, "123", new("A", "", ""), EventCars, Now).Accepted, Is.False);
            Assert.That(store.Register(eventId, Alice, new("   ", "", ""), EventCars, Now).Accepted, Is.False);
            Assert.That(store.Register(eventId, Alice, new("Alice", "", "some_other_car"), EventCars, Now).Message,
                Does.Contain("event's cars"));
            Assert.That(PlayerRegistrationStore.CleanName(new string('x', 50)), Has.Length.EqualTo(32));
            Assert.That(PlayerRegistrationStore.CleanName("a\nb;c=d\"e"), Is.EqualTo("abcde"));
            Assert.That(store.List(eventId), Is.Empty);
        });
    }

    [Test]
    public void Store_KeepsEventsApart()
    {
        var store = new PlayerRegistrationStore(_paths);
        var cup = Guid.NewGuid();
        var endurance = Guid.NewGuid();
        store.Register(cup, Alice, new("Alice", "", ""), EventCars, Now);
        store.Register(endurance, Bob, new("Bob", "", ""), EventCars, Now);

        Assert.Multiple(() =>
        {
            Assert.That(store.List(cup).Select(registration => registration.SteamId), Is.EqualTo(new[] { Alice }));
            Assert.That(store.Find(endurance, Bob)?.DisplayName, Is.EqualTo("Bob"));
            Assert.That(store.Find(endurance, Alice), Is.Null);
        });
    }

    // ---- Assigning registrations to cars ----

    [Test]
    public void Assigner_GroupsTeamsAndNamesTheCar()
    {
        var registrations = new[]
        {
            Registration(Alice, "Alice", "Night Owls", minutes: 0),
            Registration(Carla, "Carla", "", minutes: 1),
            Registration(Bob, "Bob", "night owls", minutes: 2),
        };

        var groups = RegistrationAssigner.Groups(registrations);
        var team = RegistrationAssigner.ToSlot(4, groups[0]);
        var solo = RegistrationAssigner.ToSlot(5, groups[1]);
        var expanded = RegistrationAssigner.ExpandToTeams([registrations[2]], registrations);

        Assert.Multiple(() =>
        {
            Assert.That(groups, Has.Count.EqualTo(2), "team names match ignoring case");
            Assert.That(team.SteamIds, Is.EqualTo(new[] { Alice, Bob }));
            Assert.That(team.DriverName, Is.EqualTo("Night Owls"));
            Assert.That(team.TeamName, Is.EqualTo("Night Owls"));
            Assert.That(solo.DriverName, Is.EqualTo("Carla"));
            Assert.That(solo.TeamName, Is.Empty);
            Assert.That(expanded.Single().Select(member => member.SteamId), Is.EqualTo(new[] { Alice, Bob }),
                "selecting one teammate brings the team");
        });
    }

    [Test]
    public void Assigner_AutoAssignPrefersRequestedCarAndSkipsPlacedPlayers()
    {
        var slots = new[]
        {
            new AssignableSlot(1, "bmw_m3_e30", [Carla], false),
            new AssignableSlot(2, "bmw_m3_e30", [], false),
            new AssignableSlot(3, "ks_mazda_mx5_cup", [], false),
            new AssignableSlot(4, "ks_mazda_mx5_cup", [], true),
        };
        var registrations = new[]
        {
            Registration(Alice, "Alice", "Owls", car: "ks_mazda_mx5_cup", minutes: 0),
            Registration(Bob, "Bob", "Owls", car: "ks_mazda_mx5_cup", minutes: 1),
            Registration(Carla, "Carla", "", minutes: 2),
            Registration("76561198000000004", "Dan", "", car: "ks_mazda_mx5_cup", minutes: 3),
            Registration("76561198000000005", "Eve", "", minutes: 4),
        };

        var result = RegistrationAssigner.AutoAssign(slots, registrations);

        Assert.Multiple(() =>
        {
            Assert.That(result.Assignments.Select(assignment => assignment.SlotIndex), Is.EqualTo(new[] { 3, 2 }));
            Assert.That(result.Assignments[0].SteamIds, Is.EqualTo(new[] { Alice, Bob }), "the team gets the MX-5 they asked for");
            Assert.That(result.Assignments[1].DriverName, Is.EqualTo("Dan"), "no MX-5 left, so the next free row");
            Assert.That(result.Unplaced.Single().Single().DisplayName, Is.EqualTo("Eve"), "spectator rows are never used");
        });
    }

    // ---- Steam sign-in, sessions and roles ----

    [Test]
    public void Roles_HostPcIsAdminListedIdsAreAdminOthersArePlayersOrVisitors()
    {
        var lan = IPAddress.Parse("192.168.1.30");
        Assert.Multiple(() =>
        {
            Assert.That(WebAccessPolicy.Resolve(IPAddress.Loopback, null, []), Is.EqualTo(WebRole.Admin));
            Assert.That(WebAccessPolicy.Resolve(lan, null, [Alice]), Is.EqualTo(WebRole.Visitor));
            Assert.That(WebAccessPolicy.Resolve(lan, Bob, [Alice]), Is.EqualTo(WebRole.Player));
            Assert.That(WebAccessPolicy.Resolve(lan, Alice, [Alice]), Is.EqualTo(WebRole.Admin));
        });
    }

    [Test]
    public void Sessions_ExpireAndSignInStateAndNoncesWorkOnce()
    {
        var clock = new ManualClock(Now);
        var sessions = new WebSessionStore(clock);
        var session = sessions.Create(Alice);
        string state = sessions.BeginSignIn();

        Assert.Multiple(() =>
        {
            Assert.That(sessions.Find(session.Id)?.SteamId, Is.EqualTo(Alice));
            Assert.That(session.CsrfToken, Is.Not.EqualTo(session.Id));
            Assert.That(sessions.CompleteSignIn(state), Is.True);
            Assert.That(sessions.CompleteSignIn(state), Is.False, "a sign-in state is single use");
            Assert.That(sessions.TryUseNonce("2026-11-10T18:00:00Zabc"), Is.True);
            Assert.That(sessions.TryUseNonce("2026-11-10T18:00:00Zabc"), Is.False, "a replayed Steam answer is refused");
        });

        clock.Advance(WebSessionStore.SessionLifetime + TimeSpan.FromMinutes(1));
        Assert.That(sessions.Find(session.Id), Is.Null);
    }

    [Test]
    public void SteamAssertion_AcceptsOnlySteamIdentitiesReturnedHere()
    {
        const string callback = "http://192.168.1.10:8772/auth/steam/callback";
        var genuine = SteamAnswer(callback + "?state=abc", Alice);

        Assert.Multiple(() =>
        {
            Assert.That(SteamOpenId.CheckAssertion(genuine, callback), Is.EqualTo(Alice));
            Assert.That(SteamOpenId.CheckAssertion(genuine, "http://192.168.1.99:8772/auth/steam/callback"), Is.Null,
                "an answer meant for another site is refused");
            Assert.That(SteamOpenId.CheckAssertion(With(genuine, "openid.op_endpoint", "https://evil.example/openid"), callback), Is.Null);
            Assert.That(SteamOpenId.CheckAssertion(With(genuine, "openid.claimed_id", "https://evil.example/id/76561198000000001"), callback), Is.Null);
            Assert.That(SteamOpenId.CheckAssertion(With(genuine, "openid.mode", "cancel"), callback), Is.Null);
            Assert.That(SteamOpenId.BuildLoginUrl(callback, "http://192.168.1.10:8772/"),
                Does.StartWith(SteamOpenId.Endpoint + "?").And.Contain("openid.mode=checkid_setup"));
        });
    }

    [Test]
    public async Task SteamVerification_TrustsOnlySteamsOwnConfirmation()
    {
        const string callback = "http://127.0.0.1:8772/auth/steam/callback";
        var answer = SteamAnswer(callback + "?state=abc", Alice);
        var valid = new SteamOpenId(new HttpClient(new FakeSteam("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")));
        var invalid = new SteamOpenId(new HttpClient(new FakeSteam("ns:http://specs.openid.net/auth/2.0\nis_valid:false\n")));
        var offline = new SteamOpenId(new HttpClient(new FakeSteam(null)));

        Assert.Multiple(async () =>
        {
            Assert.That(await valid.VerifyAsync(answer, callback), Is.EqualTo(Alice));
            Assert.That(await invalid.VerifyAsync(answer, callback), Is.Null);
            Assert.That(await offline.VerifyAsync(answer, callback), Is.Null);
        });
    }

    // ---- Web GUI endpoints ----

    [Test]
    public async Task WebGui_SteamSignInThenRegisterAndWithdraw()
    {
        var store = new PlayerRegistrationStore(_paths);
        var control = new EventControl(Guid.NewGuid());
        int port = ReserveLoopbackPort();
        await using var server = new RaceControlWebServer(new RaceControlWebServerOptions(true, "127.0.0.1", port),
            _paths, control, registrations: store,
            steamHttp: new HttpClient(new FakeSteam("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")));
        await server.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(server.BrowserUrl),
        };

        // Not signed in: registering is refused.
        var anonymous = await client.PostAsJsonAsync("api/v1/registration", new PlayerRegistrationRequest("Alice", "", ""));

        // Start the Steam sign-in and come back with a (fake-confirmed) answer.
        var login = await client.GetAsync("auth/steam/login");
        string state = CookieValue(login, "asrc_signin");
        string origin = $"http://127.0.0.1:{port}";
        var answer = SteamAnswer($"{origin}/auth/steam/callback?state={state}", Alice);
        string query = string.Join("&", answer.Append(new("state", state))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, $"auth/steam/callback?{query}");
        callbackRequest.Headers.Add("Cookie", $"asrc_signin={state}");
        var callback = await client.SendAsync(callbackRequest);
        string sessionId = CookieValue(callback, "asrc_session");

        using var replayRequest = new HttpRequestMessage(HttpMethod.Get, $"auth/steam/callback?{query}");
        replayRequest.Headers.Add("Cookie", $"asrc_signin={state}");
        var replay = await client.SendAsync(replayRequest);

        client.DefaultRequestHeaders.Add("Cookie", $"asrc_session={sessionId}");
        using var me = JsonDocument.Parse(await client.GetStringAsync("api/v1/me"));
        string sessionToken = me.RootElement.GetProperty("sessionToken").GetString()!;

        var withoutToken = await client.PostAsJsonAsync("api/v1/registration", new PlayerRegistrationRequest("Alice", "Owls", "bmw_m3_e30"));
        client.DefaultRequestHeaders.Add("X-ASRC-Session", sessionToken);
        var registered = await client.PostAsJsonAsync("api/v1/registration", new PlayerRegistrationRequest("Alice", "Owls", "bmw_m3_e30"));
        using var eventView = JsonDocument.Parse(await client.GetStringAsync("api/v1/event"));
        control.Info = control.Info with { IsOpen = false };
        var closed = await client.PostAsJsonAsync("api/v1/registration", new PlayerRegistrationRequest("Alice", "", ""));
        var withdrawn = await client.DeleteAsync("api/v1/registration");

        Assert.Multiple(() =>
        {
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(login.Headers.Location!.ToString(), Does.StartWith(SteamOpenId.Endpoint));
            Assert.That(Uri.UnescapeDataString(login.Headers.Location!.ToString()), Does.Contain($"{origin}/auth/steam/callback?state={state}"));
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/#players"));
            Assert.That(replay.Headers.Location!.ToString(), Does.Contain("signin=expired"), "a sign-in can't be replayed");
            Assert.That(me.RootElement.GetProperty("signedIn").GetBoolean(), Is.True);
            Assert.That(me.RootElement.GetProperty("steamId").GetString(), Is.EqualTo(Alice));
            Assert.That(withoutToken.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "player requests need the session token");
            Assert.That(registered.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(eventView.RootElement.GetProperty("registeredCount").GetInt32(), Is.EqualTo(1));
            var row = eventView.RootElement.GetProperty("roster")[0];
            Assert.That(row.GetProperty("isYou").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("preferredCar").GetString(), Is.EqualTo("BMW M3 E30"));
            Assert.That(row.GetProperty("steamId").GetString(), Is.EqualTo(Alice), "the host PC is admin and sees Steam IDs");
            Assert.That(closed.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(withdrawn.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(store.List(control.Info.PresetId), Is.Empty);
        });
    }

    [Test]
    public async Task WebGui_SignInFromAnUnknownHostNameIsRefused()
    {
        int port = ReserveLoopbackPort();
        await using var server = new RaceControlWebServer(new RaceControlWebServerOptions(true, "127.0.0.1", port),
            _paths, new EventControl(Guid.NewGuid()), registrations: new PlayerRegistrationStore(_paths));
        await server.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(server.BrowserUrl) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/steam/login");
        request.Headers.Host = $"evil.example:{port}";

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // ---- Helpers ----

    private static PlayerRegistration Registration(string steamId, string name, string team, string car = "", int minutes = 0)
        => new()
        {
            SteamId = steamId,
            DisplayName = name,
            TeamName = team,
            PreferredCarId = car,
            RegisteredAt = Now.AddMinutes(minutes),
        };

    private static Dictionary<string, string> SteamAnswer(string returnTo, string steamId) => new()
    {
        ["openid.ns"] = "http://specs.openid.net/auth/2.0",
        ["openid.mode"] = "id_res",
        ["openid.op_endpoint"] = SteamOpenId.Endpoint,
        ["openid.claimed_id"] = $"https://steamcommunity.com/openid/id/{steamId}",
        ["openid.identity"] = $"https://steamcommunity.com/openid/id/{steamId}",
        ["openid.return_to"] = returnTo,
        ["openid.response_nonce"] = $"2026-11-10T18:00:00Z{Guid.NewGuid():N}",
        ["openid.assoc_handle"] = "1234567890",
        ["openid.signed"] = "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
        ["openid.sig"] = "c2lnbmF0dXJl",
    };

    private static Dictionary<string, string> With(Dictionary<string, string> source, string key, string value)
        => new(source) { [key] = value };

    private static string CookieValue(HttpResponseMessage response, string name)
    {
        string header = response.Headers.GetValues("Set-Cookie").First(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return header[(name.Length + 1)..].Split(';')[0];
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Stands in for steamcommunity.com's check_authentication; null simulates no internet.</summary>
    private sealed class FakeSteam(string? body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (body is null)
                throw new HttpRequestException("offline");
            Assert.That(request.RequestUri!.ToString(), Is.EqualTo(SteamOpenId.Endpoint));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class EventControl(Guid presetId) : IRaceControlWebControl
    {
        public PlayerEventInfo Info { get; set; } = new(presetId, "Sunday Endurance", "Vallelunga", "Club", true,
            Now.AddDays(3), "Two-hour race, 30 min stints.",
            [new PlayerEventCar("bmw_m3_e30", "BMW M3 E30"), new PlayerEventCar("ks_mazda_mx5_cup", "Mazda MX-5 Cup")], true);

        public PlayerEventInfo? GetPlayerEvent() => Info;

        public RaceControlWebControlState GetState() => new(
            "Sunday Endurance", "Server", "RACING", "RACE", "vallelunga", "club", "",
            "OFFLINE", "Ready", false, true, false, false, false, false, false);

        public Task<RaceControlWebActionResult> ExecuteAsync(RaceControlWebAction action,
            CancellationToken cancellationToken = default) => Task.FromResult(RaceControlWebActionResult.Success("ok"));

        public Task<RaceControlWebActionResult> SetEnvironmentAsync(RaceControlWebEnvironmentRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(RaceControlWebActionResult.Success("ok"));
    }
}
