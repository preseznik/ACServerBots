using System.Reflection;
using AssettoServer.Server.Fps;

namespace AssettoServer.Tests;

public sealed class FpsClientScriptTests
{
    [Test]
    public void CarrierCars_PreserveNativeParticipantStateAndDisableNativeLeaderboard()
    {
        Assembly assembly = typeof(FpsWorld).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("AssettoServer.Server.Fps.fps.lua")
                              ?? throw new AssertionException("Embedded FPS client script was not found");
        using var reader = new StreamReader(stream);
        string script = reader.ReadToEnd();
        int snapshotStart = script.IndexOf("local snapshotEvent", StringComparison.Ordinal);
        int rosterStart = script.IndexOf("local rosterEvent", StringComparison.Ordinal);
        string snapshotDefinition = script[snapshotStart..rosterStart];

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Not.Contain("ac.setCarActive"));
            Assert.That(script, Does.Not.Contain("physics.setCarNoInput(true)"));
            Assert.That(script, Does.Contain("ac.findNodes('carRoot:' .. i)"));
            Assert.That(script, Does.Contain("root:setVisible(false)"));
            Assert.That(script, Does.Contain("ac.disableExtraHUDElements"));
            Assert.That(script, Does.Contain("'leaderboard'"));
            Assert.That(script, Does.Not.Contain("physics.lockUserControlsFor"));
            Assert.That(script, Does.Not.Contain("physics.lockUserGearboxFor"));
            Assert.That(script, Does.Contain("physics.setGentleStop(car.index, true)"));
            Assert.That(script, Does.Contain("physics.getCarInputControls()"));
            Assert.That(script, Does.Contain("ac.isKeyDown(positiveKey)"));
            Assert.That(script, Does.Contain("ac.KeyIndex.Up"));
            Assert.That(script, Does.Not.Contain("ui.keyboardButtonDown"));
            Assert.That(script, Does.Contain("}, function() end, nil, true)"));
            Assert.That(snapshotDefinition, Does.Contain("end, nil, true)"));
            Assert.That(script, Does.Contain("buttons = buttons }, false, 255)"));
            Assert.That(script, Does.Contain("not state.isInMainMenu"));
            Assert.That(script, Does.Contain("releaseFpsCamera()"));
            Assert.That(script, Does.Contain("local camera, cameraError = ac.grabCamera"));
            Assert.That(script, Does.Contain("if camera ~= nil then camera.ownShare = 0 end"));
            Assert.That(script, Does.Not.Contain("camera:dispose()"));
            Assert.That(script, Does.Contain("'quickPitsMenu'"));
            Assert.That(script, Does.Contain("ac.disableQuickMenuPitstop(true)"));
            Assert.That(script, Does.Contain("ac.accessMouseDelta(true, true, true)"));
            Assert.That(script, Does.Contain("[ASRC FPS] first snapshot"));
            Assert.That(script, Does.Contain("[ASRC FPS] local actor snapshot acquired"));
            Assert.That(script, Does.Contain("[ASRC FPS] input sample"));
            Assert.That(script, Does.Contain("[ASRC FPS] render state"));
            Assert.That(script, Does.Contain("[ASRC FPS] ready sent"));
        });
    }
}
