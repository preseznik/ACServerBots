using System.Numerics;
using System.Windows;
using System.Windows.Input;
using AssettoServer.RaceControl.Core.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Wpf;

namespace AssettoServer.RaceControl.Controls;

/// <summary>
/// Direct3D 11 chase view for manual bot control. The server remains authoritative; this
/// control renders only the track ribbon and complete vehicle poses received in telemetry.
/// </summary>
public sealed class LiveRace3DViewport : DrawingSurface
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot), typeof(LiveRaceSnapshot), typeof(LiveRace3DViewport),
        new FrameworkPropertyMetadata(null, ScenePropertyChanged));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(LiveTrackMap), typeof(LiveRace3DViewport),
        new FrameworkPropertyMetadata(null, TrackPropertyChanged));
    public static readonly DependencyProperty SelectedSessionIdProperty = DependencyProperty.Register(
        nameof(SelectedSessionId), typeof(int), typeof(LiveRace3DViewport),
        new FrameworkPropertyMetadata(-1, ScenePropertyChanged));

    private const string ShaderSource = """
        cbuffer FrameConstants : register(b0)
        {
            row_major float4x4 ViewProjection;
        };

        struct VertexInput
        {
            float3 Position : POSITION;
            float3 Normal : NORMAL;
            float4 Color : COLOR;
        };

        struct VertexOutput
        {
            float4 Position : SV_POSITION;
            float3 Normal : NORMAL;
            float4 Color : COLOR;
        };

        VertexOutput VSMain(VertexInput input)
        {
            VertexOutput output;
            output.Position = mul(float4(input.Position, 1.0f), ViewProjection);
            output.Normal = input.Normal;
            output.Color = input.Color;
            return output;
        }

        float4 PSMain(VertexOutput input) : SV_TARGET
        {
            float3 normal = normalize(input.Normal);
            float diffuse = saturate(dot(normal, normalize(float3(-0.35f, 0.82f, -0.45f))));
            float lighting = 0.36f + diffuse * 0.64f;
            return float4(input.Color.rgb * lighting, input.Color.a);
        }
        """;

    private static readonly Vector4 RoadColor = new(0.23f, 0.25f, 0.28f, 1);
    private static readonly Vector4 RoadEdgeColor = new(0.62f, 0.64f, 0.66f, 1);
    private static readonly Vector4 GrassColor = new(0.10f, 0.29f, 0.09f, 1);
    private static readonly Vector4 SelectedCarColor = new(1.0f, 0.68f, 0.12f, 1);
    private static readonly Vector4 BotCarColor = new(0.82f, 0.11f, 0.10f, 1);
    private static readonly Vector4 HumanCarColor = new(0.08f, 0.47f, 0.82f, 1);
    private static readonly Vector4 GlassColor = new(0.07f, 0.12f, 0.17f, 1);
    private static readonly Vector4 LightColor = new(0.92f, 0.94f, 0.87f, 1);

    private ID3D11Buffer? _trackVertexBuffer;
    private ID3D11Buffer? _carVertexBuffer;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11RasterizerState? _rasterizerState;
    private Vertex[] _trackVertices = [];
    private Vertex[] _carVertices = [];
    private LiveTrackMap? _renderedTrack;
    private int _trackVersion;
    private int _carVersion;
    private int _uploadedTrackVersion = -1;
    private int _uploadedCarVersion = -1;
    private float _cameraDistance = 8;

    public LiveRace3DViewport()
    {
        ClipToBounds = true;
        Stretch = System.Windows.Media.Stretch.Fill;
        LoadContent += LoadDirect3DContent;
        UnloadContent += UnloadDirect3DContent;
        Draw += DrawScene;
    }

    public LiveRaceSnapshot? Snapshot
    {
        get => (LiveRaceSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public LiveTrackMap? Track
    {
        get => (LiveTrackMap?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public int SelectedSessionId
    {
        get => (int)GetValue(SelectedSessionIdProperty);
        set => SetValue(SelectedSessionIdProperty, value);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        _cameraDistance = Math.Clamp(_cameraDistance * (eventArgs.Delta > 0 ? 0.86f : 1.16f), 4.5f, 20);
        Invalidate();
        eventArgs.Handled = true;
    }

    private static void ScenePropertyChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var viewport = (LiveRace3DViewport)dependencyObject;
        viewport.RebuildScene();
        viewport.Invalidate();
    }

    private static void TrackPropertyChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var viewport = (LiveRace3DViewport)dependencyObject;
        viewport.RebuildTrack();
        viewport.RebuildScene();
        viewport.Invalidate();
    }

    private void LoadDirect3DContent(object? sender, DrawingSurfaceEventArgs eventArgs)
    {
        ReadOnlyMemory<byte> vertexShaderByteCode = Compiler.Compile(
            ShaderSource, "VSMain", "LiveRace3D.hlsl", "vs_4_0");
        ReadOnlyMemory<byte> pixelShaderByteCode = Compiler.Compile(
            ShaderSource, "PSMain", "LiveRace3D.hlsl", "ps_4_0");

        _vertexShader = eventArgs.Device.CreateVertexShader(vertexShaderByteCode.Span);
        _pixelShader = eventArgs.Device.CreatePixelShader(pixelShaderByteCode.Span);
        _inputLayout = eventArgs.Device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 24, 0),
        ], vertexShaderByteCode.Span);
        _constantBuffer = eventArgs.Device.CreateConstantBuffer<FrameConstants>();
        _rasterizerState = eventArgs.Device.CreateRasterizerState(RasterizerDescription.CullNone);
        _uploadedTrackVersion = -1;
        _uploadedCarVersion = -1;
    }

    private void UnloadDirect3DContent(object? sender, DrawingSurfaceEventArgs eventArgs)
    {
        _trackVertexBuffer?.Dispose();
        _trackVertexBuffer = null;
        _carVertexBuffer?.Dispose();
        _carVertexBuffer = null;
        _constantBuffer?.Dispose();
        _constantBuffer = null;
        _vertexShader?.Dispose();
        _vertexShader = null;
        _pixelShader?.Dispose();
        _pixelShader = null;
        _inputLayout?.Dispose();
        _inputLayout = null;
        _rasterizerState?.Dispose();
        _rasterizerState = null;
    }

    private void DrawScene(object? sender, DrawEventArgs eventArgs)
    {
        eventArgs.Context.OMSetBlendState(null);
        eventArgs.Context.ClearRenderTargetView(eventArgs.Surface.ColorTextureView!,
            new Color4(0.035f, 0.043f, 0.055f, 1));
        if (eventArgs.Surface.DepthStencilView != null)
        {
            eventArgs.Context.ClearDepthStencilView(eventArgs.Surface.DepthStencilView,
                DepthStencilClearFlags.Depth, 1, 0);
        }

        if ((_trackVertices.Length == 0 && _carVertices.Length == 0)
            || _vertexShader == null || _pixelShader == null ||
            _inputLayout == null || _constantBuffer == null)
            return;

        if (_uploadedTrackVersion != _trackVersion)
        {
            _trackVertexBuffer?.Dispose();
            _trackVertexBuffer = _trackVertices.Length == 0
                ? null
                : eventArgs.Device.CreateBuffer<Vertex>(_trackVertices, BindFlags.VertexBuffer);
            _uploadedTrackVersion = _trackVersion;
        }
        if (_uploadedCarVersion != _carVersion)
        {
            _carVertexBuffer?.Dispose();
            _carVertexBuffer = _carVertices.Length == 0
                ? null
                : eventArgs.Device.CreateBuffer<Vertex>(_carVertices, BindFlags.VertexBuffer);
            _uploadedCarVersion = _carVersion;
        }

        LiveRaceCar? selected = GetSelectedCar();
        Matrix4x4 viewProjection = CreateViewProjection(selected, eventArgs.Surface.TextureWidth,
            eventArgs.Surface.TextureHeight);
        _constantBuffer.SetData(eventArgs.Context, new FrameConstants(viewProjection), MapMode.WriteDiscard);

        eventArgs.Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        eventArgs.Context.IASetInputLayout(_inputLayout);
        eventArgs.Context.VSSetShader(_vertexShader);
        eventArgs.Context.VSSetConstantBuffer(0, _constantBuffer);
        eventArgs.Context.PSSetShader(_pixelShader);
        eventArgs.Context.GSSetShader(null);
        eventArgs.Context.HSSetShader(null);
        eventArgs.Context.DSSetShader(null);
        eventArgs.Context.RSSetState(_rasterizerState);
        if (_trackVertexBuffer != null)
        {
            eventArgs.Context.IASetVertexBuffer(0, _trackVertexBuffer, Vertex.SizeInBytes);
            eventArgs.Context.Draw((uint)_trackVertices.Length, 0);
        }
        if (_carVertexBuffer != null)
        {
            eventArgs.Context.IASetVertexBuffer(0, _carVertexBuffer, Vertex.SizeInBytes);
            eventArgs.Context.Draw((uint)_carVertices.Length, 0);
        }
    }

    private Matrix4x4 CreateViewProjection(LiveRaceCar? selected, int width, int height)
    {
        if (selected == null)
        {
            Vector3 center = Track is { Points.Count: > 0 }
                ? new Vector3(Track.Points.Average(point => point.X),
                    Track.Points.Average(point => point.Y), Track.Points.Average(point => point.Z))
                : Vector3.Zero;
            Matrix4x4 overview = Matrix4x4.CreateLookAt(center + new Vector3(0, 80, -80), center,
                Vector3.UnitY);
            Matrix4x4 overviewProjection = Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3, Math.Max(0.1f, width / (float)Math.Max(1, height)), 0.1f, 5000);
            return overview * overviewProjection;
        }

        Vector3 forward = GetForward(selected);
        Vector3 flatForward = new(forward.X, 0, forward.Z);
        if (flatForward.LengthSquared() < 0.01f)
            flatForward = Vector3.UnitZ;
        else
            flatForward = Vector3.Normalize(flatForward);
        Vector3 target = new(selected.X, selected.Y + 0.85f, selected.Z);
        Vector3 camera = target - flatForward * _cameraDistance + new Vector3(0, 2.8f, 0);
        Matrix4x4 view = Matrix4x4.CreateLookAt(camera, target + flatForward * 12, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI * 62 / 180, Math.Max(0.1f, width / (float)Math.Max(1, height)), 0.1f, 5000);
        return view * projection;
    }

    private LiveRaceCar? GetSelectedCar()
    {
        LiveRaceCar[] activeCars = Snapshot?.Cars.Where(car => car.IsActive).ToArray() ?? [];
        return activeCars.FirstOrDefault(car => car.SessionId == SelectedSessionId)
               ?? activeCars.FirstOrDefault();
    }

    private void RebuildTrack()
    {
        if (ReferenceEquals(_renderedTrack, Track))
            return;
        _renderedTrack = Track;
        if (Track is not { Points.Count: > 2 })
        {
            _trackVertices = [];
            _trackVersion++;
            return;
        }

        var vertices = new List<Vertex>(Track.Points.Count * 30);
        int count = Track.Points.Count;
        for (int index = 0; index < count; index++)
        {
            LiveTrackPoint previous = Track.Points[(index - 1 + count) % count];
            LiveTrackPoint current = Track.Points[index];
            LiveTrackPoint next = Track.Points[(index + 1) % count];
            LiveTrackPoint following = Track.Points[(index + 2) % count];
            Vector3 currentSide = GetTrackSide(previous, next);
            Vector3 nextSide = GetTrackSide(current, following);
            float currentLeft = Math.Max(2.5f, current.LeftWidth);
            float currentRight = Math.Max(2.5f, current.RightWidth);
            float nextLeft = Math.Max(2.5f, next.LeftWidth);
            float nextRight = Math.Max(2.5f, next.RightWidth);
            Vector3 currentCenter = new(current.X, current.Y + 0.035f, current.Z);
            Vector3 nextCenter = new(next.X, next.Y + 0.035f, next.Z);
            Vector3 currentLeftPoint = currentCenter + currentSide * currentLeft;
            Vector3 currentRightPoint = currentCenter - currentSide * currentRight;
            Vector3 nextLeftPoint = nextCenter + nextSide * nextLeft;
            Vector3 nextRightPoint = nextCenter - nextSide * nextRight;

            const float terrainWidth = 30;
            var terrainDrop = new Vector3(0, -0.03f, 0);
            Vector3 currentLeftTerrain = currentLeftPoint + terrainDrop;
            Vector3 nextLeftTerrain = nextLeftPoint + terrainDrop;
            Vector3 currentRightTerrain = currentRightPoint + terrainDrop;
            Vector3 nextRightTerrain = nextRightPoint + terrainDrop;
            AddQuad(vertices,
                currentLeftTerrain + currentSide * terrainWidth,
                nextLeftTerrain + nextSide * terrainWidth,
                nextLeftTerrain, currentLeftTerrain, GrassColor);
            AddQuad(vertices,
                currentRightTerrain, nextRightTerrain,
                nextRightTerrain - nextSide * terrainWidth,
                currentRightTerrain - currentSide * terrainWidth, GrassColor);

            AddTriangle(vertices, currentLeftPoint, nextLeftPoint, currentRightPoint, RoadColor);
            AddTriangle(vertices, currentRightPoint, nextLeftPoint, nextRightPoint, RoadColor);
            AddTrackEdge(vertices, currentLeftPoint, nextLeftPoint, currentSide, RoadEdgeColor);
            AddTrackEdge(vertices, currentRightPoint, nextRightPoint, -currentSide, RoadEdgeColor);
        }
        _trackVertices = [.. vertices];
        _trackVersion++;
    }

    private void RebuildScene()
    {
        RebuildTrack();
        LiveRaceCar[] activeCars = Snapshot?.Cars.Where(car => car.IsActive).ToArray() ?? [];
        var vertices = new List<Vertex>(activeCars.Length * 108);
        LiveRaceCar? selected = activeCars.FirstOrDefault(car => car.SessionId == SelectedSessionId)
                                ?? activeCars.FirstOrDefault();
        foreach (LiveRaceCar car in activeCars)
            AddCar(vertices, car, car.SessionId == selected?.SessionId);
        _carVertices = [.. vertices];
        _carVersion++;
    }

    private static void AddCar(List<Vertex> vertices, LiveRaceCar car, bool selected)
    {
        Vector4 bodyColor = selected ? SelectedCarColor : car.IsBot ? BotCarColor : HumanCarColor;
        Quaternion rotation = new(car.OrientationX, car.OrientationY, car.OrientationZ,
            Math.Abs(car.OrientationW) < 0.0001f && Math.Abs(car.OrientationX) < 0.0001f &&
            Math.Abs(car.OrientationY) < 0.0001f && Math.Abs(car.OrientationZ) < 0.0001f
                ? 1
                : car.OrientationW);
        rotation = Quaternion.Normalize(rotation);
        Vector3 translation = new(car.X, car.Y, car.Z);
        AddBox(vertices, new Vector3(1.82f, 0.58f, 4.2f), new Vector3(0, 0.47f, 0),
            rotation, translation, bodyColor);
        AddBox(vertices, new Vector3(1.48f, 0.54f, 1.92f), new Vector3(0, 0.98f, -0.15f),
            rotation, translation, GlassColor);
        AddBox(vertices, new Vector3(1.9f, 0.12f, 0.22f), new Vector3(0, 0.39f, 2.1f),
            rotation, translation, LightColor);
    }

    private static void AddBox(List<Vertex> vertices, Vector3 size, Vector3 center,
        Quaternion rotation, Vector3 translation, Vector4 color)
    {
        Vector3 half = size / 2;
        Vector3[] points =
        [
            center + new Vector3(-half.X, -half.Y, -half.Z),
            center + new Vector3(half.X, -half.Y, -half.Z),
            center + new Vector3(half.X, half.Y, -half.Z),
            center + new Vector3(-half.X, half.Y, -half.Z),
            center + new Vector3(-half.X, -half.Y, half.Z),
            center + new Vector3(half.X, -half.Y, half.Z),
            center + new Vector3(half.X, half.Y, half.Z),
            center + new Vector3(-half.X, half.Y, half.Z),
        ];
        for (int index = 0; index < points.Length; index++)
            points[index] = Vector3.Transform(points[index], rotation) + translation;
        AddQuad(vertices, points[0], points[3], points[2], points[1], color);
        AddQuad(vertices, points[4], points[5], points[6], points[7], color);
        AddQuad(vertices, points[0], points[1], points[5], points[4], color);
        AddQuad(vertices, points[3], points[7], points[6], points[2], color);
        AddQuad(vertices, points[1], points[2], points[6], points[5], color);
        AddQuad(vertices, points[0], points[4], points[7], points[3], color);
    }

    private static void AddQuad(List<Vertex> vertices, Vector3 first, Vector3 second,
        Vector3 third, Vector3 fourth, Vector4 color)
    {
        AddTriangle(vertices, first, second, third, color);
        AddTriangle(vertices, first, third, fourth, color);
    }

    private static void AddTriangle(List<Vertex> vertices, Vector3 first, Vector3 second,
        Vector3 third, Vector4 color)
    {
        Vector3 normal = Vector3.Cross(second - first, third - first);
        normal = normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.UnitY;
        vertices.Add(new Vertex(first, normal, color));
        vertices.Add(new Vertex(second, normal, color));
        vertices.Add(new Vertex(third, normal, color));
    }

    private static void AddTrackEdge(List<Vertex> vertices, Vector3 first, Vector3 second,
        Vector3 outward, Vector4 color)
    {
        const float width = 0.14f;
        Vector3 offset = Vector3.Normalize(new Vector3(outward.X, 0, outward.Z)) * width;
        AddQuad(vertices, first - offset, second - offset, second + offset, first + offset, color);
    }

    private static Vector3 GetTrackSide(LiveTrackPoint previous, LiveTrackPoint next)
    {
        float tangentX = next.X - previous.X;
        float tangentZ = next.Z - previous.Z;
        float inverseLength = 1 / Math.Max(0.001f, MathF.Sqrt(tangentX * tangentX + tangentZ * tangentZ));
        return new Vector3(-tangentZ * inverseLength, 0, tangentX * inverseLength);
    }

    private static Vector3 GetForward(LiveRaceCar car)
    {
        var forward = new Vector3(car.ForwardX, car.ForwardY, car.ForwardZ);
        if (forward.LengthSquared() > 0.01f)
            return Vector3.Normalize(forward);
        return new Vector3(-MathF.Cos(car.HeadingRadians), 0, MathF.Sin(car.HeadingRadians));
    }

    private readonly struct Vertex(Vector3 position, Vector3 normal, Vector4 color)
    {
        public const uint SizeInBytes = 40;
        public readonly Vector3 Position = position;
        public readonly Vector3 Normal = normal;
        public readonly Vector4 Color = color;
    }

    private readonly struct FrameConstants(Matrix4x4 viewProjection)
    {
        public readonly Matrix4x4 ViewProjection = viewProjection;
    }
}
