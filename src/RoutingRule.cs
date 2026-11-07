using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RVpn
{
    internal sealed class RoutingRule
    {
        internal static readonly string[] Types = { "DOMAIN", "DOMAIN-SUFFIX", "DOMAIN-KEYWORD", "DOMAIN-REGEX",
            "GEOSITE", "GEOIP", "SRC-GEOIP", "IP-CIDR", "IP-CIDR6", "SRC-IP-CIDR", "SRC-PORT", "DST-PORT",
            "PROCESS-NAME", "PROCESS-PATH", "PROCESS-PATH-REGEX", "NETWORK", "IN-TYPE", "IN-NAME", "RULE-SET", "MATCH" };
        internal static readonly string[] Actions = { "DIRECT", "REJECT", "REJECT-DROP", "PASS", "R VPN" };
        internal const int Limit = 100;
        internal const string RussiaAddresses = "geoip-ru", RussiaDomains = "geosite-category-ru";
        private static readonly Lazy<object[]> GeoIp = new Lazy<object[]>(() => ReadSet(RussiaAddresses));
        private static readonly Lazy<object[]> GeoSite = new Lazy<object[]>(() => ReadSet(RussiaDomains));

        public string type { get; set; }
        public string value { get; set; }
        public string action { get; set; }

        internal string Line { get { return type + "," + value + "," + action; } }

        internal static RoutingRule[] Defaults()
        {
            return new[] {
                "PROCESS-NAME,steamwebhelper.exe,DIRECT", "PROCESS-NAME,steamservice.exe,DIRECT",
                "PROCESS-NAME,dota.exe,DIRECT", "PROCESS-NAME,dota2.exe,DIRECT", "PROCESS-NAME,steam.exe,DIRECT",
                "PROCESS-NAME,cs2.exe,DIRECT", "PROCESS-NAME,qbittorrent.exe,REJECT", "PROCESS-NAME,qbittorrent,REJECT",
                "PROCESS-NAME,uTorrent.exe,REJECT", "PROCESS-NAME,BitTorrent.exe,REJECT",
                "PROCESS-NAME,transmission-qt.exe,REJECT", "PROCESS-NAME,transmission-daemon,REJECT",
                "PROCESS-NAME,deluge.exe,REJECT", "PROCESS-NAME,deluge,REJECT",
                "PROCESS-NAME,WebTorrent.exe,REJECT", "PROCESS-NAME,WebTorrent,REJECT",
                "GEOIP,LAN,DIRECT", "DOMAIN-SUFFIX,ru,DIRECT", "DOMAIN-SUFFIX,su,DIRECT",
                "DOMAIN-SUFFIX,xn--p1ai,DIRECT", "GEOSITE,category-ru,DIRECT", "GEOIP,RU,DIRECT"
            }.Select(ParseLine).ToArray();
        }

        internal static RoutingRule ParseLine(string line)
        {
            var first = line.IndexOf(',');
            var last = line.LastIndexOf(',');
            return first < 0 || last == first ? null : Parse(line.Substring(0, first),
                line.Substring(first + 1, last - first - 1), line.Substring(last + 1));
        }

        internal static RoutingRule Parse(string type, string value, string action)
        {
            type = (type ?? "").Trim().ToUpperInvariant();
            value = (value ?? "").Trim();
            action = (action ?? "").Trim().ToUpperInvariant();
            if (!Types.Contains(type) || !Actions.Contains(action) || value.Length > 512 ||
                value.Any(Char.IsControl) || (value.Length == 0 && type != "MATCH")) return null;
            switch (type)
            {
                case "DOMAIN":
                case "DOMAIN-SUFFIX":
                    try { value = new IdnMapping().GetAscii(value.TrimStart('.').TrimEnd('.')).ToLowerInvariant(); }
                    catch (ArgumentException) { return null; }
                    if (Uri.CheckHostName(value) != UriHostNameType.Dns || value.Length > 253 ||
                        !Regex.IsMatch(value, @"\A[a-z0-9-]+(?:\.[a-z0-9-]+)*\z") ||
                        (type == "DOMAIN" && !value.Contains("."))) return null;
                    break;
                case "DOMAIN-KEYWORD":
                    if (!Regex.IsMatch(value, @"\A[a-zA-Z0-9.-]+\z")) return null;
                    value = value.ToLowerInvariant();
                    break;
                case "DOMAIN-REGEX":
                case "PROCESS-PATH-REGEX":
                    try { new Regex(value); }
                    catch (ArgumentException) { return null; }
                    break;
                case "PROCESS-NAME":
                    if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                    break;
                case "PROCESS-PATH":
                    if (!Regex.IsMatch(value, @"\A[A-Za-z]:[\\/]") || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                        return null;
                    value = value.Replace('/', '\\');
                    break;
                case "GEOIP":
                case "SRC-GEOIP":
                    value = value.ToUpperInvariant();
                    if (value != "RU" && value != "LAN") return null;
                    break;
                case "GEOSITE":
                    value = value.ToLowerInvariant();
                    if (value != "category-ru") return null;
                    break;
                case "RULE-SET":
                    value = value.ToLowerInvariant();
                    if (value != RussiaAddresses && value != RussiaDomains) return null;
                    break;
                case "IP-CIDR":
                case "IP-CIDR6":
                case "SRC-IP-CIDR":
                    var parts = value.Split('/');
                    IPAddress ip;
                    int prefix;
                    if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out ip) ||
                        !Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefix) ||
                        prefix < 0 || prefix > (ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128) ||
                        (type == "IP-CIDR6" && ip.AddressFamily != AddressFamily.InterNetworkV6)) return null;
                    value = ip + "/" + prefix;
                    break;
                case "SRC-PORT":
                case "DST-PORT":
                    int port;
                    if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                        return null;
                    value = port.ToString(CultureInfo.InvariantCulture);
                    break;
                case "NETWORK":
                    value = value.ToLowerInvariant();
                    if (value != "tcp" && value != "udp" && value != "icmp") return null;
                    break;
                case "IN-TYPE":
                case "IN-NAME":
                    value = value.ToLowerInvariant();
                    if (value != (type == "IN-TYPE" ? "tun" : "tun-in")) return null;
                    break;
                case "MATCH":
                    if (value.Length > 0) return null;
                    break;
            }
            return new RoutingRule { type = type, value = value, action = action };
        }

        internal bool UsesProcess { get { return type.StartsWith("PROCESS-", StringComparison.Ordinal); } }
        internal bool UsesDns { get { return type.StartsWith("DOMAIN", StringComparison.Ordinal) || SetTag == RussiaDomains; } }
        internal string SetTag
        {
            get
            {
                if (type == "GEOSITE") return RussiaDomains;
                if ((type == "GEOIP" || type == "SRC-GEOIP") && value == "RU") return RussiaAddresses;
                return type == "RULE-SET" ? value : null;
            }
        }

        internal Dictionary<string, object> Matcher()
        {
            var match = new Dictionary<string, object>();
            switch (type)
            {
                case "DOMAIN": match["domain"] = new[] { value }; break;
                case "DOMAIN-SUFFIX": match["domain_suffix"] = new[] { value }; break;
                case "DOMAIN-KEYWORD": match["domain_keyword"] = new[] { value }; break;
                case "DOMAIN-REGEX": match["domain_regex"] = new[] { value }; break;
                case "PROCESS-NAME":
                    match["process_path_regex"] = new[] { @"(?i)(?:^|[\\/])" + Regex.Escape(value) + "$" }; break;
                case "PROCESS-PATH":
                    match["process_path_regex"] = new[] { "(?i)^" + Regex.Escape(value) + "$" }; break;
                case "PROCESS-PATH-REGEX": match["process_path_regex"] = new[] { value }; break;
                case "IP-CIDR":
                case "IP-CIDR6": match["ip_cidr"] = new[] { value }; break;
                case "SRC-IP-CIDR": match["source_ip_cidr"] = new[] { value }; break;
                case "SRC-PORT": match["source_port"] = new[] { Int32.Parse(value, CultureInfo.InvariantCulture) }; break;
                case "DST-PORT": match["port"] = new[] { Int32.Parse(value, CultureInfo.InvariantCulture) }; break;
                case "NETWORK": match["network"] = new[] { value }; break;
                case "IN-TYPE":
                case "IN-NAME":
                case "MATCH": match["inbound"] = new[] { "tun-in" }; break;
                case "GEOIP":
                case "SRC-GEOIP":
                    if (value == "LAN") match[type == "GEOIP" ? "ip_is_private" : "source_ip_is_private"] = true;
                    else
                    {
                        match["rule_set"] = new[] { SetTag };
                        if (type == "SRC-GEOIP") match["rule_set_ip_cidr_match_source"] = true;
                    }
                    break;
                case "GEOSITE":
                case "RULE-SET": match["rule_set"] = new[] { SetTag }; break;
            }
            return match;
        }

        internal static object InlineSet(string tag)
        {
            return new { type = "inline", tag = tag, rules = tag == RussiaAddresses ? GeoIp.Value : GeoSite.Value };
        }

        private static object[] ReadSet(string tag)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(tag + ".json.gz"))
            using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
                return DataJson.Read<object[]>(reader.ReadToEnd());
        }
    }
}
