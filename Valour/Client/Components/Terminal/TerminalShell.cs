using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Services;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Channels;
using Valour.Shared.Models;

namespace Valour.Client.Components.Terminal;

/// <summary>
/// The side of the terminal that the shell writes to. The terminal component
/// implements it by forwarding to the terminal script.
/// </summary>
public interface ITerminalHost
{
    Task PrintAsync(IReadOnlyList<TermLine> lines);
    Task ReplaceAsync(string id, TermLine line);
    Task SetPromptAsync(List<TermSeg> prompt, string mode);
    Task SetStatusAsync(TermStatus status);
    Task ClearAsync();
    Task SetThemeAsync(string theme);
    Task CloseAsync();
    Task<bool> OpenInGuiAsync(Channel channel);
}

public sealed class TermStatus
{
    [JsonPropertyName("left")]
    public List<TermSeg> Left { get; set; } = new();

    [JsonPropertyName("right")]
    public List<TermSeg> Right { get; set; } = new();
}

public sealed class TermCompletion
{
    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("items")]
    public List<TermCompletionItem> Items { get; set; } = new();
}

public sealed class TermCompletionItem
{
    /// <summary>Text that replaces the word being completed.</summary>
    [JsonPropertyName("insert")]
    public string Insert { get; set; }

    /// <summary>Text shown when several completions are listed.</summary>
    [JsonPropertyName("display")]
    public string Display { get; set; }
}

/// <summary>
/// The text shown when the terminal opens. The terminal script plays the boot
/// lines once per page load and prints the welcome text each time.
/// </summary>
public sealed class TermBoot
{
    [JsonPropertyName("boot")]
    public List<TermLine> Boot { get; set; } = new();

    [JsonPropertyName("welcome")]
    public List<TermLine> Welcome { get; set; } = new();
}

/// <summary>
/// vsh, the ValourOS shell. Joined planets are directories under the home
/// directory, their chat channels are entries inside them, and entering a
/// channel switches the prompt to chat mode, where each line is sent as a
/// message. Messages go through the SDK, so encryption, permissions, and
/// origin scope are handled the same way as in a chat window.
/// </summary>
public sealed class TerminalShell : IAsyncDisposable
{
    private const string Host = "valouros";
    private const int HistoryCount = 30;
    private const int PageCount = 25;
    private static readonly TimeSpan TypingDuration = TimeSpan.FromSeconds(6);
    private static readonly DateTime BootTime = DateTime.UtcNow;

    private readonly ValourClient _client;
    private readonly ITerminalHost _host;

    // Keys that hold planet and channel connections open while the terminal
    // uses them. They are released when the terminal closes.
    private readonly string _connectionKey = "valouros-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<long> _openedPlanets = new();

    private Planet _planet;
    private bool _inDms;
    private Channel _channel;
    private bool _canPost;
    private long _oldestMessageId = long.MaxValue;
    private bool _reachedStart;

    private readonly HashSet<long> _printedMessages = new();
    private readonly Dictionary<long, string> _dmNames = new();
    private readonly HashSet<string> _pendingSends = new();
    private readonly Dictionary<long, DateTime> _typing = new();
    private CancellationTokenSource _commandCts;
    private CancellationTokenSource _typingCts;
    private string _theme = "deep-field";

    private readonly Dictionary<string, ShellCommand> _commands;
    private readonly Dictionary<string, ShellCommand> _chatCommands;

    private sealed record ShellCommand(
        string Name,
        string Usage,
        string Summary,
        Func<List<string>, CancellationToken, Task> Run,
        string[] Aliases = null,
        bool Hidden = false);

    private enum LocKind { Home, Dms, Planet, Channel }

    private sealed record Loc(LocKind Kind, Planet Planet = null, Channel Channel = null)
    {
        public static readonly Loc Home = new(LocKind.Home);
        public static readonly Loc Dms = new(LocKind.Dms);
    }

    public TerminalShell(ValourClient client, ITerminalHost host)
    {
        _client = client;
        _host = host;
        _commands = BuildCommands();
        _chatCommands = BuildChatCommands();
    }

    public bool InChat => _channel is not null;

    private Loc Current =>
        _channel is not null ? new Loc(LocKind.Channel, _planet, _channel) :
        _planet is not null ? new Loc(LocKind.Planet, _planet) :
        _inDms ? Loc.Dms : Loc.Home;

    private User Me => _client.Me;

    ////////////////
    // Lifecycle  //
    ////////////////

    public async Task<TermBoot> OpenAsync(string theme)
    {
        _theme = string.IsNullOrWhiteSpace(theme) ? _theme : theme;
        _client.MessageService.MessageDecrypted += OnMessageDecrypted;

        var boot = BuildBoot();

        // Resume where the terminal was left, reconnecting the channel and
        // printing what arrived while it was closed.
        if (_channel is not null)
        {
            var channel = _channel;
            _channel = null;
            await EnterChannelAsync(channel, resume: true);
        }
        else
        {
            if (_planet is not null)
                await EnsurePlanetOpenAsync(_planet);
            await RefreshPromptAsync();
        }

        return boot;
    }

    /// <summary>
    /// Releases realtime connections when the terminal is hidden. The current
    /// location is kept so the next open resumes there.
    /// </summary>
    public async Task SuspendAsync()
    {
        _commandCts?.Cancel();
        _client.MessageService.MessageDecrypted -= OnMessageDecrypted;

        if (_channel is not null)
            await DetachChannelAsync(_channel);

        foreach (var planetId in _openedPlanets)
            await _client.PlanetService.TryClosePlanetConnection(planetId, _connectionKey);
        _openedPlanets.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await SuspendAsync();
        _typingCts?.Cancel();
    }

    ///////////////
    // Execution //
    ///////////////

