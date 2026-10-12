# App Store listing kit

This folder holds what App Store Connect asks for when publishing the iOS and
iPadOS app (`gg.valour.app`): listing text, privacy answers, age rating
answers, and notes for App Review. The policy pages it links to are generated
by `Valour/Web` from the documents at the repository root.

| Item | Value |
| --- | --- |
| Bundle ID | `gg.valour.app` |
| Team | Valour Software LLC (3BRGSQ2N5K) |
| Minimum OS | iOS and iPadOS 17.0 |
| Primary category | Social Networking |
| Secondary category | Productivity |
| Privacy policy URL | https://valour.gg/privacy/ |
| Support URL | https://valour.gg/faq/ |
| Marketing URL | https://valour.gg |
| Terms of use (EULA) | https://valour.gg/terms/ |
| Copyright | 2026 Valour Software LLC |
| Price | Free |

## Store listing

Apple rejects listings that name other mobile platforms or competing
products, so this text does not mention Android, Google Play, or Discord.

**Name** (30 characters max): `Valour`

**Subtitle** (30 characters max): `Encrypted community chat`

**Promotional text** (170 characters max, editable without a new build):

> Chat, voice, threads and wikis for your communities. Every message is
> end-to-end encrypted, with no ads and no trackers.

**Keywords** (100 characters max, comma separated, no spaces after commas):

```
chat,community,encrypted,messaging,group chat,voice,forum,wiki,threads,friends,gaming,private,e2ee
```

**Description** (4,000 characters max):

> Valour is a home for your communities. Join planets built around the things
> you love, chat in real time, and keep the best conversations going with
> threads and wikis.
>
> Private by design
> Every chat message is end-to-end encrypted, in direct messages, group chats
> and planet channels. Your keys stay on your devices, and Valour stores your
> messages without them. There are no ads, no trackers, and we never sell your
> data.
>
> Everything a community needs
> • Real-time chat with channels, categories, replies, reactions and file sharing
> • Threads: posts and discussions that show up in your home feed
> • Wikis for guides, rules and resources
> • Voice and video channels, and calls with friends
> • Calendars with events and reminders
> • Roles, permissions, moderation tools and automod
> • Themes, custom emoji and profile customization
> • Bots, webhooks and an open API
>
> Safe communities
> Report messages and posts, block people, and moderate your own planets with
> roles, automod and an audit log. Valour never asks for a phone number, a
> government ID or a face scan.
>
> Open source
> Valour's client and server are open source, so anyone can see how it works.
>
> One account everywhere
> Use Valour on iPhone, iPad, Mac and the web at app.valour.gg. Your planets,
> messages and settings follow you.
>
> Valour is free to use. The optional Stargazer subscription adds perks such
> as larger uploads and helps fund the platform.

**What's New** (for the first release):

> Valour is now on the App Store.

## Screenshots

App Store Connect needs at least one screenshot for each device family the
app supports. `Info.plist` declares iPhone and iPad, so both are required.

| Display | Size (portrait) | Count |
| --- | --- | --- |
| iPhone 6.9" | 1320 x 2868 | 3 to 10 |
| iPad 13" | 2064 x 2752 | 3 to 10 |

Apple scales these down for smaller devices. Screenshots must show the
current app, so take them from the iOS app in the Simulator rather than
reusing the Google Play screenshots, which are 9:16 web captures.

## App Privacy

Answer **yes** to collecting data and **no** to tracking. Valour has no
advertising and no third-party analytics, and it does not link data with
other companies' data. Every type below is linked to the user's identity.

| Apple data type | Purposes | Notes |
| --- | --- | --- |
| Contact Info: Email Address | App Functionality | Sign-in, verification, password recovery, service notices |
| Identifiers: User ID | App Functionality | Username, tag and account ID |
| Purchases: Purchase History | App Functionality | Subscription status and credit purchases, including App Store purchases |
| User Content: Photos or Videos | App Functionality | Files users upload |
| User Content: Other User Content | App Functionality | Profiles, threads, comments, wiki pages, events, and reported message text |
| Usage Data: Product Interaction | App Functionality, Analytics | Online status, last active time, read positions, daily active records |
| Diagnostics: Crash Data, Other Diagnostic Data | App Functionality | Only when the user opts in to error reporting |
| Other Data: Other Data Types | App Functionality | Date of birth, for the minimum age check |

