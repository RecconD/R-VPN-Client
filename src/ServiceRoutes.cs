namespace RVpn
{
    internal sealed class RouteService
    {
        internal readonly string Id, Name;
        internal readonly string[] Processes, Domains;

        internal RouteService(string id, string name, string[] processes, params string[] domains)
        {
            Id = id;
            Name = name;
            Processes = processes;
            Domains = domains;
        }

        internal static readonly RouteService[] All =
        {
            new RouteService("discord", "Discord",
                new[] { "Discord.exe", "DiscordCanary.exe", "DiscordPTB.exe", "DiscordDevelopment.exe" },
                "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media"),
            new RouteService("telegram", "Telegram",
                new[] { "Telegram.exe" },
                "telegram.org", "t.me", "telegram.me", "telegram.dog", "telegra.ph", "graph.org", "telesco.pe"),
            new RouteService("youtube", "YouTube", new string[0],
                "youtube.com", "youtu.be", "youtube-nocookie.com", "googlevideo.com", "ytimg.com",
                "youtubei.googleapis.com", "youtube.googleapis.com", "youtubekids.com")
        };
    }
}