    public async Task ExecuteAsync(string line)
    {
        line ??= string.Empty;

        if (_channel is not null)
        {
            if (line.StartsWith('/') && !line.StartsWith("//"))
            {
                await RunCommandLineAsync(line[1..], _chatCommands, "/");
                return;
            }

            if (line.StartsWith("//"))
                line = line[1..];

            try
            {
                await SendAsync(line);
            }
            catch (Exception e)
            {
                _client.Logger.Log<TerminalShell>($"Chat line failed: {e}", "red");
                await PrintAsync(TermLine.Of($"vsh: couldn't send ({e.GetType().Name})", "err"));
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
            return;

        await RunCommandLineAsync(line, _commands, string.Empty);
    }

    private async Task RunCommandLineAsync(string line, Dictionary<string, ShellCommand> table, string prefix)
    {
        List<string> args;
        try
        {
            args = Tokenize(line).Select(x => x.Value).ToList();
        }
        catch (FormatException e)
        {
            await PrintAsync(TermLine.Of($"vsh: {e.Message}", "err"));
            return;
        }

        if (args.Count == 0)
            return;

        var name = args[0].ToLowerInvariant();
        if (!table.TryGetValue(name, out var command))
        {
            await PrintAsync(UnknownCommand(prefix + args[0], table, prefix));
            return;
        }

        _commandCts?.Dispose();
        _commandCts = new CancellationTokenSource();
        try
        {
            await command.Run(args, _commandCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Interrupted with Ctrl+C.
        }
        catch (Exception e)
        {
            _client.Logger.Log<TerminalShell>($"Command '{name}' failed: {e}", "red");
            await PrintAsync(TermLine.Of($"{command.Name}: something went wrong ({e.GetType().Name})", "err"));
        }
    }

    public void Interrupt() => _commandCts?.Cancel();

    /// <summary>Ctrl+D on an empty line: leaves a channel, or closes the terminal.</summary>
    public async Task EndOfInputAsync()
    {
        if (_channel is not null)
        {
            await LeaveChannelAsync();
            return;
        }

        await PrintAsync(TermLine.Of("logout", "dim"));
        await _host.CloseAsync();
    }

    public async Task NotifyTypingAsync()
    {
        if (_channel is not null && _canPost)
            await _channel.SendIsTyping();
    }

    private TermLine UnknownCommand(string name, Dictionary<string, ShellCommand> table, string prefix)
    {
        var suggestion = table.Values
            .Where(x => !x.Hidden)
            .Select(x => x.Name)
            .Distinct()
            .OrderBy(x => Distance(x, name.TrimStart('/')))
            .FirstOrDefault();

        var line = TermLine.Of($"vsh: command not found: {name}", "err");
        if (suggestion is not null && Distance(suggestion, name.TrimStart('/')) <= 2)
            line.Add("  did you mean ", "dim").Add(prefix + suggestion, "yellow").Add("?", "dim");
        return line;
    }

    //////////////
    // Commands //
    //////////////

    private Dictionary<string, ShellCommand> BuildCommands()
    {
        var list = new List<ShellCommand>
        {
            new("help", "help [command]", "list commands, or explain one", CmdHelp),
            new("man", "man <command>", "show the manual page for a command", CmdMan, Hidden: true),
            new("ls", "ls [-l] [path]", "list planets, direct messages, or channels", CmdLs, ["dir"]),
            new("cd", "cd [path]", "move between planets; cd into a channel to chat", CmdCd),
            new("pwd", "pwd", "print where you are", CmdPwd),
            new("tree", "tree [path]", "show a planet's channels as a tree", CmdTree),
            new("open", "open planet|channel|dm <name>", "open a planet, a channel, or a direct message", CmdOpen, ["join"]),
            new("planets", "planets", "list your planets with their numbers", CmdPlanets),
            new("dms", "dms", "list your direct messages", CmdDms),
            new("unread", "unread", "show where there are unread messages", CmdUnread),
            new("tail", "tail [-n count] <channel>", "print a channel's latest messages without joining", CmdTail, ["cat"]),
            new("gui", "gui [path]", "open a channel in the regular Valour window", CmdGui),
            new("whoami", "whoami", "print your user name", CmdWhoami),
            new("neofetch", "neofetch", "show system information, with style", CmdNeofetch, ["fastfetch"]),
            new("uname", "uname [-a]", "print system information", CmdUname),
            new("uptime", "uptime", "show how long ValourOS has been running", CmdUptime),
            new("date", "date", "print the date and time", CmdDate),
            new("echo", "echo [text]", "print text", CmdEcho),
            new("theme", "theme [name]", "change the terminal colors", CmdTheme),
            new("fortune", "fortune", "a message from the deep field", CmdFortune),
            new("rocket", "rocket", "launch a rocket", CmdRocket, ["sl"]),
            new("clear", "clear", "clear the screen (or press Ctrl+L)", CmdClear, ["reset"]),
            new("history", "history", "list previous commands", (_, _) => Task.CompletedTask),
            new("exit", "exit", "return to the Valour desktop", CmdExit, ["logout", "quit"]),
            new("sudo", "sudo <command>", "", CmdSudo, Hidden: true),
            new("rm", "rm", "", CmdRm, Hidden: true),
            new("vim", "vim", "", CmdEditor, ["vi", "nano", "emacs"], Hidden: true),
            new("ping", "ping", "", CmdPing, Hidden: true),
            new("hostname", "hostname", "", (_, _) => PrintAsync(TermLine.Of(Host)), Hidden: true),
        };

        return Index(list);
    }

    private Dictionary<string, ShellCommand> BuildChatCommands()
    {
        var list = new List<ShellCommand>
        {
            new("help", "/help", "list chat commands", CmdChatHelp),
            new("leave", "/leave", "leave the channel (or press Ctrl+D)", (_, _) => LeaveChannelAsync(), ["part", "exit"]),
            new("join", "/join <channel>", "switch to another channel", CmdChatJoin),
            new("more", "/more [count]", "load older messages", CmdChatMore),
            new("me", "/me <action>", "describe what you're doing", CmdChatMe),
            new("shrug", "/shrug [text]", @"append ¯\_(ツ)_/¯", (a, _) => SendAsync(JoinArgs(a, 1) + @" ¯\_(ツ)_/¯")),
            new("tableflip", "/tableflip [text]", "append (╯°□°)╯︵ ┻━┻", (a, _) => SendAsync(JoinArgs(a, 1) + " (╯°□°)╯︵ ┻━┻")),
            new("who", "/who", "list who has been chatting here", CmdChatWho),
            new("topic", "/topic", "show the channel description", CmdChatTopic),
            new("gui", "/gui", "open this channel in the regular Valour window", (_, _) => OpenGuiAsync(_channel)),
            new("clear", "/clear", "clear the screen", CmdClear),
            new("quit", "/quit", "return to the Valour desktop", CmdExit),
        };

        return Index(list);
    }

    private static Dictionary<string, ShellCommand> Index(List<ShellCommand> list)
    {
        var table = new Dictionary<string, ShellCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in list)
        {
            table[command.Name] = command;
            foreach (var alias in command.Aliases ?? Array.Empty<string>())
                table[alias] = command;
        }
        return table;
    }

    private async Task CmdHelp(List<string> args, CancellationToken ct)
    {
        if (args.Count > 1)
        {
            await CmdMan(args, ct);
            return;
        }

        var lines = new List<TermLine>
        {
            TermLine.Of("vsh, the ValourOS shell", "bold"),
            TermLine.Of("Planets are directories in ~, channels are inside them. cd into a channel to chat.", "dim"),
            TermLine.Empty,
        };

        foreach (var command in _commands.Values.Distinct().Where(x => !x.Hidden))
        {
            lines.Add(new TermLine()
                .Add("  " + command.Usage.PadRight(34), "cyan")
                .Add(command.Summary));
        }

        lines.Add(TermLine.Empty);
        lines.Add(TermLine.Of("Keys", "bold"));
        lines.AddRange(KeyHelp());
        await PrintAsync(lines);
    }

    private static IEnumerable<TermLine> KeyHelp()
    {
        (string keys, string what)[] keys =
        [
            ("Tab", "complete commands, planets, channels, and @names"),
            ("Up / Down, Ctrl+P / Ctrl+N", "move through history"),
            ("Ctrl+R", "search history"),
            ("Ctrl+A / Ctrl+E", "jump to the start or end of the line"),
            ("Alt+B / Alt+F", "move by word"),
            ("Ctrl+W / Ctrl+U / Ctrl+K", "delete a word, to the start, or to the end"),
            ("Ctrl+Y", "paste what was deleted last"),
            ("Ctrl+C", "cancel the line or the running command"),
            ("Ctrl+D", "leave the channel, or log out"),
            ("Ctrl+L", "clear the screen"),
            ("Ctrl+= / Ctrl+-", "make the text bigger or smaller"),
            ("Ctrl+/ or Cmd+/", "return to the Valour desktop"),
        ];

        foreach (var (k, what) in keys)
            yield return new TermLine().Add("  " + k.PadRight(34), "magenta").Add(what);
    }

    private async Task CmdMan(List<string> args, CancellationToken ct)
    {
        if (args.Count < 2)
        {
            await PrintAsync(TermLine.Of("What manual page do you want?", "err"),
                TermLine.Of("For example, try 'man open'.", "dim"));
            return;
        }

        var name = args[1].TrimStart('/');
        if (!_commands.TryGetValue(name, out var command) && !_chatCommands.TryGetValue(name, out command))
        {
            await PrintAsync(TermLine.Of($"No manual entry for {name}", "err"));
            return;
        }

        var title = $"{command.Name.ToUpperInvariant()}(1)";
        var middle = "ValourOS Manual";
        var gap = Math.Max(2, 60 - title.Length * 2 - middle.Length) / 2;
        var lines = new List<TermLine>
        {
            TermLine.Of(title + new string(' ', gap) + middle + new string(' ', gap) + title, "bold"),
            TermLine.Empty,
            TermLine.Of("NAME", "bold"),
            TermLine.Of($"       {command.Name} - {(string.IsNullOrEmpty(command.Summary) ? "you found a secret" : command.Summary)}"),
            TermLine.Empty,
            TermLine.Of("SYNOPSIS", "bold"),
            TermLine.Of($"       {command.Usage}", "cyan"),
        };

        var detail = ManDetail(command.Name);
        if (detail is not null)
        {
            lines.Add(TermLine.Empty);
            lines.Add(TermLine.Of("DESCRIPTION", "bold"));
            lines.AddRange(detail.Select(x => TermLine.Of("       " + x)));
        }

        if (command.Aliases is { Length: > 0 })
        {
            lines.Add(TermLine.Empty);
            lines.Add(TermLine.Of("ALIASES", "bold"));
            lines.Add(TermLine.Of("       " + string.Join(", ", command.Aliases)));
        }

        await PrintAsync(lines);
    }

    private static string[] ManDetail(string name) => name switch
    {
        "ls" => [
            "With no path, lists the current directory. In ~ that is your planets",
            "and dms/. Inside a planet it is the planet's channels. -l adds details.",
            "A * marks unread messages.",
        ],
        "cd" => [
            "Paths look like ~/Planet Name/#channel. Use Tab to complete names,",
            "'..' to go up, and 'cd' alone to go home. Changing into a chat channel",
            "starts chat mode, where every line you type is sent as a message and",
            "commands start with /. Leave with /leave or Ctrl+D.",
        ],
        "open" => [
            "open planet <name or number>   change into a planet",
            "open channel <name>            join a channel in the current planet",
            "open dm <name>                 join a direct message",
            "open <path>                    the same as cd",
            "Names can be shortened as long as only one thing matches.",
        ],
        "tail" => [
            "Prints the latest messages of a channel without joining it.",
            "-n sets how many messages to print (default 20, at most 100).",
        ],
        "theme" => [
            "Themes: deep-field, phosphor, amber, aubergine, dracula, paper.",
            "The choice is remembered on this device.",
        ],
        "gui" => [
            "Opens the channel in the regular Valour window and returns to the",
            "desktop. With no path, uses the channel you are in.",
        ],
        _ => null,
    };

    private async Task CmdChatHelp(List<string> args, CancellationToken ct)
    {
        var lines = new List<TermLine>
        {
            TermLine.Of("Chat mode", "bold"),
            TermLine.Of("Type a message and press Enter to send it. Shift+Enter starts a new line.", "dim"),
            TermLine.Of("Mention people with @name (Tab completes). Start a message with // to send a leading /.", "dim"),
            TermLine.Empty,
        };

        foreach (var command in _chatCommands.Values.Distinct())
            lines.Add(new TermLine().Add("  " + command.Usage.PadRight(22), "cyan").Add(command.Summary));

        await PrintAsync(lines);
    }

    private async Task CmdLs(List<string> args, CancellationToken ct)
    {
        var detailed = args.Skip(1).Any(x => x is "-l" or "-la" or "-al");
        var path = args.Skip(1).FirstOrDefault(x => !x.StartsWith('-'));

        var loc = Current;
        if (path is not null)
        {
            var (resolved, error) = await ResolvePathAsync(path, Current, ct);
            if (resolved is null)
            {
                await PrintAsync(TermLine.Of($"ls: {error}", "err"));
                return;
            }
            loc = resolved;
        }

        switch (loc.Kind)
        {
            case LocKind.Home:
                await PrintAsync(await ListHomeAsync(detailed));
                break;
            case LocKind.Dms:
                await PrintAsync(await ListDmsAsync(detailed));
                break;
            case LocKind.Planet:
                await PrintAsync(ListChannels(loc.Planet, detailed));
                break;
            case LocKind.Channel:
                await PrintAsync(new TermLine().Add(ChannelLabel(loc.Channel), ChannelStyle(loc.Channel)));
                break;
        }
    }

    private Task<List<TermLine>> ListHomeAsync(bool detailed)
    {
        var planets = _client.PlanetService.JoinedPlanets.ToList();
        var lines = new List<TermLine>();

        if (detailed)
        {
            lines.Add(TermLine.Of($"total {planets.Count + 1}", "dim"));
            lines.Add(new TermLine()
                .Add("drwx------  ", "dim").Add(Me.Name.PadRight(14), "yellow")
                .Add("direct       ", "dim").Add("dms/", "blue bold"));

            foreach (var planet in planets)
            {
                var unread = _client.UnreadService.IsPlanetUnread(planet.Id);
                var perms = planet.OwnerId == Me.Id ? "drwxr-x---  " : "dr-xr-x---  ";
                lines.Add(new TermLine()
                    .Add(perms, "dim")
                    .Add((planet.OwnerId == Me.Id ? Me.Name : "member").PadRight(14), "yellow")
                    .Add((planet.NodeName ?? "hub").PadRight(13), "dim")
                    .Add(planet.Name + "/", "blue bold")
                    .Add(unread ? " *" : "", "green bold"));
            }

            return Task.FromResult(lines);
        }

        var entries = new List<(string text, string style)> { ("dms/", "blue bold") };
        entries.AddRange(planets.Select(p =>
            (p.Name + "/" + (_client.UnreadService.IsPlanetUnread(p.Id) ? "*" : ""), "blue bold")));
        lines.AddRange(Columns(entries));
        return Task.FromResult(lines);
    }

    private async Task<List<TermLine>> ListDmsAsync(bool detailed)
    {
        var lines = new List<TermLine>();
        var dms = _client.ChannelService.DirectChatChannels.ToList();
        if (dms.Count == 0)
        {
            lines.Add(TermLine.Of("(no direct messages yet)", "dim"));
            return lines;
        }

        var entries = new List<(string, string)>();
        foreach (var dm in dms)
        {
            var name = "@" + await DmNameAsync(dm);
            var unread = _client.UnreadService.IsChannelUnread(null, dm.Id);
            if (detailed)
            {
                lines.Add(new TermLine()
                    .Add("-rw-------  ", "dim")
                    .Add(dm.ChannelType == ChannelTypeEnum.GroupChat ? "group   " : "direct  ", "dim")
                    .Add(dm.IsEncrypted ? "e2ee  " : "      ", "green")
                    .Add(name, "magenta bold")
                    .Add(unread ? " *" : "", "green bold"));
            }
            else
            {
                entries.Add((name + (unread ? "*" : ""), "magenta bold"));
            }
        }

        if (!detailed)
            lines.AddRange(Columns(entries));
        return lines;
    }

    private List<TermLine> ListChannels(Planet planet, bool detailed)
    {
        var channels = planet.Channels.ToList()
            .Where(x => x.ChannelType != ChannelTypeEnum.PlanetCategory)
            .ToList();

        if (channels.Count == 0)
            return [TermLine.Of("(no channels you can see)", "dim")];

        if (!detailed)
        {
            return Columns(channels.Select(c =>
                (ChannelLabel(c) + (_client.UnreadService.IsChannelUnread(planet.Id, c.Id) ? "*" : ""),
                    ChannelStyle(c))).ToList());
        }

        var lines = new List<TermLine> { TermLine.Of($"total {channels.Count}", "dim") };
        foreach (var channel in channels)
        {
            var parent = channel.ParentId is not null && planet.Channels.TryGet(channel.ParentId.Value, out var p)
                ? p.Name
                : "-";
            lines.Add(new TermLine()
                .Add(channel.IsChatChannel ? "-rw-r--r--  " : "crw-r--r--  ", "dim")
                .Add(Truncate(parent, 14).PadRight(15), "yellow")
                .Add(channel.IsEncrypted ? "e2ee  " : "      ", "green")
                .Add(ChannelLabel(channel), ChannelStyle(channel))
                .Add(_client.UnreadService.IsChannelUnread(planet.Id, channel.Id) ? " *" : "", "green bold")
                .Add(string.IsNullOrWhiteSpace(channel.Description) ? "" : "  " + Truncate(channel.Description, 48), "dim"));
        }
        return lines;
    }

    private async Task CmdCd(List<string> args, CancellationToken ct)
    {
        var path = args.Count > 1 ? args[1] : "~";
        if (path == "-")
            path = "..";

        var (loc, error) = await ResolvePathAsync(path, Current, ct);
        if (loc is null)
        {
            await PrintAsync(TermLine.Of($"cd: {error}", "err"));
            return;
        }

        await GoToAsync(loc, ct);
    }

    private async Task GoToAsync(Loc loc, CancellationToken ct)
    {
        switch (loc.Kind)
        {
            case LocKind.Home:
                _planet = null;
                _inDms = false;
                break;
            case LocKind.Dms:
                _planet = null;
                _inDms = true;
                break;
            case LocKind.Planet:
                var changed = _planet?.Id != loc.Planet.Id;
                _planet = loc.Planet;
                _inDms = false;
                if (changed)
                    await PrintPlanetMotdAsync(loc.Planet);
                break;
            case LocKind.Channel:
                _planet = loc.Planet;
                _inDms = loc.Planet is null;
                await EnterChannelAsync(loc.Channel);
                return;
        }

        await RefreshPromptAsync();
    }

    private async Task PrintPlanetMotdAsync(Planet planet)
    {
        var lines = new List<TermLine>
        {
            new TermLine().Add("── ", "dim").Add(planet.Name, "accent bold").Add(" " + new string('─', Math.Max(3, 56 - planet.Name.Length)), "dim"),
        };

        if (!string.IsNullOrWhiteSpace(planet.Description))
        {
            foreach (var row in Wrap(planet.Description.Trim(), 70).Take(4))
                lines.Add(TermLine.Of("  " + row, "dim"));
        }

        var chat = planet.Channels.ToList().Count(x => x.IsChatChannel);
        lines.Add(new TermLine()
            .Add("  ")
            .Add($"{chat} chat channel{(chat == 1 ? "" : "s")}", "cyan")
            .Add("  ·  ", "dim")
            .Add(planet.EncryptionMode.ToString().ToLowerInvariant() + " encryption", "green")
            .Add("  ·  ", "dim")
            .Add("try ", "dim").Add("ls", "yellow").Add(" or ", "dim").Add("cd #", "yellow").Add("<Tab>", "dim"));
        lines.Add(TermLine.Empty);
        await PrintAsync(lines);
    }

    private async Task CmdPwd(List<string> args, CancellationToken ct) =>
        await PrintAsync(TermLine.Of(PathOf(Current).Replace("~", "/home/" + Me.Name)));

    private async Task CmdTree(List<string> args, CancellationToken ct)
    {
        var loc = Current;
        if (args.Count > 1)
        {
            var (resolved, error) = await ResolvePathAsync(args[1], Current, ct);
            if (resolved is null)
            {
                await PrintAsync(TermLine.Of($"tree: {error}", "err"));
                return;
            }
            loc = resolved;
        }

        if (loc.Kind == LocKind.Home)
        {
            var planets = _client.PlanetService.JoinedPlanets.ToList();
            var lines = new List<TermLine> { TermLine.Of("~", "blue bold") };
            for (var i = 0; i < planets.Count; i++)
            {
                var last = i == planets.Count - 1;
                lines.Add(new TermLine().Add(last ? "└── " : "├── ", "dim").Add(planets[i].Name + "/", "blue bold"));
            }
            lines.Add(TermLine.Empty);
            lines.Add(TermLine.Of($"{planets.Count} planets. Run 'tree' inside a planet to see its channels.", "dim"));
            await PrintAsync(lines);
            return;
        }

        if (loc.Kind != LocKind.Planet)
        {
            await PrintAsync(TermLine.Of("tree: works on planets (try 'tree ~/Planet')", "err"));
            return;
        }

        var planet = loc.Planet;
        var all = planet.Channels.ToList();
        var output = new List<TermLine> { TermLine.Of(planet.Name, "blue bold") };
        var counts = (dirs: 0, files: 0);

        void Walk(long? parentId, string indent)
        {
            var children = all.Where(x => x.ParentId == parentId).ToList();
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var last = i == children.Count - 1;
                var line = new TermLine().Add(indent + (last ? "└── " : "├── "), "dim");

                if (child.ChannelType == ChannelTypeEnum.PlanetCategory)
                {
                    counts.dirs++;
                    output.Add(line.Add(child.Name + "/", "blue bold"));
                    Walk(child.Id, indent + (last ? "    " : "│   "));
                }
                else
                {
                    counts.files++;
                    var unread = _client.UnreadService.IsChannelUnread(planet.Id, child.Id);
                    output.Add(line.Add(ChannelLabel(child), ChannelStyle(child)).Add(unread ? " *" : "", "green bold"));
                }
            }
        }

        // Channels whose category is hidden from this person are listed last.
        var visibleIds = all.Select(x => x.Id).ToHashSet();
        Walk(null, "");
        foreach (var orphan in all.Where(x => x.ParentId is not null && !visibleIds.Contains(x.ParentId.Value)))
        {
            counts.files++;
            output.Add(new TermLine().Add("└── ", "dim").Add(ChannelLabel(orphan), ChannelStyle(orphan)));
        }

        output.Add(TermLine.Empty);
        output.Add(TermLine.Of($"{counts.dirs} categories, {counts.files} channels", "dim"));
        await PrintAsync(output);
    }

    private async Task CmdOpen(List<string> args, CancellationToken ct)
    {
        if (args.Count < 2)
        {
            await PrintAsync(TermLine.Of("usage: open planet|channel|dm <name>", "err"));
            return;
        }

        var kind = args[1].ToLowerInvariant();
        var name = JoinArgs(args, 2);

        switch (kind)
        {
            case "planet":
            {
                var (planet, error) = MatchPlanet(name);
                if (planet is null)
                {
                    await PrintAsync(TermLine.Of($"open: {error}", "err"));
                    return;
                }

                if (!await EnsurePlanetOpenAsync(planet))
                    return;

                await GoToAsync(new Loc(LocKind.Planet, planet), ct);
                return;
            }
            case "channel":
            {
                if (_planet is null)
                {
                    await PrintAsync(TermLine.Of("open: open a planet first (open planet <name>)", "err"),
                        TermLine.Of("      or give a full path, like cd ~/Planet/#general", "dim"));
                    return;
                }

                var (channel, error) = MatchChannel(_planet, name);
                if (channel is null)
                {
                    await PrintAsync(TermLine.Of($"open: {error}", "err"));
                    return;
                }

                await GoToAsync(new Loc(LocKind.Channel, _planet, channel), ct);
                return;
            }
            case "dm":
            {
                var (dm, error) = await MatchDmAsync(name);
                if (dm is null)
                {
                    await PrintAsync(TermLine.Of($"open: {error}", "err"));
                    return;
                }

                await GoToAsync(new Loc(LocKind.Channel, null, dm), ct);
                return;
            }
            default:
            {
                var (loc, error) = await ResolvePathAsync(JoinArgs(args, 1), Current, ct);
                if (loc is null)
                {
                    await PrintAsync(TermLine.Of($"open: {error}", "err"));
                    return;
                }

                await GoToAsync(loc, ct);
                return;
            }
        }
    }

    private async Task CmdPlanets(List<string> args, CancellationToken ct)
    {
        var planets = _client.PlanetService.JoinedPlanets.ToList();
        if (planets.Count == 0)
        {
            await PrintAsync(TermLine.Of("You haven't joined any planets yet. Find some with the Discover window.", "dim"));
            return;
        }

        var lines = new List<TermLine>();
        for (var i = 0; i < planets.Count; i++)
        {
            var planet = planets[i];
            var unread = _client.UnreadService.IsPlanetUnread(planet.Id);
            lines.Add(new TermLine()
                .Add($"{i + 1,4}  ", "dim")
                .Add(unread ? "● " : "  ", "green")
                .Add(planet.Name, unread ? "bold" : null)
                .Add(planet.OwnerId == Me.Id ? "  (owner)" : "", "yellow"));
        }
        lines.Add(TermLine.Of("open one with 'open planet <number or name>'", "dim"));
        await PrintAsync(lines);
    }

    private async Task CmdDms(List<string> args, CancellationToken ct) =>
        await PrintAsync(await ListDmsAsync(true));

    private async Task CmdUnread(List<string> args, CancellationToken ct)
    {
        var lines = new List<TermLine>();
        foreach (var planet in _client.PlanetService.JoinedPlanets.ToList())
        {
            if (!_client.UnreadService.IsPlanetUnread(planet.Id))
                continue;

            var line = new TermLine().Add("● ", "green").Add(planet.Name, "blue bold");
            if (_openedPlanets.Contains(planet.Id) || _client.PlanetService.IsPlanetConnected(planet))
            {
                var channels = planet.Channels.ToList()
                    .Where(c => c.IsChatChannel && _client.UnreadService.IsChannelUnread(planet.Id, c.Id))
                    .Select(c => "#" + c.Name);
                line.Add("  " + string.Join(" ", channels), "cyan");
            }
            lines.Add(line);
        }

        foreach (var dm in _client.ChannelService.DirectChatChannels.ToList())
        {
            if (_client.UnreadService.IsChannelUnread(null, dm.Id))
                lines.Add(new TermLine().Add("● ", "green").Add("@" + await DmNameAsync(dm), "magenta bold"));
        }

        if (lines.Count == 0)
            lines.Add(TermLine.Of("All caught up. The stars are quiet.", "dim"));

        await PrintAsync(lines);
    }

    private async Task CmdTail(List<string> args, CancellationToken ct)
    {
        var count = 20;
        string path = null;
        for (var i = 1; i < args.Count; i++)
        {
            if (args[i] == "-n" && i + 1 < args.Count && int.TryParse(args[i + 1], out var n))
            {
                count = n;
                i++;
            }
            else if (args[i].StartsWith("-n") && int.TryParse(args[i][2..], out var n2))
            {
                count = n2;
            }
            else if (args[i] == "-f")
            {
                // tail -f is joining the channel.
            }
            else
            {
                path = args[i];
            }
        }

        count = Math.Clamp(count, 1, 100);

        Channel channel;
        if (path is null)
        {
            await PrintAsync(TermLine.Of($"usage: {args[0]} [-n count] <channel>", "err"));
            return;
        }

        var (loc, error) = await ResolvePathAsync(path, Current, ct);
        if (loc is null)
        {
            await PrintAsync(TermLine.Of($"{args[0]}: {error}", "err"));
            return;
        }

        if (loc.Kind != LocKind.Channel)
        {
            await PrintAsync(TermLine.Of($"{args[0]}: {path}: Is a directory", "err"));
            return;
        }

        channel = loc.Channel;
        if (args.Contains("-f"))
        {
            await GoToAsync(loc, ct);
            return;
        }

        var result = await channel.GetMessagesWithResultAsync(long.MaxValue, count);
        ct.ThrowIfCancellationRequested();
        if (!result.Success)
        {
            await PrintAsync(TermLine.Of($"{args[0]}: couldn't read {ChannelLabel(channel)}: {result.Message}", "err"));
            return;
        }

        var lines = new List<TermLine>();
        foreach (var message in result.Data.OrderBy(x => x.Id))
            lines.Add(await RenderMessageAsync(message, channel, withId: false));

        if (lines.Count == 0)
            lines.Add(TermLine.Of("(no messages yet)", "dim"));

        await PrintAsync(lines);
    }

    private async Task CmdGui(List<string> args, CancellationToken ct)
    {
        var channel = _channel;
        if (args.Count > 1)
        {
            var (loc, error) = await ResolvePathAsync(args[1], Current, ct);
            if (loc is null || loc.Kind != LocKind.Channel)
            {
                await PrintAsync(TermLine.Of($"gui: {error ?? "not a channel"}", "err"));
                return;
            }
            channel = loc.Channel;
        }

        if (channel is null)
        {
            await PrintAsync(TermLine.Of("gui: which channel? (gui ~/Planet/#channel)", "err"));
            return;
        }

        await OpenGuiAsync(channel);
    }

    private async Task OpenGuiAsync(Channel channel)
    {
        if (channel is null)
            return;

        await PrintAsync(TermLine.Of($"starting graphical session for {ChannelLabel(channel)}...", "dim"));
        if (!await _host.OpenInGuiAsync(channel))
            await PrintAsync(TermLine.Of("gui: couldn't open a window for that channel", "err"));
    }

    private Task CmdWhoami(List<string> args, CancellationToken ct) =>
        PrintAsync(TermLine.Of(Me.Name));

    private async Task CmdNeofetch(List<string> args, CancellationToken ct)
    {
        string[] art =
        [
            "              .  *        ",
            "     *    .-\"\"\"\"\"-.     . ",
            "        .'  .-.    '.     ",
            "   .   /   (   )  o  \\    ",
            "      ;  o  '-'    .  ;   ",
            " ~~~~~|~~~~~~~~~~~~~~~|~~~",
            "      ;   .    ( )    ;   ",
            "   *   \\     o  '    /  . ",
            "        '.  .     .'      ",
            "     .    '-.....-'    *  ",
            "                          ",
        ];

        var planets = _client.PlanetService.JoinedPlanets.Count;
        var dms = _client.ChannelService.DirectChatChannels.Count;
        var nodes = _client.NodeService.Nodes.Select(x => x.Name).Where(x => x is not null).Distinct().ToList();
        var header = $"{Me.Name}@{Host}";

        var info = new List<(string key, string value)>
        {
            ("", header),
            ("", new string('-', header.Length)),
            ("OS", $"ValourOS {VersionString()} (Deep Field) {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"),
            ("Host", Uri.TryCreate(_client.BaseAddress, UriKind.Absolute, out var baseUri) ? baseUri.Host : "app.valour.gg"),
            ("Kernel", RuntimeInformation.FrameworkDescription),
            ("Uptime", FormatDuration(DateTime.UtcNow - BootTime)),
            ("Planets", $"{planets} (joined)"),
            ("DMs", dms.ToString()),
            ("Nodes", nodes.Count == 0 ? "none" : string.Join(", ", nodes)),
            ("Shell", "vsh 1.0"),
            ("Terminal", "valour-term"),
            ("Theme", _theme),
            ("Font", "JetBrains Mono"),
            ("Encryption", E2eeLabel()),
        };

        var lines = new List<TermLine>();
        var rows = Math.Max(art.Length, info.Count + 3);
        for (var i = 0; i < rows; i++)
        {
            var line = new TermLine();
            var artRow = i < art.Length ? art[i] : new string(' ', art[0].Length);
            line.Add(artRow + "   ", i < 5 ? "accent" : "magenta");

            if (i < info.Count)
            {
                var (key, value) = info[i];
                if (key == "")
                    line.Add(value, i == 0 ? "accent bold" : "dim");
                else
                    line.Add(key, "accent bold").Add(": ").Add(value);
            }
            else if (i == info.Count + 1)
            {
                foreach (var c in new[] { "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white" })
                    line.Add("   ", "bg-" + c);
            }
            else if (i == info.Count + 2)
            {
                foreach (var c in new[] { "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white" })
                    line.Add("   ", "bg-" + c + " bright");
            }

            lines.Add(line);
        }

        await PrintAsync(lines);
    }

    private Task CmdUname(List<string> args, CancellationToken ct)
    {
        if (args.Contains("-a"))
        {
            return PrintAsync(TermLine.Of(
                $"ValourOS {Host} {VersionString()} {RuntimeInformation.FrameworkDescription} " +
                $"{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()} Valour/DeepField"));
        }

        return PrintAsync(TermLine.Of("ValourOS"));
    }

    private Task CmdUptime(List<string> args, CancellationToken ct)
    {
        var now = DateTime.Now;
        return PrintAsync(TermLine.Of(
            $" {now:HH:mm:ss} up {FormatDuration(DateTime.UtcNow - BootTime)},  1 user,  " +
            $"load average: {_client.PlanetService.JoinedPlanets.Count / 10.0:0.00}, 0.42, 0.07"));
    }

    private Task CmdDate(List<string> args, CancellationToken ct) =>
        PrintAsync(TermLine.Of(DateTime.Now.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)));