Not collected: name, phone number, physical address, location, contacts,
health, browsing history, search history, advertising data, and sensitive
info. Live call audio is relayed in real time and not stored, so it is not
declared. Chat text is end-to-end encrypted and Valour cannot read it; the
"Other User Content" entry covers text a recipient chooses to report.

## Age rating

Valour requires users to be at least 13 and checks the date of birth at
registration. In the questionnaire:

- **Messaging and chat:** yes. Users talk with each other.
- **User-generated content:** yes. Users post text, images and video.
- **Advertising:** no.
- **Unrestricted web access:** no. Links open in the system browser.
- **Mature themes, sexual content, violence, profanity:** answer for what the
  iOS app shows by default. NSFW planets, channels and threads are hidden in
  the iOS app for accounts created there, and the setting can't be turned on
  inside the app.

## App Review information

**Sign-in required:** yes. Create a review account on production, confirm its
email, join it to a small demo planet with a few channels, and give Apple the
email and password. Do not use a personal or staff account.

**Notes** (paste into the Notes field):

> Valour is a community chat app. Messages are end-to-end encrypted. On first
> sign-in the app creates encryption keys on the device and shows a recovery
> code; tap "I saved it" to continue.
>
> The review account is a member of the "App Review" planet, which has text
> channels, a thread feed and a wiki.
>
> Moderation: long-press a message to report it. Tap a user's avatar to block
> them. Settings > Safety lists blocked users. Planet owners have roles,
> automod and an audit log.
>
> Account deletion: Settings > Profile > Delete account.
>
> In-app purchases: Settings > Subscriptions sells the Stargazer
> subscriptions and Valour Credit packs through In-App Purchase, with a
> Restore purchases button. Sign in with Apple is on the sign-in page.
>
> NSFW content is hidden in the iOS app for accounts created in it, and the
> setting can't be changed inside the app.
>
> The app uses only standard encryption algorithms. The French encryption
> declaration is uploaded under App Encryption Documentation.

**Contact:** Kobe Albright, kobe@valour.gg, +1 860 993 6587.

## Encryption export compliance

The app uses non-exempt encryption (end-to-end encryption with standard
algorithms) and is distributed in France, so App Store Connect needs the ANSSI
acknowledgement of receipt under App Information > App Encryption
Documentation. Once Apple approves the document, add these keys to
`Valour/Client.Maui/Platforms/iOS/Info.plist` so builds skip the export
questions:

```xml
<key>ITSAppUsesNonExemptEncryption</key>
<true/>
<key>ITSEncryptionExportComplianceCode</key>
<string>CODE FROM APPLE</string>
```

## Before submitting

The app changes App Review requires are in version 0.9.20: In-App Purchase
for Stargazer and Valour Credits with Restore purchases (guideline 3.1.1),
Sign in with Apple (4.8), NSFW content hidden by default in the iOS app
(1.1.4), and Apple token revocation on account deletion (5.1.1). What remains
is configuration:

- **In-App Purchase products.** Create the subscription group and credit
  packs with the IDs in [Payments](../../Docs/Payments.md#setting-up-app-store-in-app-purchase),
  add the In-App Purchase key to the server's `AppStore` settings, and set the
  App Store Server Notifications URL. Submit the products with the build.
- **Sign in with Apple.** Enable it on the App ID, create the Services ID and
  key, and add them to the server's `ExternalAuth:Apple` settings (see
  [Sign-in methods](../../Docs/SignInMethods.md#turning-on-google-discord-and-apple)).
- **Upload pipeline.** `.github/workflows/ios.yml` builds and signs the app and
  uploads it to App Store Connect from `main`. It needs three repository
  secrets: `IOS_DIST_CERT_P12_BASE64` and `IOS_DIST_CERT_PASSWORD` (an Apple
  Distribution certificate exported as .p12) and `IOS_PROVISION_PROFILE_BASE64`
  (an App Store provisioning profile for `gg.valour.app`). The upload uses the
  existing `ASC_API_*` key, which needs the App Manager or Developer role.
- **Encryption compliance code.** Add it to `Info.plist` once Apple approves
  the French declaration (see above).
