# ValourOS terminal

Pressing Ctrl+/ (or Cmd+/ on macOS) while signed in swaps the app for a
full-screen terminal called ValourOS. It is a keyboard-only way to move between
planets and chat. Pressing the shortcut again, typing `exit`, or pressing
Ctrl+D at an empty prompt returns to the app.

The terminal is an extra: the app works the same without it, and it has no
server-side parts.

## Using it

Joined planets appear as directories under the home directory (`~`), direct
messages appear under `~/dms`, and a planet's chat channels are entries inside
its directory. Entering a channel, with `cd #general` or
`open channel general`, switches the prompt to chat mode. In chat mode each
line is sent as a message, and commands start with `/`. `/leave` or Ctrl+D
returns to the shell.

`help` lists the shell commands and `/help` lists the chat commands. `man
<command>` explains one command. Names can be shortened as long as only one
planet or channel matches, and Tab completes commands, paths, and `@names`.

The line editor follows readline conventions: history with the arrow keys or
Ctrl+P and Ctrl+N, reverse search with Ctrl+R, word movement with Alt+B and
Alt+F, deletion with Ctrl+W, Ctrl+U, and Ctrl+K, and Ctrl+Y to paste the last
deleted text. `!!` and `!n` repeat earlier commands. Shell history is saved on
the device; chat messages are kept in memory only, because they may contain
text that is encrypted everywhere else.

## How it works

The feature has three parts in
[Valour/Client/Components/Terminal](../Valour/Client/Components/Terminal):

- `ValourTerminal.razor.ts` draws the terminal and runs the line editor. It
  registers the global shortcut, plays the swap animation, and owns history,
  completion display, and themes. It builds its own DOM inside the component's
  root element, so Blazor never re-renders the terminal.
- `TerminalShell.cs` is the shell. It parses command lines, resolves paths to
  planets and channels, and writes output as `TermLine` values, which are runs
  of text with style keys that the script maps to the theme palette.
- `ValourTerminal.razor` connects the two. Output from the shell is sent to the
  script through the renderer's dispatcher, because realtime events can arrive
  on other threads.

Chat uses the same SDK paths as a chat window. Messages are sent through
`MessageService.SendMessage`, so they are encrypted on the device like any other
message, and a channel without posting permission or a device without ready
encryption opens read-only. The shell opens planet and channel connections with
its own connection key and releases them when the terminal closes. It remembers
the current channel, and on the next open it reconnects and prints the messages
that arrived in between.

Mentions in received messages are shown by name. When sending, `@name` and
`#channel` become mention tokens if exactly one member or chat channel matches.

## Visual style

The terminal intentionally departs from the [design language](DesignLanguage.md):
it uses scanlines, a blinking cursor, and a power-off animation, because it
imitates a text console. These effects stay inside the terminal. The `theme`
command switches between six palettes, and `prefers-reduced-motion` replaces the
animation with a fade and stops the cursor blinking.