    private Task CmdEcho(List<string> args, CancellationToken ct) =>
        PrintAsync(TermLine.Of(JoinArgs(args, 1)));

    private static readonly string[] Themes = ["deep-field", "phosphor", "amber", "aubergine", "dracula", "paper"];

    private async Task CmdTheme(List<string> args, CancellationToken ct)
    {
        if (args.Count < 2)
        {
            var line = new TermLine().Add("themes: ", "dim");
            foreach (var theme in Themes)
                line.Add(theme + " ", theme == _theme ? "accent bold" : null);
            await PrintAsync(line, TermLine.Of("usage: theme <name>", "dim"));
            return;
        }

        var name = args[1].ToLowerInvariant();
        var match = Themes.FirstOrDefault(x => x == name) ?? Themes.FirstOrDefault(x => x.StartsWith(name));
        if (match is null)
        {
            await PrintAsync(TermLine.Of($"theme: no theme named '{args[1]}'", "err"));
            return;
        }

        _theme = match;
        await _host.SetThemeAsync(match);
        await PrintAsync(TermLine.Of($"theme set to {match}", "dim"));
    }

    private static readonly string[] Fortunes =
    [
        "The stars are closer than they look. Especially the ones in your planet list.",
        "A message you send today will be read by someone who needed it.",
        "Victor has visited 1,024 planets and still hasn't found a better one than yours.",
        "End-to-end encryption: because the void doesn't need to read your messages.",
        "There is no cloud. It's just someone else's planet.",
        "Your next unread message contains exactly the right number of emoji.",
        "In space, no one can hear you type. In ValourOS, everyone can read it.",
        "Real Valournauts use Tab completion.",
        "rm -rf / is not a personality.",
        "Every planet started as one person saying hello into the dark.",
    ];

