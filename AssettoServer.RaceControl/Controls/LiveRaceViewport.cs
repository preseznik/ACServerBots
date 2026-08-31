using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AssettoServer.RaceControl.Core.Runtime;

namespace AssettoServer.RaceControl.Controls;

public sealed class LiveRaceViewport : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot), typeof(LiveRaceSnapshot), typeof(LiveRaceViewport),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(LiveTrackMap), typeof(LiveRaceViewport),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedSessionIdProperty = DependencyProperty.Register(
        nameof(SelectedSessionId), typeof(int), typeof(LiveRaceViewport),
        new FrameworkPropertyMetadata(-1,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty FullTrackProperty = DependencyProperty.Register(
        nameof(FullTrack), typeof(bool), typeof(LiveRaceViewport),
        new FrameworkPropertyMetadata(true,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty ZoomMetersProperty = DependencyProperty.Register(
        nameof(ZoomMeters), typeof(double), typeof(LiveRaceViewport),
        new FrameworkPropertyMetadata(180d,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty ChaseViewProperty = DependencyProperty.Register(
        nameof(ChaseView), typeof(bool), typeof(LiveRaceViewport),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<CarHitTarget> _carHitTargets = [];
    private LiveTrackMap? _arenaGeometryTrack;
    private Size _arenaGeometrySize;
    private WorldBounds _arenaGeometryBounds;
    private StreamGeometry? _arenaGeometry;

    public LiveRaceViewport()
    {
        // FrameworkElement does not clip custom drawing by default. Focus mode deliberately
        // renders only a window around one car, so the rest of the track must not bleed into
        // adjacent navigation and details panels.
        ClipToBounds = true;
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

    public bool FullTrack
    {
        get => (bool)GetValue(FullTrackProperty);
        set => SetValue(FullTrackProperty, value);
    }

    public double ZoomMeters
    {
        get => (double)GetValue(ZoomMetersProperty);
        set => SetValue(ZoomMetersProperty, value);
    }

    public bool ChaseView
    {
        get => (bool)GetValue(ChaseViewProperty);
        set => SetValue(ChaseViewProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        _carHitTargets.Clear();
        var background = Brush("PreviewBrush", Brushes.Black);
        var border = Brush("BorderBrush", Brushes.DimGray);
        drawingContext.DrawRectangle(background, new Pen(border, 1), new Rect(RenderSize));
        if (ActualWidth < 40 || ActualHeight < 40)
            return;

        var activeCars = Snapshot?.Cars.Where(car => car.IsActive).ToArray() ?? [];
        var selected = activeCars.FirstOrDefault(car => car.SessionId == SelectedSessionId);
        if (ChaseView && selected != null)
        {
            DrawChaseView(drawingContext, activeCars, selected);
            return;
        }
        if (!TryGetWorldBounds(activeCars, out var bounds))
        {
            DrawCenteredText(drawingContext, "WAITING FOR LIVE POSITION DATA", Brush("MutedTextBrush", Brushes.Gray));
            return;
        }

        const double padding = 28;
        double scale = Math.Min((ActualWidth - padding * 2) / Math.Max(1, bounds.Width),
            (ActualHeight - padding * 2) / Math.Max(1, bounds.Height));
        Point Map(float x, float z) => new(
            ActualWidth * 0.5 + (x - bounds.CenterX) * scale,
            ActualHeight * 0.5 + (z - bounds.CenterZ) * scale);

        DrawTrack(drawingContext, Map, scale, bounds);
        foreach (var car in activeCars.Where(car => car.SessionId != SelectedSessionId))
            DrawCar(drawingContext, car, Map, selected: false);
        if (selected != null)
            DrawCar(drawingContext, selected, Map, selected: true);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonDown(eventArgs);
        if (ChaseView)
            return;
        Point click = eventArgs.GetPosition(this);
        var hit = _carHitTargets
            .Select(target => (Target: target, DistanceSquared: (target.Center - click).LengthSquared))
            .Where(candidate => candidate.DistanceSquared <= 24 * 24)
            .OrderBy(candidate => candidate.DistanceSquared)
            .FirstOrDefault();
        if (hit.Target == default)
            return;

        SetCurrentValue(SelectedSessionIdProperty, hit.Target.SessionId);
        eventArgs.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        if (ChaseView)
        {
            eventArgs.Handled = true;
            return;
        }
        if (eventArgs.Delta == 0)
            return;

        const double minimumWidth = 50;
        const double maximumWidth = 600;
        double notches = Math.Max(1, Math.Abs(eventArgs.Delta) / 120d);
        if (FullTrack)
        {
            if (eventArgs.Delta < 0)
            {
                eventArgs.Handled = true;
                return;
            }

            var activeCars = Snapshot?.Cars.Where(car => car.IsActive).ToArray() ?? [];
            double fittedWidth = TryGetWorldBounds(activeCars, out var bounds)
                ? Math.Max(bounds.Width, bounds.Height)
                : ZoomMeters;
            SetCurrentValue(FullTrackProperty, false);
            SetCurrentValue(ZoomMetersProperty,
                Math.Clamp(fittedWidth * Math.Pow(0.85, notches), minimumWidth, maximumWidth));
            eventArgs.Handled = true;
            return;
        }

        double factor = Math.Pow(0.85, notches);
        double nextWidth = eventArgs.Delta > 0 ? ZoomMeters * factor : ZoomMeters / factor;
        if (eventArgs.Delta < 0 && ZoomMeters >= maximumWidth - 0.5)
            SetCurrentValue(FullTrackProperty, true);
        else
            SetCurrentValue(ZoomMetersProperty, Math.Clamp(nextWidth, minimumWidth, maximumWidth));
        eventArgs.Handled = true;
    }

    private bool TryGetWorldBounds(IReadOnlyList<LiveRaceCar> cars, out WorldBounds bounds)
    {
        var selected = cars.FirstOrDefault(car => car.SessionId == SelectedSessionId)
                       ?? cars.FirstOrDefault();
        if (!FullTrack && selected != null)
        {
            float half = (float)Math.Clamp(ZoomMeters * 0.5, 25, 500);
            bounds = new WorldBounds(selected.X - half, selected.X + half,
                selected.Z - half, selected.Z + half);
            return true;
        }

        if (Track is { HasFpsArena: true } fpsArena)
        {
            bounds = new WorldBounds(fpsArena.MinimumX, fpsArena.MaximumX,
                fpsArena.MinimumZ, fpsArena.MaximumZ).Expand(2);
            return true;
        }
        if (Track is { Points.Count: > 1 })
        {
            bounds = WorldBounds.From(Track.Points.Select(point => (point.X, point.Z)));
            bounds = bounds.Expand(Math.Max(10, Math.Max(bounds.Width, bounds.Height) * 0.05));
            return true;
        }
        if (cars.Count > 0)
        {
            bounds = WorldBounds.From(cars.Select(car => (car.X, car.Z))).Expand(60);
            return true;
        }

        bounds = default;
        return false;
    }

    private void DrawTrack(DrawingContext drawingContext, Func<float, float, Point> map,
        double scale, WorldBounds bounds)
    {
        if (Track is { HasFpsArena: true } arena)
        {
            DrawArena(drawingContext, arena, map, scale, bounds);
            return;
        }
        if (Track is not { Points.Count: > 1 })
            return;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(map(Track.Points[0].X, Track.Points[0].Z), false, false);
            foreach (var point in Track.Points.Skip(1))
                context.LineTo(map(point.X, point.Z), true, false);
            context.LineTo(map(Track.Points[0].X, Track.Points[0].Z), true, false);
        }
        geometry.Freeze();
        double averageWidth = Track.Points.Average(point => Math.Max(2, point.LeftWidth + point.RightWidth));
        double roadPixels = Math.Clamp(averageWidth * scale, 3, 30);
        drawingContext.DrawGeometry(null, new Pen(Brush("PanelRaisedBrush", Brushes.DarkSlateGray), roadPixels), geometry);
        drawingContext.DrawGeometry(null, new Pen(Brush("BorderBrush", Brushes.Gray), 1), geometry);
    }

    private void DrawArena(DrawingContext drawingContext, LiveTrackMap arena,
        Func<float, float, Point> map, double scale, WorldBounds bounds)
    {
        if (_arenaGeometry is null || !ReferenceEquals(_arenaGeometryTrack, arena)
                                   || _arenaGeometrySize != RenderSize
                                   || _arenaGeometryBounds != bounds)
        {
            var geometry = new StreamGeometry();
            double halfCell = Math.Max(0.75, arena.ArenaCellSize * scale * 0.5);
            using (var context = geometry.Open())
            {
                foreach (var cell in arena.ArenaCells)
                {
                    Point center = map(cell.X, cell.Z);
                    context.BeginFigure(center + new Vector(-halfCell, -halfCell), true, true);
                    context.LineTo(center + new Vector(halfCell, -halfCell), false, false);
                    context.LineTo(center + new Vector(halfCell, halfCell), false, false);
                    context.LineTo(center + new Vector(-halfCell, halfCell), false, false);
                }
            }
            geometry.Freeze();
            _arenaGeometry = geometry;
            _arenaGeometryTrack = arena;
            _arenaGeometrySize = RenderSize;
            _arenaGeometryBounds = bounds;
        }

        drawingContext.PushOpacity(0.42);
        drawingContext.DrawGeometry(Brush("PanelRaisedBrush", Brushes.DarkSlateGray), null,
            _arenaGeometry);
        drawingContext.Pop();
        Point first = map(arena.MinimumX, arena.MinimumZ);
        Point second = map(arena.MaximumX, arena.MaximumZ);
        var outline = new Rect(new Point(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
            new Point(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));
        drawingContext.DrawRectangle(null, new Pen(Brush("BorderBrush", Brushes.Gray), 1), outline);
    }

    private void DrawChaseView(DrawingContext drawingContext, IReadOnlyList<LiveRaceCar> cars,
        LiveRaceCar selected)
    {
        double horizon = ActualHeight * 0.24;
        drawingContext.DrawRectangle(Brush("PanelRaisedBrush", Brushes.DarkSlateGray), null,
            new Rect(0, 0, ActualWidth, horizon));
        drawingContext.DrawRectangle(Brush("PreviewBrush", Brushes.Black), null,
            new Rect(0, horizon, ActualWidth, ActualHeight - horizon));

        var forward = GetHeadingForward(selected);
        var side = new Vector(-forward.Y, forward.X);
        var roadSegments = new List<ChaseSegment>();
        if (Track is { Points.Count: > 1 })
        {
            for (int index = 0; index < Track.Points.Count; index++)
            {
                var first = Track.Points[index];
                var second = Track.Points[(index + 1) % Track.Points.Count];
                double worldDistance = Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Z - first.Z, 2));
                if (worldDistance > 80
                    || !TryProjectChase(first.X, first.Z, selected, forward, side, out var firstPoint, out double firstDepth)
                    || !TryProjectChase(second.X, second.Z, selected, forward, side, out var secondPoint, out double secondDepth))
                    continue;
                double depth = (firstDepth + secondDepth) * 0.5;
                double width = Math.Clamp((first.LeftWidth + first.RightWidth
                                           + second.LeftWidth + second.RightWidth) * ActualWidth
                                          / Math.Max(20, depth) * 0.32, 3, 150);
                roadSegments.Add(new ChaseSegment(firstPoint, secondPoint, depth, width));
            }
        }

        foreach (var segment in roadSegments.OrderByDescending(segment => segment.Depth))
        {
            drawingContext.DrawLine(new Pen(Brush("PanelRaisedBrush", Brushes.DimGray), segment.Width),
                segment.Start, segment.End);
            drawingContext.DrawLine(new Pen(Brush("BorderBrush", Brushes.Gray), 1),
                segment.Start, segment.End);
        }

        foreach (var car in cars.Where(car => car.SessionId != selected.SessionId)
                     .Select(car => (Car: car, Projection: ProjectCar(car, selected, forward, side)))
                     .Where(item => item.Projection.HasValue)
                     .OrderByDescending(item => item.Projection!.Value.Depth))
        {
            var projection = car.Projection!.Value;
            double scale = Math.Clamp(38 / projection.Depth, 0.35, 1.7);
            var rect = new Rect(projection.Point.X - 15 * scale, projection.Point.Y - 9 * scale,
                30 * scale, 18 * scale);
            Brush fill = car.Car.IsBot ? Brush("AccentBrush", Brushes.Red)
                : Brush("InfoBrush", Brushes.DeepSkyBlue);
            drawingContext.DrawRoundedRectangle(fill, new Pen(Brush("TextBrush", Brushes.White), 1),
                rect, 2, 2);
            if (projection.Depth < 70)
                DrawText(drawingContext, car.Car.Name,
                    new Point(rect.Left, rect.Top - 17), Brush("TextBrush", Brushes.White), 11);
        }

        double carWidth = Math.Clamp(ActualWidth * 0.075, 44, 78);
        double carHeight = carWidth * 0.52;
        var playerCar = new StreamGeometry();
        using (var context = playerCar.Open())
        {
            context.BeginFigure(new Point(ActualWidth * 0.5 - carWidth * 0.5, ActualHeight - 16), true, true);
            context.LineTo(new Point(ActualWidth * 0.5 - carWidth * 0.38, ActualHeight - carHeight), true, false);
            context.LineTo(new Point(ActualWidth * 0.5 + carWidth * 0.38, ActualHeight - carHeight), true, false);
            context.LineTo(new Point(ActualWidth * 0.5 + carWidth * 0.5, ActualHeight - 16), true, false);
        }
        playerCar.Freeze();
        drawingContext.DrawGeometry(Brush("WarningBrush", Brushes.Gold),
            new Pen(Brush("TextBrush", Brushes.White), 2), playerCar);
        DrawText(drawingContext, $"{selected.Name}  •  {selected.SpeedKmh:F0} km/h",
            new Point(16, ActualHeight - 34), Brush("TextBrush", Brushes.White), 13);
        DrawText(drawingContext, "MANUAL CONTROL  •  ↑ throttle  ↓ brake  ← → steer  •  Esc release",
            new Point(16, 14), Brush("TextBrush", Brushes.White), 12);
    }

    private (Point Point, double Depth)? ProjectCar(LiveRaceCar car, LiveRaceCar selected,
        Vector forward, Vector side) => TryProjectChase(car.X, car.Z, selected, forward, side,
        out var point, out double depth) ? (point, depth) : null;

    private bool TryProjectChase(float x, float z, LiveRaceCar selected, Vector forward, Vector side,
        out Point point, out double depth)
    {
        var relative = new Vector(x - selected.X, z - selected.Z);
        depth = Vector.Multiply(relative, forward) + 10;
        double lateral = Vector.Multiply(relative, side);
        if (depth is < 3 or > 240 || Math.Abs(lateral) > depth * 1.5)
        {
            point = default;
            return false;
        }
        double horizon = ActualHeight * 0.24;
        double bottom = ActualHeight - 54;
        double distanceRatio = Math.Clamp(depth / 240, 0, 1);
        point = new Point(ActualWidth * 0.5 + lateral * ActualWidth * 0.72 / depth,
            horizon + (bottom - horizon) * (1 - Math.Sqrt(distanceRatio)));
        return true;
    }

    private static Vector GetForward(LiveRaceCar car)
    {
        double speedSquared = car.VelocityX * car.VelocityX + car.VelocityZ * car.VelocityZ;
        if (speedSquared > 0.25)
        {
            double inverse = 1 / Math.Sqrt(speedSquared);
            return new Vector(car.VelocityX * inverse, car.VelocityZ * inverse);
        }
        return new Vector(-Math.Cos(car.HeadingRadians), Math.Sin(car.HeadingRadians));
    }

    private static Vector GetHeadingForward(LiveRaceCar car) =>
        new(-Math.Cos(car.HeadingRadians), Math.Sin(car.HeadingRadians));

    private void DrawCar(DrawingContext drawingContext, LiveRaceCar car,
        Func<float, float, Point> map, bool selected)
    {
        Point center = map(car.X, car.Z);
        double length = selected ? 13 : 10;
        double width = selected ? 8 : 6;
        double directionX;
        double directionZ;
        double speedSquared = car.VelocityX * car.VelocityX + car.VelocityZ * car.VelocityZ;
        if (speedSquared > 0.25)
        {
            double inverse = 1 / Math.Sqrt(speedSquared);
            directionX = car.VelocityX * inverse;
            directionZ = car.VelocityZ * inverse;
        }
        else
        {
            directionX = -Math.Cos(car.HeadingRadians);
            directionZ = Math.Sin(car.HeadingRadians);
        }
        var screenDirection = new Vector(directionX, directionZ);
        var side = new Vector(-screenDirection.Y, screenDirection.X);
        Point nose = center + screenDirection * length;
        Point rearLeft = center - screenDirection * (length * 0.65) + side * width;
        Point rearRight = center - screenDirection * (length * 0.65) - side * width;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(nose, true, true);
            context.LineTo(rearLeft, true, false);
            context.LineTo(rearRight, true, false);
        }
        geometry.Freeze();
        Brush fill = selected ? Brush("WarningBrush", Brushes.Gold)
            : car.IsBot ? Brush("AccentBrush", Brushes.Red)
            : Brush("InfoBrush", Brushes.DeepSkyBlue);
        drawingContext.DrawGeometry(fill, new Pen(Brush("TextBrush", Brushes.White), selected ? 2 : 1), geometry);
        _carHitTargets.Add(new CarHitTarget(car.SessionId, center));

        if (selected || !FullTrack || Snapshot?.IsFps == true)
        {
            string position = car.RacePosition.HasValue ? $"P{car.RacePosition}  " : string.Empty;
            string score = Snapshot?.IsFps == true
                ? $"  {car.Score} pts  {car.Kills}/{car.Deaths}"
                : string.Empty;
            DrawText(drawingContext, $"{position}{car.Name}{score}", center + new Vector(12, -22),
                Brush("TextBrush", Brushes.White), selected ? 13 : 11);
        }
    }

    private void DrawCenteredText(DrawingContext drawingContext, string text, Brush brush)
    {
        var formatted = FormatText(text, brush, 12);
        drawingContext.DrawText(formatted,
            new Point((ActualWidth - formatted.Width) / 2, (ActualHeight - formatted.Height) / 2));
    }

    private void DrawText(DrawingContext drawingContext, string text, Point origin, Brush brush, double size) =>
        drawingContext.DrawText(FormatText(text, brush, size), origin);

    private FormattedText FormatText(string text, Brush brush, double size) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush Brush(string resourceKey, Brush fallback) => TryFindResource(resourceKey) as Brush ?? fallback;

    private readonly record struct CarHitTarget(int SessionId, Point Center);
    private readonly record struct ChaseSegment(Point Start, Point End, double Depth, double Width);

    private readonly record struct WorldBounds(float MinimumX, float MaximumX, float MinimumZ, float MaximumZ)
    {
        public double Width => MaximumX - MinimumX;
        public double Height => MaximumZ - MinimumZ;
        public double CenterX => (MinimumX + MaximumX) * 0.5;
        public double CenterZ => (MinimumZ + MaximumZ) * 0.5;

        public WorldBounds Expand(double amount) => new(
            MinimumX - (float)amount, MaximumX + (float)amount,
            MinimumZ - (float)amount, MaximumZ + (float)amount);

        public static WorldBounds From(IEnumerable<(float X, float Z)> points)
        {
            using var enumerator = points.GetEnumerator();
            if (!enumerator.MoveNext())
                return default;
            float minX = enumerator.Current.X;
            float maxX = minX;
            float minZ = enumerator.Current.Z;
            float maxZ = minZ;
            while (enumerator.MoveNext())
            {
                minX = Math.Min(minX, enumerator.Current.X);
                maxX = Math.Max(maxX, enumerator.Current.X);
                minZ = Math.Min(minZ, enumerator.Current.Z);
                maxZ = Math.Max(maxZ, enumerator.Current.Z);
            }
            return new WorldBounds(minX, maxX, minZ, maxZ);
        }
    }
}
