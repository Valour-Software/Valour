# Valour documentation

These guides describe the implementation in this repository. Configuration examples
use placeholder values; use private local settings when running a server or tests.

## Getting started

- [Project overview and local setup](../README.md)
- [Frequently asked questions](../FAQ.md)
- [Deployment](Deployment/README.md)
- [Self-hosted voice](Deployment/SelfHostVoice.md)
- [Bot development](../Valour/Docs/BOT_GUIDE.md)

## Application architecture

- [Reactive models and real-time updates](ReactiveModelSystem.md)
- [API routing and authorization](../Valour/Docs/API_ROUTES.md)
- [Roles and permissions](../Valour/Docs/ROLES.md)
- [Channel activity notifications](ChannelActivityNotifications.md)
- [Direct and group calls](DirectAndGroupCalls.md)
- [SDK](../Valour/Sdk/README.md) and [shared contracts](../Valour/Shared/README.md)
- [Public website](../Valour/Web/README.md)

## Federation

- [Operator and planet-owner guide](Federation.md)
- [Architecture and protocol](FederationArchitecture.md)
- [Recipient-bound invite grants](FederationInviteGrants.md)

## Villages

- [World architecture](VillageArchitecture.md)
- [Tilesets and artwork](VillageTilesets.md)
- [Staff default template](VillageDefaultTemplate.md)
- [Release verification](VillageReleaseQA.md)
- [JavaScript tests](../Valour/Tests/Js/README.md)
- [Browser tests](../Valour/Tests/Browser/README.md)

## Maintaining these guides

Describe the code that runs in this checkout. Verify method names, routes, defaults,
and commands against their source before changing an explanation. Explain why a
constraint matters where that helps someone work on the system. Keep design
proposals, implementation history, and results from individual test runs out of
these references.

Use ordinary English and complete explanations. Avoid em dashes, promotional
language, and unexplained abbreviations. Link to the relevant source or guide
instead of copying large classes or maintaining duplicate endpoint lists.
