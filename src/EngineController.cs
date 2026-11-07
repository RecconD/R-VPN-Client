using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RVpn
{
    internal sealed class VlessProfile
    {
        internal string Host, Uuid, ServerName, PublicKey, ShortId, WebSocketPath, WebSocketHost;
        internal int Port;

        internal static VlessProfile Parse(string link)
        {
            Uri uri;
            Guid id;
            if (String.IsNullOrEmpty(link) || link.Length > 4096 || !Uri.TryCreate(link, UriKind.Absolute, out uri) ||
                uri.Scheme != "vless" || uri.Port < 1 || uri.HostNameType == UriHostNameType.Unknown ||
                !Guid.TryParseExact(uri.UserInfo, "D", out id) || id == Guid.Empty)
                throw new UserFault("Не удалось прочитать параметры сервера.");

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0) continue;
                var name = Uri.UnescapeDataString(pair.Substring(0, separator));
                if (parameters.ContainsKey(name)) throw new UserFault("В ключе повторяются параметры сервера.");
                parameters[name] = Uri.UnescapeDataString(pair.Substring(separator + 1));
            }
            Func<string, string> get = name => parameters.ContainsKey(name) ? parameters[name] : "";
            var type = get("type");
            var sni = get("sni");
            var sid = get("sid");
            var publicKey = get("pbk");
            var encryption = get("encryption");
            var flow = get("flow");
            if (encryption == "" || encryption == "none")
            {
                if (type == "ws" && get("security") == "tls" && flow == "")
                {
                    var path = get("path");
                    if (sni == "") sni = uri.IdnHost;
                    if (Uri.CheckHostName(sni) != UriHostNameType.Unknown && sni.Length <= 253 &&
                        Regex.IsMatch(path, @"\A/[A-Za-z0-9_./~-]{0,511}\z"))
                        return new VlessProfile { Host = uri.IdnHost, Uuid = id.ToString("D"),
                            ServerName = sni, Port = uri.Port, WebSocketPath = path };
                }
                else if ((type == "tcp" || type == "raw" || type == "") && get("security") == "reality" &&
                    flow == "xtls-rprx-vision" && Uri.CheckHostName(sni) == UriHostNameType.Dns &&
                    sni.Length <= 253 && Regex.IsMatch(publicKey, @"^[A-Za-z0-9_-]{43}$") &&
                    Regex.IsMatch(sid, @"^(?:[A-Fa-f0-9]{2}){0,8}$"))
                    return new VlessProfile { Host = uri.IdnHost, Uuid = id.ToString("D"), ServerName = sni,
                        PublicKey = publicKey, ShortId = sid, Port = uri.Port };
            }
            throw new UserFault("Нужен профиль VLESS WebSocket с TLS или VLESS TCP с REALITY.");
        }

        internal string Config(string address, int controlPort, string secret, bool tun, string[] bypassServices = null,
            Dictionary<string, bool> sites = null, string[] directDomains = null, RoutingRule[] routingRules = null)
        {
            var ipv4 = IPAddress.Parse(address).AddressFamily == AddressFamily.InterNetwork;
            object[] inbounds = tun
                ? new object[] { new { type = "tun", tag = "tun-in", interface_name = "R VPN", mtu = 1400,
                    address = new[] { "172.31.249.1/30", "fdfe:dcba:9876::1/126" }, auto_route = true,
                    strict_route = true, stack = "system", dns_mode = "hijack",
                    route_exclude_address = new[] { address + (ipv4 ? "/32" : "/128") } } }
                : new object[0];
            var vpnOutbound = new Dictionary<string, object> {
                { "type", "vless" }, { "tag", "rvpn" }, { "server", address }, { "server_port", Port },
                { "uuid", Uuid }, { "packet_encoding", "xudp" }
            };
            // A wildcard source skips sing-box's default interface binding for the relay only.
            // Windows then uses the ordinary route for the relay address excluded above.
            if (tun) vpnOutbound.Add(ipv4 ? "inet4_bind_address" : "inet6_bind_address", ipv4 ? "0.0.0.0" : "::");
            if (WebSocketPath != null)
            {
                vpnOutbound["tls"] = new { enabled = true, server_name = ServerName, alpn = new[] { "http/1.1" } };
                var transport = new Dictionary<string, object> { { "type", "ws" }, { "path", WebSocketPath } };
                if (WebSocketHost != null) transport.Add("headers", new Dictionary<string, string> { { "Host", WebSocketHost } });
                vpnOutbound["transport"] = transport;
            }
            else
            {
                vpnOutbound["flow"] = "xtls-rprx-vision";
                vpnOutbound["tls"] = new { enabled = true, server_name = ServerName, utls = new { enabled = true, fingerprint = "chrome" },
                    reality = new { enabled = true, public_key = PublicKey, short_id = ShortId } };
            }
            var outbounds = new List<object> { vpnOutbound };
            var dnsServers = new List<object> { new { type = "https", tag = "secure-dns", server = "1.1.1.1",
                detour = "rvpn", tls = new { enabled = true, server_name = "cloudflare-dns.com" } } };
            // Both VPN exits currently have IPv4 internet access.
            var dnsRules = new List<object> { new { query_type = new[] { "AAAA" }, action = "predefined", rcode = "NOERROR" } };
            var routes = new List<object> { new { action = "sniff" }, new { protocol = "dns", action = "hijack-dns" } };
            var direct = new HashSet<string>(bypassServices ?? new string[0], StringComparer.Ordinal);
            var hasServiceBypass = RouteService.All.Any(service => direct.Contains(service.Id));
            var siteRules = sites ?? new Dictionary<string, bool>();
            var managedDomains = directDomains ?? new string[0];
            var manualRules = (routingRules ?? new RoutingRule[0]).Where(rule => rule.action != "PASS").ToArray();
            if (hasServiceBypass || siteRules.Any(site => !site.Value) || managedDomains.Length > 0 ||
                manualRules.Any(rule => rule.action == "DIRECT"))
            {
                outbounds.Add(new { type = "direct", tag = "direct", domain_resolver = "secure-dns" });
                dnsServers.Add(new { type = "https", tag = "direct-dns", server = "1.1.1.1",
                    tls = new { enabled = true, server_name = "cloudflare-dns.com" } });
            }
            // A specific host takes precedence over a wider suffix and service rules.
            foreach (var site in siteRules.OrderByDescending(site => site.Key.Length))
            {
                var domains = new[] { site.Key };
                routes.Add(new { domain_suffix = domains, action = "route", outbound = site.Value ? "rvpn" : "direct" });
                if (site.Value)
                    dnsRules.Add(new { domain_suffix = domains, action = "route", server = "secure-dns" });
                else
                    AddDirectDnsRules(dnsRules, domains);
            }
            foreach (var rule in manualRules)
            {
                var match = rule.Matcher();
                if (rule.UsesDns)
                {
                    if (rule.action == "DIRECT") AddDirectDnsRules(dnsRules, match);
                    else
                    {
                        var dnsRule = new Dictionary<string, object>(match);
                        if (rule.action == "R VPN") { dnsRule["action"] = "route"; dnsRule["server"] = "secure-dns"; }
                        else { dnsRule["action"] = "reject"; dnsRule["method"] = rule.action == "REJECT-DROP" ? "drop" : "default"; }
                        dnsRules.Add(dnsRule);
                    }
                }
                if (rule.action == "DIRECT" || rule.action == "R VPN")
                {
                    match["action"] = "route";
                    match["outbound"] = rule.action == "DIRECT" ? "direct" : "rvpn";
                }
                else
                {
                    match["action"] = "reject";
                    match["method"] = rule.action == "REJECT-DROP" ? "drop" : "default";
                }
                routes.Add(match);
            }
            if (managedDomains.Length > 0)
            {
                routes.Add(new { domain_suffix = managedDomains, action = "route", outbound = "direct" });
                AddDirectDnsRules(dnsRules, managedDomains);
            }
            if (hasServiceBypass)
            {
                // A game process takes precedence over shared Steam hostnames.
                foreach (var service in RouteService.All.Where(service => service.Processes.Length > 0))
                    routes.Add(new { process_path_regex = new[] {
                        @"(?i)(^|[\\/])(" + String.Join("|", service.Processes.Select(Regex.Escape)) + ")$"
                    }, action = "route",
                        outbound = direct.Contains(service.Id) ? "direct" : "rvpn" });
                var domains = RouteService.All.Where(service => direct.Contains(service.Id))
                    .SelectMany(service => service.Domains).Distinct().ToArray();
                if (domains.Length > 0)
                {
                    routes.Add(new { domain_suffix = domains, action = "route", outbound = "direct" });
                    AddDirectDnsRules(dnsRules, domains);
                }
            }
            return DataJson.Write(new
            {
                log = new { disabled = true },
                inbounds = inbounds,
                outbounds = outbounds,
                dns = new { servers = dnsServers, rules = dnsRules, final = "secure-dns" },
                route = new { auto_detect_interface = tun, find_process = hasServiceBypass || manualRules.Any(rule => rule.UsesProcess),
                    rule_set = manualRules.Select(rule => rule.SetTag).Where(tag => tag != null).Distinct().Select(RoutingRule.InlineSet).ToArray(),
                    rules = routes, @final = "rvpn" },
                experimental = new { clash_api = new { external_controller = "127.0.0.1:" + controlPort,
                    secret = secret, access_control_allow_private_network = false } }
            });
        }

        private static void AddDirectDnsRules(List<object> rules, string[] domains)
        {
            AddDirectDnsRules(rules, new Dictionary<string, object> { { "domain_suffix", domains } });
        }

        private static void AddDirectDnsRules(List<object> rules, Dictionary<string, object> match)
        {
            rules.Add(new Dictionary<string, object>(match) { { "action", "evaluate" }, { "server", "direct-dns" }, { "timeout", "900ms" } });
            rules.Add(new Dictionary<string, object>(match) { { "action", "respond" }, { "match_response", true }, { "response_rcode", "NOERROR" } });
            rules.Add(new Dictionary<string, object>(match) { { "action", "route" }, { "server", "secure-dns" } });
        }
    }

    internal sealed class VpnProbeFault : Exception
    {
        internal VpnProbeFault() : base("Сервер VPN не ответил. Попробуйте подключиться ещё раз.") { }
    }

    internal sealed class EngineSession : IDisposable
    {
        private readonly Process host;
        private readonly EventWaitHandle stop;
        private readonly HttpClient control;
        private int disposed;
        internal IPAddress EntryAddress { get; private set; }
        internal bool HasExited { get { return Volatile.Read(ref disposed) != 0 || host.HasExited; } }

        internal static bool CheckRules(string directory, RoutingRule[] rules)
        {
            var core = Path.Combine(directory, "core");
            if (!CoreFiles.Check(core)) return false;
            var profile = VlessProfile.Parse("vless://00000000-0000-4000-8000-000000000001@203.0.113.11:443?security=tls&type=ws&path=%2Ftest");
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(Path.Combine(core, "sing-box.exe"), "check -c stdin --disable-color")
                {
                    WorkingDirectory = core, CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardInput = true, RedirectStandardError = true
                }))
                {
                    process.BeginErrorReadLine();
                    process.StandardInput.Write(profile.Config(profile.Host, 46329, "check-rules-only", true, routingRules: rules));
                    process.StandardInput.Close();
                    if (process.WaitForExit(8000)) return process.ExitCode == 0;
                    process.Kill();
                    return false;
                }
            }
            catch (IOException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        private EngineSession(Process host, EventWaitHandle stop, int port, string secret, IPAddress entryAddress)
        {
            this.host = host;
            this.stop = stop;
            EntryAddress = entryAddress;
            control = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
            {
                BaseAddress = new Uri("http://127.0.0.1:" + port + "/"),
                Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 1024 * 1024
            };
            control.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        internal static async Task<EngineSession> Start(string baseDirectory, VlessProfile profile, Uri probe,
            string[] bypassServices, Dictionary<string, bool> sites, string[] directDomains, RoutingRule[] routingRules, CancellationToken cancel)
        {
            var core = Path.Combine(baseDirectory, "core");
            var launcher = Path.Combine(core, "RVPN.EngineHost.exe");
            if (!File.Exists(launcher) || !await Task.Run(() => CoreFiles.Check(core), cancel).ConfigureAwait(false))
                throw new UserFault("Файлы VPN повреждены. Распакуйте архив заново.");

            var address = await ResolveAddress(profile.Host, cancel).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            var port = FreePort();
            var secretBytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(secretBytes);
            var secret = Convert.ToBase64String(secretBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Array.Clear(secretBytes, 0, secretBytes.Length);
            var marker = Guid.NewGuid().ToString("N");
            var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\RVPN.Stop." + marker);
            EngineSession session = null;
            Process process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo(launcher, Process.GetCurrentProcess().Id + " " + marker)
                {
                    UseShellExecute = false, WorkingDirectory = core, RedirectStandardInput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (process == null) throw new UserFault("Не удалось запустить VPN.");
                session = new EngineSession(process, stopEvent, port, secret, IPAddress.Parse(address));
                byte[] config = Encoding.UTF8.GetBytes(profile.Config(address, port, secret, true, bypassServices, sites, directDomains, routingRules));
                try
                {
                    await process.StandardInput.BaseStream.WriteAsync(config, 0, config.Length, cancel).ConfigureAwait(false);
                    process.StandardInput.Close();
                }
                finally { Array.Clear(config, 0, config.Length); }

                var deadline = Stopwatch.StartNew();
                bool ready = false;
                while (deadline.Elapsed < TimeSpan.FromSeconds(9) && !session.HasExited)
                {
                    cancel.ThrowIfCancellationRequested();
                    var response = await session.Read("version", cancel).ConfigureAwait(false);
                    if (response != null) { ready = true; break; }
                    await Task.Delay(230, cancel).ConfigureAwait(false);
                }
                if (!ready) throw new UserFault("Не удалось запустить сетевой адаптер R VPN.");

                var delay = await session.Read("proxies/rvpn/delay?timeout=4500&url=" + Uri.EscapeDataString(probe.AbsoluteUri), cancel).ConfigureAwait(false);
                var latency = DataJson.Long(delay, "delay");
                if (latency <= 0 || latency > 4500 || session.HasExited)
                    throw new VpnProbeFault();
                return session;
            }
            catch
            {
                if (session != null) session.Dispose();
                else
                {
                    if (process != null) { try { process.Kill(); } catch (InvalidOperationException) { } process.Dispose(); }
                    stopEvent.Dispose();
                }
                throw;
            }
        }

        internal static async Task<long?> MeasureEntryPing(IPAddress address, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(address, 1000).ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();
                    return reply.Status == IPStatus.Success ? (long?)reply.RoundtripTime : null;
                }
            }
            catch (PingException) { return null; }
        }

        private static async Task<string> ResolveAddress(string hostname, CancellationToken cancel)
        {
            IPAddress literal;
            if (IPAddress.TryParse(hostname, out literal)) return literal.ToString();
            try
            {
                var lookup = Dns.GetHostAddressesAsync(hostname);
                if (await Task.WhenAny(lookup, Task.Delay(9000, cancel)).ConfigureAwait(false) == lookup)
                {
                    var addresses = await lookup.ConfigureAwait(false);
                    var address = addresses.FirstOrDefault(value => value.AddressFamily == AddressFamily.InterNetwork)
                        ?? addresses.FirstOrDefault();
                    if (address != null) return address.ToString();
                }
            }
            catch (SocketException) { }
            cancel.ThrowIfCancellationRequested();
            throw new UserFault("Не удалось найти адрес VPN-сервера.");
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        private async Task<Dictionary<string, object>> Read(string path, CancellationToken cancel)
        {
            try
            {
                using (var response = await control.GetAsync(path, cancel).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                        return DataJson.Read<Dictionary<string, object>>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { cancel.ThrowIfCancellationRequested(); }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            return null;
        }

        internal async Task<long[]> Traffic(CancellationToken cancel)
        {
            var usage = await Read("connections", cancel).ConfigureAwait(false);
            if (usage == null) return null;
            return new[] { DataJson.Long(usage, "downloadTotal"), DataJson.Long(usage, "uploadTotal") };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try
            {
                stop.Set();
                if (!host.WaitForExit(6500)) { host.Kill(); host.WaitForExit(1000); }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { control.Dispose(); host.Dispose(); stop.Dispose(); }
        }
    }
}
