# Frequently asked questions

## What is Valour?

Valour is an open-source community platform. Communities are called planets and
can contain chat, thread feeds, wikis, voice and video channels, roles, an economy,
and a village. The client lets you keep several conversations open in separate
tabs and panes.

## How do I use it?

Open [app.valour.gg](https://app.valour.gg) in a browser. Application downloads are
listed in [GitHub releases](https://github.com/Valour-Software/Valour/releases).

## Is it free?

Valour offers optional Stargazer subscriptions with additional account perks.
The server's configured payment services determine which purchase controls are
available on a self-hosted instance.

## Does Valour support voice and video?

Yes. Planets have call channels, and direct and group conversations support private
calls. The instance can use Cloudflare RealtimeKit or LiveKit. A planet can also
configure its own LiveKit server for planet calls. Private calls use the instance
provider. See [Direct and group calls](Docs/DirectAndGroupCalls.md).

## Can I bring a community or connect other services?

The client includes a Discord importer. Bots, OAuth applications, and incoming
webhooks provide ways to connect other systems. Bots follow the planet's membership
and permission rules. See [the bot guide](Valour/Docs/BOT_GUIDE.md).

## What account information is required?

Registration uses an email address, username, and password. Email delivery and
verification behavior depend on the instance's email configuration. The registration
flow does not require a phone number or government ID.

## Can I host Valour myself?

Yes. The Compose bundle includes the application and its PostgreSQL, Redis, media,
and HTTPS services. See [Self-hosting](README.md#self-hosting) for setup and
[Deployment](Docs/Deployment/README.md) for operational details.

## What is federation?

A hub manages accounts and the planet registry while independent community nodes
host planet data. The client uses separate credentials to connect to each community
node. Planet owners can move between hub and community hosting through a verified
handoff. See [Federation](Docs/Federation.md).

## Is there an SDK?

The .NET SDK is in [Valour/Sdk](Valour/Sdk). It handles API requests, real-time node
connections, model caching, and client services. Its source is built alongside the
application, so use a project reference when developing against this checkout.

## How can I contribute or report a problem?

Follow [the local setup guide](README.md#contribute). Search
[GitHub issues](https://github.com/Valour-Software/Valour/issues) before filing a bug
or suggestion. Include reproduction steps and relevant logs without credentials.
Security reports go to the address in [the security policy](SECURITY.md).

## What license and rules apply?

The source license is in [LICENSE](LICENSE). The
[trademark notice](README.md#trademark-notice) covers the Valour name and branding.
Platform use is covered by [the terms](TERMS_OF_SERVICE.md),
[platform rules](PLATFORM_RULES.md), and [economy rules](PLATFORM_ECO_RULES.md).

For account or support questions, contact support@valour.gg.
