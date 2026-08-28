using System.Drawing;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Server.Fps;
// ReSharper disable InconsistentNaming

namespace AssettoServer.Tests;

// Based on https://github.com/ac-custom-shaders-patch/acc-lua-sdk/blob/ca9530fbb5c81d0c23c4c1ba7a8f198870d2b2a3/tests/test_struct_item.lua
public class OnlineEventGeneratorTests
{
    [Test]
    public void Test_Minimal()
    {
        var testMessage1 = OnlineEventGenerator.ParseClientMessage(typeof(TestMessage1));
        Assert.That(OnlineEventGenerator.GenerateStructure(testMessage1.Key, testMessage1.Fields), Is.EqualTo("int i00;int i01;"));
        
        var testMessage2 = OnlineEventGenerator.ParseClientMessage(typeof(TestMessage2));
        Assert.That(OnlineEventGenerator.GenerateStructure(testMessage2.Key, testMessage2.Fields), Is.EqualTo("double i10;int i11;"));

        var testMessage3 = OnlineEventGenerator.ParseClientMessage(typeof(TestMessage3));
        Assert.That(OnlineEventGenerator.GenerateStructure(testMessage3.Key, testMessage3.Fields), Is.EqualTo("double i23;int i22;uint8_t i21;char i20[20];"));

        var testMessage4 = OnlineEventGenerator.ParseClientMessage(typeof(TestMessage4));
        Assert.That(OnlineEventGenerator.GenerateStructure(testMessage4.Key, testMessage4.Fields), Is.EqualTo("double i23;int i22[4];uint8_t i21;char i20[20];"));
        
        var testMessage5 = OnlineEventGenerator.ParseClientMessage(typeof(TestMessage5));
        Assert.That(OnlineEventGenerator.GenerateStructure(testMessage5.Key, testMessage5.Fields), Is.EqualTo("rgbm i51;float i50;"));
    }

    [Test]
    public void FpsProtocol_ProducesDistinctCspCompatibleMessageDefinitions()
    {
        Type[] packets =
        [
            typeof(FpsInputPacket), typeof(FpsReadyPacket), typeof(FpsSnapshotPacket),
            typeof(FpsRosterPacket), typeof(FpsMatchPacket), typeof(FpsKillPacket), typeof(FpsHitPacket),
            typeof(FpsShotPacket), typeof(FpsClientDiagnosticPacket),
        ];
        var definitions = packets.Select(OnlineEventGenerator.ParseClientMessage).ToArray();
        var snapshotDefinition = definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot");
        int snapshotPayloadBytes = snapshotDefinition.Fields.Sum(field =>
            Math.Abs(field.Size) * (field.Array ?? 1));

        Assert.Multiple(() =>
        {
            Assert.That(definitions.Select(definition => definition.Key), Is.Unique);
            Assert.That(definitions.Select(definition => definition.PacketType), Is.Unique);
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsInput").Udp, Is.True);
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot").Udp, Is.True);
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsKill").Udp, Is.False);
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsClientDiagnostic").Udp,
                Is.False);
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsClientDiagnostic").Structure,
                Does.Contain("char stage[48]"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot").Structure,
                Does.Contain($"uint8_t actorIDs[{FpsSnapshotPacket.Capacity}]"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot").Structure,
                Does.Contain($"uint32_t spawnCounts[{FpsSnapshotPacket.Capacity}]"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot").Structure,
                Does.Contain($"uint8_t ammo[{FpsSnapshotPacket.Capacity}]"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot").Structure,
                Does.Contain($"uint8_t collisionDirections[{FpsSnapshotPacket.Capacity}]"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot").Structure,
                Does.Not.Contain("vec2 collisionNormals"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsSnapshot").Structure,
                Does.Contain($"float reloadRemaining[{FpsSnapshotPacket.Capacity}]"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsShot").Structure,
                Does.Contain("uint8_t impact"));
            Assert.That(definitions.Single(definition => definition.Key == "ASRC_FpsShot").Structure,
                Does.Contain("uint8_t targetID"));
            Assert.That(snapshotPayloadBytes, Is.LessThanOrEqualTo(704),
                "CSP silently drops oversized UDP online events before invoking the Lua callback");
        });
    }

    [Test]
    public void FpsWorldRoutesOnlineEventsOverTheirDeclaredTransport()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FpsWorld.UsesUdpTransport<FpsShotPacket>(), Is.True);
            Assert.That(FpsWorld.UsesUdpTransport<FpsSnapshotPacket>(), Is.True);
            Assert.That(FpsWorld.UsesUdpTransport<FpsHitPacket>(), Is.False);
            Assert.That(FpsWorld.UsesUdpTransport<FpsKillPacket>(), Is.False);
        });
    }
}

public class TestMessage1
{
    [OnlineEventField(Name = "i00")]
    public int i00;
    [OnlineEventField(Name = "i01")]
    public int i01;
}

public class TestMessage2
{
    [OnlineEventField(Name = "i10")]
    public double i10;
    [OnlineEventField(Name = "i11")]
    public int i11;
}

public class TestMessage3
{
    [OnlineEventField(Name = "i20", Size = 20)]
    public string i20 = null!;
    [OnlineEventField(Name = "i21")]
    public byte i21;
    [OnlineEventField(Name = "i22")]
    public int i22;
    [OnlineEventField(Name = "i23")]
    public double i23;
}

public class TestMessage4
{
    [OnlineEventField(Name = "i20", Size = 20)]
    public string i20 = null!;
    [OnlineEventField(Name = "i21")]
    public byte i21;
    [OnlineEventField(Name = "i22", Size = 4)]
    public int[] i22 = null!;
    [OnlineEventField(Name = "i23")]
    public double i23;
}

public class TestMessage5
{
    [OnlineEventField(Name = "i50")]
    public float i50;
    [OnlineEventField(Name = "i51")]
    public Color i51;
}
