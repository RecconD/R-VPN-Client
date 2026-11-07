using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;

namespace RVpn
{
    internal static class BrandDialog
    {
        private static Brush Color(string value)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }

        internal static bool Confirm(Window owner, string title, string message, string primary, string secondary)
        {
            StackPanel body;
            var window = Create(owner, title, message, out body);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 17, 0, 0) };
            if (secondary != null)
            {
                var cancel = Button(secondary, false, owner);
                cancel.IsCancel = true;
                cancel.Margin = new Thickness(0, 0, 8, 0);
                actions.Children.Add(cancel);
            }
            var accept = Button(primary, true, owner);
            accept.IsDefault = true;
            accept.Click += (sender, args) => window.DialogResult = true;
            actions.Children.Add(accept);
            body.Children.Add(actions);
            return window.ShowDialog() == true;
        }

        internal static void Notify(Window owner, string title, string message)
        {
            Confirm(owner, title, message, "Понятно", null);
        }

        internal static Window Progress(Window owner, string title, string message)
        {
            StackPanel body;
            var window = Create(owner, title, message, out body);
            body.Children.Add(new ProgressBar { Height = 6, IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 0),
                Foreground = Color("#FED215"), Background = Surface(owner, "Inset", "#191919"), BorderThickness = new Thickness(0) });
            return window;
        }

        private static Window Create(Window owner, string title, string message, out StackPanel body)
        {
            body = new StackPanel();
            var window = new Window {
                Owner = owner, Title = title, Width = 400, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = Surface(owner, "Panel", "#252524"), Foreground = Surface(owner, "Text", "#F2F1EA"),
                FontFamily = owner == null ? new FontFamily("Segoe UI") : owner.FontFamily
            };
            if (owner != null) window.Icon = owner.Icon;
            WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(16) });
            window.Content = new Border { Background = window.Background, BorderBrush = Surface(owner, "Line", "#41403B"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Child = body };
            var heading = new DockPanel();
            var close = Button("×", false, owner);
            close.Padding = new Thickness(6, 1, 6, 1);
            close.ToolTip = "Закрыть";
            close.Click += (sender, args) => window.Close();
            DockPanel.SetDock(close, Dock.Right);
            heading.Children.Add(close);
            heading.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 18,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(heading);
            body.Children.Add(new TextBlock { Text = message, Foreground = Surface(owner, "Muted", "#AAA9A1"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
            return window;
        }

        private static Brush Surface(Window owner, string key, string fallback)
        {
            return (owner == null ? null : owner.TryFindResource(key) as Brush) ?? Color(fallback);
        }

        private static Button Button(string label, bool primary, Window owner)
        {
            var button = new Button {
                Content = label, Padding = new Thickness(12, 7, 12, 7), FontWeight = FontWeights.SemiBold,
                Background = primary ? Color("#FED215") : Surface(owner, "Inset", "#191919"),
                Foreground = primary ? Color("#211B0F") : Surface(owner, "Text", "#F2F1EA"),
                BorderBrush = primary ? Color("#FED215") : Surface(owner, "Line", "#41403B"), BorderThickness = new Thickness(1)
            };
            var shell = new FrameworkElementFactory(typeof(Border), "Shell");
            shell.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
                shell.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            shell.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = shell };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, .84, "Shell"));
            template.Triggers.Add(hover);
            button.Template = template;
            return button;
        }
    }
}
