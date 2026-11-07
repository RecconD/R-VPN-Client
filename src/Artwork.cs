using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Xml;

namespace RVpn
{
    internal static class Art
    {
        internal static string Resource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("Missing resource");
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        internal static Geometry Logo()
        {
            var xml = new XmlDocument { XmlResolver = null };
            xml.LoadXml(Resource("logo.svg"));
            var geometry = Geometry.Parse(xml.DocumentElement.SelectSingleNode("*[local-name()='path']").Attributes["d"].Value);
            geometry.Freeze();
            return geometry;
        }

        internal static Color ColorOf(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        internal static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush(ColorOf(hex)); brush.Freeze(); return brush;
        }

        internal static LinearGradientBrush Gradient(string first, string last, double angle)
        {
            var brush = new LinearGradientBrush(ColorOf(first), ColorOf(last), angle); brush.Freeze(); return brush;
        }

        internal static RadialGradientBrush Radial(string first, string last, Point origin, double radius)
        {
            var brush = new RadialGradientBrush(ColorOf(first), ColorOf(last)) {
                Center = origin, GradientOrigin = origin, RadiusX = radius, RadiusY = radius
            };
            brush.Freeze();
            return brush;
        }

        internal static DropShadowEffect Shadow(string color, double depth, double radius, double opacity)
        {
            var effect = new DropShadowEffect {
                Color = ColorOf(color), Direction = 315, ShadowDepth = depth, BlurRadius = radius, Opacity = opacity
            };
            effect.Freeze();
            return effect;
        }

        internal static ImageSource WindowIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RVPN.ico"))
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return decoder.Frames.First(frame => frame.PixelWidth == 64);
            }
        }

        internal static BitmapSource WindowScreenshot(System.Windows.Controls.Border surface)
        {
            var dpi = VisualTreeHelper.GetDpi(surface);
            var visual = new DrawingVisual();
            var rect = new Rect(surface.RenderSize);
            var radius = surface.CornerRadius.TopLeft;
            using (var drawing = visual.RenderOpen())
                drawing.DrawRoundedRectangle(new VisualBrush(surface) { Viewbox = rect, ViewboxUnits = BrushMappingMode.Absolute },
                    null, rect, radius, radius);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(rect.Width * dpi.DpiScaleX),
                (int)Math.Ceiling(rect.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        internal static void WritePng(BitmapSource bitmap, Stream target)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(target);
        }
    }

    internal sealed class DialArtwork : FrameworkElement
    {
        private const int TickCount = 48;
        private static readonly Vector[] Directions = Enumerable.Range(0, TickCount).Select(index =>
            new Vector(Math.Sin(index * Math.PI / 24), -Math.Cos(index * Math.PI / 24))).ToArray();
        private static readonly Pen DarkTick = TickPen("#A4A095", 1);
        private static readonly Pen LightTick = TickPen("#7C715A", 1);
        private static readonly Pen DarkAccent = TickPen("#EFD071", 1.55);
        private static readonly Pen LightAccent = TickPen("#896714", 1.55);
        private static readonly Brush Halo = Art.Radial("#72EAC102", "#00EAC102", new Point(.5, .5), .5);
        private static readonly DependencyProperty SweepPositionProperty = DependencyProperty.Register(
            "SweepPosition", typeof(double), typeof(DialArtwork),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
        private bool online;
        private bool sweeping;

        internal DialArtwork()
        {
            Loaded += (sender, args) => RefreshMotion();
            IsVisibleChanged += (sender, args) => RefreshMotion();
            Unloaded += (sender, args) => StopSweep();
        }

        internal bool Online
        {
            get { return online; }
            set
            {
                if (online == value) return;
                online = value;
                RefreshMotion();
                InvalidateVisual();
            }
        }
        internal bool Light { get; set; }
        internal double SweepPosition { get { return (double)GetValue(SweepPositionProperty); } }

        internal void RefreshMotion()
        {
            var window = Window.GetWindow(this);
            var active = Online && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation &&
                (window == null || window.WindowState != WindowState.Minimized);
            if (!active) { StopSweep(); return; }
            if (sweeping) return;
            sweeping = true;
            BeginAnimation(SweepPositionProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(4.08))
                { RepeatBehavior = RepeatBehavior.Forever });
        }

        private void StopSweep()
        {
            if (!sweeping) return;
            sweeping = false;
            BeginAnimation(SweepPositionProperty, null);
        }

        private static Pen TickPen(string color, double width)
        {
            var pen = new Pen(Art.Brush(color), width);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext drawing)
        {
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            var radius = Math.Min(Math.Min(ActualWidth, ActualHeight) / 2 - 10, 153);
            if (radius < 102) return;

            if (Online) drawing.DrawEllipse(Halo, null, center, radius * .85, radius * .85);
            var tick = Light ? LightTick : DarkTick;
            var accent = Light ? LightAccent : DarkAccent;
            var turn = SweepPosition;
            for (var index = 0; index < TickCount; index++)
            {
                var length = index % 4 == 0 ? 10 : 4;
                var start = center + Directions[index] * (radius - length);
                var end = center + Directions[index] * radius;
                drawing.PushOpacity(Online ? .34 : .27);
                drawing.DrawLine(tick, start, end);
                drawing.Pop();
                if (!Online) continue;
                var phase = (turn + 1 - (double)index / TickCount) % 1;
                var opacity = sweeping ? WaveOpacity(phase) : .12;
                if (opacity <= 0) continue;
                drawing.PushOpacity(opacity);
                drawing.DrawLine(accent, start, end);
                drawing.Pop();
            }
        }

        private static double WaveOpacity(double phase)
        {
            if (phase < .03) return phase / .03 * .72;
            if (phase < .07) return .72 - (phase - .03) / .04 * .33;
            if (phase < .13) return .39 - (phase - .07) / .06 * .26;
            return phase < .22 ? .13 * (.22 - phase) / .09 : 0;
        }
    }

    internal sealed class SegmentDisplay : FrameworkElement
    {
        private static readonly string[] Digits = { "abcdef", "bc", "abdeg", "abcdg", "bcfg",
            "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
        private static readonly Rect[] Segments = {
            new Rect(2, 0, 6, 1), new Rect(9, 1, 1, 5), new Rect(9, 8, 1, 5),
            new Rect(2, 13, 6, 1), new Rect(0, 8, 1, 5), new Rect(0, 1, 1, 5), new Rect(2, 6.5, 6, 1)
        };
        private Brush ink;
        internal string DigitsText { get; private set; }

        internal void SetDisplay(string text, Brush brush)
        {
            if (DigitsText == text && ink == brush) return;
            DigitsText = text;
            ink = brush;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            if (String.IsNullOrEmpty(DigitsText) || ActualWidth == 0 || ActualHeight == 0) return;
            var width = DigitsText.Length * 12 - 2;
            var scale = Math.Min(ActualWidth / width, ActualHeight / 14);
            drawing.PushTransform(new TranslateTransform((ActualWidth - width * scale) / 2, (ActualHeight - 14 * scale) / 2));
            drawing.PushTransform(new ScaleTransform(scale, scale));
            for (var index = 0; index < DigitsText.Length; index++)
            {
                foreach (var segment in Digits[DigitsText[index] - '0'])
                {
                    var shape = Segments[segment - 'a'];
                    shape.Offset(index * 12, 0);
                    drawing.DrawRectangle(ink, null, shape);
                }
            }
            drawing.Pop();
            drawing.Pop();
        }
    }
}
