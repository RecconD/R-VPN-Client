using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RVpn
{
    internal class UserFault : Exception
    {
        internal UserFault(string message) : base(message) { }
    }

    internal sealed class AccessRevokedFault : UserFault
    {
        internal AccessRevokedFault() : base("Доступ больше не активен. Введите новый код.") { }
    }

    internal static class DataJson
    {
        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
        }

        internal static string Write(object value) { return Serializer().Serialize(value); }
        internal static T Read<T>(string value) { return Serializer().Deserialize<T>(value); }

        internal static long Long(IDictionary<string, object> data, string name)
        {
            object value;
            long number;
            return data != null && data.TryGetValue(name, out value) &&
                long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out number) && number >= 0 ? number : 0;
        }
    }

    internal sealed class ClientSettings
    {
        internal const string OriginalService = "https://31.77.138.106/";
        private const string OriginalHost = "31.77.138.106";
        internal const string RelayService = "https://31.77.57.217:8443/";
        internal const string BackupRelayService = "https://31.77.57.217:2053/";

        public string activationService { get; set; }
        public string probeUrl { get; set; }
        public bool automaticUpdateChecks { get; set; }
        public bool connectionDiagnostics { get; set; }

        internal Uri Service { get; private set; }
        internal Uri Probe { get; private set; }
        internal bool Demo { get { return Service == null; } }

        internal static ClientSettings Load(string directory)
        {
            var path = Path.Combine(directory, "RVPN.Settings.json");
            var value = new ClientSettings();
            if (File.Exists(path))
            {
                try
                {
                    if (new FileInfo(path).Length > 4096) throw new FormatException();
                    value = DataJson.Read<ClientSettings>(File.ReadAllText(path)) ?? new ClientSettings();
                }
                catch (Exception)
                {
                    throw new UserFault("Проверьте настройки в RVPN.Settings.json.");
                }
            }
            var consent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R VPN", "privacy.json");
            if (File.Exists(consent) && new FileInfo(consent).Length < 1024)
            {
                try
                {
                    var options = DataJson.Read<ClientSettings>(File.ReadAllText(consent));
                    if (options != null) { value.automaticUpdateChecks = options.automaticUpdateChecks; value.connectionDiagnostics = options.connectionDiagnostics; }
                }
                catch (Exception) { value.automaticUpdateChecks = value.connectionDiagnostics = false; }
            }
            if (!String.IsNullOrWhiteSpace(value.activationService))
            {
                value.Service = ReadHttps(value.activationService.TrimEnd('/') + "/");
                if (!String.IsNullOrEmpty(value.Service.Query)) throw new UserFault("В адресе активации не должно быть параметров.");
                if (value.Service.AbsoluteUri == OriginalService) value.Service = new Uri(RelayService);
            }
            value.Probe = ReadHttps(String.IsNullOrWhiteSpace(value.probeUrl) ? "https://www.gstatic.com/generate_204" : value.probeUrl);
            return value;
        }

        internal bool AcceptsSavedOrigin(string origin)
        {
            return Service.AbsoluteUri == origin || (Service.AbsoluteUri == RelayService && origin == OriginalService);
        }

        internal VlessProfile ConnectionProfile(string link, bool legacy = true)
        {
            var profile = VlessProfile.Parse(link);
            if (legacy && Service.AbsoluteUri == RelayService && profile.Host == OriginalHost &&
                profile.ServerName == OriginalHost && profile.WebSocketPath == "/v1/tunnel")
            {
                profile.Host = profile.ServerName = profile.WebSocketHost = Service.IdnHost;
                profile.Port = Service.Port;
            }
            return profile;
        }

        private static Uri ReadHttps(string value)
        {
            Uri result;
            if (!Uri.TryCreate(value, UriKind.Absolute, out result) || result.Scheme != Uri.UriSchemeHttps ||
                !String.IsNullOrEmpty(result.UserInfo) || !String.IsNullOrEmpty(result.Fragment) || result.IsLoopback)
                throw new UserFault("Укажите действительный HTTPS-адрес в RVPN.Settings.json.");
            return result;
        }
    }

    internal sealed class ServerNode
    {
        public string id { get; set; }
        public string country { get; set; }
        public string city { get; set; }
        public bool online { get; set; }
        public string probeUrl { get; set; }
    }

    internal static class ServerSelection
    {
        internal static async Task<ServerNode> Check(ServerNode[] nodes, CancellationToken cancel)
        {
            using (var http = new HttpClient(new HttpClientHandler {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false
            }) { Timeout = TimeSpan.FromSeconds(2) })
            {
                var checks = nodes.Where(node => node != null && node.online)
                    .Select(async node => new KeyValuePair<ServerNode, long[]>(node,
                        await Measure(http, node, cancel).ConfigureAwait(false)));
                var results = await Task.WhenAll(checks).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                return Best(results);
            }
        }

        internal static ServerNode Best(IEnumerable<KeyValuePair<ServerNode, long[]>> results)
        {
            return results.Where(result => result.Value.Length >= 2)
                .Select(result => new { Node = result.Key, Times = result.Value.OrderBy(time => time).ToArray() })
                .OrderByDescending(result => result.Times.Length)
                .ThenBy(result => (result.Times[(result.Times.Length - 1) / 2] + result.Times[result.Times.Length / 2]) / 2.0)
                .ThenBy(result => result.Times.Last() - result.Times.First())
                .Select(result => result.Node)
                .FirstOrDefault();
        }

        private static async Task<long[]> Measure(HttpClient http, ServerNode node, CancellationToken cancel)
        {
            Uri url;
            if (!Uri.TryCreate(node.probeUrl, UriKind.Absolute, out url) || url.Scheme != Uri.UriSchemeHttps ||
                url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
                url.AbsolutePath != "/rvpn-probe/" + node.id)
                return new long[0];

            var replies = new List<long>();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.ConnectionClose = true;
                    request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                    var elapsed = Stopwatch.StartNew();
                    try
                    {
                        using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
                            if (response.StatusCode == HttpStatusCode.NoContent) replies.Add(elapsed.ElapsedMilliseconds);
                    }
                    catch (HttpRequestException) { }
                    catch (OperationCanceledException) { cancel.ThrowIfCancellationRequested(); }
                }
            }
            return replies.ToArray();
        }
    }

    internal sealed class Account
    {
        public string userId { get; set; }
        public string accessToken { get; set; }
        public string status { get; set; }
        public string expiresAt { get; set; }
        public string vlessUrl { get; set; }
        public string[] alternateVlessUrls { get; set; }
        public long usedBytes { get; set; }
        public long limitBytes { get; set; }
        public string assignedNode { get; set; }
        public ServerNode[] nodes { get; set; }
        public string[] directDomains { get; set; }

        internal DateTimeOffset? Expiration
        {
            get
            {
                DateTimeOffset value;
                return DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out value) ? (DateTimeOffset?)value : null;
            }
        }

        internal bool CanConnect
        {
            get
            {
                return status == "pending" || (status == "active" &&
                    Expiration.HasValue && Expiration.Value > DateTimeOffset.UtcNow);
            }
        }

        internal Account Validate(string savedToken)
        {
            if (String.IsNullOrEmpty(accessToken)) accessToken = savedToken;
            if (String.IsNullOrEmpty(userId) || !Regex.IsMatch(userId, @"^[a-zA-Z0-9._-]{3,64}$") ||
                String.IsNullOrEmpty(accessToken) || !Regex.IsMatch(accessToken, @"^[A-Za-z0-9._~-]{32,512}$") ||
                (status != "pending" && status != "active" && status != "expired" && status != "disabled") ||
                (status == "active" && !Expiration.HasValue) || usedBytes < 0 || limitBytes < 0)
                throw new UserFault("Сервер вернул неполные данные подписки.");
            if (CanConnect) VlessProfile.Parse(vlessUrl);
            if (nodes != null)
                nodes = Array.FindAll(nodes, node => node != null && Regex.IsMatch(node.id ?? "", @"^[a-z0-9-]{2,24}$") &&
                    !String.IsNullOrWhiteSpace(node.country) && !String.IsNullOrWhiteSpace(node.city));
            directDomains = (directDomains ?? new string[0])
                .Where(domain => domain != null && domain.Length <= 253 && Regex.IsMatch(domain,
                    @"\A(?:ru|su|xn--p1ai|(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{2,59}))\z"))
                .Distinct(StringComparer.Ordinal).OrderBy(domain => domain, StringComparer.Ordinal).Take(100).ToArray();
            return this;
        }
    }

    internal sealed class LocalState
    {
        public string deviceId { get; set; }
        public string origin { get; set; }
        public Account account { get; set; }
        public string[] bypassServices { get; set; }
        public bool lightTheme { get; set; }
        public string nodeSelection { get; set; }
        public Dictionary<string, bool> sites { get; set; }
        public RoutingRule[] routingRules { get; set; }
    }

    internal sealed class ClientStore
    {
        private readonly string file;
        private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("R VPN Desktop state v1");

        internal ClientStore(string directory) { file = Path.Combine(directory, "state.dat"); }

        internal LocalState Load()
        {
            try
            {
                if (File.Exists(file) && new FileInfo(file).Length < 192 * 1024)
                {
                    var clear = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser);
                    try
                    {
                        var state = DataJson.Read<LocalState>(Encoding.UTF8.GetString(clear));
                        Guid id;
                        if (state != null && Guid.TryParseExact(state.deviceId, "N", out id)) return state;
                    }
                    finally { Array.Clear(clear, 0, clear.Length); }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (CryptographicException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return new LocalState { deviceId = Guid.NewGuid().ToString("N") };
        }

        internal void Save(LocalState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var clear = Encoding.UTF8.GetBytes(DataJson.Write(state));
            var staging = file + "." + Guid.NewGuid().ToString("N");
            try
            {
                var protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(staging, protectedBytes);
                if (File.Exists(file)) File.Replace(staging, file, null);
                else File.Move(staging, file);
            }
            finally
            {
                Array.Clear(clear, 0, clear.Length);
                if (File.Exists(staging)) File.Delete(staging);
            }
        }
    }

    internal static class ActivationCode
    {
        internal static string Symbols(string value)
        {
            return Regex.Replace((value ?? "").ToUpperInvariant(), "[^A-Z0-9]", "");
        }

        internal static string Format(string value)
        {
            var clean = Symbols(value);
            if (clean.Length > 8) clean = clean.Substring(0, 8);
            return clean.Length > 4 ? clean.Insert(4, "-") : clean;
        }
    }

    internal sealed class ActivationClient : IDisposable
    {
        private readonly HttpClient http;
        private readonly Uri[] origins;
        private volatile Uri lastOrigin;

        internal ActivationClient(Uri service)
        {
            origins = service.AbsoluteUri == ClientSettings.RelayService
                ? new[] { service, new Uri(ClientSettings.BackupRelayService), new Uri(ClientSettings.OriginalService) }
                : new[] { service };
            lastOrigin = service;
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                Timeout = TimeSpan.FromSeconds(15),
                MaxResponseContentBufferSize = 65536
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("R-VPN-Windows/0.1");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        internal Task<Account> Redeem(string code, string deviceId, CancellationToken cancel)
        {
            if (!Regex.IsMatch(code, @"^[A-Z0-9]{4}-[A-Z0-9]{4}$")) throw new UserFault("Введите восемь символов кода.");
            return Send(HttpMethod.Post, "v1/redeem", DataJson.Write(new { code = code, deviceId = deviceId }), null, cancel);
        }

        internal Task<Account> Refresh(string token, CancellationToken cancel, string node = null)
        {
            if (node == null) return Send(HttpMethod.Get, "v1/profile", null, token, cancel);
            return Send(HttpMethod.Post, "v1/connect", DataJson.Write(new { node = node }), token, cancel);
        }

        internal async Task ReportConnection(string token, int port, bool reachable, string node, CancellationToken cancel)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(lastOrigin, "v1/reachability")))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(DataJson.Write(new { port = port, reachable = reachable, node = node }),
                    Encoding.UTF8, "application/json");
                using (await http.SendAsync(request, timeout.Token).ConfigureAwait(false)) { }
            }
        }

        private async Task<Account> Send(HttpMethod method, string path, string body, string token, CancellationToken cancel)
        {
            var first = lastOrigin;
            var endpoints = origins.OrderBy(origin => origin != first).ToArray();
            string failure = "Сервис активации сейчас недоступен.";
            foreach (var origin in endpoints)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using (var request = new HttpRequestMessage(method, new Uri(origin, path)))
            {
                if (endpoints.Length > 1) timeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                try
                {
                    using (var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status == 502 || status == 503 || status == 504) continue;
                        if ((int)response.StatusCode == 429) throw new UserFault("Слишком много попыток. Попробуйте позже.");
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            if (token != null) throw new AccessRevokedFault();
                            throw new UserFault("Код не принят. Проверьте его у владельца VPN.");
                        }
                        if (token == null && response.StatusCode == HttpStatusCode.Conflict)
                            throw new UserFault("Ключ уже активирован на другом устройстве.");
                        if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Conflict)
                            throw new UserFault(token == null ? "Код не подошёл или уже использован." : "Профиль не найден. Введите новый код.");
                        if (!response.IsSuccessStatusCode) throw new UserFault("Сервис активации сейчас недоступен.");
                        var account = DataJson.Read<Account>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        if (account == null) throw new UserFault("Сервер вернул пустой профиль.");
                        account.Validate(token);
                        lastOrigin = origin;
                        return account;
                    }
                }
                catch (OperationCanceledException)
                {
                    cancel.ThrowIfCancellationRequested();
                    failure = "Сервер активации не ответил вовремя.";
                }
                catch (HttpRequestException) { failure = "Не удалось связаться с сервером активации."; }
                catch (ArgumentException) { throw new UserFault("Сервер вернул некорректный ответ."); }
                catch (InvalidOperationException) { throw new UserFault("Сервер вернул некорректный ответ."); }
            }
            throw new UserFault(failure);
        }

        public void Dispose() { http.Dispose(); }
    }
}
