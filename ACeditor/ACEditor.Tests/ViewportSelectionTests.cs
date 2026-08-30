using System.Numerics;
using System.Reflection;
using System.Threading;
using ACEditor.App.Controls;
using ACEditor.Core.Models;
using Vortice.D3DCompiler;

namespace ACEditor.Tests;

public sealed class ViewportSelectionTests
{
    [Test]
    public void ZoomScalingHasNoEditorDistanceClamp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TrackViewport.ScaleZoom(0.5f, 1_200), Is.LessThan(0.1f));
            Assert.That(TrackViewport.ScaleZoom(100_000f, -1_200), Is.GreaterThan(500_000f));
        });
    }

    [Test]
    public void PerspectiveClipRangePreservesTrackLayerDepthPrecision()
    {
        (float nearPlane, float farPlane) = TrackViewport.CalculateClipPlanes(4_400f, 2_000f);

        Assert.Multiple(() =>
        {
            Assert.That(nearPlane, Is.EqualTo(0.44f).Within(0.0001f));
            Assert.That(farPlane, Is.EqualTo(12_400f));
            Assert.That(farPlane / nearPlane, Is.LessThan(30_000f));
            Assert.That(TrackViewport.CalculateClipPlanes(0.001f, 2_000f).Near, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Kn5TransparencyModesBecomeShaderAndDepthStateInputs()
    {
        var opaque = new TrackMaterial();
        var cutout = new TrackMaterial { AlphaTested = true };
        var blended = new TrackMaterial { BlendMode = MaterialBlendMode.AlphaBlend };
        var coverage = new TrackMaterial { BlendMode = MaterialBlendMode.AlphaToCoverage };
        var customReference = new TrackMaterial
        {
            Properties = { ["ksAlphaRef"] = [0.27f] }
        };

        Assert.Multiple(() =>
        {
            Assert.That(TrackViewport.ResolveAlphaMode(opaque), Is.EqualTo(0));
            Assert.That(TrackViewport.ResolveAlphaMode(cutout), Is.EqualTo(1));
            Assert.That(TrackViewport.ResolveAlphaMode(blended), Is.EqualTo(2));
            Assert.That(TrackViewport.ResolveAlphaMode(coverage), Is.EqualTo(1));
            Assert.That(TrackViewport.ResolveAlphaReference(customReference), Is.EqualTo(0.27f));
            Assert.That(TrackViewport.ResolveAlphaReference(opaque), Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void NormalPassExcludesNativeNonRenderableMeshesButCollisionPassRetainsThem()
    {
        var physicsMesh = new TrackMesh { SourceVisible = true, SourceRenderable = false };

        Assert.Multiple(() =>
        {
            Assert.That(TrackViewport.ShouldRenderMesh(physicsMesh, collisionOverlay: false), Is.False);
            Assert.That(TrackViewport.ShouldRenderMesh(physicsMesh, collisionOverlay: true), Is.True);
        });
    }

    [Test]
    public void ViewportShaderCompilesForDirect3D11()
    {
        FieldInfo field = typeof(TrackViewport).GetField("ShaderSource",
                              BindingFlags.Static | BindingFlags.NonPublic)
                          ?? throw new AssertionException("Missing viewport shader source.");
        string source = (string)(field.GetRawConstantValue()
                                 ?? throw new AssertionException("Viewport shader source is empty."));

        Assert.Multiple(() =>
        {
            Assert.That(Compiler.Compile(source, "VSMain", "ACEditorViewport.hlsl", "vs_4_0").Length,
                Is.GreaterThan(0));
            Assert.That(Compiler.Compile(source, "PSMain", "ACEditorViewport.hlsl", "ps_4_0").Length,
                Is.GreaterThan(0));
        });
    }

    [Test]
    public void LayoutFilterIncludesSharedAndSelectedGeometryOnly()
    {
        var shared = new TrackNode { Ownership = "shared" };
        var selected = new TrackNode { Ownership = "layout_gp_a, layout_sprint_a" };
        var other = new TrackNode { Ownership = "layout_gp_b" };

        Assert.Multiple(() =>
        {
            Assert.That(TrackViewport.ShouldRenderRoot(shared, "layout_gp_a"), Is.True);
            Assert.That(TrackViewport.ShouldRenderRoot(selected, "layout_gp_a"), Is.True);
            Assert.That(TrackViewport.ShouldRenderRoot(other, "layout_gp_a"), Is.False);
            Assert.That(TrackViewport.ShouldRenderRoot(other, null), Is.True);
        });
    }

    [Test]
    public void LayoutFilterIncludesSharedAndSelectedRoutesOnly()
    {
        string[] layouts = ["layout_gp_a", "layout_gp_b"];
        var selected = new TrackRoute { Id = "layout_gp_a/ai/fast_lane.ai" };
        var other = new TrackRoute { Id = "layout_gp_b/ai/fast_lane.ai" };
        var shared = new TrackRoute { Id = "ai/fast_lane.ai" };

        Assert.Multiple(() =>
        {
            Assert.That(TrackViewport.ShouldRenderRoute(selected, "layout_gp_a", layouts), Is.True);
            Assert.That(TrackViewport.ShouldRenderRoute(other, "layout_gp_a", layouts), Is.False);
            Assert.That(TrackViewport.ShouldRenderRoute(shared, "layout_gp_a", layouts), Is.True);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void SelectingNode_FramesItsBoundsWithoutResettingOrbitDirection()
    {
        TrackNode left = CreateTriangleNode("left", -1_000);
        TrackNode right = CreateTriangleNode("right", 1_000);
        var project = new TrackProject();
        project.Scene.Roots.Add(left);
        project.Scene.Roots.Add(right);
        var viewport = new TrackViewport();

        viewport.Project = project;
        SetField(viewport, "_yaw", 0.42f);
        SetField(viewport, "_pitch", -0.31f);
        viewport.SelectedNode = right;

        Vector3 cameraTarget = GetField<Vector3>(viewport, "_sceneCenter");
        Assert.Multiple(() =>
        {
            Assert.That(cameraTarget.X, Is.EqualTo(1_000).Within(0.001f));
            Assert.That(cameraTarget.Y, Is.EqualTo(0).Within(0.001f));
            Assert.That(cameraTarget.Z, Is.EqualTo(0).Within(0.001f));
            Assert.That(GetField<float>(viewport, "_yaw"), Is.EqualTo(0.42f));
            Assert.That(GetField<float>(viewport, "_pitch"), Is.EqualTo(-0.31f));
            Assert.That(GetField<float>(viewport, "_distance"), Is.LessThan(10));
        });
    }

    private static TrackNode CreateTriangleNode(string name, float x)
    {
        var mesh = new TrackMesh { Name = name };
        mesh.Positions.AddRange([
            new Position3(x - 1, -1, 0),
            new Position3(x + 1, -1, 0),
            new Position3(x, 1, 0)
        ]);
        mesh.Indices.AddRange([0, 1, 2]);
        return new TrackNode { Name = name, Mesh = mesh };
    }

    private static T GetField<T>(object target, string name) =>
        (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
            ?? throw new AssertionException($"Missing field {name}."));

    private static void SetField<T>(object target, string name, T value) =>
        (target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException($"Missing field {name}.")).SetValue(target, value);
}
