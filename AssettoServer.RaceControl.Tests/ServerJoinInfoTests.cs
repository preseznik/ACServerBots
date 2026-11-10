using AssettoServer.RaceControl.Core.Network;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class ServerJoinInfoTests
{
    [Test]
    public void LanAddress_GivesEndpointAndContentManagerLink()
    {
        var join = ServerJoinInfo.Create(" 192.168.1.10 ", 8081)!;

        Assert.Multiple(() =>
        {
            Assert.That(join.Endpoint, Is.EqualTo("192.168.1.10:8081"));
            Assert.That(join.ContentManagerLink, Is.EqualTo("acmanager://race/online/join?ip=192.168.1.10&httpPort=8081"));
            Assert.That(join.Summary, Is.EqualTo("Players join at 192.168.1.10:8081."));
            Assert.That(join.IsLoopback, Is.False);
            Assert.That(join.IsVpnOverlay, Is.False);
        });
    }

    [Test]
    public void VpnAndLoopbackAddresses_SayWhoCanJoin()
    {
        var vpn = ServerJoinInfo.Create("100.101.102.103", 8081)!;
        var loopback = ServerJoinInfo.Create("127.0.0.1", 8081)!;

        Assert.Multiple(() =>
        {
            Assert.That(vpn.IsVpnOverlay, Is.True);
            Assert.That(vpn.Summary, Does.StartWith("Players on your VPN join at 100.101.102.103:8081"));
            Assert.That(loopback.IsLoopback, Is.True);
            Assert.That(loopback.Summary, Does.StartWith("Only this PC can join"));
        });
    }

    [TestCase("0.0.0.0", 8081)]
    [TestCase("", 8081)]
    [TestCase(null, 8081)]
    [TestCase("not-an-ip", 8081)]
    [TestCase("::1", 8081)]
    [TestCase("192.168.1.10", 0)]
    [TestCase("192.168.1.10", 70000)]
    public void UnusableAddressOrPort_HasNoJoinInfo(string? address, int port)
        => Assert.That(ServerJoinInfo.Create(address, port), Is.Null);
}
