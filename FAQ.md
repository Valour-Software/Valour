# Valour Frequently Asked Questions

| Content                                           |
| ------------------------------------------------- |
| [About Valour](#about-valour)                     |
| [Using Valour](#using-valour)                     |
| [Privacy](#privacy)                               |
| [Self-hosting & Federation](#self-hosting--federation) |
| [Building on Valour](#building-on-valour)         |
| [Supporting Valour](#supporting-valour)           |

## About Valour

### What is Valour?
Valour is an open-source community platform. Communities live on **planets**, spaces you shape with real-time chat, a reddit-style thread feed, a publishable wiki, voice channels, roles and permissions, themes, and your own economy.

### Is Valour a Discord knock-off?
Nope! Valour is built on a different stack (Blazor/.NET rather than Electron) and a broader idea. Multi-window chat, thread feeds, public wikis, and built-in economies aren't things a chat clone does. We want to be a home for your community, not just a room.

### Is Valour free?
Yes. Optional **Stargazer** subscriptions add perks like larger upload limits and profile flair, and help fund the platform.

## Using Valour

### How do I use Valour?
Use it in any browser at [app.valour.gg](https://app.valour.gg), or download the Windows and Android apps from [GitHub releases](https://github.com/Valour-Software/Valour/releases/latest).

### Does Valour have voice chat?
Yes! Planets can create voice channels. Voice runs on our infrastructure by default, and self-hosted instances (or individual planets) can bring their own LiveKit server.

### Can I bring my existing community?
Yes. Valour has a built-in Discord importer, plus incoming webhooks, bots, and an open API for integrating anything else.

### I found a bug / have a suggestion!
Please check the [GitHub issues](https://github.com/Valour-Software/Valour/issues) first, then open a new one if it hasn't been reported.

## Privacy

### Do I need to show ID or a phone number?
No. All you need to register is an email address. No government ID, no face scans, no phone verification.

### Would Valour see my messages?
Valour staff only access private messages when legally required, or when needed to confirm a user's report of illegal activity.

### How is Valour funded?
Through optional subscriptions. Not ads, and definitely not your data. We never sell user data.

## Self-hosting & Federation

### Can I run my own Valour?
Yes! A single `docker compose up -d` runs a full instance with Postgres, Redis, media storage, and automatic HTTPS. See the [README](README.md#self-hosting).

### What is federation?
Communities on official infrastructure can migrate to independently operated **community nodes** without handing that server your Valour login. See [Docs/Federation.md](Docs/Federation.md).

## Building on Valour

### Are bots allowed?
Yes. Bots and OAuth apps are built on the same official API the client uses, and automated accounts are welcome here.

### Is there anything I have to worry about if I make something for Valour?
Valour is licensed AGPL-3.0: you can modify and use it however you like, but derivatives must also be open source under AGPL-3.0. See [LICENSE](LICENSE). Bots and integrations may use the Valour name; forks of the platform itself may not (see the [trademark notice](README.md#trademark-notice)).

### Are there API wrappers?
The official .NET SDK lives in this repo at [`Valour/Sdk`](Valour/Sdk). It's what the client itself uses, so it's always up to date.

## Supporting Valour

### I want to contribute code!
See the [contribution guide in the README](README.md#contribute) for local setup. Signed commits are required.

### I want to support the project!
You can subscribe to a Stargazer tier in the app, or support us on [Patreon](https://www.patreon.com/valourapp).

<br/>

# [I need to contact Valour Staff!](https://static.valour.gg/contact)
