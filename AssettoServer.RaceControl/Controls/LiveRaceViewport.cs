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

    private readonly List<CarHitTarget> _carHitTargets = [];

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

        DrawTrack(drawingContext, Map, scale);
        foreach (var car in activeCars.Where(car => car.SessionId != SelectedSessionId))
            DrawCar(drawingContext, car, Map, selected: false);
        var selected = activeCars.FirstOrDefault(car => car.SessionId == SelectedSessionId);
        if (selected != null)
            DrawCar(drawingContext, selected, Map, selected: true);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonDown(eventArgs);
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

    private void DrawTrack(DrawingContext drawingContext, Func<float, float, Point> map, double scale)
    {
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

        if (selected || !FullTrack)
        {
            string position = car.RacePosition.HasValue ? $"P{car.RacePosition}  " : string.Empty;
            DrawText(drawingContext, $"{position}{car.Name}", center + new Vector(12, -22),
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
