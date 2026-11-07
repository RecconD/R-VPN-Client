using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

[assembly: AssemblyTitle("R VPN")]
[assembly: AssemblyDescription("R VPN Desktop for Windows")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace RVpn
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var directory = AppDomain.CurrentDomain.BaseDirectory;
            int updateParent;
            if (args.Length == 2 && args[0] == "--apply-update" && Int32.TryParse(args[1], out updateParent))
                return WindowsUpdate.Apply(directory, updateParent);
            if (args.Length == 2 && args[0] == "--clean-update")
                Task.Run(() => WindowsUpdate.Clean(directory, System.IO.Path.Combine(directory, args[1])));
            Version approvedUpdate = null;
            if (args.Length == 2 && args[0] == "--approved-update" && Desktop.IsAdministrator())
                Version.TryParse(args[1], out approvedUpdate);
            WindowsShell.SetApplicationIdentity();
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            try
            {
                if (args.Length == 2 && args[0] == "--qa")
                    return DesktopQa.Run(app, directory, System.IO.Path.GetFullPath(args[1]));
                var settings = ClientSettings.Load(directory);
                var demo = settings.Demo || args.Contains("--demo");
                var local = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R VPN", demo ? "Preview" : "Client");
                string sid;
                using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
                using (var instance = new Mutex(false, @"Local\RVPN.Desktop." + sid))
                {
                    bool acquired;
                    try { acquired = instance.WaitOne(args.Contains("--connect") || approvedUpdate != null ? 10000 : 0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { MessageBox.Show("R VPN уже открыт.", "R VPN", MessageBoxButton.OK, MessageBoxImage.Information); return 0; }
                    try
                    {
                        var controller = new Desktop(directory, new ClientStore(local), settings, demo, approvedUpdate);
                        if (args.Contains("--connect") && !demo)
                            controller.Window.Loaded += async (sender, eventArgs) => await controller.ToggleConnection();
                        return app.Run(controller.Window);
                    }
                    finally { instance.ReleaseMutex(); }
                }
            }
            catch (UserFault problem)
            {
                MessageBox.Show(problem.Message, "R VPN", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            catch (Exception)
            {
                MessageBox.Show("Не удалось открыть R VPN. Распакуйте весь архив и повторите запуск.", "R VPN", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }

    internal enum LinkState { Off, Connecting, On, Stopping }
    internal enum DesktopPage { Home, Profile, Advanced, Rules }

    internal sealed partial class Desktop
    {
        internal Window Window { get; private set; }
        internal LinkState Connection { get; private set; }
        private readonly string directory;
        private readonly ClientStore store;
        private readonly ClientSettings settings;
        private readonly LocalState local;
        private readonly bool demo;
        private readonly ActivationClient activation;
        private readonly DialArtwork artwork;
        private readonly SegmentDisplay minutesDisplay, secondsDisplay;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly DispatcherTimer metricsTimer;
        private readonly DispatcherTimer sessionTimer;
        private readonly Stopwatch sessionClock = new Stopwatch();
        private CancellationTokenSource pending;
        private EngineSession session;
        private DateTimeOffset nextProfileRefresh, nextEntryPing;
        private Account account;
        private bool activating, formatting, sampling, closed, restoringRoute;
        private bool checkingServers;
        private int serverChangeCooldown;
        private string connectedNode;
        private ServerNode connectedLocation;
        private string[] appliedBypasses = new string[0];
        private string[] appliedDirectDomains = new string[0];
        private RoutingRule[] appliedRules = new RoutingRule[0];
        private Dictionary<string, bool> appliedSites = new Dictionary<string, bool>();
        private DesktopPage currentPage;
        private GradientStop activationHighlight;
        private string feedbackState = "";
        private string linkMessage;

        internal Desktop(string directory, ClientStore store, ClientSettings settings, bool demo, Version approvedUpdate = null)
        {
            this.directory = directory;
            this.store = store;
            this.settings = settings;
            this.demo = demo;
            local = store.Load();
            if (String.IsNullOrEmpty(local.nodeSelection)) local.nodeSelection = "auto";
            var savedBypasses = local.bypassServices ?? RouteService.All.Select(service => service.Id).ToArray();
            local.bypassServices = RouteService.All.Select(service => service.Id).Where(savedBypasses.Contains).ToArray();
            local.routingRules = (local.routingRules ?? RoutingRule.Defaults()).Where(rule => rule != null)
                .Select(rule => RoutingRule.Parse(rule.type, rule.value, rule.action)).Where(rule => rule != null)
                .Take(RoutingRule.Limit).ToArray();
            local.sites = (local.sites ?? new Dictionary<string, bool>()).Where(site => SiteDomain(site.Key) == site.Key)
                .Take(50).ToDictionary(site => site.Key, site => site.Value);
            if (demo) account = local.account;
            else if (local.account != null && settings.AcceptsSavedOrigin(local.origin))
            {
                try { account = local.account.Validate(null); }
                catch (UserFault) { account = null; }
            }
            if (!demo) activation = new ActivationClient(settings.Service);
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Window.xaml"))
                Window = (Window)XamlReader.Load(resource);
            Window.Title = demo ? "R VPN · Демо" : "R VPN";
            Get<TextBlock>("ClientVersion").Text = "Версия клиента " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            var updater = demo ? null : new WindowsUpdate(Window, settings.Service, directory, approvedUpdate, settings.automaticUpdateChecks);
            var checkUpdate = Get<Button>("CheckUpdate");
            var updateFeedback = Get<TextBlock>("UpdateFeedback");
            checkUpdate.Click += async (sender, args) =>
            {
                if (updater == null)
                {
                    updateFeedback.Text = "Демо · версия клиента актуальна.";
                    return;
                }
                checkUpdate.IsEnabled = false;
                try { await updater.Check(message => updateFeedback.Text = message); }
                finally { checkUpdate.IsEnabled = true; }
            };
            var fonts = new Uri("pack://application:,,,/");
            Window.FontFamily = new FontFamily(fonts, "./fonts/#Comfortaa");
            Window.Resources["BodyFont"] = Window.FontFamily;
            Window.Resources["WordmarkFont"] = new FontFamily(fonts, "./fonts/#Manrope");
            var logo = Art.Logo();
            Window.Icon = Art.WindowIcon();
            Get<System.Windows.Shapes.Path>("HeaderLogo").Data = logo;
            Get<System.Windows.Shapes.Path>("ButtonLogo").Data = logo;
            artwork = new DialArtwork();
            Get<Grid>("DialMount").Children.Add(artwork);
            minutesDisplay = new SegmentDisplay();
            secondsDisplay = new SegmentDisplay();
            Get<Grid>("MinuteDisplayMount").Children.Add(minutesDisplay);
            Get<Grid>("SecondDisplayMount").Children.Add(secondsDisplay);
            Window.StateChanged += (sender, args) => artwork.RefreshMotion();
            Get<Border>("DemoBadge").Visibility = demo ? Visibility.Visible : Visibility.Collapsed;
            LoadPalette();
            if (SystemParameters.ClientAreaAnimation)
                Get<Rectangle>("WordmarkDot").BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, .22, TimeSpan.FromMilliseconds(900)) {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
            var activate = Get<Button>("ActivateButton");
            activate.IsVisibleChanged += (sender, args) => AnimateActivation();
            activate.IsEnabledChanged += (sender, args) => AnimateActivation();
            MountRoutingControls();
            MountSites();
            MountRuleEditor();
            ShowPage(DesktopPage.Home);
            SetFeedback(DefaultFeedback(), "");
            SetConnection(LinkState.Off, null);
            PaintAccount();

            Get<Button>("HomeButton").Click += (sender, args) => ShowPage(DesktopPage.Home);
            Get<Button>("ProfileButton").Click += (sender, args) => ShowPage(DesktopPage.Profile);
            Get<Button>("AdvancedButton").Click += (sender, args) => ShowPage(DesktopPage.Advanced);
            Get<Button>("RulesButton").Click += (sender, args) => ShowPage(DesktopPage.Rules);
            Get<Button>("DarkTheme").Click += (sender, args) => SetTheme(false);
            Get<Button>("LightTheme").Click += (sender, args) => SetTheme(true);
            Get<Button>("AddSite").Click += (sender, args) => AddSite();
            var siteInput = Get<TextBox>("SiteInput");
            siteInput.TextChanged += (sender, args) =>
            {
                Get<TextBlock>("SiteFeedback").Visibility = Visibility.Collapsed;
            };
            siteInput.KeyDown += (sender, args) =>
            {
                if (args.Key != Key.Enter) return;
                args.Handled = true;
                AddSite();
            };
            Get<Button>("ConnectButton").Click += async (sender, args) => await ToggleConnection();
            Get<Button>("ChangeServer").Click += async (sender, args) => await ChangeServer();
            Get<Button>("ApplyRouting").Click += async (sender, args) => await Reconnect("Применяем маршруты…");
            Get<Button>("ActivateButton").Click += async (sender, args) => await Activate();
            Get<Button>("MinButton").Click += (sender, args) => Window.WindowState = WindowState.Minimized;
            Get<Button>("CloseButton").Click += (sender, args) => Window.Close();
            MountScreenshotShortcut();
            Window.PreviewKeyDown += (sender, args) =>
            {
                var screenshot = Get<Popup>("ScreenshotResult");
                if (args.Key == Key.Escape && screenshot.IsOpen)
                {
                    screenshot.IsOpen = false;
                    args.Handled = true;
                    return;
                }
                if (args.Key != Key.S || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;
                args.Handled = true;
                if (!args.IsRepeat) CopyScreenshot();
            };
            Get<Grid>("Header").MouseLeftButtonDown += (sender, args) =>
            {
                if (args.ClickCount == 1 && Window.WindowState == WindowState.Normal) Window.DragMove();
            };
            var code = Get<TextBox>("CodeInput");
            code.TextChanged += (sender, args) => FormatKey();
            code.PreviewKeyDown += async (sender, args) =>
            {
                if (args.Key == Key.Enter) { args.Handled = true; await Activate(); }
            };
            metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            metricsTimer.Tick += async (sender, args) => await Sample();
            metricsTimer.Start();
            sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            sessionTimer.Tick += (sender, args) => PaintSessionTime();
            sessionTimer.Start();
            Window.Closing += (sender, args) => Release();
        }

        internal T Get<T>(string name) where T : class { return (T)Window.FindName(name); }
        private Brush Token(string name) { return (Brush)Window.Resources[name]; }
        private void Text(string name, string value) { Get<TextBlock>(name).Text = value; }
        private void Metric(string name, string value)
        {
            var split = value.IndexOf(' ');
            Get<Run>(name + "Value").Text = split < 0 ? value : value.Substring(0, split);
            Get<Run>(name + "Unit").Text = split < 0 ? "" : value.Substring(split);
        }
        private string DefaultFeedback() { return demo ? "Демо-код RVPN-2026" : "Введите код, который выдал владелец VPN."; }

        private void LoadPalette()
        {
            var light = local.lightTheme;
            Window.Resources["Surface"] = Art.Gradient(light ? "#F2EEE3" : "#323232", light ? "#D5D0C4" : "#151515", 45);
            Window.Resources["Panel"] = Art.Gradient(light ? "#F3F0E9" : "#2D2D2C", light ? "#DEDAD0" : "#20201F", 42);
            Window.Resources["NavWell"] = Art.Gradient(light ? "#E0DACD" : "#151515", light ? "#EBE6DC" : "#1C1C1B", 90);
            Window.Resources["NavShadow"] = Art.Shadow(light ? "#685C47" : "#000000", 3, 9, light ? .17 : .48);
            Window.Resources["HeroBase"] = Art.Gradient(light ? "#EDE8DE" : "#373736", light ? "#D6D0C4" : "#171717", 47);
            Window.Resources["KeyIdle"] = Art.Gradient(light ? "#F6F2E9" : "#353535", light ? "#DED6C6" : "#1A1A19", 45);
            Window.Resources["KeyGradient"] = Art.Gradient("#FFD51D", "#EAC102", 12);
            Window.Resources["CardShadow"] = Art.Shadow(light ? "#685C47" : "#000000", 8, 22, light ? .22 : .47);
            Window.Resources["KeyShadow"] = Art.Shadow(light ? "#685C47" : "#000000", 6, 17, light ? .22 : .56);
            Window.Resources["KeyGlow"] = Art.Shadow("#FFD337", 0, 23, light ? .34 : .54);
            Window.Resources["HeroHighlight"] = Art.Radial(light ? "#37FFFFFF" : "#15FFFFFF", "#00FFFFFF", new Point(.18, .04), .73);
            Window.Resources["KeyBase"] = Art.Gradient(light ? "#CEC7B9" : "#101010", light ? "#E7E0D2" : "#20201E", 43);
            Window.Resources["KeyInset"] = Art.Radial("#00000000", light ? "#32584932" : "#80000000", new Point(.56, .56), .75);
            SetColor("Raised", light ? "#DED8CC" : "#242423");
            SetColor("Inset", light ? "#E3DED3" : "#1A1A19");
            SetColor("Text", light ? "#282620" : "#F2F2F0");
            SetColor("Muted", light ? "#5B554B" : "#B5B4AF");
            SetColor("Line", light ? "#C6C1B5" : "#3B3B39");
            SetColor("Groove", light ? "#D3CCC0" : "#141413");
            SetColor("Focus", light ? "#7D735D" : "#9C9A91");
            SetColor("FieldFocus", light ? "#EBE6DC" : "#212120");
            SetColor("Accent", light ? "#7A5B08" : "#FED215");
            SetColor("TrafficDown", light ? "#137D41" : "#38C970");
            SetColor("TrafficUp", light ? "#AA3029" : "#FF746A");
            SetColor("ClockIdle", light ? "#9D978A" : "#66665F");
            SetColor("ClockActive", light ? "#615B50" : "#B5B6AE");
            SetColor("Teal", light ? "#366C4E" : "#8AC49B");
            SetColor("Error", light ? "#962F38" : "#FF8B9A");
            SetColor("KeyLine", light ? "#C4BAA7" : "#3B3931");
            SetColor("Gold", "#FED215");
            SetColor("GoldLine", light ? "#B88B17" : "#FFE16A");
            SetColor("Ink", "#211B0F");
            Window.Resources["RouteTrackIdle"] = Art.Gradient(light ? "#D4CEC1" : "#121211", light ? "#E5DFD3" : "#1D1D1B", 90);
            Window.Resources["RouteTrackOn"] = Art.Gradient(light ? "#FBD124" : "#FED215", light ? "#EBBB05" : "#EAC102", 9);
            Window.Resources["RouteThumbIdle"] = Art.Gradient(light ? "#8F897D" : "#AAA8A2", light ? "#756E61" : "#8A8984", 90);
            SetColor("RouteDivider", light ? "#D1CABE" : "#353533");
            var switchGlow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = light ? 8 : 13,
                Opacity = light ? .30 : .60, Color = Art.ColorOf(light ? "#B98B22" : "#FFD645") };
            switchGlow.Freeze();
            Window.Resources["RouteGlow"] = switchGlow;
            Window.Resources["Brand"] = Art.Gradient("#FED215", "#EAC102", 15);
            if (activationHighlight != null) activationHighlight.BeginAnimation(GradientStop.OffsetProperty, null);
            activationHighlight = new GradientStop(Art.ColorOf("#FFE16A"), .25);
            Get<Button>("ActivateButton").Background = new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(Art.ColorOf("#FED215"), 0), activationHighlight,
                new GradientStop(Art.ColorOf("#EAC102"), 1)
            }, 15);
            artwork.Light = light;
            PaintSessionTime();
            SetTab("DarkTheme", !light);
            SetTab("LightTheme", light);
            var themeSelection = Get<Border>("ThemeSelection");
            SlideSelection(themeSelection, light ? themeSelection.Width + Get<Button>("LightTheme").Margin.Left : 0, 280);
        }

        private void AnimateActivation()
        {
            var activate = Get<Button>("ActivateButton");
            activationHighlight.BeginAnimation(GradientStop.OffsetProperty,
                activate.IsVisible && activate.IsEnabled && SystemParameters.ClientAreaAnimation
                    ? new DoubleAnimation(.25, .75, TimeSpan.FromSeconds(3.6)) {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    }
                    : null);
        }

        private void SetTheme(bool light)
        {
            if (local.lightTheme == light) return;
            local.lightTheme = light;
            TrySave();
            LoadPalette();
            ShowPage(currentPage);
            PaintFeedback();
            PaintConnection();
            AnimateActivation();
        }

        private void SetColor(string name, string color)
        { Window.Resources[name] = Art.Brush(color); }

        private void ShowPage(DesktopPage page)
        {
            currentPage = page;
            Get<Grid>("HomePage").Visibility = page == DesktopPage.Home ? Visibility.Visible : Visibility.Collapsed;
            Get<Grid>("ProfilePage").Visibility = page == DesktopPage.Profile ? Visibility.Visible : Visibility.Collapsed;
            Get<Grid>("AdvancedPage").Visibility = page == DesktopPage.Advanced ? Visibility.Visible : Visibility.Collapsed;
            Get<Grid>("RulesPage").Visibility = page == DesktopPage.Rules ? Visibility.Visible : Visibility.Collapsed;
            SetTab("HomeButton", page == DesktopPage.Home);
            SetTab("ProfileButton", page == DesktopPage.Profile);
            SetTab("AdvancedButton", page == DesktopPage.Advanced);
            SetTab("RulesButton", page == DesktopPage.Rules);
            if (page == DesktopPage.Rules) PaintRuleRows();
            var nav = Get<Border>("BottomNav");
            var selection = Get<Border>("NavSelection");
            var tabWidth = (nav.Width - nav.Padding.Left - nav.Padding.Right - nav.BorderThickness.Left - nav.BorderThickness.Right) / 4;
            selection.Width = tabWidth - selection.Margin.Left - selection.Margin.Right;
            SlideSelection(selection, tabWidth * (int)page, 330);
        }

        private void SlideSelection(Border selection, double to, int duration)
        {
            var slide = (TranslateTransform)selection.RenderTransform;
            if ((double)slide.GetAnimationBaseValue(TranslateTransform.XProperty) == to) return;
            var from = slide.X;
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = to;
            if (Window.IsLoaded && SystemParameters.ClientAreaAnimation)
            {
                var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration)),
                    new KeySpline(.22, .76, .31, 1)));
                slide.BeginAnimation(TranslateTransform.XProperty, animation);
            }
        }

        private void SetTab(string name, bool active)
        {
            var tab = Get<Button>(name);
            tab.Foreground = Token(active ? "Accent" : "Muted");
        }

        private void MountRoutingControls()
        {
            var panel = Get<StackPanel>("ServiceToggles");
            var icons = DataJson.Read<Dictionary<string, string>>(Art.Resource("route-icons.json"));
            foreach (var service in RouteService.All)
            {
                var toggle = new ToggleButton { Content = service.Name,
                    Tag = Geometry.Parse(icons[service.Id]),
                    BorderThickness = new Thickness(0, 0, 0, service == RouteService.All.Last() ? 0 : 1),
                    Style = (Style)Window.Resources["ServiceToggle"], IsChecked = !local.bypassServices.Contains(service.Id) };
                AutomationProperties.SetName(toggle, service.Name + " через VPN");
                AutomationProperties.SetAutomationId(toggle, "Route-" + service.Id);
                toggle.Checked += (sender, args) => SaveRoute(service.Id, toggle);
                toggle.Unchecked += (sender, args) => SaveRoute(service.Id, toggle);
                panel.Children.Add(toggle);
            }
        }

        private void SaveRoute(string id, ToggleButton toggle)
        {
            if (restoringRoute) return;
            var previous = local.bypassServices;
            local.bypassServices = RouteService.All.Select(service => service.Id)
                .Where(key => key == id ? toggle.IsChecked != true : previous.Contains(key)).ToArray();
            if (!TrySave())
            {
                local.bypassServices = previous;
                restoringRoute = true;
                toggle.IsChecked = !previous.Contains(id);
                restoringRoute = false;
                Text("RoutingNote", "Не удалось сохранить настройку маршрута.");
                return;
            }
            PaintRoutingNote();
        }

        private void PaintRoutingNote()
        {
            var note = "Остальной трафик идёт через VPN.\nПравила применяются при подключении.";
            var managedDomains = ManagedDomains();
            var pendingRoutes = false;
            if (Connection == LinkState.On)
            {
                pendingRoutes = !appliedBypasses.SequenceEqual(local.bypassServices) ||
                    !appliedDirectDomains.SequenceEqual(managedDomains) ||
                    !appliedRules.SequenceEqual(local.routingRules) ||
                    appliedSites.Count != local.sites.Count || appliedSites.Any(site => !local.sites.ContainsKey(site.Key) || local.sites[site.Key] != site.Value);
                note = pendingRoutes ? "Переподключите VPN, чтобы применить изменения." : "Настройки применены к текущему подключению.";
            }
            if (managedDomains.Length > 0)
                note += pendingRoutes ? "\nОбщий список сайтов применяется при подключении." :
                    "\nСайты РФ из списка администратора направляются напрямую.";
            Text("RoutingNote", note);
            Get<Button>("ApplyRouting").Visibility = pendingRoutes ? Visibility.Visible : Visibility.Collapsed;
            Get<Button>("ApplyRules").Visibility = pendingRoutes ? Visibility.Visible : Visibility.Collapsed;
        }

        private string[] ManagedDomains()
        {
            return account != null && account.directDomains != null ? account.directDomains : new string[0];
        }

        private static string SiteDomain(string input)
        {
            var value = input.Trim();
            if (value.Length == 0 || value.Length > 1024) return null;
            Uri uri;
            if (!Uri.TryCreate(value.Contains("://") ? value : "https://" + value, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                uri.UserInfo.Length != 0 || uri.HostNameType != UriHostNameType.Dns) return null;
            var domain = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
            return domain.Length <= 253 && Regex.IsMatch(domain,
                @"\A(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59})\z") ? domain : null;
        }

        private void AddSite()
        {
            var input = Get<TextBox>("SiteInput");
            var domain = SiteDomain(input.Text);
            if (domain == null) { SiteFeedback("Введите домен или ссылку на сайт."); return; }
            if (local.sites.ContainsKey(domain)) { SiteFeedback("Этот сайт уже есть в списке."); return; }
            if (local.sites.Count >= 50) { SiteFeedback("В список можно добавить до 50 сайтов."); return; }
            if (!SaveSite(domain, true)) return;
            input.Clear();
            MountSites();
            input.Focus();
        }

        private void MountSites()
        {
            var panel = Get<StackPanel>("SiteToggles");
            panel.Children.Clear();
            foreach (var site in local.sites)
            {
                var domain = site.Key;
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                var remove = new Button { Content = "×", Width = 30, Height = 32, Padding = new Thickness(0),
                    Style = (Style)Window.Resources["ControlButton"] };
                AutomationProperties.SetName(remove, "Удалить " + domain);
                DockPanel.SetDock(remove, Dock.Right);
                remove.Click += (sender, args) =>
                {
                    if (SaveSite(domain, null))
                    {
                        MountSites();
                        Get<TextBox>("SiteInput").Focus();
                    }
                };
                row.Children.Add(remove);
                var toggle = new ToggleButton { Content = domain, IsChecked = site.Value,
                    Tag = Geometry.Parse("F0 M0,8 A8,8 0 1 0 16,8 A8,8 0 1 0 0,8 M2,8 A6,6 0 1 1 14,8 A6,6 0 1 1 2,8"),
                    Style = (Style)Window.Resources["ServiceToggle"] };
                AutomationProperties.SetName(toggle, domain + " через VPN");
                RoutedEventHandler set = (sender, args) =>
                {
                    if (restoringRoute || SaveSite(domain, toggle.IsChecked == true)) return;
                    restoringRoute = true;
                    toggle.IsChecked = local.sites[domain];
                    restoringRoute = false;
                };
                toggle.Checked += set;
                toggle.Unchecked += set;
                row.Children.Add(toggle);
                panel.Children.Add(row);
            }
        }

        private bool SaveSite(string domain, bool? throughVpn)
        {
            var previous = local.sites;
            local.sites = new Dictionary<string, bool>(previous);
            if (throughVpn.HasValue) local.sites[domain] = throughVpn.Value;
            else local.sites.Remove(domain);
            if (!TrySave())
            {
                local.sites = previous;
                SiteFeedback("Не удалось сохранить настройку сайта.");
                return false;
            }
            Get<TextBlock>("SiteFeedback").Visibility = Visibility.Collapsed;
            PaintRoutingNote();
            return true;
        }

        private void SiteFeedback(string message)
        {
            var feedback = Get<TextBlock>("SiteFeedback");
            feedback.Text = message;
            feedback.Visibility = Visibility.Visible;
        }

        private void FormatKey()
        {
            if (formatting) return;
            var code = Get<TextBox>("CodeInput");
            var input = code.Text;
            var position = ActivationCode.Symbols(input.Substring(0, Math.Min(code.CaretIndex, input.Length))).Length;
            var formatted = ActivationCode.Format(input);
            if (input != formatted)
            {
                formatting = true;
                try { code.Text = formatted; code.CaretIndex = Math.Min(formatted.Length, position + (position > 4 ? 1 : 0)); }
                finally { formatting = false; }
            }
            if (!activating) SetFeedback(DefaultFeedback(), "");
        }

        private void PaintFeedback()
        {
            Get<TextBlock>("KeyFeedback").Foreground = Token(feedbackState == "Error" ? "Error" : feedbackState == "Success" ? "Teal" : "Muted");
            Get<TextBox>("CodeInput").Tag = feedbackState;
        }

        private void SetFeedback(string message, string state)
        {
            feedbackState = state;
            Text("KeyFeedback", message);
            PaintFeedback();
        }

        internal async Task Activate()
        {
            if (activating || checkingServers || Connection != LinkState.Off || closed) return;
            var code = Get<TextBox>("CodeInput");
            var value = code.Text;
            if (value.Length != 9) { SetFeedback("Введите восемь символов кода.", "Error"); code.Focus(); return; }
            activating = true;
            UpdateControls();
            SetFeedback("Проверяем код…", "");
            try
            {
                Account updated;
                if (demo)
                {
                    await Task.Delay(270, lifetime.Token);
                    if (value != "RVPN-2026") throw new UserFault("Попробуйте демо-код RVPN-2026.");
                    updated = new Account { userId = "R-10482", status = "pending", usedBytes = 82 * 1024 * 1024 };
                }
                else
                {
                    TrySave();
                    updated = await activation.Redeem(value, local.deviceId, lifetime.Token);
                }
                SaveAccount(updated);
                code.Clear();
                SetFeedback(demo ? "Демо-код принят. Доступ на 30 дней с первого подключения." : "Код принят. Теперь можно подключиться.", "Success");
                PaintConnection();
            }
            catch (UserFault problem) { SetFeedback(problem.Message, "Error"); }
            catch (OperationCanceledException) { }
            finally { activating = false; UpdateControls(); }
        }

        private void SaveAccount(Account updated)
        {
            account = updated;
            local.account = updated;
            local.origin = demo ? null : settings.Service.AbsoluteUri;
            TrySave();
            PaintAccount();
            PaintLocation();
            PaintRoutingNote();
            if (currentPage == DesktopPage.Rules) PaintRuleRows();
        }

        private ServerNode[] AvailableNodes()
        {
            return account != null && account.nodes != null ? account.nodes : new ServerNode[0];
        }

        private void PaintLocation()
        {
            var nodes = AvailableNodes();
            var node = Connection == LinkState.On ? connectedLocation : nodes.FirstOrDefault(item => item.id == local.nodeSelection);
            if (node == null && nodes.Length == 1) node = nodes[0];
            Text("LocationCountry", node != null ? node.country : nodes.Length > 1 ? "Авто" : "Финляндия");
            Text("LocationCity", node != null ? node.city : nodes.Length > 1 ? "Автоматический выбор сервера" : "Хельсинки");
            Get<Grid>("GermanFlag").Visibility = node != null && node.id == "de-01" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task ChangeServer()
        {
            if (closed || demo || account == null || activating || Connection != LinkState.Off ||
                checkingServers || serverChangeCooldown > 0) return;
            var cancel = lifetime.Token;
            checkingServers = true;
            UpdateControls();
            try { await CheckServers(cancel); }
            catch (UserFault problem)
            {
                if (!closed) SetConnection(LinkState.Off, problem.Message);
            }
            catch (OperationCanceledException) { }
            finally { checkingServers = false; }

            try
            {
                for (serverChangeCooldown = 5; serverChangeCooldown > 0 && !closed; serverChangeCooldown--)
                {
                    UpdateControls();
                    await Task.Delay(1000, cancel);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                serverChangeCooldown = 0;
                if (!closed) UpdateControls();
            }
        }

        private async Task CheckServers(CancellationToken cancel)
        {
            var updated = await activation.Refresh(account.accessToken, cancel);
            cancel.ThrowIfCancellationRequested();
            SaveAccount(updated);
            var node = await ServerSelection.Check(AvailableNodes(), cancel);
            cancel.ThrowIfCancellationRequested();
            if (node == null)
            {
                SetConnection(LinkState.Off, "Нет стабильного ответа от серверов. Текущий выбор сохранён.");
                return;
            }
            var previous = local.nodeSelection;
            local.nodeSelection = node.id;
            if (!TrySave())
            {
                local.nodeSelection = previous;
                SetConnection(LinkState.Off, "Не удалось сохранить выбранный сервер.");
                return;
            }
            SetConnection(LinkState.Off, (previous == node.id ? "Текущий сервер показал лучший результат · " : "Выбран сервер · ") + node.country);
        }

        private bool TrySave()
        {
            try { store.Save(local); return true; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.Cryptography.CryptographicException) { }
            return false;
        }

        private void PaintAccount()
        {
            Text("UserIdText", account == null ? "—" : account.userId);
            Text("UsageText", account == null ? "—" : Bytes(account.usedBytes) + (account.limitBytes > 0 ? " / " + Bytes(account.limitBytes) : ""));
            var title = "Ожидает активации";
            var note = "30 дней с первого подключения";
            if (account != null)
            {
                if (account.status == "pending") { title = "30 дней"; note = "С первого подключения"; }
                else if (account.CanConnect)
                {
                    var days = (int)Math.Ceiling((account.Expiration.Value - DateTimeOffset.UtcNow).TotalDays);
                    title = days + " дн.";
                    note = "До " + account.Expiration.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
                }
                else { title = "Доступ завершён"; note = "Введите новый код активации"; }
            }
            Text("AccessText", title);
            Text("AccessNote", note);
        }

        internal async Task ToggleConnection()
        {
            if (closed || activating || checkingServers || Connection == LinkState.Stopping) return;
            if (Connection == LinkState.Connecting) { if (pending != null) pending.Cancel(); return; }
            if (Connection == LinkState.On) { await Disconnect(null); return; }
            if (!demo && account == null)
            {
                ShowPage(DesktopPage.Profile); Get<TextBox>("CodeInput").Focus(); return;
            }

            var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            pending = operation;
            connectedNode = null;
            connectedLocation = null;
            appliedBypasses = local.bypassServices;
            appliedSites = local.sites;
            appliedDirectDomains = ManagedDomains();
            appliedRules = local.routingRules;
            SetConnection(LinkState.Connecting, null);
            try
            {
                if (demo)
                {
                    await Task.Delay(900, operation.Token);
                    Metric("Rx", "50 МБ"); Metric("Tx", "32 МБ");
                    PaintPing(36);
                    SetConnection(LinkState.On, null);
                    return;
                }

                SaveAccount(await activation.Refresh(account.accessToken, operation.Token));
                if (!account.CanConnect) throw new UserFault("Срок доступа закончился. Введите новый код.");
                if (!IsAdministrator())
                {
                    Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--connect") {
                        WorkingDirectory = directory, UseShellExecute = true, Verb = "runas"
                    });
                    Window.Close();
                    return;
                }
                if (AvailableNodes().Length > 0)
                {
                    var choice = AvailableNodes().Any(node => node.id == local.nodeSelection && node.online) ? local.nodeSelection : "auto";
                    SaveAccount(await activation.Refresh(account.accessToken, operation.Token, choice));
                    connectedNode = account.assignedNode;
                    connectedLocation = AvailableNodes().FirstOrDefault(node => node.id == connectedNode);
                }
                appliedDirectDomains = ManagedDomains();
                session = await TryStartTunnel(operation.Token);
                if (session == null && connectedNode != "fi-01" && AvailableNodes().Any(node => node.id == "fi-01" && node.online))
                {
                    SaveAccount(await activation.Refresh(account.accessToken, operation.Token, "fi-01"));
                    connectedNode = account.assignedNode;
                    connectedLocation = AvailableNodes().FirstOrDefault(node => node.id == connectedNode);
                    session = await TryStartTunnel(operation.Token);
                }
                if (session == null) throw new VpnProbeFault();
                nextProfileRefresh = DateTimeOffset.UtcNow.AddSeconds(25);
                nextEntryPing = DateTimeOffset.MinValue;
                Get<Border>("EntryPingCard").ToolTip = "ICMP-пинг до входа R VPN · " + session.EntryAddress;
                SetConnection(LinkState.On, null);
            }
            catch (VpnProbeFault problem)
            {
                SetConnection(LinkState.Off, problem.Message);
            }
            catch (AccessRevokedFault problem) { SaveAccount(null); SetConnection(LinkState.Off, problem.Message); }
            catch (UserFault problem) { SetConnection(LinkState.Off, problem.Message); }
            catch (OperationCanceledException) { SetConnection(LinkState.Off, null); }
            catch (Win32Exception) { SetConnection(LinkState.Off, "Для запуска VPN подтвердите доступ к сетевому адаптеру Windows."); }
            catch (Exception) { SetConnection(LinkState.Off, "Не удалось подключиться. Попробуйте ещё раз."); }
            finally { pending = null; operation.Dispose(); }
        }

        private async Task<EngineSession> TryStartTunnel(CancellationToken cancel)
        {
            if (!account.CanConnect) throw new AccessRevokedFault();
            var links = new[] { account.vlessUrl }.Concat(account.alternateVlessUrls ?? new string[0]).Distinct().Take(3);
            foreach (var link in links)
            {
                var profile = settings.ConnectionProfile(link, connectedNode == null);
                try
                {
                    var result = await EngineSession.Start(directory, profile, settings.Probe,
                        appliedBypasses, appliedSites, appliedDirectDomains, appliedRules, cancel);
                    ReportConnection(profile.Port, true, connectedNode);
                    return result;
                }
                catch (VpnProbeFault) { ReportConnection(profile.Port, false, connectedNode); }
            }
            return null;
        }

        private async void ReportConnection(int port, bool reachable, string node)
        {
            if (!settings.connectionDiagnostics) return;
            try { await activation.ReportConnection(account.accessToken, port, reachable, node, lifetime.Token); }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }

        internal static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private async Task Disconnect(string note)
        {
            SetConnection(LinkState.Stopping, null);
            var active = session;
            session = null;
            if (active != null) await Task.Run(() => active.Dispose());
            SetConnection(LinkState.Off, note);
        }

        private async Task Reconnect(string note)
        {
            if (closed || Connection != LinkState.On) return;
            await Disconnect(note);
            if (!closed && Connection == LinkState.Off) await ToggleConnection();
        }

        private async Task Sample()
        {
            var active = session;
            if (sampling || closed) return;
            sampling = true;
            string revokedMessage = null;
            try
            {
                if (active == null)
                {
                    IPAddress entry;
                    if (!demo && Connection == LinkState.Off && DateTimeOffset.UtcNow >= nextEntryPing &&
                        IPAddress.TryParse(settings.Service.Host, out entry))
                    {
                        nextEntryPing = DateTimeOffset.UtcNow.AddSeconds(10);
                        var idlePing = await EngineSession.MeasureEntryPing(entry, lifetime.Token);
                        if (Connection == LinkState.Off) PaintLocationPing(idlePing);
                    }
                    return;
                }
                if (active.HasExited) { await Disconnect("Соединение завершилось. Можно подключиться заново."); return; }
                if (account != null && !account.CanConnect) { await Disconnect("Доступ закончился. Введите новый код."); return; }
                var usage = await active.Traffic(lifetime.Token);
                if (usage != null && session == active) { Metric("Rx", Bytes(usage[0])); Metric("Tx", Bytes(usage[1])); }
                if (session == active && DateTimeOffset.UtcNow >= nextEntryPing)
                {
                    nextEntryPing = DateTimeOffset.UtcNow.AddSeconds(10);
                    var ping = await EngineSession.MeasureEntryPing(active.EntryAddress, lifetime.Token);
                    if (session == active)
                    {
                        PaintPing(ping);
                    }
                }
                if (session != active || account == null || nextProfileRefresh > DateTimeOffset.UtcNow) return;
                nextProfileRefresh = DateTimeOffset.UtcNow.AddSeconds(35);
                var updated = await activation.Refresh(account.accessToken, lifetime.Token);
                if (session == active)
                {
                    SaveAccount(updated);
                    if (!updated.CanConnect) await Disconnect("Доступ закончился или отозван. Введите новый код.");
                }
            }
            catch (OperationCanceledException) { }
            catch (AccessRevokedFault problem) { revokedMessage = problem.Message; }
            catch (UserFault) { }
            catch (InvalidOperationException) { }
            finally { sampling = false; }
            if (revokedMessage != null && session == active && Connection == LinkState.On)
            {
                SaveAccount(null);
                await Disconnect(revokedMessage);
            }
        }

        private void SetConnection(LinkState state, string note)
        {
            if (state == LinkState.On && Connection != LinkState.On) sessionClock.Restart();
            else if (state != LinkState.On) sessionClock.Reset();
            Connection = state;
            PaintSessionTime();
            if (state == LinkState.Off) nextEntryPing = DateTimeOffset.MinValue;
            linkMessage = note;
            if (state == LinkState.Off) { Metric("Rx", "0 Б"); Metric("Tx", "0 Б"); PaintPing(null); }
            PaintConnection();
            PaintLocation();
            UpdateControls();
            PaintRoutingNote();
            var animatePulse = state == LinkState.Connecting && SystemParameters.ClientAreaAnimation;
            AnimatePulse(Get<Border>("PulseOuter"), animatePulse, 0, .54);
            AnimatePulse(Get<Border>("PulseInner"), animatePulse, 260, .33);
            if (state == LinkState.On && SystemParameters.ClientAreaAnimation)
            {
                var scale = (ScaleTransform)Get<Border>("KeyCap").RenderTransform;
                var animation = new DoubleAnimation(.91, 1, TimeSpan.FromMilliseconds(290)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
        }

        private void PaintSessionTime()
        {
            var elapsed = sessionClock.Elapsed;
            var minutes = ((long)elapsed.TotalMinutes).ToString("00", CultureInfo.InvariantCulture);
            var seconds = elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture);
            var ink = Token(Connection == LinkState.On ? "ClockActive" : "ClockIdle");
            minutesDisplay.SetDisplay(minutes, ink);
            secondsDisplay.SetDisplay(seconds, ink);
            var text = minutes + ":" + seconds;
            var label = Get<TextBlock>("SessionTime");
            if (label.Text == text) return;
            label.Text = text;
            AutomationProperties.SetName(label, "Время подключения " + text);
        }

        private void PaintPing(long? ping)
        {
            Get<Run>("PingValue").Text = (ping ?? 0).ToString(CultureInfo.InvariantCulture);
            PaintLocationPing(ping);
        }

        private void PaintLocationPing(long? ping)
        {
            Text("LocationPing", (ping ?? 0).ToString(CultureInfo.InvariantCulture) + " ms");
        }

        private static void AnimatePulse(Border ring, bool active, int delay, double peak)
        {
            var scale = (ScaleTransform)ring.RenderTransform;
            DoubleAnimationUsingKeyFrames size = null;
            DoubleAnimationUsingKeyFrames opacity = null;
            if (active)
            {
                var start = TimeSpan.FromMilliseconds(delay);
                var end = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1030));
                size = new DoubleAnimationUsingKeyFrames { BeginTime = start, FillBehavior = FillBehavior.Stop };
                size.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                size.KeyFrames.Add(new SplineDoubleKeyFrame(1.33, end, new KeySpline(.22, .57, .34, 1)));
                opacity = new DoubleAnimationUsingKeyFrames { BeginTime = start, FillBehavior = FillBehavior.Stop };
                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(237))));
                opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, end));
            }
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
            ring.BeginAnimation(UIElement.OpacityProperty, opacity);
        }

        private void PaintConnection()
        {
            var online = Connection == LinkState.On;
            var busy = Connection == LinkState.Connecting;
            var stopping = Connection == LinkState.Stopping;
            var status = online ? (demo ? "Демо подключено" : "Подключено") : busy ? "Подключаемся" : stopping ? "Отключаемся" : "Отключено";
            Text("StatusText", status);
            Get<TextBlock>("StatusText").Foreground = Token(busy ? "Accent" : "Text");
            Get<Ellipse>("StateDot").Fill = Token(online || busy ? "Accent" : "Muted");
            var location = connectedLocation == null ? "Финляндия" : connectedLocation.country;
            var onlineNote = "Выход · " + location + (appliedRules.Length == 0 && appliedDirectDomains.Length == 0 && appliedBypasses.Length == 0 && appliedSites.All(site => site.Value)
                ? "." : ". Действуют исключения маршрутов.");
            Text("StatusNote", linkMessage ?? (demo ? "Демонстрация интерфейса и подключения." : online ? onlineNote : account == null ? "Сначала активируйте код в профиле." : "Сервер готов к подключению."));
            var cap = Get<Border>("KeyCap");
            cap.Background = Token(online ? "KeyGradient" : "KeyIdle");
            cap.Effect = (Effect)Window.Resources[online ? "KeyGlow" : "KeyShadow"];
            cap.BorderBrush = Token(online ? "GoldLine" : "KeyLine");
            Get<System.Windows.Shapes.Path>("ButtonLogo").Fill = Token(online ? "Ink" : "Accent");
            AutomationProperties.SetName(Get<Button>("ConnectButton"), online ? "Выключить VPN" : busy ? "Отменить подключение" : "Включить VPN");
            artwork.Online = online;
            artwork.InvalidateVisual();
        }

        private void UpdateControls()
        {
            Get<Button>("ConnectButton").IsEnabled = !activating && !checkingServers && Connection != LinkState.Stopping;
            var change = Get<Button>("ChangeServer");
            var off = Connection == LinkState.Off;
            change.IsEnabled = !demo && account != null && off && !activating && !checkingServers && serverChangeCooldown == 0;
            change.Content = checkingServers ? "Проверяем…" : off && serverChangeCooldown > 0
                ? "Повтор через " + serverChangeCooldown + " с" : "Сменить сервер";
            Get<Button>("ActivateButton").IsEnabled = !activating && !checkingServers && off;
            Get<TextBox>("CodeInput").IsReadOnly = activating || checkingServers || !off;
            Get<StackPanel>("ServiceToggles").IsEnabled = Connection != LinkState.Connecting && Connection != LinkState.Stopping;
            Get<StackPanel>("SiteControls").IsEnabled = Get<StackPanel>("ServiceToggles").IsEnabled;
        }

        private void Release()
        {
            if (closed) return;
            closed = true;
            metricsTimer.Stop();
            sessionTimer.Stop();
            lifetime.Cancel();
            if (session != null) { session.Dispose(); session = null; }
            if (activation != null) activation.Dispose();
        }

        private static string Bytes(long bytes)
        {
            var amount = (double)bytes;
            var units = new[] { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            int index = 0;
            while (amount >= 1024 && index < units.Length - 1) { amount /= 1024; index++; }
            return amount.ToString(index == 0 ? "0" : "0.#", CultureInfo.GetCultureInfo("ru-RU")) + " " + units[index];
        }
    }
}
