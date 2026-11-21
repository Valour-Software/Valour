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
>
> Terms of Use: https://valour.gg/terms/
> Privacy Policy: https://valour.gg/privacy/

Apple requires an app that sells auto-renewable subscriptions to link its
terms of use from the product page. The two lines at the end of the
description do that, so keep them in every listing language.

**What's New** (for the first release):

> Valour is now on the App Store.

## Screenshots

App Store Connect needs at least one screenshot for each device family the
app supports. `Info.plist` declares iPhone and iPad, so both are required.

| Display | Size | Files |
| --- | --- | --- |
| iPhone 6.3" with Dynamic Island | 1206 x 2622 (portrait) | `screenshots/iphone/iphone-*.png` (8) |
| iPhone Duo (folded) | 1398 x 2034 (portrait) | `screenshots/duo/duo-*.png` (8) |
| iPad 13" | 2752 x 2064 (landscape) | `screenshots/ipad/ipad-*.png` (4) |

Upload them in file-name order. Apple scales them down for smaller devices.

The product page also takes two creative assets, which go under Header and
Search Results in App Store Connect:

| Placement | Size | File |
| --- | --- | --- |
| Product page header | 3840 x 1646 (21:9) | `creative/header.png` |
| Search results | 3840 x 2560 (3:2) | `creative/search-results.png` |

Apple keeps only a centred safe area visible on every device, so the focal
point of each sits inside it. The header has no text. The search results image
carries the English subtitle, so another listing language needs its own copy.
Neither image may contain transparency. They come from the same `npm run stills`
command as the screenshots (`--only header` and `--only search`).

They are the Google Play store images at Apple's sizes, made by
`npm run stills` in `Tools/Films` on the `promo/e2ee-film` branch, which
writes them to `Tools/Films/out/app-store/`. Each screen is the client's
interface recreated with its real copy, in an iPhone or tablet frame. Every
community, person and message in them is invented for the listing. The
sign-in screen shows Sign in with Apple and Google, so the images name no
competing product. They are exported as 24-bit PNG, because App Store Connect
rejects images with an alpha channel.

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

**Notes** (4,000 characters max; paste into the Notes field after filling in
the three values in angle brackets):

```
SCREEN RECORDING (iPhone, current iOS, starts at launch): <LINK>

DEMO ACCOUNT: <EMAIL> / <PASSWORD>
On first sign-in the app creates encryption keys on the device and shows a recovery code. Tap "I saved it" to continue. The account is a member of the "App Review" planet (community), which has text channels, a thread feed and a wiki.

1. PURPOSE AND AUDIENCE
Valour is a community chat app for people aged 13 and over: friend groups, hobby and gaming communities, and open-source projects. Communities are called planets. Each has text channels, voice and video channels, threads, wikis and a calendar. It solves the problem that group chat usually means giving up privacy: every chat message is end-to-end encrypted, there are no ads or trackers, and no phone number or ID is needed. The app is free. The client and server are open source.

2. MAIN FEATURES AND HOW TO REACH THEM
- Register: "Create account" on the sign-in page (email, username, password, birthday, terms). Sign in with Apple and Google are on the same page.
- Chat: open the App Review planet from the planet list, choose a channel, type a message.
- Threads and wiki: in the planet's channel list.
- Direct messages and friends: Home.
- Report content: long-press a message, thread or comment, then Report. Reports go to the planet's moderators or to Valour staff.
- Block a user: long-press a message, then Member > Block. Settings > Safety lists blocked users and unblocks them. Messages from blocked users are hidden.
- Moderation: planet owners have roles, automod and an audit log. Valour staff review platform reports and remove content and accounts that break the rules (https://valour.gg/terms/). Uploaded images are checked against known abuse material.
- Adult content: NSFW communities are hidden in the iOS app, and the setting cannot be turned on inside the app.
- Delete account: Settings > Profile > Delete account. It confirms identity, then deletes the account and its data permanently.
- Purchases: Settings > Subscriptions. Stargazer, Stargazer Plus and Stargazer Pro are optional monthly auto-renewable subscriptions that add perks such as larger uploads. Valour Credit packs (500, 1000, 2000) are consumables used for subscriptions and cosmetic items. All are sold only through In-App Purchase. The page has Restore purchases, Manage (opens Apple subscription settings), renewal terms, and links to the terms of use and privacy policy.

3. EXTERNAL SERVICES
- Apple: In-App Purchase with the App Store Server API, and Sign in with Apple.
- Google and Discord: optional OAuth sign-in.
- Cloudflare: content delivery, file storage, and relaying live voice and video (RealtimeKit).
- Microsoft Azure: hosting for our own servers and database.
- Microsoft PhotoDNA: matching uploaded images against known child abuse material.
- SendGrid: account emails such as verification and password reset.
- Klipy: GIF search.
- Sentry: crash reports, only if the user opts in.
The app has no AI features, no advertising and no analytics SDKs. Card payments are not offered in the iOS app.

4. REGIONS
The app works the same in every region. It is in English. There is no region-specific content or feature.

5. REGULATED INDUSTRY AND THIRD-PARTY MATERIAL
Valour is not in a regulated industry and includes no protected third-party material. Content is created by users, who agree to the terms of use before registering. The app uses only standard encryption algorithms; the French encryption declaration is under App Encryption Documentation.
```

## Screen recording

App Review asks for one recording made on a physical iPhone running the
current iOS release, starting with the app being launched from the Home
Screen. Record it with the iPhone's own screen recorder using the build that
is submitted, then upload it somewhere Apple can open without signing in and
put the link at the top of the notes. Keep it under about five minutes.

Use a new throwaway account for the recording, because it ends with the
account being deleted. The review account must still exist afterwards. A
second account has to have posted a message in the demo planet beforehand, so
there is someone else's content to report and block.

1. Launch the app from the Home Screen.
2. Create an account: email, username, password, birthday, and the terms
   checkbox. Show the Sign in with Apple button on the way.
3. Confirm the email, sign in, and save the recovery code.
4. Join the demo planet, open a channel and send a message. Open a thread and
   the wiki briefly.
5. Long-press the other account's message and submit a report through to the
   "Report submitted" screen.
6. Long-press the same message, choose Member, then Block. Show that the
   message is hidden, then open Settings > Safety to show the blocked list.
7. Open Settings > Subscriptions. Buy a subscription or a credit pack with a
   sandbox account through to the confirmation, and show Restore purchases
   and Manage.
8. Open Settings > Profile > Delete account, confirm, and show the app
   returning to the sign-in page. Try to sign in again to show the account is
   gone.

**Contact:** enter the name, email and phone number of the person App Review
should reach in App Store Connect. Do not record them here.

## Encryption export compliance

The app uses non-exempt encryption (end-to-end encryption with standard
algorithms) and is distributed in France, so App Store Connect needs the ANSSI
acknowledgement of receipt under App Information > App Encryption
Documentation.

The iOS `Info.plist` does not declare encryption, so App Store Connect asks
the export compliance questions for each uploaded build. Answer them on the
build's page under TestFlight before it can be tested or submitted: the app
uses encryption, with standard algorithms only, and is distributed in France.

Declaring `ITSAppUsesNonExemptEncryption` as true without a compliance code
makes App Store Connect refuse the upload (error 90592, "Invalid Export
Compliance Code"). When Apple issues the code for the French declaration,
both keys can go into `Info.plist` together so builds skip the questions:

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
- **Encryption questions.** Answer the export compliance questions for the
  uploaded build in App Store Connect (see above).
