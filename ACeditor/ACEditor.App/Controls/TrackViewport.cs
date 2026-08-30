using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using ACEditor.Core.Models;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.Wpf;

namespace ACEditor.App.Controls;

public enum ViewportRenderMode
{
    Wireframe,
    Filled,
    Textured,
    Lit,
    TexturedLit
}

public sealed class TrackViewport : DrawingSurface
{
    private const int VerticesPerChunk = 300_000;
    private const string ShaderSource = """
        cbuffer FrameConstants : register(b0)
        {
            row_major float4x4 ViewProjection;
            uint RenderMode;
            uint HasTexture;
            uint AlphaMode;
            float AlphaReference;
        };
        Texture2D DiffuseTexture : register(t0);
        SamplerState DiffuseSampler : register(s0);
        struct VertexInput { float3 Position : POSITION; float3 Normal : NORMAL; float4 Color : COLOR; float2 UV : TEXCOORD; };
        struct VertexOutput { float4 Position : SV_POSITION; float3 Normal : NORMAL; float4 Color : COLOR; float2 UV : TEXCOORD; };
        VertexOutput VSMain(VertexInput input)
        {
            VertexOutput output;
            output.Position = mul(float4(input.Position, 1.0f), ViewProjection);
            output.Normal = input.Normal;
            output.Color = input.Color;
            output.UV = input.UV;
            return output;
        }
        float4 PSMain(VertexOutput input) : SV_TARGET
        {
            float4 sampled = HasTexture != 0
                ? DiffuseTexture.Sample(DiffuseSampler, input.UV)
                : float4(1.0f, 1.0f, 1.0f, 1.0f);
            if ((AlphaMode & 1) != 0) clip(sampled.a - AlphaReference);
            bool useTexture = (RenderMode == 2 || RenderMode == 4) && HasTexture != 0;
            float4 baseColor = useTexture ? sampled : input.Color;
            if ((AlphaMode & 2) != 0) baseColor.a = sampled.a;
            bool useLighting = RenderMode == 3 || RenderMode == 4;
            if (!useLighting) return baseColor;
            float3 normal = normalize(input.Normal);
            float diffuse = saturate(dot(normal, normalize(float3(-0.35f, 0.82f, -0.45f))));
            float lighting = 0.28f + diffuse * 0.72f;
            return float4(baseColor.rgb * lighting, baseColor.a);
        }
        """;

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(TrackProject), typeof(TrackViewport),
        new FrameworkPropertyMetadata(null, RebuildPropertyChanged));
    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode), typeof(TrackNode), typeof(TrackViewport),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, RebuildPropertyChanged));
    public static readonly DependencyProperty RouteOnlyProperty = DependencyProperty.Register(
        nameof(RouteOnly), typeof(bool), typeof(TrackViewport),
        new FrameworkPropertyMetadata(false, RebuildPropertyChanged));
    public static readonly DependencyProperty CollisionOverlayProperty = DependencyProperty.Register(
        nameof(CollisionOverlay), typeof(bool), typeof(TrackViewport),
        new FrameworkPropertyMetadata(false, RebuildPropertyChanged));
    public static readonly DependencyProperty OrthographicProperty = DependencyProperty.Register(
        nameof(Orthographic), typeof(bool), typeof(TrackViewport),
        new FrameworkPropertyMetadata(false, RebuildPropertyChanged));
    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(TrackViewport),
        new FrameworkPropertyMetadata(0, RebuildPropertyChanged));
    public static readonly DependencyProperty TextureRevisionProperty = DependencyProperty.Register(
        nameof(TextureRevision), typeof(int), typeof(TrackViewport),
        new FrameworkPropertyMetadata(0, TextureRevisionPropertyChanged));
    public static readonly DependencyProperty RenderModeProperty = DependencyProperty.Register(
        nameof(RenderMode), typeof(ViewportRenderMode), typeof(TrackViewport),
        new FrameworkPropertyMetadata(ViewportRenderMode.TexturedLit, RenderModePropertyChanged));
    public static readonly DependencyProperty ActiveLayoutProperty = DependencyProperty.Register(
        nameof(ActiveLayout), typeof(string), typeof(TrackViewport),
        new FrameworkPropertyMetadata(null, RebuildPropertyChanged));

    private readonly List<ID3D11Buffer> _vertexBuffers = [];
    private readonly Dictionary<string, ID3D11ShaderResourceView> _textureViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PickCandidate> _pickCandidates = [];
    private readonly Dictionary<TrackNode, NodeBounds> _selectionBounds = [];
    private RenderChunk[] _renderChunks = [];
    private ID3D11Buffer? _constantBuffer;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11RasterizerState? _solidRasterizerState;
    private ID3D11RasterizerState? _wireframeRasterizerState;
    private ID3D11BlendState? _alphaBlendState;
    private ID3D11DepthStencilState? _depthNormalState;
    private ID3D11DepthStencilState? _depthReadState;
    private ID3D11DepthStencilState? _depthOffState;
    private ID3D11SamplerState? _samplerState;
    private TrackProject? _uploadedTextureProject;
    private int _sceneVersion;
    private int _uploadedVersion = -1;
    private Vector3 _sceneCenter;
    private float _sceneRadius = 60;
    private float _yaw = -0.7f;
    private float _pitch = 0.55f;
    private float _distance = 140;
    private float _orthographicHeight = 120;
    private Point _lastMouse;
    private Point _mouseDown;
    private Matrix4x4 _lastViewProjection;
    private int _lastWidth;
    private int _lastHeight;

    public TrackViewport()
    {
        ClipToBounds = true;
        Stretch = System.Windows.Media.Stretch.Fill;
        Focusable = true;
        LoadContent += LoadDirect3DContent;
        UnloadContent += UnloadDirect3DContent;
        Draw += DrawScene;
    }

    public TrackProject? Project { get => (TrackProject?)GetValue(ProjectProperty); set => SetValue(ProjectProperty, value); }
    public TrackNode? SelectedNode { get => (TrackNode?)GetValue(SelectedNodeProperty); set => SetValue(SelectedNodeProperty, value); }
    public bool RouteOnly { get => (bool)GetValue(RouteOnlyProperty); set => SetValue(RouteOnlyProperty, value); }
    public bool CollisionOverlay { get => (bool)GetValue(CollisionOverlayProperty); set => SetValue(CollisionOverlayProperty, value); }
    public bool Orthographic { get => (bool)GetValue(OrthographicProperty); set => SetValue(OrthographicProperty, value); }
    public int Revision { get => (int)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
    public int TextureRevision { get => (int)GetValue(TextureRevisionProperty); set => SetValue(TextureRevisionProperty, value); }
    public ViewportRenderMode RenderMode { get => (ViewportRenderMode)GetValue(RenderModeProperty); set => SetValue(RenderModeProperty, value); }
    public string? ActiveLayout { get => (string?)GetValue(ActiveLayoutProperty); set => SetValue(ActiveLayoutProperty, value); }

    protected override void OnMouseDown(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        Focus(); CaptureMouse();
        _lastMouse = _mouseDown = eventArgs.GetPosition(this);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        Point current = eventArgs.GetPosition(this);
        float dx = (float)(current.X - _lastMouse.X);
        float dy = (float)(current.Y - _lastMouse.Y);
        if (eventArgs.RightButton == MouseButtonState.Pressed)
        {
            _yaw += dx * 0.008f;
            _pitch = MathF.IEEERemainder(_pitch + dy * 0.008f, MathF.Tau);
            Invalidate();
        }
        else if (eventArgs.MiddleButton == MouseButtonState.Pressed)
        {
            Vector3 right;
            Vector3 up;
            if (Orthographic)
            {
                right = Vector3.UnitX;
                up = Vector3.UnitZ;
            }
            else
            {
                (_, right, up) = CameraAxes();
            }
            float scale = (Orthographic ? _orthographicHeight : _distance) * 0.0016f;
            _sceneCenter += (-right * dx + up * dy) * scale;
            Invalidate();
        }
        _lastMouse = current;
    }

    protected override void OnMouseUp(MouseButtonEventArgs eventArgs)
    {
        Point current = eventArgs.GetPosition(this);
        if (eventArgs.ChangedButton == MouseButton.Left &&
            Math.Abs(current.X - _mouseDown.X) + Math.Abs(current.Y - _mouseDown.Y) < 5)
            Pick(current);
        ReleaseMouseCapture();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        if (Orthographic)
        {
            _orthographicHeight = ScaleZoom(_orthographicHeight, eventArgs.Delta);
        }
        else
        {
            _distance = ScaleZoom(_distance, eventArgs.Delta);
        }
        Invalidate(); eventArgs.Handled = true;
    }

    internal static float ScaleZoom(float current, int wheelDelta)
    {
        float next = current * MathF.Pow(0.84f, wheelDelta / 120f);
        return float.IsFinite(next) && next > float.Epsilon ? next : current;
    }

    private static void RebuildPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var viewport = (TrackViewport)sender;
        bool resetCamera = args.Property == ProjectProperty;
        viewport.RebuildScene(resetCamera);
        if (args.Property == SelectedNodeProperty && viewport.SelectedNode is not null)
            viewport.FrameSelectedNode(viewport.SelectedNode);
        viewport.Invalidate();
    }

    private static void RenderModePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((TrackViewport)sender).Invalidate();

    private static void TextureRevisionPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var viewport = (TrackViewport)sender;
        viewport.DisposeTextures();
        viewport.Invalidate();
    }

    private void LoadDirect3DContent(object? sender, DrawingSurfaceEventArgs eventArgs)
    {
        ReadOnlyMemory<byte> vertexCode = Compiler.Compile(ShaderSource, "VSMain", "ACEditorViewport.hlsl", "vs_4_0");
        ReadOnlyMemory<byte> pixelCode = Compiler.Compile(ShaderSource, "PSMain", "ACEditorViewport.hlsl", "ps_4_0");
        _vertexShader = eventArgs.Device.CreateVertexShader(vertexCode.Span);
        _pixelShader = eventArgs.Device.CreatePixelShader(pixelCode.Span);
        _inputLayout = eventArgs.Device.CreateInputLayout([
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 24, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 40, 0)
        ], vertexCode.Span);
        _constantBuffer = eventArgs.Device.CreateConstantBuffer<FrameConstants>();
        _solidRasterizerState = eventArgs.Device.CreateRasterizerState(RasterizerDescription.CullNone);
        _wireframeRasterizerState = eventArgs.Device.CreateRasterizerState(RasterizerDescription.Wireframe);
        _alphaBlendState = eventArgs.Device.CreateBlendState(BlendDescription.NonPremultiplied);
        _depthNormalState = eventArgs.Device.CreateDepthStencilState(
            new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual));
        _depthReadState = eventArgs.Device.CreateDepthStencilState(
            new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual));
        _depthOffState = eventArgs.Device.CreateDepthStencilState(
            new DepthStencilDescription(false, DepthWriteMask.Zero, ComparisonFunction.Always));
        _samplerState = eventArgs.Device.CreateSamplerState(SamplerDescription.AnisotropicWrap);
        _uploadedTextureProject = null;
        _uploadedVersion = -1;
    }

    private void UnloadDirect3DContent(object? sender, DrawingSurfaceEventArgs eventArgs)
    {
        DisposeBuffers();
        _constantBuffer?.Dispose(); _constantBuffer = null;
        _vertexShader?.Dispose(); _vertexShader = null;
        _pixelShader?.Dispose(); _pixelShader = null;
        _inputLayout?.Dispose(); _inputLayout = null;
        _solidRasterizerState?.Dispose(); _solidRasterizerState = null;
        _wireframeRasterizerState?.Dispose(); _wireframeRasterizerState = null;
        _alphaBlendState?.Dispose(); _alphaBlendState = null;
        _depthNormalState?.Dispose(); _depthNormalState = null;
        _depthReadState?.Dispose(); _depthReadState = null;
        _depthOffState?.Dispose(); _depthOffState = null;
        _samplerState?.Dispose(); _samplerState = null;
        DisposeTextures();
    }

    private void DrawScene(object? sender, DrawEventArgs eventArgs)
    {
        eventArgs.Context.OMSetBlendState(null);
        eventArgs.Context.ClearRenderTargetView(eventArgs.Surface.ColorTextureView!, new Color4(0.035f, 0.043f, 0.055f, 1));
        if (eventArgs.Surface.DepthStencilView != null)
            eventArgs.Context.ClearDepthStencilView(eventArgs.Surface.DepthStencilView, DepthStencilClearFlags.Depth, 1, 0);
        if (_vertexShader is null || _pixelShader is null || _inputLayout is null || _constantBuffer is null ||
            _solidRasterizerState is null || _wireframeRasterizerState is null || _alphaBlendState is null ||
            _depthNormalState is null || _depthReadState is null || _depthOffState is null || _samplerState is null) return;

        if (_uploadedVersion != _sceneVersion)
        {
            DisposeBuffers();
            foreach (RenderChunk chunk in _renderChunks)
                if (chunk.Vertices.Length > 0)
                    _vertexBuffers.Add(eventArgs.Device.CreateBuffer<Vertex>(chunk.Vertices, BindFlags.VertexBuffer));
            _uploadedVersion = _sceneVersion;
        }
        if (!ReferenceEquals(_uploadedTextureProject, Project)) UploadTextures(eventArgs.Device);

        _lastWidth = eventArgs.Surface.TextureWidth;
        _lastHeight = eventArgs.Surface.TextureHeight;
        _lastViewProjection = CreateViewProjection(_lastWidth, _lastHeight);
        eventArgs.Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        eventArgs.Context.IASetInputLayout(_inputLayout);
        eventArgs.Context.VSSetShader(_vertexShader);
        eventArgs.Context.VSSetConstantBuffer(0, _constantBuffer);
        eventArgs.Context.PSSetShader(_pixelShader);
        eventArgs.Context.PSSetConstantBuffer(0, _constantBuffer);
        eventArgs.Context.PSSetSampler(0, _samplerState);
        eventArgs.Context.GSSetShader(null); eventArgs.Context.HSSetShader(null); eventArgs.Context.DSSetShader(null);
        eventArgs.Context.RSSetState(RenderMode == ViewportRenderMode.Wireframe
            ? _wireframeRasterizerState : _solidRasterizerState);
        for (int i = 0; i < _vertexBuffers.Count; i++)
        {
            RenderChunk chunk = _renderChunks[i];
            ID3D11ShaderResourceView? textureView = chunk.TextureName is not null &&
                _textureViews.TryGetValue(chunk.TextureName, out ID3D11ShaderResourceView? found) ? found : null;
            bool needsTexture = RenderMode is ViewportRenderMode.Textured or ViewportRenderMode.TexturedLit ||
                chunk.AlphaMode != 0;
            bool useTexture = needsTexture && textureView is not null;
            eventArgs.Context.OMSetBlendState((chunk.AlphaMode & 2) != 0 ? _alphaBlendState : null);
            eventArgs.Context.OMSetDepthStencilState(chunk.DepthMode switch
            {
                MaterialDepthMode.NoWrite => _depthReadState,
                MaterialDepthMode.Off => _depthOffState,
                _ => _depthNormalState
            }, 0);
            _constantBuffer.SetData(eventArgs.Context,
                new FrameConstants(_lastViewProjection, (uint)RenderMode, useTexture ? 1u : 0u,
                    chunk.AlphaMode, chunk.AlphaReference),
                MapMode.WriteDiscard);
            eventArgs.Context.PSSetShaderResource(0, useTexture ? textureView! : null!);
            eventArgs.Context.IASetVertexBuffer(0, _vertexBuffers[i], Vertex.SizeInBytes);
            eventArgs.Context.Draw((uint)chunk.Vertices.Length, 0);
        }
        eventArgs.Context.PSSetShaderResource(0, null!);
        eventArgs.Context.OMSetBlendState(null);
        eventArgs.Context.OMSetDepthStencilState(_depthNormalState, 0);
    }

    private void RebuildScene(bool resetCamera)
    {
        MaterialBatchKey untexturedKey = new(string.Empty, 0, MaterialDepthMode.Normal, 0.5f);
        var batches = new Dictionary<MaterialBatchKey, List<Vertex>>
        {
            [untexturedKey] = []
        };
        List<Vertex> untextured = batches[untexturedKey];
        var bounds = new BoundsAccumulator();
        _pickCandidates.Clear();
        _selectionBounds.Clear();
        if (Project is not null)
        {
            AddGrid(untextured, bounds);
            if (!RouteOnly)
                foreach (TrackNode root in Project.Scene.Roots.Where(root => ShouldRenderRoot(root, ActiveLayout)))
                    AddNode(root, batches, bounds);
            if (RouteOnly)
                foreach (TrackRoute route in Project.Routes.Where(route =>
                             ShouldRenderRoute(route, ActiveLayout, Project.LayoutIds)))
                    AddRoute(route, untextured, bounds);
            if (SelectedNode is not null && !RouteOnly && _selectionBounds.ContainsKey(SelectedNode))
                AddGizmo(SelectedNode, untextured, bounds);
        }
        if (bounds.HasValue)
        {
            _sceneRadius = Math.Max(2, bounds.Radius);
            if (resetCamera)
            {
                _sceneCenter = bounds.Center;
                _distance = _sceneRadius * 2.2f;
                _orthographicHeight = _sceneRadius * 2.3f;
            }
        }
        _renderChunks = batches.OrderBy(batch => (batch.Key.AlphaMode & 2) != 0 ? 1 : 0)
            .SelectMany(batch => batch.Value
                .Chunk(VerticesPerChunk - VerticesPerChunk % 3)
                .Select(chunk => new RenderChunk(chunk.ToArray(),
                    string.IsNullOrEmpty(batch.Key.TextureName) ? null : batch.Key.TextureName,
                    batch.Key.AlphaMode, batch.Key.DepthMode, batch.Key.AlphaReference)))
            .ToArray();
        _sceneVersion++;
    }

    internal static bool ShouldRenderRoot(TrackNode root, string? activeLayout)
    {
        if (string.IsNullOrWhiteSpace(activeLayout)) return true;
        string[] owners = root.Ownership.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return owners.Length == 0 || owners.Any(owner =>
            owner.Equals("shared", StringComparison.OrdinalIgnoreCase) ||
            owner.Equals(activeLayout, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool ShouldRenderRoute(TrackRoute route, string? activeLayout,
        IReadOnlyCollection<string> layouts)
    {
        if (string.IsNullOrWhiteSpace(activeLayout)) return true;
        string[] segments = route.Id.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? routeLayout = layouts.FirstOrDefault(layout =>
            segments.Contains(layout, StringComparer.OrdinalIgnoreCase));
        return routeLayout is null || routeLayout.Equals(activeLayout, StringComparison.OrdinalIgnoreCase);
    }

    private BoundsAccumulator AddNode(TrackNode node, Dictionary<MaterialBatchKey, List<Vertex>> batches,
        BoundsAccumulator bounds)
    {
        var nodeBounds = new BoundsAccumulator();
        if (!node.IsVisible) return nodeBounds;
        if (node.Mesh is { } mesh && ShouldRenderMesh(mesh, CollisionOverlay))
        {
            Vector4 color = ResolveColor(mesh, node);
            TrackMaterial? material = Project is not null && (uint)mesh.MaterialIndex < Project.Scene.Materials.Count
                ? Project.Scene.Materials[mesh.MaterialIndex]
                : null;
            string textureName = ResolveDiffuseTexture(mesh) ?? string.Empty;
            uint alphaMode = ResolveAlphaMode(material);
            if (mesh.SourceTransparent) alphaMode |= 2u;
            var batchKey = new MaterialBatchKey(textureName, alphaMode,
                material?.DepthMode ?? MaterialDepthMode.Normal, ResolveAlphaReference(material));
            if (!batches.TryGetValue(batchKey, out List<Vertex>? output))
                batches[batchKey] = output = [];
            var meshBounds = new BoundsAccumulator();
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                int ia = mesh.Indices[i], ib = mesh.Indices[i + 1], ic = mesh.Indices[i + 2];
                if ((uint)ia >= mesh.Positions.Count || (uint)ib >= mesh.Positions.Count || (uint)ic >= mesh.Positions.Count) continue;
                Vector3 a = mesh.Positions[ia].ToVector(), b = mesh.Positions[ib].ToVector(), c = mesh.Positions[ic].ToVector();
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.LengthSquared() < 1e-10f) continue;
                face = Vector3.Normalize(face);
                AddVertex(output, a, GetNormal(mesh, ia, face), color, GetUv(mesh, ia));
                AddVertex(output, b, GetNormal(mesh, ib, face), color, GetUv(mesh, ib));
                AddVertex(output, c, GetNormal(mesh, ic, face), color, GetUv(mesh, ic));
                bounds.Include(a); bounds.Include(b); bounds.Include(c);
                meshBounds.Include(a); meshBounds.Include(b); meshBounds.Include(c);
                nodeBounds.Include(a); nodeBounds.Include(b); nodeBounds.Include(c);
            }
            if (meshBounds.HasValue)
                _pickCandidates.Add(new PickCandidate(node, new NodeBounds(meshBounds.Center, meshBounds.Radius)));
        }
        foreach (TrackNode child in node.Children) nodeBounds.Include(AddNode(child, batches, bounds));
        if (nodeBounds.HasValue)
            _selectionBounds[node] = new NodeBounds(nodeBounds.Center, nodeBounds.Radius);
        return nodeBounds;
    }

    private string? ResolveDiffuseTexture(TrackMesh mesh)
    {
        if (Project is null || (uint)mesh.MaterialIndex >= Project.Scene.Materials.Count) return null;
        TrackMaterial material = Project.Scene.Materials[mesh.MaterialIndex];
        string? mapped = material.TextureSlots
            .Where(slot => slot.Key.Equals("txDiffuse", StringComparison.OrdinalIgnoreCase))
            .Select(slot => slot.Value).FirstOrDefault()
            ?? material.TextureSlots
                .Where(slot => slot.Key.Contains("diffuse", StringComparison.OrdinalIgnoreCase) ||
                               slot.Key.Contains("albedo", StringComparison.OrdinalIgnoreCase) ||
                               slot.Key.Contains("base", StringComparison.OrdinalIgnoreCase))
                .Select(slot => slot.Value).FirstOrDefault()
            ?? material.TextureSlots
                .Where(slot => !slot.Key.Contains("normal", StringComparison.OrdinalIgnoreCase) &&
                               !slot.Key.Contains("map", StringComparison.OrdinalIgnoreCase) &&
                               !slot.Key.Contains("mask", StringComparison.OrdinalIgnoreCase))
                .Select(slot => slot.Value).FirstOrDefault();
        if (mapped is null) return null;
        return Project.Scene.Textures.FirstOrDefault(texture =>
            texture.Name.Equals(mapped, StringComparison.OrdinalIgnoreCase))?.Name;
    }

    internal static uint ResolveAlphaMode(TrackMaterial? material)
    {
        if (material is null) return 0;
        uint mode = material.AlphaTested || material.BlendMode == MaterialBlendMode.AlphaToCoverage ? 1u : 0u;
        if (material.BlendMode == MaterialBlendMode.AlphaBlend) mode |= 2u;
        return mode;
    }

    internal static bool ShouldRenderMesh(TrackMesh mesh, bool collisionOverlay) =>
        collisionOverlay || (mesh.SourceVisible && mesh.SourceRenderable);

    internal static float ResolveAlphaReference(TrackMaterial? material)
    {
        if (material is not null)
        {
            float[]? values = material.Properties
                .FirstOrDefault(property => property.Key.Equals("ksAlphaRef", StringComparison.OrdinalIgnoreCase) ||
                                            property.Key.Equals("alphaRef", StringComparison.OrdinalIgnoreCase)).Value;
            if (values is { Length: > 0 } && float.IsFinite(values[0]))
                return Math.Clamp(values[0], 0, 1);
        }
        return 0.5f;
    }

    private Vector4 ResolveColor(TrackMesh mesh, TrackNode node)
    {
        if (ReferenceEquals(node, SelectedNode)) return new Vector4(1.0f, 0.55f, 0.12f, 1);
        if (CollisionOverlay)
        {
            return mesh.CollisionRole switch
            {
                CollisionRole.Driveable => new Vector4(0.10f, 0.72f, 0.45f, 1),
                CollisionRole.Terrain => new Vector4(0.50f, 0.40f, 0.20f, 1),
                CollisionRole.Barrier => new Vector4(0.92f, 0.22f, 0.20f, 1),
                CollisionRole.Camera => new Vector4(0.60f, 0.28f, 0.82f, 1),
                CollisionRole.Water => new Vector4(0.08f, 0.55f, 0.85f, 1),
                CollisionRole.VisualOnly => new Vector4(0.28f, 0.30f, 0.34f, 1),
                _ => new Vector4(0.45f, 0.49f, 0.54f, 1)
            };
        }
        int hash = HashCode.Combine(mesh.MaterialIndex, mesh.Name);
        return new Vector4(0.30f + (hash & 31) / 180f, 0.34f + ((hash >> 5) & 31) / 200f,
            0.38f + ((hash >> 10) & 31) / 180f, 1);
    }

    private static Vector3 GetNormal(TrackMesh mesh, int index, Vector3 fallback) =>
        index < mesh.Normals.Count && mesh.Normals[index].ToVector().LengthSquared() > 0.001f
            ? mesh.Normals[index].ToVector() : fallback;

    private static Vector2 GetUv(TrackMesh mesh, int index) => index < mesh.TextureCoordinates.Count
        ? new Vector2(mesh.TextureCoordinates[index].X, mesh.TextureCoordinates[index].Y)
        : Vector2.Zero;

    private static void AddRoute(TrackRoute route, List<Vertex> output, BoundsAccumulator bounds)
    {
        if (route.Points.Count < 2) return;
        int segmentCount = route.IsClosed ? route.Points.Count : route.Points.Count - 1;
        for (int index = 0; index < segmentCount; index++)
        {
            RoutePoint current = route.Points[index];
            RoutePoint next = route.Points[(index + 1) % route.Points.Count];
            Vector3 a = current.Position.ToVector() + new Vector3(0, 0.04f, 0);
            Vector3 b = next.Position.ToVector() + new Vector3(0, 0.04f, 0);
            Vector3 direction = b - a;
            var side = new Vector3(-direction.Z, 0, direction.X);
            if (side.LengthSquared() < 0.001f) continue;
            side = Vector3.Normalize(side);
            float left = Math.Max(1.5f, current.LeftWidth);
            float right = Math.Max(1.5f, current.RightWidth);
            Vector3 al = a + side * left, ar = a - side * right;
            Vector3 bl = b + side * Math.Max(1.5f, next.LeftWidth), br = b - side * Math.Max(1.5f, next.RightWidth);
            AddQuad(output, al, bl, br, ar, new Vector4(0.08f, 0.55f, 0.66f, 0.82f));
            bounds.Include(al); bounds.Include(ar); bounds.Include(bl); bounds.Include(br);
        }
    }

    private static void AddGrid(List<Vertex> output, BoundsAccumulator bounds)
    {
        const int count = 20;
        const float spacing = 10;
        var color = new Vector4(0.15f, 0.18f, 0.22f, 1);
        for (int i = -count; i <= count; i++)
        {
            float offset = i * spacing;
            AddQuad(output, new Vector3(-count * spacing, -0.02f, offset - 0.02f),
                new Vector3(count * spacing, -0.02f, offset - 0.02f),
                new Vector3(count * spacing, -0.02f, offset + 0.02f),
                new Vector3(-count * spacing, -0.02f, offset + 0.02f), color);
            AddQuad(output, new Vector3(offset - 0.02f, -0.02f, -count * spacing),
                new Vector3(offset + 0.02f, -0.02f, -count * spacing),
                new Vector3(offset + 0.02f, -0.02f, count * spacing),
                new Vector3(offset - 0.02f, -0.02f, count * spacing), color);
        }
    }

    private void AddGizmo(TrackNode node, List<Vertex> output, BoundsAccumulator bounds)
    {
        Vector3 center = _selectionBounds.TryGetValue(node, out NodeBounds selectedBounds)
            ? selectedBounds.Center
            : new Vector3(node.Transform.M41, node.Transform.M42, node.Transform.M43);
        float size = Math.Clamp(_sceneRadius * 0.08f, 1, 20);
        AddBox(output, center + Vector3.UnitX * size * 0.5f, new Vector3(size, size * 0.045f, size * 0.045f), new Vector4(0.9f, 0.16f, 0.12f, 1));
        AddBox(output, center + Vector3.UnitY * size * 0.5f, new Vector3(size * 0.045f, size, size * 0.045f), new Vector4(0.14f, 0.82f, 0.30f, 1));
        AddBox(output, center + Vector3.UnitZ * size * 0.5f, new Vector3(size * 0.045f, size * 0.045f, size), new Vector4(0.10f, 0.46f, 0.95f, 1));
        bounds.Include(center);
    }

    private void Pick(Point point)
    {
        if (_lastWidth <= 0 || _lastHeight <= 0) return;
        PickCandidate? best = null;
        double bestDistance = 28;
        foreach (PickCandidate candidate in _pickCandidates)
        {
            Vector4 clip = Vector4.Transform(new Vector4(candidate.Center, 1), _lastViewProjection);
            if (clip.W <= 0.001f) continue;
            float x = (clip.X / clip.W * 0.5f + 0.5f) * _lastWidth;
            float y = (-clip.Y / clip.W * 0.5f + 0.5f) * _lastHeight;
            double distance = Math.Sqrt((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y));
            if (distance < bestDistance) { best = candidate; bestDistance = distance; }
        }
        if (best is null) return;
        if (ReferenceEquals(best.Node, SelectedNode))
        {
            FrameSelectedNode(best.Node);
            Invalidate();
        }
        else
        {
            SelectedNode = best.Node;
        }
    }

    private void FrameSelectedNode(TrackNode node)
    {
        NodeBounds bounds = _selectionBounds.TryGetValue(node, out NodeBounds found)
            ? found
            : new NodeBounds(new Vector3(node.Transform.M41, node.Transform.M42, node.Transform.M43), 1);
        _sceneCenter = bounds.Center;

        float aspect = _lastWidth > 0 && _lastHeight > 0
            ? Math.Max(0.1f, _lastWidth / (float)_lastHeight)
            : 16f / 9f;
        const float verticalHalfFov = MathF.PI / 6;
        float horizontalHalfFov = MathF.Atan(MathF.Tan(verticalHalfFov) * aspect);
        float limitingHalfFov = Math.Min(verticalHalfFov, horizontalHalfFov);
        float fittedRadius = Math.Max(0.000001f, bounds.Radius);
        float fittedDistance = fittedRadius / MathF.Sin(limitingHalfFov) * 1.15f;
        if (Orthographic)
            _orthographicHeight = Math.Max(0.000001f, fittedRadius * 2.3f);
        else
            _distance = fittedDistance;
    }

    private Matrix4x4 CreateViewProjection(int width, int height)
    {
        if (Orthographic)
        {
            float aspect = Math.Max(0.1f, width / (float)Math.Max(1, height));
            Matrix4x4 topView = Matrix4x4.CreateLookAt(_sceneCenter + Vector3.UnitY * _distance,
                _sceneCenter, Vector3.UnitZ);
            float heightMetres = Math.Max(0.000001f, _orthographicHeight);
            return topView * Matrix4x4.CreateOrthographic(heightMetres * aspect, heightMetres,
                0.01f, Math.Max(1000, _distance + _sceneRadius * 5));
        }
        (Vector3 forward, _, Vector3 up) = CameraAxes();
        Vector3 eye = _sceneCenter - forward * _distance;
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, _sceneCenter, up);
        (float nearPlane, float farPlane) = CalculateClipPlanes(_distance, _sceneRadius);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3,
            Math.Max(0.1f, width / (float)Math.Max(1, height)), nearPlane, farPlane);
        return view * projection;
    }

    internal static (float Near, float Far) CalculateClipPlanes(float distance, float sceneRadius)
    {
        float farPlane = Math.Max(100f, distance + sceneRadius * 4f);
        float nearPlane = Math.Max(0.000001f, distance / 10_000f);
        return (nearPlane, farPlane);
    }

    private (Vector3 Forward, Vector3 Right, Vector3 Up) CameraAxes()
    {
        var forward = Vector3.Normalize(new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw), -MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw)));
        var right = new Vector3(MathF.Cos(_yaw), 0, -MathF.Sin(_yaw));
        var up = Vector3.Normalize(Vector3.Cross(forward, right));
        return (forward, right, up);
    }

    private void UploadTextures(ID3D11Device device)
    {
        DisposeTextures();
        _uploadedTextureProject = Project;
        if (Project is null) return;

        foreach (TrackTexture texture in Project.Scene.Textures)
        {
            if (texture.EmbeddedData is not { Length: > 0 } data || _textureViews.ContainsKey(texture.Name))
                continue;
            try
            {
                _textureViews.Add(texture.Name, DdsTextureLoader.CreateShaderResourceView(device, data));
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or
                                                OverflowException or SharpGenException)
            {
                System.Diagnostics.Debug.WriteLine($"Could not preview texture '{texture.Name}': {exception.Message}");
            }
        }
    }

    private void DisposeTextures()
    {
        foreach (ID3D11ShaderResourceView view in _textureViews.Values) view.Dispose();
        _textureViews.Clear();
        _uploadedTextureProject = null;
    }

    private void DisposeBuffers() { foreach (ID3D11Buffer buffer in _vertexBuffers) buffer.Dispose(); _vertexBuffers.Clear(); }
    private static void AddVertex(List<Vertex> output, Vector3 position, Vector3 normal, Vector4 color, Vector2 uv = default) =>
        output.Add(new Vertex(position, normal, color, uv));
    private static void AddTriangle(List<Vertex> output, Vector3 a, Vector3 b, Vector3 c, Vector4 color)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        normal = normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.UnitY;
        AddVertex(output, a, normal, color); AddVertex(output, b, normal, color); AddVertex(output, c, normal, color);
    }
    private static void AddQuad(List<Vertex> output, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 color)
    { AddTriangle(output, a, b, c, color); AddTriangle(output, a, c, d, color); }
    private static void AddBox(List<Vertex> output, Vector3 center, Vector3 size, Vector4 color)
    {
        Vector3 h = size / 2;
        Vector3[] p = [center + new Vector3(-h.X,-h.Y,-h.Z), center + new Vector3(h.X,-h.Y,-h.Z),
            center + new Vector3(h.X,h.Y,-h.Z), center + new Vector3(-h.X,h.Y,-h.Z),
            center + new Vector3(-h.X,-h.Y,h.Z), center + new Vector3(h.X,-h.Y,h.Z),
            center + new Vector3(h.X,h.Y,h.Z), center + new Vector3(-h.X,h.Y,h.Z)];
        AddQuad(output,p[0],p[3],p[2],p[1],color); AddQuad(output,p[4],p[5],p[6],p[7],color);
        AddQuad(output,p[0],p[1],p[5],p[4],color); AddQuad(output,p[3],p[7],p[6],p[2],color);
        AddQuad(output,p[1],p[2],p[6],p[5],color); AddQuad(output,p[0],p[4],p[7],p[3],color);
    }

    private readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 UV) { public const uint SizeInBytes = 48; }
    private readonly record struct FrameConstants(Matrix4x4 ViewProjection, uint RenderMode, uint HasTexture,
        uint AlphaMode, float AlphaReference);
    private readonly record struct MaterialBatchKey(string TextureName, uint AlphaMode,
        MaterialDepthMode DepthMode, float AlphaReference);
    private readonly record struct RenderChunk(Vertex[] Vertices, string? TextureName, uint AlphaMode,
        MaterialDepthMode DepthMode, float AlphaReference);
    private readonly record struct NodeBounds(Vector3 Center, float Radius);
    private sealed record PickCandidate(TrackNode Node, NodeBounds Bounds)
    {
        public Vector3 Center => Bounds.Center;
    }
    private sealed class BoundsAccumulator
    {
        private Vector3 _min = new(float.PositiveInfinity), _max = new(float.NegativeInfinity);
        public bool HasValue { get; private set; }
        public Vector3 Center => (_min + _max) * 0.5f;
        public float Radius => Vector3.Distance(_min, _max) * 0.5f;
        public void Include(Vector3 point) { _min = Vector3.Min(_min, point); _max = Vector3.Max(_max, point); HasValue = true; }
        public void Include(BoundsAccumulator other)
        {
            if (!other.HasValue) return;
            Include(other._min);
            Include(other._max);
        }
    }
}
