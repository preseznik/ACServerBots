using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ACEditor.Core.Models;

namespace ACEditor.App.Controls;

public sealed class RouteElevationProfile : FrameworkElement
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(TrackProject), typeof(RouteElevationProfile),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public TrackProject? Project { get => (TrackProject?)GetValue(ProjectProperty); set => SetValue(ProjectProperty, value); }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var background = new SolidColorBrush(Color.FromRgb(17, 21, 27));
        context.DrawRectangle(background, null, new Rect(RenderSize));
        Rect plot = new(48, 10, Math.Max(1, ActualWidth - 60), Math.Max(1, ActualHeight - 32));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(47, 55, 66)), 1);
        for (int line = 0; line <= 4; line++)
        {
            double y = plot.Top + plot.Height * line / 4;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        TrackRoute? route = Project?.Routes.FirstOrDefault(item => item.Points.Count > 1);
        if (route is null)
        {
            DrawText(context, "No route loaded", new Point(plot.Left + 8, plot.Top + 8), Colors.Gray);
            return;
        }
        var distances = new double[route.Points.Count];
        for (int i = 1; i < route.Points.Count; i++)
            distances[i] = distances[i - 1] + System.Numerics.Vector3.Distance(
                route.Points[i - 1].Position.ToVector(), route.Points[i].Position.ToVector());
        double total = Math.Max(1, distances[^1]);
        float minimum = route.Points.Min(point => point.Position.Y);
        float maximum = route.Points.Max(point => point.Position.Y);
        float range = Math.Max(1, maximum - minimum);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            for (int i = 0; i < route.Points.Count; i++)
            {
                var point = new Point(plot.Left + plot.Width * distances[i] / total,
                    plot.Bottom - plot.Height * (route.Points[i].Position.Y - minimum) / range);
                if (i == 0) stream.BeginFigure(point, false, false); else stream.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(66, 188, 208)), 2), geometry);
        DrawText(context, $"{maximum:0} m", new Point(4, plot.Top - 4), Colors.Gray);
        DrawText(context, $"{minimum:0} m", new Point(4, plot.Bottom - 12), Colors.Gray);
        DrawText(context, $"{total:0} m", new Point(plot.Right - 48, plot.Bottom + 4), Colors.Gray);
    }

    private static void DrawText(DrawingContext context, string value, Point origin, Color color)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, new SolidColorBrush(color), 1);
        context.DrawText(text, origin);
    }
}
