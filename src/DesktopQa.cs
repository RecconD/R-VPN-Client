using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RVpn
{
    internal static class DesktopQa
    {
        internal static int Run(Application app, string directory, string output)
        {
            Directory.CreateDirectory(output);
            var reportPath = Path.Combine(output, "desktop-checks.json");
            var testFolder = Path.Combine(output, "test-state-" + Guid.NewGuid().ToString("N"));
            var checks = new List<string>();
            try
            {
                var store = new ClientStore(testFolder);
                var desktop = new Desktop(directory, store, ClientSettings.Load(directory), true);
                var window = desktop.Window;
                window.ShowInTaskbar = false;
                window.Loaded += async (sender, args) =>
                {
                    int result = 1;
                    try
                    {
                        await RenderTick(window);
                        Assert(CoreFiles.Check(Path.Combine(directory, "core")), "vendor hashes", checks);
                        Assert(((LinearGradientBrush)window.Background).GradientStops[0].Color == Art.ColorOf("#323232") &&
                            desktop.Get<Button>("DarkTheme").IsVisible, "matte dark layout and theme controls", checks);
                        GlyphTypeface comfortaa, bold, heading, wordmark;
                        Assert(new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
                                .TryGetGlyphTypeface(out comfortaa) && comfortaa.FamilyNames.Values.Contains("Comfortaa") &&
                            comfortaa.CharacterToGlyphMap.ContainsKey('Я') &&
                            new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)
                                .TryGetGlyphTypeface(out bold) && bold.Weight == FontWeights.Bold && bold.FontUri != comfortaa.FontUri &&
                            new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
                                .TryGetGlyphTypeface(out heading) && heading.Weight == FontWeights.SemiBold &&
                            new Typeface(desktop.Get<TextBlock>("Wordmark").FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)
                                .TryGetGlyphTypeface(out wordmark) && wordmark.FamilyNames.Values.Contains("Manrope"),
                            "embedded Comfortaa and site Manrope wordmark resolve", checks);
                        Assert(window.ResizeMode == ResizeMode.CanMinimize && window.FindName("MaxButton") == null &&
                            window.ActualWidth == 1120 && window.ActualHeight == 700,
                            "wide fixed size with minimize and close (requested " + window.Width + "x" + window.Height +
                            "; actual " + window.ActualWidth + "x" + window.ActualHeight +
                            "; virtual screen " + SystemParameters.VirtualScreenWidth + "x" + SystemParameters.VirtualScreenHeight +
                            "; resize " + window.ResizeMode + ")", checks);
                        var screenshot = Art.WindowScreenshot((Border)window.Content);
                        var corner = new byte[4];
                        var center = new byte[4];
                        screenshot.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
                        screenshot.CopyPixels(new Int32Rect(screenshot.PixelWidth / 2, 12, 1, 1), center, 4, 0);
                        Assert(System.Windows.Shell.WindowChrome.GetWindowChrome(window).CornerRadius.TopLeft == 18 &&
                            corner[3] == 0 && center[3] == 255 && ((BitmapSource)window.Icon).PixelWidth == 64,
                            "rounded window, transparent screenshot corners and app icon", checks);
                        using (var encoded = new MemoryStream())
                        {
                            Art.WritePng(screenshot, encoded);
                            encoded.Position = 0;
                            var png = new PngBitmapDecoder(encoded, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            Assert(png.Frames[0].PixelWidth == screenshot.PixelWidth && png.Frames[0].PixelHeight == screenshot.PixelHeight &&
                                desktop.Get<Button>("SaveScreenshotButton").Content.Equals("Сохранить PNG"),
                                "app screenshot encodes to PNG and has an explicit save action", checks);
                        }
                        var order = new[] { "LocationCard", "ConnectionIntro", "Hero", "MetricsCard", "BottomNav" };
                        var top = order.Select(name => desktop.Get<FrameworkElement>(name).TranslatePoint(new Point(0, 0), window).Y).ToArray();
                        Assert(top.Zip(top.Skip(1), (before, after) => before < after).All(value => value) &&
                            Math.Abs(desktop.Get<Border>("BottomNav").TranslatePoint(new Point(260, 0), window).X - window.ActualWidth / 2) < 2,
                            "phone-style home order and centered bottom navigation", checks);
                        Assert(desktop.Connection == LinkState.Off, "startup offline", checks);
                        var dial = (DialArtwork)desktop.Get<Grid>("DialMount").Children[0];
                        var minutes = (SegmentDisplay)desktop.Get<Grid>("MinuteDisplayMount").Children[0];
                        var seconds = (SegmentDisplay)desktop.Get<Grid>("SecondDisplayMount").Children[0];
                        var hero = desktop.Get<Grid>("Hero");
                        var key = desktop.Get<Button>("ConnectButton");
                        var keyCenter = key.TranslatePoint(new Point(key.ActualWidth / 2, key.ActualHeight / 2), hero);
                        var minuteCenter = minutes.TranslatePoint(new Point(minutes.ActualWidth / 2, minutes.ActualHeight / 2), hero);
                        var secondCenter = seconds.TranslatePoint(new Point(seconds.ActualWidth / 2, seconds.ActualHeight / 2), hero);
                        Assert(minutes.DigitsText == "00" && seconds.DigitsText == "00" &&
                            minuteCenter.X < keyCenter.X && keyCenter.X < secondCenter.X &&
                            Math.Abs(minuteCenter.Y - keyCenter.Y) < 1 && Math.Abs(secondCenter.Y - keyCenter.Y) < 1,
                            "seven-segment minutes and seconds sit on opposite sides of the button", checks);
                        var metrics = desktop.Get<Border>("MetricsCard");
                        foreach (var pair in new[] { new[] { "RxArrow", "RxReadout" }, new[] { "TxArrow", "TxReadout" }, new[] { "PingUnit", "PingReadout" } })
                        {
                            var prefix = desktop.Get<FrameworkElement>(pair[0]);
                            var value = desktop.Get<FrameworkElement>(pair[1]);
                            var prefixCenter = prefix.TranslatePoint(new Point(prefix.ActualWidth / 2, prefix.ActualHeight / 2), metrics);
                            var valueCenter = value.TranslatePoint(new Point(value.ActualWidth / 2, value.ActualHeight / 2), metrics);
                            Assert(prefixCenter.X < valueCenter.X && Math.Abs(prefixCenter.Y - valueCenter.Y) < 1,
                                pair[0] + " aligns to the left of its value", checks);
                        }
                        Assert(desktop.Get<Run>("PingValue").Text == "0" && desktop.Get<TextBlock>("LocationPing").Text == "0 ms",
                            "ping displays zero before a reply", checks);
                        var connection = desktop.ToggleConnection();
                        Assert(desktop.Connection == LinkState.Connecting, "connecting state", checks);
                        if (SystemParameters.ClientAreaAnimation)
                        {
                            await Task.Delay(380);
                            await RenderTick(window);
                            var outer = desktop.Get<Border>("PulseOuter");
                            var inner = desktop.Get<Border>("PulseInner");
                            Assert(outer.CornerRadius == new CornerRadius(44) && inner.CornerRadius == outer.CornerRadius &&
                                outer.Opacity > 0 && inner.Opacity > 0 && ((ScaleTransform)outer.RenderTransform).ScaleX > 1,
                                "connecting shows two expanding rounded contours", checks);
                            Capture(window, Path.Combine(output, "desktop-home-connecting.png"));
                        }
                        await connection;
                        Assert(desktop.Get<Border>("PulseOuter").Opacity == 0 && desktop.Get<Border>("PulseInner").Opacity == 0,
                            "connecting contours clear when connected", checks);
                        Assert(desktop.Connection == LinkState.On && desktop.Get<TextBlock>("StatusText").Text == "Демо подключено", "demo state is explicit", checks);
                        // Wait for an observable dispatcher/render update instead
                        // of assuming the first timer tick lands within 1120 ms.
                        var clockWait = Stopwatch.StartNew();
                        while (clockWait.ElapsedMilliseconds < 3500 &&
                            (seconds.DigitsText == "00" || (SystemParameters.ClientAreaAnimation && dial.SweepPosition == 0)))
                        {
                            await Task.Delay(100);
                            await RenderTick(window);
                        }
                        Assert(minutes.DigitsText == "00" && seconds.DigitsText != "00" &&
                            (!SystemParameters.ClientAreaAnimation || dial.SweepPosition > 0),
                            "connected clock advances and dial sweep follows motion preferences" +
                            " (time=" + minutes.DigitsText + ":" + seconds.DigitsText + ", sweep=" + dial.SweepPosition + ")", checks);
                        Capture(window, Path.Combine(output, "desktop-home-dark.png"));
                        await desktop.ToggleConnection();
                        Assert(desktop.Connection == LinkState.Off, "disconnect resets session", checks);
                        Assert(dial.SweepPosition == 0 && minutes.DigitsText == "00" && seconds.DigitsText == "00" &&
                            desktop.Get<Run>("PingValue").Text == "0", "disconnect stops the dial and resets clock and ping", checks);
                        connection = desktop.ToggleConnection();
                        await desktop.ToggleConnection();
                        await connection;
                        Assert(desktop.Connection == LinkState.Off, "cancel pending connection", checks);

                        InvokeButton(desktop.Get<Button>("AdvancedButton"));
                        await RenderTick(window);
                        var tab = desktop.Get<Button>("AdvancedButton");
                        GlyphTypeface tabFont;
                        Assert(new Typeface(tab.FontFamily, FontStyles.Normal, tab.FontWeight, FontStretches.Normal)
                            .TryGetGlyphTypeface(out tabFont) && tabFont.FamilyNames.Values.Contains("Comfortaa"),
                            "bottom navigation uses embedded Comfortaa", checks);
                        var routeToggles = desktop.Get<StackPanel>("ServiceToggles").Children.OfType<ToggleButton>().ToArray();
                        Assert(desktop.Get<Grid>("AdvancedPage").IsVisible && !desktop.Get<Grid>("HomePage").IsVisible &&
                            routeToggles.Length == 3 && routeToggles.All(toggle => toggle.IsChecked == false),
                            "Discord, Telegram and YouTube retain direct defaults", checks);
                        var discord = routeToggles.Single(toggle => (string)toggle.Content == "Discord");
                        var youtube = routeToggles.Single(toggle => (string)toggle.Content == "YouTube");
                        youtube.Focus();
                        Assert(((SolidColorBrush)((Border)youtube.Template.FindName("Row", youtube)).BorderBrush).Color == Colors.Transparent &&
                            youtube.FocusVisualStyle != null, "route focus keeps the row border and uses a keyboard cue", checks);
                        ((IToggleProvider)new ToggleButtonAutomationPeer(discord).GetPattern(PatternInterface.Toggle)).Toggle();
                        youtube.IsChecked = true;
                        var track = (Border)youtube.Template.FindName("Track", youtube);
                        Assert(((LinearGradientBrush)track.Background).GradientStops[0].Color ==
                            Art.ColorOf("#FED215") && track.Effect != null, "enabled route uses the gold gradient and glow", checks);
                        Assert(store.Load().bypassServices.SequenceEqual(new[] { "telegram" }) &&
                            routeToggles.Single(toggle => (string)toggle.Content == "Telegram").IsChecked == false,
                            "independent routing choices persist", checks);
                        var siteInput = desktop.Get<TextBox>("SiteInput");
                        siteInput.Text = "https://Example.com/some/path";
                        InvokeButton(desktop.Get<Button>("AddSite"));
                        await RenderTick(window);
                        var sitePanel = desktop.Get<StackPanel>("SiteToggles");
                        var siteRow = (DockPanel)sitePanel.Children[0];
                        var siteToggle = siteRow.Children.OfType<ToggleButton>().Single();
                        Assert(siteToggle.IsChecked == true && store.Load().sites["example.com"], "custom URL normalizes to domain", checks);
                        ((IToggleProvider)new ToggleButtonAutomationPeer(siteToggle).GetPattern(PatternInterface.Toggle)).Toggle();
                        Assert(!store.Load().sites["example.com"], "custom website direct route persists", checks);
                        InvokeButton(siteRow.Children.OfType<Button>().Single());
                        await RenderTick(window);
                        Assert(sitePanel.Children.Count == 0 && store.Load().sites.Count == 0, "custom route can be removed", checks);
                        await desktop.ToggleConnection();
                        discord.IsChecked = false;
                        var applyRouting = desktop.Get<Button>("ApplyRouting");
                        await RenderTick(window);
                        var serviceList = desktop.Get<StackPanel>("ServiceToggles");
                        var applyPoint = applyRouting.TranslatePoint(new Point(applyRouting.ActualWidth / 2, 0), serviceList);
                        Assert(Math.Abs(applyPoint.X - serviceList.ActualWidth / 2) < 1 &&
                            applyPoint.Y > serviceList.ActualHeight && applyPoint.Y - serviceList.ActualHeight < 20,
                            "apply button is centered directly below the service list", checks);
                        Capture(window, Path.Combine(output, "desktop-routes-pending-dark.png"));
                        Assert(desktop.Get<TextBlock>("RoutingNote").Text.Contains("Переподключите") && applyRouting.IsVisible,
                            "routing changes while connected are marked pending", checks);
                        InvokeButton(applyRouting);
                        await RenderTick(window);
                        for (var wait = 0; wait < 30 && desktop.Connection != LinkState.On; wait++)
                            await Task.Delay(100);
                        Assert(desktop.Connection == LinkState.On && !applyRouting.IsVisible &&
                            desktop.Get<TextBlock>("RoutingNote").Text.Contains("Настройки применены"),
                            "applying saved route choices reconnects the active session", checks);
                        var changeServer = desktop.Get<Button>("ChangeServer");
                        Assert(!changeServer.IsEnabled && changeServer.ContextMenu == null,
                            "server change is unavailable while connected and has no popup menu", checks);
                        var finland = new ServerNode { id = "fi-01" };
                        var germany = new ServerNode { id = "de-01" };
                        var candidates = new[] {
                            new KeyValuePair<ServerNode, long[]>(finland, new long[] { 50, 70, 60 }),
                            new KeyValuePair<ServerNode, long[]>(germany, new long[] { 23, 24 })
                        };
                        Assert(ServerSelection.Best(candidates) == finland, "server selection prefers consistent replies", checks);
                        candidates[1] = new KeyValuePair<ServerNode, long[]>(germany, new long[] { 150, 40, 42 });
                        Assert(ServerSelection.Best(candidates) == germany, "server selection compares median times when equally reachable", checks);
                        Assert((await EngineSession.MeasureEntryPing(System.Net.IPAddress.Loopback, CancellationToken.None)).HasValue,
                            "entry ping measures an ICMP reply", checks);
                        await desktop.ToggleConnection();
                        await RenderTick(window);
                        Capture(window, Path.Combine(output, "desktop-advanced-dark.png"));

                        InvokeButton(desktop.Get<Button>("RulesButton"));
                        await RenderTick(window);
                        Assert(desktop.Get<Grid>("RulesPage").IsVisible && desktop.Get<StackPanel>("RuleRows").Children.Count == 22 &&
                            desktop.Get<TextBlock>("SessionTime").Text == "00:00", "rules navigation, imported defaults and reset session clock", checks);
                        var advancedRules = desktop.Get<Button>("RulesRawToggle");
                        advancedRules.Focus();
                        var advancedSurface = (Border)advancedRules.Template.FindName("ButtonSurface", advancedRules);
                        Assert(Equals(advancedSurface.BorderBrush, advancedRules.BorderBrush) && advancedRules.FocusVisualStyle != null,
                            "advanced rule button retains its normal border when focused", checks);
                        Capture(window, Path.Combine(output, "desktop-rules-dark.png"));
                        var picker = desktop.Get<ComboBox>("RuleType");
                        picker.IsDropDownOpen = true;
                        await RenderTick(window);
                        Assert(((Popup)picker.Template.FindName("PART_Popup", picker)).IsOpen,
                            "themed rule type dropdown opens", checks);
                        picker.IsDropDownOpen = false;
                        desktop.Get<TextBox>("RuleValue").Text = "example.org";
                        InvokeButton(desktop.Get<Button>("AddRuleFirst"));
                        await RenderTick(window);
                        InvokeButton(desktop.Get<Button>("SaveRules"));
                        await RenderTick(window);
                        for (var wait = 0; wait < 40 && !desktop.Get<Grid>("RulesPage").IsEnabled; wait++) await Task.Delay(100);
                        Assert(store.Load().routingRules.Length == 23 && store.Load().routingRules[0].value == "example.org",
                            "rules editor validates and saves typed content in order", checks);
                        var progress = BrandDialog.Progress(window, "Обновление R VPN", "Скачиваем новую версию…");
                        progress.Show();
                        await RenderTick(progress);
                        Assert(progress.Content is Border && System.Windows.Shell.WindowChrome.GetWindowChrome(progress).CornerRadius.TopLeft == 16,
                            "update progress uses rounded branded dialog", checks);
                        Capture(progress, Path.Combine(output, "desktop-update-dark.png"));
                        progress.Close();

                        InvokeButton(desktop.Get<Button>("ProfileButton"));
                        await RenderTick(window);
                        Assert(desktop.Get<Grid>("HomePage").Visibility == Visibility.Collapsed, "profile navigation", checks);
                        var highlight = ((LinearGradientBrush)desktop.Get<Button>("ActivateButton").Background).GradientStops[1];
                        var initialOffset = highlight.Offset;
                        if (SystemParameters.ClientAreaAnimation) await Task.Delay(350);
                        Assert(SystemParameters.ClientAreaAnimation ? Math.Abs(highlight.Offset - initialOffset) > .001 : !highlight.HasAnimatedProperties,
                            "activation gradient moves when Windows animations are enabled", checks);
                        var code = desktop.Get<TextBox>("CodeInput");
                        var placeholder = (TextBlock)code.Template.FindName("FieldPlaceholder", code);
                        Assert(placeholder.IsVisible && placeholder.Text == "XXXX-XXXX", "activation placeholder shown on empty field", checks);
                        var placeholderStart = placeholder.TranslatePoint(new Point(0, 0), code).X;
                        code.Text = "TEST-0000";
                        await desktop.Activate();
                        Assert((string)code.Tag == "Error", "wrong demo code rejected", checks);
                        code.Text = " rvpn 20-26 ";
                        Assert(code.Text == "RVPN-2026", "short code formatting", checks);
                        await desktop.Activate();
                        Assert(desktop.Get<TextBlock>("UserIdText").Text == "R-10482" && (string)code.Tag == "Success", "demo activation", checks);
                        code.Text = "R";
                        code.Clear();
                        window.Activate();
                        Keyboard.Focus(code);
                        await RenderTick(window);
                        var field = (Border)code.Template.FindName("FieldSurface", code);
                        Assert(code.IsKeyboardFocused && field.BorderThickness == new Thickness(1) &&
                            ((SolidColorBrush)field.BorderBrush).Color == ((SolidColorBrush)window.Resources["Line"]).Color &&
                            ((SolidColorBrush)field.Background).Color == ((SolidColorBrush)window.Resources["FieldFocus"]).Color &&
                            code.FocusVisualStyle == null, "activation focus changes fill without brightening the border", checks);
                        Assert(!placeholder.IsVisible && code.CaretIndex == 0 &&
                            Math.Abs(code.GetRectFromCharacterIndex(0).X - placeholderStart) <= 2,
                            "empty activation field focuses at the start of its placeholder (visible=" + placeholder.IsVisible +
                            ", caret=" + code.CaretIndex + ", x=" + code.GetRectFromCharacterIndex(0).X + ", hint=" + placeholderStart + ")", checks);
                        Capture(window, Path.Combine(output, "desktop-profile-dark.png"));
                        InvokeButton(desktop.Get<Button>("LightTheme"));
                        await RenderTick(window);
                        Assert(store.Load().lightTheme && ((LinearGradientBrush)window.Background).GradientStops[0].Color == Art.ColorOf("#F2EEE3"),
                            "light theme switches and persists", checks);
                        Capture(window, Path.Combine(output, "desktop-profile-light.png"));
                        InvokeButton(desktop.Get<Button>("DarkTheme"));
                        await RenderTick(window);
                        Assert(desktop.Get<Button>("ActivateButton").ActualWidth > 80 && code.ActualWidth > 210, "activation fits fixed window", checks);
                        Capture(window, Path.Combine(output, "desktop-profile-compact-dark.png"));
                        InvokeButton(desktop.Get<Button>("AdvancedButton"));
                        await RenderTick(window);
                        var advancedScroll = (ScrollViewer)desktop.Get<Grid>("AdvancedPage").Children[0];
                        Assert(advancedScroll.ScrollableHeight < 60 && routeToggles.All(toggle => toggle.ActualWidth > 470),
                            "all service settings fit in fixed window", checks);

                        var profile = VlessProfile.Parse("vless://00000000-0000-4000-8000-000000000001@203.0.113.11:443?type=tcp&security=reality&flow=xtls-rprx-vision&sni=example.net&pbk=jNXHt1yRo0vDuchQlIP6Z0ZvjT3KtzVI-T4E7RoLJS0&sid=0123456789abcdef");
                        Assert(profile.Port == 443 && profile.Host == "203.0.113.11", "VLESS REALITY parser", checks);
                        var webSocket = VlessProfile.Parse("vless://00000000-0000-4000-8000-000000000001@203.0.113.11:443?type=ws&security=tls&path=%2Fv1%2Ftunnel&encryption=none");
                        Assert(webSocket.WebSocketPath == "/v1/tunnel" && webSocket.ServerName == webSocket.Host,
                            "VLESS WebSocket parser with IP certificate identity", checks);
                        var relaySettingsPath = Path.Combine(testFolder, "RVPN.Settings.json");
                        VlessProfile relay;
                        try
                        {
                            File.WriteAllText(relaySettingsPath, "{\"activationService\":\"https://31.77.138.106/\"}");
                            var relaySettings = ClientSettings.Load(testFolder);
                            relay = relaySettings.ConnectionProfile("vless://00000000-0000-4000-8000-000000000001@31.77.138.106:8443?type=ws&security=tls&path=%2Fv1%2Ftunnel");
                            Assert(relaySettings.Service.IdnHost == "31.77.57.217" && relaySettings.AcceptsSavedOrigin("https://31.77.138.106/") &&
                                !relaySettings.AcceptsSavedOrigin("https://example.net/") && relay.Host == relaySettings.Service.IdnHost &&
                                relay.ServerName == relay.Host && relay.WebSocketHost == relay.Host && relay.Port == 8443 && relay.Uuid == webSocket.Uuid,
                                "managed relay preserves the saved account and connects with the relay TLS identity", checks);
                        }
                        finally { if (File.Exists(relaySettingsPath)) File.Delete(relaySettingsPath); }
                        CheckRouting(profile, checks);
                        await Task.Run(() =>
                        {
                            CheckCore(directory, profile.Config(profile.Host, 46329, "test-control-key-not-used", true));
                            CheckCore(directory, profile.Config(profile.Host, 46329, "test-control-key-not-used", true,
                                RouteService.All.Select(service => service.Id).ToArray()));
                            CheckCore(directory, webSocket.Config(webSocket.Host, 46329, "test-control-key-not-used", true));
                            CheckCore(directory, relay.Config("31.77.57.217", 46329, "test-control-key-not-used", true));
                            CheckCore(directory, webSocket.Config(webSocket.Host, 46329, "test-control-key-not-used", true,
                                new[] { "youtube" }, new Dictionary<string, bool> { { "youtube.com", true }, { "example.com", false } },
                                new[] { "gosuslugi.ru" }, RoutingRule.Defaults()));
                        });
                        checks.Add("official sing-box validates full and split TUN configurations");
                        await CheckHost(directory, profile);
                        await CheckHost(directory, webSocket, RoutingRule.Defaults());
                        checks.Add("network host starts and stops the core with a loopback control port");
                        File.WriteAllText(reportPath, DataJson.Write(new { success = true, checks = checks }), new UTF8Encoding(false));
                        result = 0;
                    }
                    catch (Exception exception)
                    {
                        File.WriteAllText(reportPath, DataJson.Write(new { success = false, checks = checks, error = exception.Message }), new UTF8Encoding(false));
                    }
                    finally
                    {
                        var state = Path.Combine(testFolder, "state.dat");
                        if (File.Exists(state)) File.Delete(state);
                        if (Directory.Exists(testFolder)) Directory.Delete(testFolder);
                        app.Shutdown(result);
                    }
                };
                return app.Run(window);
            }
            catch (Exception exception)
            {
                File.WriteAllText(reportPath, DataJson.Write(new { success = false, checks = checks, error = exception.ToString() }), new UTF8Encoding(false));
                return 1;
            }
        }

        private static void Assert(bool condition, string check, List<string> checks)
        {
            if (!condition) throw new InvalidOperationException("Check failed: " + check);
            checks.Add(check);
        }

        private static void InvokeButton(Button button)
        {
            var peer = new ButtonAutomationPeer(button);
            var action = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            action.Invoke();
        }

        private static Task RenderTick(Window window)
        {
            return window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle).Task;
        }

        private static void Capture(Window window, string file)
        {
            var target = Art.WindowScreenshot((Border)window.Content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using (var stream = File.Create(file)) encoder.Save(stream);
        }

        private static void CheckRouting(VlessProfile profile, List<string> checks)
        {
            var config = DataJson.Read<Dictionary<string, object>>(profile.Config(profile.Host, 46329, "test-control-key-not-used",
                true, new[] { "telegram", "youtube" }));
            var route = (Dictionary<string, object>)config["route"];
            var rules = ((IEnumerable)route["rules"]).Cast<Dictionary<string, object>>().ToList();
            var gameRule = rules.Single(rule => MatchesProcess(rule, @"C:\Apps\Telegram.EXE"));
            var steamRule = rules.Single(rule => MatchesProcess(rule, @"C:\Apps\Discord.exe"));
            var webRule = rules.Single(rule => rule.ContainsKey("domain_suffix"));
            var domains = ((IEnumerable)webRule["domain_suffix"]).Cast<string>();
            var dns = (Dictionary<string, object>)config["dns"];
            var dnsRules = ((IEnumerable)dns["rules"]).Cast<Dictionary<string, object>>().ToArray();
            Assert((string)gameRule["outbound"] == "direct" && (string)steamRule["outbound"] == "rvpn" &&
                !MatchesProcess(gameRule, @"C:\Apps\not-telegram.exe") &&
                rules.IndexOf(gameRule) < rules.IndexOf(webRule) && (string)route["final"] == "rvpn",
                "Telegram bypass is independent of Discord and the default VPN route", checks);
            Assert((string)dnsRules[0]["action"] == "predefined" && (string)dnsRules[0]["rcode"] == "NOERROR" &&
                ((IEnumerable)dnsRules[0]["query_type"]).Cast<string>().SequenceEqual(new[] { "AAAA" }),
                "system DNS receives no IPv6 addresses for IPv4-only exits", checks);
            Assert((string)webRule["outbound"] == "direct" && domains.Contains("googlevideo.com") &&
                !domains.Contains("vk.com") && (string)dnsRules[1]["server"] == "direct-dns" &&
                domains.SequenceEqual(((IEnumerable)dnsRules[1]["domain_suffix"]).Cast<string>()) &&
                (string)dnsRules[2]["action"] == "respond" && (string)dnsRules[3]["server"] == "secure-dns" &&
                (string)dns["final"] == "secure-dns", "excluded websites keep direct routing with tunneled DNS fallback", checks);
            var managed = DataJson.Read<Dictionary<string, object>>(profile.Config(profile.Host, 46329,
                "test-control-key-not-used", true, new[] { "telegram" },
                new Dictionary<string, bool> { { "lk.gosuslugi.ru", true } }, new[] { "gosuslugi.ru" }, RoutingRule.Defaults()));
            var managedRoute = (Dictionary<string, object>)managed["route"];
            var managedRules = ((IEnumerable)managedRoute["rules"]).Cast<Dictionary<string, object>>().ToArray();
            Assert(((IEnumerable)managedRules[2]["domain_suffix"]).Cast<string>().Single() == "lk.gosuslugi.ru" &&
                (string)managedRules[2]["outbound"] == "rvpn" &&
                (string)managedRules.Single(rule => MatchesProcess(rule, @"C:\Games\Steam.exe"))["outbound"] == "direct" &&
                (string)managedRules.Single(rule => MatchesProcess(rule, @"C:\Apps\qBittorrent.exe"))["action"] == "reject" &&
                (string)managedRoute["final"] == "rvpn", "personal site override, imported process policies and default VPN route", checks);
        }

        private static bool MatchesProcess(Dictionary<string, object> rule, string path)
        {
            return rule.ContainsKey("process_path_regex") &&
                ((IEnumerable)rule["process_path_regex"]).Cast<string>().Any(pattern => Regex.IsMatch(path, pattern));
        }

        private static void CheckCore(string directory, string config)
        {
            var core = Path.Combine(directory, "core");
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(core, "sing-box.exe"), "check -c stdin --disable-color")
            { WorkingDirectory = core, CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true }))
            {
                process.StandardInput.Write(config);
                process.StandardInput.Close();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(8000)) { process.Kill(); throw new InvalidOperationException("Core check timeout"); }
                if (process.ExitCode != 0) throw new InvalidOperationException("Core check: " + error.GetAwaiter().GetResult());
            }
        }

        private static async Task CheckHost(string directory, VlessProfile profile, RoutingRule[] rules = null)
        {
            const string secret = "loopback-test-only-not-an-activation-key";
            var core = Path.Combine(directory, "core");
            var marker = Guid.NewGuid().ToString("N");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\RVPN.Stop." + marker))
            using (var child = Process.Start(new ProcessStartInfo(Path.Combine(core, "RVPN.EngineHost.exe"), Process.GetCurrentProcess().Id + " " + marker)
            { WorkingDirectory = core, UseShellExecute = false, RedirectStandardInput = true, WindowStyle = ProcessWindowStyle.Hidden }))
            using (var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMilliseconds(900) })
            {
                try
                {
                    child.StandardInput.Write(profile.Config(profile.Host, port, secret, false, routingRules: rules));
                    child.StandardInput.Close();
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                    var clock = Stopwatch.StartNew();
                    bool listening = false;
                    while (clock.ElapsedMilliseconds < 5500 && !child.HasExited)
                    {
                        try
                        {
                            using (var response = await http.GetAsync("http://127.0.0.1:" + port + "/version"))
                                if (response.IsSuccessStatusCode) { listening = true; break; }
                        }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) { }
                        await Task.Delay(200);
                    }
                    if (!listening) throw new InvalidOperationException("Local core failed to start. Host exit: " + (child.HasExited ? child.ExitCode.ToString() : "running"));
                    stop.Set();
                    if (!await Task.Run(() => child.WaitForExit(6500)) || child.ExitCode != 0)
                        throw new InvalidOperationException("Local core did not stop cleanly");
                }
                finally { stop.Set(); if (!child.HasExited) child.Kill(); }
            }
        }
    }
}