    private Task CmdFortune(List<string> args, CancellationToken ct) =>
        PrintAsync(TermLine.Of(Fortunes[Random.Shared.Next(Fortunes.Length)], "yellow"));

    private async Task CmdRocket(List<string> args, CancellationToken ct)
    {
        string[] rocket =
        [
            "        /\\",
            "       /  \\",
            "      | V  |",
            "      |    |",
            "      |    |",
            "     /| || |\\",
            "    / |____| \\",
            "      /_/\\_\\",
        ];

        var lines = rocket.Select(x => TermLine.Of(x, "white")).ToList();
        await PrintAsync(lines);

        string[] flames = ["       (  )", "      ( () )", "     (  ()  )", "      '.  .'", "        ''"];
        foreach (var flame in flames)
        {
            await Task.Delay(90, ct);
            await PrintAsync(TermLine.Of(flame, "yellow"));
        }

        for (var i = 3; i > 0; i--)
        {
            await Task.Delay(400, ct);
            await PrintAsync(TermLine.Of($"T-minus {i}...", "dim"));
        }

        await Task.Delay(400, ct);
        await PrintAsync(TermLine.Of("Liftoff! Victor waves from the window.", "green bold"));
    }

    private Task CmdClear(List<string> args, CancellationToken ct) => _host.ClearAsync();

    private Task CmdExit(List<string> args, CancellationToken ct) => _host.CloseAsync();

    private Task CmdSudo(List<string> args, CancellationToken ct) =>
        PrintAsync(
            TermLine.Of($"[sudo] password for {Me.Name}: ", "dim"),
            TermLine.Of($"{Me.Name} is not in the sudoers file. This incident will be reported to Victor.", "err"));

    private Task CmdRm(List<string> args, CancellationToken ct) =>
        args.Any(x => x.Contains("rf")) && args.Any(x => x is "/" or "/*" or "~")
            ? PrintAsync(TermLine.Of("rm: nice try. The planets are staying right where they are.", "err"))
            : PrintAsync(TermLine.Of("rm: ValourOS is read-only. Delete things in the regular window.", "err"));

    private Task CmdEditor(List<string> args, CancellationToken ct) =>
        PrintAsync(TermLine.Of($"{args[0]}: the only editor here is the message box. It saves on Enter.", "yellow"));

    private async Task CmdPing(List<string> args, CancellationToken ct)
    {
        var target = args.Count > 1 ? args[1] : "victor";
        await PrintAsync(TermLine.Of($"PING {target} (10.0.0.42): 56 data bytes"));
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(350, ct);
            var ms = 12 + Random.Shared.NextDouble() * 18;
            await PrintAsync(TermLine.Of($"64 bytes from {target}: icmp_seq={i} ttl=64 time={ms:0.000} ms"));
        }
        await PrintAsync(TermLine.Of($"--- {target} ping statistics: 4 packets transmitted, 4 received, 0% packet loss", "dim"));
    }

    ///////////////
    // Chat mode //
    ///////////////

    private async Task EnterChannelAsync(Channel channel, bool resume = false)
    {
        if (!channel.IsChatChannel)
        {
            await PrintAsync(TermLine.Of(
                $"{ChannelLabel(channel)}: voice channels need a sound card. Try 'gui' to join it.", "err"));
            return;
        }

        if (_channel is not null)
            await DetachChannelAsync(_channel);

        if (channel.PlanetId is not null && !await EnsurePlanetOpenAsync(channel.Planet))
            return;

        var open = await channel.OpenWithResult(_connectionKey);
        if (!open.Success)
        {
            await PrintAsync(TermLine.Of($"join: couldn't connect to {ChannelLabel(channel)}: {open.Message}", "err"));
            return;
        }

        _channel = channel;
        _typing.Clear();
        if (!resume)
        {
            _printedMessages.Clear();
            _oldestMessageId = long.MaxValue;
            _reachedStart = false;
        }

        channel.MessageReceived += OnMessageReceived;
        channel.MessageEdited += OnMessageEdited;
        channel.MessageDeleted += OnMessageDeleted;
        channel.TypingUpdated += OnTypingUpdated;

        _canPost = await CanPostAsync(channel);

        if (!resume)
        {
            var title = channel.PlanetId is null ? "@" + await DmNameAsync(channel) : "#" + channel.Name;
            var lines = new List<TermLine>
            {
                new TermLine().Add("-!- ", "dim").Add("You have joined ", "dim").Add(title, "accent bold")
                    .Add(channel.PlanetId is null ? "" : " on " + channel.Planet?.Name, "dim"),
            };

            if (!string.IsNullOrWhiteSpace(channel.Description))
                lines.Add(new TermLine().Add("-!- ", "dim").Add("Topic: ", "dim").Add(channel.Description));

            lines.Add(new TermLine().Add("-!- ", "dim").Add(channel.IsEncrypted ? "Messages are end-to-end encrypted. " : "", "green")
                .Add("Type to chat, ", "dim").Add("/help", "yellow").Add(" for commands, ", "dim").Add("Ctrl+D", "yellow").Add(" to leave.", "dim"));

            if (!_canPost)
                lines.Add(new TermLine().Add("-!- ", "dim").Add(CannotPostReason(), "warn"));

            await PrintAsync(lines);
        }

        if (resume)
        {
            await PrintAsync(TermLine.Of($"-- resumed {LocationName(Current)} --", "dim"));
            await LoadHistoryAsync(HistoryCount, latest: true);
        }
        else
        {
            await LoadHistoryAsync(HistoryCount);
        }

        await channel.UpdateUserState(DateTime.UtcNow);

        if (channel.IsEncrypted && _client.E2eeService.Status == E2eeStatus.Ready)
            _ = Task.Run(() => _client.E2eeService.ServeChannelAsync(channel));

        await RefreshPromptAsync();
    }

    private async Task LeaveChannelAsync()
    {
        if (_channel is null)
            return;

        var channel = _channel;
        await DetachChannelAsync(channel);
        _channel = null;

        await PrintAsync(new TermLine().Add("-!- ", "dim").Add("You have left ", "dim").Add(ChannelLabel(channel), "accent"));
        await RefreshPromptAsync();
    }

    private async Task DetachChannelAsync(Channel channel)
    {
        channel.MessageReceived -= OnMessageReceived;
        channel.MessageEdited -= OnMessageEdited;
        channel.MessageDeleted -= OnMessageDeleted;
        channel.TypingUpdated -= OnTypingUpdated;
        _typingCts?.Cancel();
        await channel.Close(_connectionKey);
    }

    /// <summary>
    /// Prints a page of messages older than the oldest one shown, or with
    /// <paramref name="latest"/>, the newest messages not shown yet.
    /// </summary>
    private async Task LoadHistoryAsync(int count, bool latest = false)
    {
        var channel = _channel;
        var result = await channel.GetMessagesWithResultAsync(latest ? long.MaxValue : _oldestMessageId, count);
        if (!result.Success)
        {
            await PrintAsync(TermLine.Of($"couldn't load history: {result.Message}", "err"));
            return;
        }

        var messages = result.Data.OrderBy(x => x.Id).ToList();
        if (!latest)
            _reachedStart = messages.Count < count;
        if (messages.Count > 0)
            _oldestMessageId = Math.Min(_oldestMessageId, messages[0].Id);

        var lines = new List<TermLine>();
        DateTime? lastDay = null;
        foreach (var message in messages)
        {
            if (!_printedMessages.Add(message.Id))
                continue;

            var day = message.TimeSent.ToLocalTime().Date;
            if (lastDay != day)
            {
                lines.Add(TermLine.Of($"-- {day:dddd, MMMM d yyyy} --", "dim"));
                lastDay = day;
            }

            lines.Add(await RenderMessageAsync(message, channel));
        }

        if (_reachedStart && !latest)
            lines.Insert(0, TermLine.Of("-- beginning of channel history --", "dim"));

        await PrintAsync(lines);
    }

    private async Task CmdChatMore(List<string> args, CancellationToken ct)
    {
        if (_reachedStart)
        {
            await PrintAsync(TermLine.Of("-!- no older messages", "dim"));
            return;
        }

        var count = args.Count > 1 && int.TryParse(args[1], out var n) ? Math.Clamp(n, 1, 100) : PageCount;
        await PrintAsync(TermLine.Of($"-!- {count} older messages:", "dim"));
        await LoadHistoryAsync(count);
    }

    private async Task CmdChatJoin(List<string> args, CancellationToken ct)
    {
        var name = JoinArgs(args, 1);
        if (string.IsNullOrWhiteSpace(name))
        {
            await PrintAsync(TermLine.Of("usage: /join <channel>", "err"));
            return;
        }

        if (_planet is null)
        {
            var (dm, dmError) = await MatchDmAsync(name);
            if (dm is null)
            {
                await PrintAsync(TermLine.Of($"join: {dmError}", "err"));
                return;
            }
            await EnterChannelAsync(dm);
            return;
        }

        var (channel, error) = MatchChannel(_planet, name);
        if (channel is null)
        {
            await PrintAsync(TermLine.Of($"join: {error}", "err"));
            return;
        }

        await EnterChannelAsync(channel);
    }

    private Task CmdChatMe(List<string> args, CancellationToken ct)
    {
        var action = JoinArgs(args, 1);
        return string.IsNullOrWhiteSpace(action) ? Task.CompletedTask : SendAsync($"*{action}*");
    }

    private async Task CmdChatWho(List<string> args, CancellationToken ct)
    {
        if (_channel?.PlanetId is null)
        {
            var users = await _channel!.GetChannelMemberUsersAsync();
            await PrintAsync(new TermLine().Add("-!- In this chat: ", "dim")
                .Add(string.Join(", ", users.Select(x => x.Name)), "magenta"));
            return;
        }

        var chatters = await _channel.FetchRecentChattersAsync();
        var line = new TermLine().Add("-!- Recently here: ", "dim");
        if (chatters.Count == 0)
            line.Add("nobody yet. Say hello!", "dim");
        foreach (var member in chatters)
            line.AddRange(NickSegs(member.Name, member.GetRoleColor(), member.UserId)).Add(" ");
        await PrintAsync(line);
    }

    private Task CmdChatTopic(List<string> args, CancellationToken ct) =>
        PrintAsync(new TermLine().Add("-!- Topic: ", "dim")
            .Add(string.IsNullOrWhiteSpace(_channel?.Description) ? "(none)" : _channel.Description));

    private async Task SendAsync(string text)
    {
        var channel = _channel;
        if (channel is null)
            return;

        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        if (!_canPost)
        {
            await PrintAsync(TermLine.Of(CannotPostReason(), "err"));
            return;
        }

        var message = new Message(_client)
        {
            Content = ResolveOutgoingMentions(text, channel),
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId,
            AuthorUserId = Me.Id,
            AuthorMemberId = channel.Planet?.MyMember?.Id,
            TimeSent = DateTime.UtcNow,
            Fingerprint = Guid.NewGuid().ToString(),
        };

        var pendingId = "fp-" + message.Fingerprint;
        _pendingSends.Add(message.Fingerprint);
        var pending = await RenderMessageAsync(message, channel, withId: false);
        pending.Id = pendingId;
        pending.LineStyle = "self pending";
        await PrintAsync(pending);

        TaskResult<Message> result;
        try
        {
            result = await _client.MessageService.SendMessage(message);
        }
        catch (Exception e)
        {
            _client.Logger.Log<TerminalShell>($"Sending from the terminal failed: {e}", "red");
            result = TaskResult<Message>.FromFailure($"something went wrong while sending ({e.GetType().Name})");
        }

        _pendingSends.Remove(message.Fingerprint);

        if (!result.Success)
        {
            var failed = await RenderMessageAsync(message, channel, withId: false);
            failed.LineStyle = "self failed";
            failed.Segs.Add(new TermSeg { Text = "  ✗ not sent: " + result.Message, Style = "err" });
            await _host.ReplaceAsync(pendingId, failed);
            return;
        }

        // The relayed copy may have arrived first and replaced the pending line.
        if (result.Data is not null && _printedMessages.Add(result.Data.Id))
        {
            var sent = await RenderMessageAsync(result.Data, channel);
            await _host.ReplaceAsync(pendingId, sent);
        }
    }

    private async Task OnMessageReceived(Message message)
    {
        var channel = _channel;
        if (channel is null || message.ChannelId != channel.Id)
            return;

        _typing.Remove(message.AuthorUserId);

        if (!_printedMessages.Add(message.Id))
            return;

        var line = await RenderMessageAsync(message, channel);

        // A message sent from this terminal replaces its pending line. Our
        // messages from other devices are printed like anyone else's.
        if (message.Fingerprint is not null && _pendingSends.Contains(message.Fingerprint))
        {
            await _host.ReplaceAsync("fp-" + message.Fingerprint, line);
            return;
        }

        await PrintAsync(line);
        await RefreshStatusAsync();
    }

    private async Task OnMessageEdited(Message message)
    {
        var channel = _channel;
        if (channel is null || message.ChannelId != channel.Id)
            return;

        await _host.ReplaceAsync("m-" + message.Id, await RenderMessageAsync(message, channel));
    }

    private async Task OnMessageDeleted(Message message)
    {
        if (_channel is null || message.ChannelId != _channel.Id)
            return;

        var line = new TermLine
        {
            Kind = "msg",
            Id = "m-" + message.Id,
            Time = message.TimeSent.ToLocalTime().ToString("HH:mm"),
            Nick = [new TermSeg { Text = "*", Style = "dim" }],
            LineStyle = "deleted",
        };
        line.Add("message deleted", "dim ital");
        await _host.ReplaceAsync(line.Id, line);
    }

    private async Task OnMessageDecrypted(Message message)
    {
        var channel = _channel;
        if (channel is null || message.ChannelId != channel.Id || !_printedMessages.Contains(message.Id))
            return;

        await _host.ReplaceAsync("m-" + message.Id, await RenderMessageAsync(message, channel));
    }

    private async Task OnTypingUpdated(ChannelTypingUpdate update)
    {
        if (update.UserId == Me.Id || _channel is null || update.ChannelId != _channel.Id)
            return;

        _typing[update.UserId] = DateTime.UtcNow + TypingDuration;
        await RefreshStatusAsync();

        // Clear the indicator once it expires.
        _typingCts?.Cancel();
        _typingCts = new CancellationTokenSource();
        var token = _typingCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TypingDuration + TimeSpan.FromMilliseconds(200), token);
                await RefreshStatusAsync();
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private async Task<bool> CanPostAsync(Channel channel)
    {
        if (_client.E2eeService.Status != E2eeStatus.Ready)
            return false;

        try
        {
            if (channel.PlanetId is not null)
            {
                var member = channel.Planet?.MyMember;
                return member is not null &&
                       await channel.HasPermissionAsync(member, ChatChannelPermissions.PostMessages);
            }

            return await channel.HasPermissionAsync(Me.Id, ChatChannelPermissions.PostMessages);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string CannotPostReason() => _client.E2eeService.Status switch
    {
        E2eeStatus.Ready => "Read-only: you don't have permission to post here.",
        E2eeStatus.NotSetUp => "Read-only: set up encryption in the regular window to send messages.",
        E2eeStatus.NeedsVerification => "Read-only: link this device in the regular window to send messages.",
        _ => "Read-only: encryption isn't ready on this device yet.",
    };

    ///////////////////////
    // Message rendering //
    ///////////////////////

    private static readonly Regex InlineRegex = new(
        @"(?<mention>«@(?<mtype>[mucr])-(?<mid>\d+)»)|(?<code>`[^`\n]+`)|(?<url>https?://[^\s<>«»]+)|(?<bold>\*\*[^*\n]+\*\*)|(?<ital>(?<![\w*])\*[^*\n]+\*(?![\w*]))",
        RegexOptions.Compiled);

    private async Task<TermLine> RenderMessageAsync(Message message, Channel channel, bool withId = true)
    {
        var line = new TermLine
        {
            Kind = "msg",
            Id = withId && message.Id != 0 ? "m-" + message.Id : null,
            Time = message.TimeSent.ToLocalTime().ToString("HH:mm"),
        };

        // Author
        string name;
        string color = null;
        long authorId = message.AuthorUserId;
        if (!string.IsNullOrWhiteSpace(message.OverrideName))
        {
            name = message.OverrideName;
        }
        else if (message.AuthorUserId == Me.Id && message.PlanetId is null)
        {
            name = Me.Name;
        }
        else
        {
            try
            {
                var author = await message.FetchAuthorAsync();
                name = author?.Name ?? "unknown";
                if (author is PlanetMember member)
                    color = member.GetRoleColor();
            }
            catch (Exception)
            {
                name = "unknown";
            }
        }

        line.Nick = NickSegs(name, color, authorId);

        var styles = new List<string>();
        if (message.AuthorUserId == Me.Id)
            styles.Add("self");

        // Reply context
        if (message.ReplyToId is not null)
        {
            var reply = message.ReplyTo;
            if (reply is not null)
            {
                string replyName;
                try
                {
                    replyName = (await reply.FetchAuthorAsync())?.Name ?? "someone";
                }
                catch (Exception)
                {
                    replyName = "someone";
                }
                line.Add("↳ " + replyName + ": ", "dim");
                line.Add(Truncate(PlainPreview(reply), 40) + "  ", "dim ital");
            }
            else
            {
                line.Add("↳ reply  ", "dim");
            }
        }

        // Body
        if (message.IsEncrypted && message.DecryptionState != MessageDecryptionState.Decrypted)
        {
            line.Add("🔒 " + (message.DecryptionState switch
            {
                MessageDecryptionState.WaitingForKey => "encrypted; waiting for a member's device to share the key",
                MessageDecryptionState.DeviceNotVerified => "encrypted; this device isn't verified yet",
                MessageDecryptionState.NeedsNewerVersion => "encrypted with a newer version of Valour",
                MessageDecryptionState.Invalid => "hidden: this message failed verification",
                _ => "encrypted message",
            }), "dim ital");
        }
        else
        {
            var (segs, mentionsMe) = await RenderBodyAsync(message.Content ?? string.Empty, channel);
            line.AddRange(segs);
            if (mentionsMe)
                styles.Add("mentioned");
        }

        if (message.IsEmbed())
            line.Add(" [embed]", "dim");

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var attachment in message.Attachments)
                line.Add($" [{AttachmentIcon(attachment)} {attachment.FileName ?? "file"}]", "cyan");
        }

        if (message.EditedTime is not null)
            line.Add(" (edited)", "dim");

        if (line.Segs.Count == 0)
            line.Add("(empty)", "dim");

        if (styles.Count > 0)
            line.LineStyle = string.Join(' ', styles);

        return line;
    }

    private static string AttachmentIcon(MessageAttachment attachment) =>
        attachment.MimeType?.Split('/')[0] switch
        {
            "image" => "img",
            "video" => "vid",
            "audio" => "snd",
            _ => "file",
        };

    private static string PlainPreview(Message message)
    {
        if (message.IsEncrypted && message.DecryptionState != MessageDecryptionState.Decrypted)
            return "encrypted message";
        var text = message.Content ?? string.Empty;
        text = Regex.Replace(text, "«@[mucr]-\\d+»", "@…");
        return text.Replace('\n', ' ');
    }

    private async Task<(List<TermSeg> segs, bool mentionsMe)> RenderBodyAsync(string content, Channel channel)
    {
        var segs = new List<TermSeg>();
        var mentionsMe = false;
        var pos = 0;

        foreach (Match match in InlineRegex.Matches(content))
        {
            if (match.Index > pos)
                segs.Add(new TermSeg { Text = content[pos..match.Index] });

            if (match.Groups["mention"].Success)
            {
                var (text, isMe, color) = await ResolveMentionAsync(
                    match.Groups["mtype"].Value[0], long.Parse(match.Groups["mid"].Value), channel);
                mentionsMe |= isMe;
                segs.Add(new TermSeg { Text = text, Style = isMe ? "mention-me" : "mention", Color = color });
            }
            else if (match.Groups["code"].Success)
            {
                segs.Add(new TermSeg { Text = match.Value.Trim('`'), Style = "code" });
            }
            else if (match.Groups["url"].Success)
            {
                segs.Add(new TermSeg { Text = match.Value, Href = match.Value, Style = "link" });
            }
            else if (match.Groups["bold"].Success)
            {
                segs.Add(new TermSeg { Text = match.Value[2..^2], Style = "bold" });
            }
            else if (match.Groups["ital"].Success)
            {
                segs.Add(new TermSeg { Text = match.Value[1..^1], Style = "ital" });
            }

            pos = match.Index + match.Length;
        }

        if (pos < content.Length)
            segs.Add(new TermSeg { Text = content[pos..] });

        return (segs, mentionsMe);
    }

    private async Task<(string text, bool isMe, string color)> ResolveMentionAsync(char type, long id, Channel channel)
    {
        try
        {
            switch (type)
            {
                case 'm':
                    if (channel.Planet is null)
                        break;
                    var member = await channel.Planet.FetchMemberAsync(id);
                    if (member is not null)
                        return ("@" + member.Name, member.UserId == Me.Id, null);
                    break;
                case 'u':
                    var user = await _client.UserService.FetchUserAsync(id);
                    if (user is not null)
                        return ("@" + user.Name, user.Id == Me.Id, null);
                    break;
                case 'c':
                    if (channel.Planet is not null && channel.Planet.Channels.TryGet(id, out var target))
                        return ("#" + target.Name, false, null);
                    return ("#channel", false, null);
                case 'r':
                    if (channel.Planet is not null)
                    {
                        var role = await channel.Planet.FetchRoleAsync(id);
                        if (role is not null)
                        {
                            var mine = channel.Planet.MyMember?.Roles?.Any(x => x.Id == role.Id) == true;
                            return ("@" + role.Name, mine, role.Color);
                        }
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // Unknown mentions fall through to the generic label.
        }

        return ("@unknown", false, null);
    }

    private static readonly Regex OutgoingMentionRegex = new(@"(?<![\w«])@(?<name>[\w.\-]+)", RegexOptions.Compiled);
    private static readonly Regex OutgoingChannelRegex = new(@"(?<![\w&«])#(?<name>[\w\-]+)", RegexOptions.Compiled);

    /// <summary>
    /// Turns @name and #channel into mention tokens when exactly one member
    /// or channel matches, so the terminal can mention people like the
    /// message box does.
    /// </summary>
    private string ResolveOutgoingMentions(string text, Channel channel)
    {
        var planet = channel.Planet;
        if (planet is null)
            return text;

        var members = planet.Members.ToList();
        text = OutgoingMentionRegex.Replace(text, m =>
        {
            var name = m.Groups["name"].Value;
            var matches = members.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? $"«@m-{matches[0].Id}»" : m.Value;
        });

        var channels = planet.Channels.ToList();
        text = OutgoingChannelRegex.Replace(text, m =>
        {
            var name = m.Groups["name"].Value;
            var matches = channels.Where(x => x.IsChatChannel && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? $"«@c-{matches[0].Id}»" : m.Value;
        });

        return text;
    }

    private static List<TermSeg> NickSegs(string name, string color, long userId)
    {
        // Default role colors are white, which reads as "no color". Give those
        // names a stable color from the user id, like IRC clients do.
        var useHash = string.IsNullOrWhiteSpace(color) ||
                      color.Equals("#ffffff", StringComparison.OrdinalIgnoreCase) ||
                      color.Equals("#fff", StringComparison.OrdinalIgnoreCase);

        return
        [
            useHash
                ? new TermSeg { Text = name, Style = "nick n" + (int)((ulong)userId % 8) }
                : new TermSeg { Text = name, Color = color, Style = "nick" },
        ];
    }

    ////////////////
    // Completion //
    ////////////////

    public async Task<TermCompletion> CompleteAsync(string line, int cursor)
    {
        line ??= string.Empty;
        cursor = Math.Clamp(cursor, 0, line.Length);
        var prefix = line[..cursor];

        List<Token> tokens;
        try
        {
            tokens = Tokenize(prefix, allowUnterminated: true);
        }
        catch (FormatException)
        {
            return new TermCompletion { Start = cursor, End = cursor };
        }

        var trailingSpace = prefix.Length > 0 && char.IsWhiteSpace(prefix[^1]) &&
                            (tokens.Count == 0 || tokens[^1].End < prefix.Length);
        var current = trailingSpace || tokens.Count == 0 ? string.Empty : tokens[^1].Value;
        var start = trailingSpace || tokens.Count == 0 ? cursor : tokens[^1].Start;
        var argIndex = trailingSpace ? tokens.Count : Math.Max(0, tokens.Count - 1);

        var result = new TermCompletion { Start = start, End = cursor };

        if (_channel is not null)
        {
            if (prefix.StartsWith('/') && argIndex == 0)
            {
                AddMatches(result, _chatCommands.Keys.Select(x => "/" + x), current, " ");
            }
            else if (current.StartsWith('@'))
            {
                var names = await ChatNamesAsync();
                AddMatches(result, names.Select(x => "@" + x), current, " ", escape: false);
            }
            else if (current.StartsWith('#') && _channel.Planet is not null)
            {
                AddMatches(result, _channel.Planet.Channels.ToList().Where(x => x.IsChatChannel).Select(x => "#" + x.Name), current, " ", escape: false);
            }
            else if (prefix.StartsWith("/join ", StringComparison.OrdinalIgnoreCase) && _planet is not null)
            {
                AddMatches(result, _planet.Channels.ToList().Where(x => x.IsChatChannel).Select(x => x.Name), current.TrimStart('#'), " ");
            }

            return result;
        }

        if (argIndex == 0)
        {
            AddMatches(result, _commands.Where(x => !x.Value.Hidden).Select(x => x.Key), current, " ");
            return result;
        }

        var command = tokens[0].Value.ToLowerInvariant();
        switch (command)
        {
            case "open" or "join" when argIndex == 1:
                AddMatches(result, ["planet", "channel", "dm"], current, " ");
                await AddPathMatchesAsync(result, current);
                break;
            case "open" or "join" when argIndex >= 2:
            {
                var kind = tokens[1].Value.ToLowerInvariant();
                // Names may span several words, so complete the rest of the line.
                var nameStart = tokens.Count > 2 ? tokens[2].Start : cursor;
                var typed = trailingSpace && tokens.Count <= 2 ? string.Empty : UnescapeRange(prefix, nameStart);
                result.Start = nameStart;
                IEnumerable<string> names = kind switch
                {
                    "planet" => _client.PlanetService.JoinedPlanets.Select(x => x.Name),
                    "channel" => _planet?.Channels.ToList().Where(x => x.IsChatChannel).Select(x => x.Name) ?? [],
                    "dm" => await DmNamesAsync(),
                    _ => [],
                };
                AddMatches(result, names, typed.TrimStart('#', '@'), "");
                break;
            }
            case "cd" or "tree" or "gui" when argIndex == 1:
                await AddPathMatchesAsync(result, current);
                break;
            case "ls" or "dir" or "tail" or "cat" when !current.StartsWith('-'):
                await AddPathMatchesAsync(result, current);
                break;
            case "help" or "man":
                AddMatches(result, _commands.Where(x => !x.Value.Hidden).Select(x => x.Key), current, " ");
                break;
            case "theme":
                AddMatches(result, Themes, current, " ");
                break;
        }

        return result;
    }

    private async Task AddPathMatchesAsync(TermCompletion result, string current)
    {
        var slash = current.LastIndexOf('/');
        var dirPart = slash >= 0 ? current[..(slash + 1)] : string.Empty;
        var namePart = slash >= 0 ? current[(slash + 1)..] : current;

        var dir = Current.Kind == LocKind.Channel
            ? new Loc(_planet is null ? LocKind.Dms : LocKind.Planet, _planet)
            : Current;

        if (dirPart.Length > 0)
        {
            var (resolved, _) = await ResolvePathAsync(dirPart, Current, CancellationToken.None);
            if (resolved is null)
                return;
            dir = resolved;
        }

        var entries = new List<(string name, bool isDir)>();
        switch (dir.Kind)
        {
            case LocKind.Home:
                entries.Add(("dms", true));
                entries.AddRange(_client.PlanetService.JoinedPlanets.Select(p => (p.Name, true)));
                if (dirPart.Length == 0)
                    entries.Add(("~", true));
                break;
            case LocKind.Dms:
                entries.AddRange((await DmNamesAsync()).Select(x => ("@" + x, false)));
                entries.Add(("..", true));
                break;
            case LocKind.Planet:
                entries.AddRange(dir.Planet.Channels.ToList().Where(x => x.IsChatChannel).Select(c => ("#" + c.Name, false)));
                entries.Add(("..", true));
                break;
        }

        foreach (var (name, isDir) in entries)
        {
            if (!name.StartsWith(namePart, StringComparison.OrdinalIgnoreCase))
                continue;

            if (namePart.Length == 0 && name == "..")
                continue;

            result.Items.Add(new TermCompletionItem
            {
                Insert = Escape(dirPart + name) + (isDir ? "/" : " "),
                Display = name + (isDir ? "/" : ""),
            });
        }
    }

    private static void AddMatches(TermCompletion result, IEnumerable<string> options, string current, string suffix, bool escape = true)
    {
        foreach (var option in options.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (option.StartsWith(current, StringComparison.OrdinalIgnoreCase))
            {
                result.Items.Add(new TermCompletionItem
                {
                    Insert = (escape ? Escape(option) : option) + suffix,
                    Display = option,
                });
            }
        }
    }

    private async Task<List<string>> ChatNamesAsync()
    {
        var names = new List<string>();
        if (_channel?.Planet is not null)
        {
            try
            {
                names.AddRange((await _channel.FetchRecentChattersAsync()).Select(x => x.Name));
            }
            catch (Exception)
            {
                // Recent chatters are a convenience; cached members still work.
            }
            names.AddRange(_channel.Planet.Members.ToList().Select(x => x.Name));
        }
        else if (_channel is not null)
        {
            names.AddRange((await _channel.GetChannelMemberUsersAsync()).Select(x => x.Name));
        }

        return names.Where(x => !string.IsNullOrWhiteSpace(x) && !x.Contains(' ')).Distinct().ToList();
    }

    private async Task<List<string>> DmNamesAsync()
    {
        var names = new List<string>();
        foreach (var dm in _client.ChannelService.DirectChatChannels.ToList())
            names.Add(await DmNameAsync(dm));
        return names;
    }

    ////////////////
    // Navigation //
    ////////////////

    private async Task<(Loc loc, string error)> ResolvePathAsync(string path, Loc from, CancellationToken ct)
    {
        var loc = from;
        if (path.StartsWith('~') || path.StartsWith('/'))
        {
            loc = Loc.Home;
            path = path.TrimStart('~');
        }

        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();

            if (part == ".")
                continue;

            if (part == "..")
            {
                loc = loc.Kind switch
                {
                    LocKind.Channel => loc.Planet is null ? Loc.Dms : new Loc(LocKind.Planet, loc.Planet),
                    _ => Loc.Home,
                };
                continue;
            }

            switch (loc.Kind)
            {
                case LocKind.Home:
                    if (part.Equals("dms", StringComparison.OrdinalIgnoreCase))
                    {
                        loc = Loc.Dms;
                        break;
                    }

                    var (planet, planetError) = MatchPlanet(part);
                    if (planet is null)
                        return (null, planetError);
                    if (!await EnsurePlanetOpenAsync(planet))
                        return (null, $"{planet.Name}: couldn't connect to the planet");
                    loc = new Loc(LocKind.Planet, planet);
                    break;

                case LocKind.Dms:
                    var (dm, dmError) = await MatchDmAsync(part);
                    if (dm is null)
                        return (null, dmError);
                    loc = new Loc(LocKind.Channel, null, dm);
                    break;

                case LocKind.Planet:
                    var (channel, channelError) = MatchChannel(loc.Planet, part);
                    if (channel is null)
                        return (null, channelError);
                    loc = new Loc(LocKind.Channel, loc.Planet, channel);
                    break;

                case LocKind.Channel:
                    return (null, $"{part}: Not a directory");
            }
        }

        return (loc, null);
    }

    private (Planet planet, string error) MatchPlanet(string name)
    {
        var planets = _client.PlanetService.JoinedPlanets.ToList();
        if (string.IsNullOrWhiteSpace(name))
            return (null, "which planet? (try 'planets')");

        if (int.TryParse(name, out var number) && number >= 1 && number <= planets.Count &&
            !planets.Any(x => x.Name == name))
            return (planets[number - 1], null);

        return BestMatch(planets, x => x.Name, name, "planet");
    }

    private (Channel channel, string error) MatchChannel(Planet planet, string name)
    {
        name = name.TrimStart('#');
        var channels = planet.Channels.ToList().Where(x => x.ChannelType != ChannelTypeEnum.PlanetCategory).ToList();
        return BestMatch(channels, x => x.Name, name, "channel");
    }

    private async Task<(Channel channel, string error)> MatchDmAsync(string name)
    {
        name = name.TrimStart('@');
        var dms = _client.ChannelService.DirectChatChannels.ToList();
        var named = new List<(Channel dm, string name)>();
        foreach (var dm in dms)
            named.Add((dm, await DmNameAsync(dm)));

        var (match, error) = BestMatch(named, x => x.name, name, "direct message");
        return (match.dm, error);
    }

    private static (T match, string error) BestMatch<T>(List<T> items, Func<T, string> getName, string query, string what)
    {
        if (string.IsNullOrWhiteSpace(query))
            return (default, $"which {what}?");

        foreach (var test in new Func<string, bool>[]
                 {
                     n => string.Equals(n, query, StringComparison.OrdinalIgnoreCase),
                     n => n.StartsWith(query, StringComparison.OrdinalIgnoreCase),
                     n => n.Contains(query, StringComparison.OrdinalIgnoreCase),
                 })
        {
            var matches = items.Where(x => getName(x) is { } n && test(n)).ToList();
            if (matches.Count == 1)
                return (matches[0], null);
            if (matches.Count > 1)
                return (default, $"'{query}' could be {string.Join(", ", matches.Take(5).Select(getName))}{(matches.Count > 5 ? ", ..." : "")}");
        }

        return (default, $"{query}: No such {what}");
    }

    private async Task<bool> EnsurePlanetOpenAsync(Planet planet)
    {
        if (planet is null)
            return false;

        if (_openedPlanets.Contains(planet.Id))
            return true;

        var result = await _client.PlanetService.TryOpenPlanetConnection(planet, _connectionKey);
        if (!result.Success)
        {
            await PrintAsync(TermLine.Of($"{planet.Name}: connection failed: {result.Message}", "err"));
            return false;
        }

        _openedPlanets.Add(planet.Id);
        return true;
    }

    private async Task<string> DmNameAsync(Channel channel)
    {
        if (channel.ChannelType == ChannelTypeEnum.GroupChat && !string.IsNullOrWhiteSpace(channel.Name))
            return channel.Name;

        if (channel.Members is null)
            return channel.Name ?? "direct";

        var others = channel.Members.Where(x => x.UserId != Me.Id).ToList();
        if (others.Count == 0)
            return _dmNames[channel.Id] = Me.Name;

        var names = new List<string>();
        foreach (var other in others)
            names.Add((await _client.UserService.FetchUserAsync(other.UserId))?.Name ?? "unknown");
        return _dmNames[channel.Id] = string.Join(",", names);
    }

    //////////////////////
    // Prompt & status  //
    //////////////////////

    private string PathOf(Loc loc) => loc.Kind switch
    {
        LocKind.Home => "~",
        LocKind.Dms => "~/dms",
        LocKind.Planet => "~/" + loc.Planet.Name,
        LocKind.Channel when loc.Planet is null => "~/dms/" + ChannelLabel(loc.Channel),
        LocKind.Channel => "~/" + loc.Planet.Name + "/#" + loc.Channel.Name,
        _ => "~",
    };

    private string LocationName(Loc loc) => loc.Kind == LocKind.Channel
        ? ChannelLabel(loc.Channel) + (loc.Planet is null ? "" : " on " + loc.Planet.Name)
        : PathOf(loc);

    private string ChannelLabel(Channel channel) => channel.ChannelType switch
    {
        ChannelTypeEnum.DirectChat or ChannelTypeEnum.GroupChat =>
            "@" + (_dmNames.TryGetValue(channel.Id, out var dmName) ? dmName : channel.Name ?? "dm"),
        ChannelTypeEnum.PlanetVoice or ChannelTypeEnum.PlanetVideo => "~" + channel.Name,
        _ => "#" + channel.Name,
    };

    private static string ChannelStyle(Channel channel) =>
        channel.IsChatChannel ? "cyan" : "yellow";

    public async Task RefreshPromptAsync()
    {
        List<TermSeg> prompt;
        string mode;
        if (_channel is not null)
        {
            var label = _channel.PlanetId is null ? "@" + await DmNameAsync(_channel) : "#" + _channel.Name;
            prompt =
            [
                new TermSeg { Text = "[", Style = "dim" },
                new TermSeg { Text = label, Style = "accent bold" },
                new TermSeg { Text = "] ", Style = "dim" },
                new TermSeg { Text = Me.Name, Style = "green bold" },
                new TermSeg { Text = _canPost ? " ❯ " : " (read-only) ❯ ", Style = _canPost ? "dim" : "warn" },
            ];
            mode = "chat";
        }
        else
        {
            prompt =
            [
                new TermSeg { Text = Me.Name + "@" + Host, Style = "green bold" },
                new TermSeg { Text = ":" },
                new TermSeg { Text = PathOf(Current), Style = "blue bold" },
                new TermSeg { Text = "$ " },
            ];
            mode = "shell";
        }

        await _host.SetPromptAsync(prompt, mode);
        await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        var status = new TermStatus();
        status.Left.Add(new TermSeg { Text = " valouros ", Style = "tab-session" });
        status.Left.Add(new TermSeg { Text = " 0:vsh" + (_channel is null ? "*" : "-"), Style = _channel is null ? "tab-active" : "tab" });
        if (_channel is not null)
        {
            var label = _channel.PlanetId is null ? "@" + await DmNameAsync(_channel) : "#" + _channel.Name;
            status.Left.Add(new TermSeg { Text = " 1:" + label + "*", Style = "tab-active" });
        }

        var now = DateTime.UtcNow;
        var typingIds = _typing.Where(x => x.Value > now).Select(x => x.Key).ToList();
        if (typingIds.Count > 0)
        {
            var names = new List<string>();
            foreach (var id in typingIds.Take(3))
                names.Add((await _client.UserService.FetchUserAsync(id))?.Name ?? "someone");
            var text = typingIds.Count > 3 ? "several people are typing" :
                string.Join(", ", names) + (typingIds.Count == 1 ? " is typing" : " are typing");
            status.Right.Add(new TermSeg { Text = text + "… ", Style = "typing" });
        }

        var e2ee = _client.E2eeService.Status == E2eeStatus.Ready;
        status.Right.Add(new TermSeg { Text = e2ee ? " e2ee ✓ " : " e2ee ✗ ", Style = e2ee ? "ok" : "warn" });
        var node = _channel?.Planet?.NodeName ?? _planet?.NodeName ?? _client.NodeService.Nodes.FirstOrDefault()?.Name;
        if (node is not null)
            status.Right.Add(new TermSeg { Text = " " + node + " ", Style = "node" });

        await _host.SetStatusAsync(status);
    }

    ///////////
    // Boot  //
    ///////////

    private TermBoot BuildBoot()
    {
        var boot = new TermBoot();
        var nodes = _client.NodeService.Nodes.Select(x => x.Name).Where(x => x is not null).Distinct().ToList();
        var planets = _client.PlanetService.JoinedPlanets.ToList();

        void Ok(string text) => boot.Boot.Add(new TermLine().Add("[  ", "dim").Add("OK", "green bold").Add("  ] ", "dim").Add(text));
        void Stamp(double t, string text) => boot.Boot.Add(new TermLine().Add($"[{t,12:0.000000}] ", "dim").Add(text));

        Stamp(0, $"Booting ValourOS {VersionString()} on {RuntimeInformation.FrameworkDescription}");
        Stamp(0.000042, "Command line: BOOT_IMAGE=/vmvalour root=/dev/deepfield ro quiet splash");
        Stamp(0.013370, $"Detected {Environment.ProcessorCount} CPU core(s), {planets.Count} planet(s) in orbit");
        Stamp(0.271828, "Mounting cosmic filesystem at /planets");
        Ok("Reached target Local File Systems.");
        Ok("Started Journal Service.");
        foreach (var node in nodes.DefaultIfEmpty("hub"))
            Ok($"Started Node Connection Service ({node}).");
        if (_client.E2eeService.Status == E2eeStatus.Ready)
            Ok("Started End-to-End Encryption Keyring.");
        else
            boot.Boot.Add(new TermLine().Add("[", "dim").Add("FAILED", "red bold").Add("] ", "dim").Add("Failed to start End-to-End Encryption Keyring. See 'the regular window'."));
        Ok("Started Victor the Astronaut (mascot daemon).");
        Ok($"Loaded {planets.Count} planet(s) and {_client.ChannelService.DirectChatChannels.Count} direct chat(s).");
        Ok("Reached target Deep Field.");
        boot.Boot.Add(TermLine.Empty);
        boot.Boot.Add(TermLine.Of($"ValourOS {VersionString()} {Host} tty1", "bold"));
        boot.Boot.Add(TermLine.Empty);
        boot.Boot.Add(new TermLine().Add($"{Host} login: ").Add(Me.Name, "bold"));
        boot.Boot.Add(new TermLine().Add("Password: ").Add("••••••••", "dim"));
        boot.Boot.Add(TermLine.Empty);

        boot.Welcome.AddRange(Banner());
        boot.Welcome.Add(TermLine.Empty);
        var unread = planets.Count(p => _client.UnreadService.IsPlanetUnread(p.Id));
        boot.Welcome.Add(new TermLine().Add("Welcome to ValourOS " + VersionString() + ", ").Add(Me.Name, "accent bold").Add("!"));
        boot.Welcome.Add(TermLine.Empty);
        boot.Welcome.Add(new TermLine().Add(" * ", "dim").Add($"{planets.Count} planet{(planets.Count == 1 ? "" : "s")} in orbit").Add(unread > 0 ? $", {unread} with unread messages" : "", "green"));
        boot.Welcome.Add(new TermLine().Add(" * ", "dim").Add("Type ").Add("help", "yellow").Add(" to see what vsh can do, or ").Add("ls", "yellow").Add(" to look around."));
        boot.Welcome.Add(new TermLine().Add(" * ", "dim").Add("Press ").Add("Tab", "yellow").Add(" to complete names and ").Add("Ctrl+/", "yellow").Add(" to return to the desktop."));
        boot.Welcome.Add(TermLine.Empty);
        boot.Welcome.Add(TermLine.Of($"Last login: {DateTime.Now.ToString("ddd MMM d HH:mm", CultureInfo.InvariantCulture)} on tty1", "dim"));
        return boot;
    }

    private static readonly Dictionary<char, string[]> BannerFont = new()
    {
        ['V'] = ["██╗   ██╗", "██║   ██║", "██║   ██║", "╚██╗ ██╔╝", " ╚████╔╝ ", "  ╚═══╝  "],
        ['A'] = [" █████╗ ", "██╔══██╗", "███████║", "██╔══██║", "██║  ██║", "╚═╝  ╚═╝"],
        ['L'] = ["██╗     ", "██║     ", "██║     ", "██║     ", "███████╗", "╚══════╝"],
        ['O'] = [" ██████╗ ", "██╔═══██╗", "██║   ██║", "██║   ██║", "╚██████╔╝", " ╚═════╝ "],
        ['U'] = ["██╗   ██╗", "██║   ██║", "██║   ██║", "██║   ██║", "╚██████╔╝", " ╚═════╝ "],
        ['R'] = ["██████╗ ", "██╔══██╗", "██████╔╝", "██╔══██╗", "██║  ██║", "╚═╝  ╚═╝"],
        ['S'] = ["███████╗", "██╔════╝", "███████╗", "╚════██║", "███████║", "╚══════╝"],
    };

    private static List<TermLine> Banner()
    {
        var lines = new List<TermLine>();
        for (var row = 0; row < 6; row++)
        {
            var valour = string.Concat("VALOUR".Select(c => BannerFont[c][row]));
            var os = string.Concat("OS".Select(c => BannerFont[c][row]));
            lines.Add(new TermLine().Add(valour, "banner banner-" + row).Add(" " + os, "banner-os"));
        }
        return lines;
    }

    /////////////
    // Helpers //
    /////////////

    private Task PrintAsync(params TermLine[] lines) => _host.PrintAsync(lines);

    private Task PrintAsync(List<TermLine> lines) => _host.PrintAsync(lines);

    private string E2eeLabel() => _client.E2eeService.Status switch
    {
        E2eeStatus.Ready => "ready (keys on this device)",
        E2eeStatus.NotSetUp => "not set up",
        E2eeStatus.NeedsVerification => "this device needs linking",
        var other => other.ToString(),
    };

    private static string VersionString()
    {
        var version = typeof(ValourClient).Assembly.GetName().Version;
        return version is null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalMinutes < 1)
            return $"{(int)span.TotalSeconds} secs";
        if (span.TotalHours < 1)
            return $"{(int)span.TotalMinutes} min{((int)span.TotalMinutes == 1 ? "" : "s")}";
        if (span.TotalDays < 1)
            return $"{(int)span.TotalHours}:{span.Minutes:00}";
        return $"{(int)span.TotalDays} days, {span.Hours}:{span.Minutes:00}";
    }

    private static string JoinArgs(List<string> args, int from) =>
        args.Count > from ? string.Join(' ', args.Skip(from)) : string.Empty;

    private static string Truncate(string text, int max) =>
        text is null ? string.Empty : text.Length <= max ? text : text[..(max - 1)] + "…";

    private static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > width)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                if (line.Length > 0)
                    line.Append(' ');
                line.Append(word);
            }
            yield return line.ToString();
        }
    }

    private static List<TermLine> Columns(List<(string text, string style)> entries)
    {
        if (entries.Count == 0)
            return [];

        // Lay entries out in columns like ls, assuming about 96 characters.
        var width = Math.Min(40, entries.Max(x => x.text.Length) + 2);
        var perRow = Math.Max(1, 96 / width);
        var rows = (entries.Count + perRow - 1) / perRow;
        var lines = new List<TermLine>();
        for (var r = 0; r < rows; r++)
        {
            var line = new TermLine();
            for (var c = 0; c < perRow; c++)
            {
                var i = c * rows + r;
                if (i >= entries.Count)
                    break;
                var (text, style) = entries[i];
                line.Add(Truncate(text, width - 1).PadRight(width), style);
            }
            lines.Add(line);
        }
        return lines;
    }

    private static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private readonly record struct Token(string Value, int Start, int End);

    /// <summary>
    /// Splits a command line into words. Quotes group words, and a backslash
    /// escapes the next character, as in a POSIX shell.
    /// </summary>
    private static List<Token> Tokenize(string line, bool allowUnterminated = false)
    {
        var tokens = new List<Token>();
        var current = new StringBuilder();
        var start = -1;
        char quote = '\0';

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                else if (c == '\\' && quote == '"' && i + 1 < line.Length)
                    current.Append(line[++i]);
                else
                    current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (start >= 0)
                {
                    tokens.Add(new Token(current.ToString(), start, i));
                    current.Clear();
                    start = -1;
                }
                continue;
            }

            if (start < 0)
                start = i;

            if (c is '"' or '\'')
                quote = c;
            else if (c == '\\' && i + 1 < line.Length)
                current.Append(line[++i]);
            else
                current.Append(c);
        }

        if (quote != '\0' && !allowUnterminated)
            throw new FormatException($"unmatched {quote}");

        if (start >= 0)
            tokens.Add(new Token(current.ToString(), start, line.Length));

        return tokens;
    }

    private static string UnescapeRange(string line, int start) =>
        string.Join(' ', Tokenize(line[start..], allowUnterminated: true).Select(x => x.Value)) +
        (line.Length > 0 && char.IsWhiteSpace(line[^1]) ? " " : "");

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is ' ' or '\'' or '"' or '\\' or '$' or '&' or '(' or ')' or ';' or '|' or '<' or '>' or '`' or '!' or '*' or '?')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
