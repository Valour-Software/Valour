# End-to-end encryption

Every chat message on Valour is end-to-end encrypted: direct chats, group chats,
and every planet channel. Text is encrypted on the sender's device and decrypted
on recipients' devices. The server stores and relays ciphertext and signed
metadata. It never receives a key that can decrypt what members write, and it
refuses a message from a client that is not encrypted.

Two kinds of text are known to the server. Text it writes itself, such as
webhook posts and automod responses, is sealed to the channel's key as soon as
it is written, and only the sealed copy is kept. Messages written before
encryption stay plain text in the database until an operator turns on sealing,
which then replaces them with sealed copies (see
[Server-sealed messages](#server-sealed-messages) and
[Plain-text history](#plain-text-history)).

Threads, thread comments, and wiki pages are public publishing features. They
are not encrypted, so they can appear on the public website and in search
engines.

The formats and cryptography live in [`Valour/Sdk/E2ee/`](../Valour/Sdk/E2ee/).
The SDK service that uses them is [`E2eeService`](../Valour/Sdk/Services/E2ee/),
a partial class split by area. Server endpoints are in
[`E2eeApi.cs`](../Valour/Server/Api/Dynamic/E2eeApi.cs) and services in
[`Valour/Server/Services/E2ee/`](../Valour/Server/Services/E2ee/). The
algorithms come from BouncyCastle, because the browser WebAssembly runtime does
not provide X25519, Ed25519, or authenticated encryption. Random bytes come from
the .NET random number generator (`RandomNumberGenerator`).

## What is protected

| Protected from the server | Visible to the server |
| --- | --- |
| Message text and embed content | Who sent a message, in which channel, and when |
| Live embed updates from bots | Which messages share a search term, but not the term |
| Search words and automod trigger words | Reply targets, mentions, custom emoji IDs, and attachment records |
| Reported text, until a recipient reports it | Links the sender asks to preview |
| | Channel and planet membership, and who holds keys |

Uploaded files, images, GIF attachments, and invite attachments are stored and
served as ordinary attachments. Only text and embeds are inside the encrypted
payload, which also lists a digest of each attached file so recipients can tell
whether the server changed them (see
[Attachments and link previews](#attachments-and-link-previews)). Embed
interaction payloads (button clicks and form submissions) go to the embed's bot
in plain form.

Mentions are sent as metadata so the server can notify people. The sender's SDK
builds them from the text, and recipients ignore any mention the decrypted text
does not contain. The client also lists up to five URLs for link previews,
following the rules the server applies to text it can read: a link written as
`<url>` or inside code is not previewed, and a link inside a `||spoiler||` is
sent as `||url||` so its preview is hidden as a spoiler. The part of a link after
`#` is never sent, because it can hold a secret such as the key in an encrypted
invite link. Fetching a preview reveals that URL to the proxy but not the
surrounding text.

Push notifications and notification rows for messages, including channel
activity alerts, say "Encrypted message" instead of the text. A notification's
`SourceId` is the message ID, and the SDK's
`NotificationService.FetchNotificationMessageAsync` fetches and decrypts that
message so an app can show its text.

## Primitives

| Purpose | Algorithm |
| --- | --- |
| Signatures | Ed25519 |
| Key agreement | X25519 |
| Authenticated encryption | ChaCha20-Poly1305 |
| Key derivation | HKDF-SHA256 with a purpose string such as `valour-e2ee/message/v1` |
| Commitments and search terms | HMAC-SHA256 |
| Sealed boxes | Ephemeral X25519 plus ChaCha20-Poly1305, bound to a context string |

Every signed or hashed structure uses the canonical big-endian encoding in
[`E2eeEncoding.cs`](../Valour/Sdk/E2ee/E2eeEncoding.cs), starting with a
four-byte format tag such as `VMH1`. A context string names the purpose,
channel, generation, and recipient of every sealed box, so a box cannot be
replayed in another place.

## Identity

### Devices and the user key

Each device creates its own Ed25519 signing key and X25519 encryption key. The
private keys never leave the device. A device ID is derived from the signing
key, so the server cannot attach a known ID to a different key.

All of a user's devices share a **user key**. Channel keys are sealed to the
user key rather than to each device, so adding a device does not require
resharing every channel. When a device is revoked, a new user key generation
replaces the old one, and each generation carries the previous one encrypted
under it. The newest user key can therefore recover older generations and
history stays readable.

### The key log

Every change to a user's devices is an entry in their signed **key log**:
`Genesis`, `AddDevice`, `RevokeDevice`, `RotateUserKey`, `SetRecovery`, and
`Reset`. Each entry chains to the hash of the previous one. A `Genesis` or
`Reset` entry is signed by the device it introduces, because no earlier device
of that epoch exists. Every other entry must be signed by a device that is
active at that point in the log, or by the account's recovery key.
[`UserKeyLogVerifier`](../Valour/Sdk/E2ee/UserKeyLog.cs) replays the log and
rejects any entry that breaks these rules. A new device also signs a join proof,
so a device key cannot be listed under someone else's account. A
`RevokeDevice` entry also records where the removed device's membership log
entries end (see [Removed devices](#removed-devices)).

The server runs the same verifier before it stores an entry. It refuses entries
dated more than ten minutes away from its own clock in either direction, since
other devices compare message times with the times in the log, and it accepts
at most 4,096 entries per account. Key logs are kept by the hub; community nodes
keep verified copies (see [Community nodes](#community-nodes)).

A device that verifies another user's log stores a **pin**: the entry count and
head hash. Later copies must extend the pinned log. A conflicting log raises
`ContactWarning` with `Conflict` set, and the keys are not used. A higher epoch
(see [Reset](#reset)) raises `ContactWarning` with `KeysReset` set. The app
shows a toast for each warning.

### Security numbers

A user's security number is 30 digits derived from their user ID, key epoch, and
both public keys (signing and encryption) of the first user key of that epoch.
The membership log identifies members by the same keys (see
[Membership logs](#membership-logs)), so a matching number means matching keys.
The app calls it a **security code**. It appears in a channel's encryption
details and in the user's own encryption settings. Two people who compare codes
in person or on a call can mark each other as checked. The mark is cleared when
the other person resets their keys, which the app shows as their security code
changing.

Actions that trust keys a person looked at take the security number that was
shown: `SetVerifiedAsync`, `AcceptKeyResetAsync`, and `ConfirmMemberKeysAsync`.
Each loads the user's key log again and fails without changing anything if the
number no longer matches, so keys that changed while the person was comparing
are never trusted unseen. The app then shows the new number.

A device keeps these marks, and the key epoch the person accepted, in its pins.
Tabs and processes that share a store merge pins when they save: the longer key
log wins, and a mark or accepted epoch comes from whichever copy changed it
last, so removing a mark sticks. A mark made for an earlier epoch never carries
over to a newer one. Before trusting a user on first use, a device checks the
store for a pin another tab saved.

### Setup and the recovery code

An account gets its keys automatically at its first login on any app or SDK
client. The first device creates the key log and a recovery code: 20 random
bytes shown as eight groups of four Crockford base32 characters. The code
derives a signing and encryption key that the key log lists as the recovery key,
and the user key is sealed to it. Valour does not store the code.

The app shows the code once, right after setup. The person can confirm they
saved it or choose to save it later. Until they confirm, the device keeps a
marker and the encryption settings show a reminder with a button to show the
code again or, after a restart, to create a new code. Creating a new code
replaces the old one. `E2eeService.PendingRecoveryCode` holds a newly created
code in memory until `MarkRecoveryCodeSavedAsync` is called, the device signs
out of encryption, or the program exits. `IsRecoveryCodeUnsavedAsync` reports
the marker, and `RecoveryCodeChanged` fires when a code is created or saved.

Typing the code on a new device lets that device act as a trusted device for one
step: it adds itself to the key log and opens the user key. The server accepts
an `AddDevice` entry sent directly only when the recovery key signed it; other
devices are added through [device linking](#adding-a-device).

A device stores a new device key or recovery code, marked as pending, before it
sends the matching key log entry. If the app stops, or the response is lost,
after the server accepted the entry, the next start finds the pending key in the
key log, finishes saving it, and shows a pending recovery code again. Pending
keys the key log never lists are discarded after 15 minutes.

### Reset

A user who has lost every device and their recovery code can reset, which the
app calls **starting over**. Reset starts a new **epoch** with new keys. Earlier encrypted messages become unreadable to
that user, and contacts see a notice that the user's keys changed. Private
planets and group chats do not trust a new epoch until someone confirms it (see
[Membership logs](#membership-logs)).

A reset is also what the server would fake to slip in keys of its own, so a
direct chat pauses after the other person resets. Until the person accepts the
new keys, their device neither shares keys with the new epoch nor sends with a
key the new epoch created, and sending fails with an explanation. Messages from
the new epoch can still be read, since each is signed. `IsKeyResetPendingAsync`
and `AcceptKeyResetAsync` expose this state. The chat shows a notice that the
person's security code changed, with options to compare codes and to confirm
that it's really them. Confirming in the notice accepts only
the keys it was shown (see [Security numbers](#security-numbers)).

A reset is signed only by the new device it introduces, so anyone who can sign
in to the account, and the server itself, can make one. A device that a reset
removed therefore does not join the new epoch on its own (see
[Device status](#device-status)). The app tells the person that the account's
keys were reset from another sign-in. If they did not do it, their password may
be compromised: they should change it and reset the keys from that device, which
starts another epoch with keys only they hold. If they did it, they approve the
device from the device they reset on, which needs the link secret described in
[Adding a device](#adding-a-device). A device the server added cannot supply
that secret, so a forged reset cannot pull the person's other devices into it.

### Device status

`E2eeService.Status` is one of:

| Status | Meaning |
| --- | --- |
| `Unknown` | Not loaded yet, or no account is signed in |
| `NotSetUp` | The account has never set up encryption |
| `NeedsVerification` | The account has keys, but this device must be linked, restored, or reset |
| `Ready` | This device holds the account's keys and can send |
| `Error` | The account's key log, or the user key the server returned for this device, failed verification |
| `Unavailable` | The server could not be reached while loading the keys |

Messages can only be sent from a `Ready` device, because only it can encrypt.
`StatusChanged` fires on every change. When the status is `Unavailable`, the SDK
tries again after 5 seconds, then after waits that double up to 5 minutes, and
again when the connection returns; `InitializeAsync` tries again right away.
Only one timed retry is pending at a time, and the wait keeps growing until
loading succeeds with nothing left to retry, so a key restore that cannot reach
the server after the keys loaded also waits longer each time. Messages that
arrive meanwhile wait and are decrypted once the keys load.

When a connection returns, the SDK catches up on what realtime events would have
told it: this account's key log, other users' key logs, membership logs, and
keys for messages that are still waiting.

A device that another of the account's devices removed deletes its keys. A
device that a reset replaced keeps them, because the server could forge a reset:
it is `NeedsVerification` with `KeysResetElsewhere` set, still reads the
messages it could read before, and must be linked, restored, or reset to send.
The `KeysResetElsewhereDetected` event lets the app tell the person.
`E2eeCoordinator` shows a toast and a banner saying that the keys were reset
from another sign-in and that the password may be compromised if the person did
not do it, and offers **Reset keys from this device** next to verifying the
device. Browser tabs share storage, so a tab never deletes a device key that
another tab stored; it switches to that key instead.

A new device on an account that already has keys is `NeedsVerification`.
`E2eeCoordinator` in the client prompts the person to link or restore it and
shows a banner until they do. The banner can be dismissed and appears again when
the status changes. The chat input shows a notice with a button to verify the
device in place of the text box, and messages the device cannot read show why.
Apps never reset keys without asking, because a reset makes earlier messages
unreadable.

Signing out of the app removes the device from the account's key log
(`E2eeService.SignOutDeviceAsync`), and the confirmation explains that the
device must be approved or restored again to read encrypted messages. If it was
the account's last device, the recovery code restores access at the next login.

## Adding a device

A new device joins through a **link session** at `api/e2ee/link-sessions`. The
session expires after ten minutes. The server relays everything the two devices
exchange: the new device's keys, the signed `AddDevice` entry, the user key box,
and the approving device's pins. So each side checks the other with a secret
that one device shows on its screen and the other reads. The server never sees
it.

There are two ways to link:

- **The new device shows a QR code.** The code holds a fingerprint of the new
  device's keys and a 16-byte secret. The existing device sees the request as a
  prompt, scans the code, and checks the fingerprint against the keys the server
  relayed.
- **The existing device shows a code.** Under Settings, then Encryption, then
  **Link a device**, it shows a QR code and the same secret as 16 characters in
  four groups, such as `7K2M-9QXD-4TRB-0HZN`, for a new device without a camera.
  The secret is 10 random bytes written in Crockford base32, which leaves out
  letters that are easy to confuse with digits. The session ID is derived from
  the secret, so a new device that only has the typed code can find the session.
  The new device answers with a MAC (a keyed hash) over its keys under the
  secret, which the existing device checks before it offers to approve.

In both cases the approving device appends the `AddDevice` entry, seals the user
key to the new device, and sends an approval MAC under the secret. The MAC
covers the user ID, the session ID, the new device's ID, the entry body, the
user key generation, the box, and the sealed pins. The new device accepts the
approval only if the MAC matches the entry it finds in the key log and the box
it downloads. The entry chains to the rest of the key log, so the MAC also fixes
the log the new device joins. Without a matching MAC the new device refuses the
keys and reports why through the `LinkFailed` event. This matters after a reset
the server forged: the server then holds a device that can sign an `AddDevice`
entry and seal a user key it knows, but it cannot make the MAC.

The 80-bit typed secret is long enough that the server cannot recover it from
the MACs it relays within the ten minutes the session lasts. Codes are not
compared by eye, because a number shown on both screens would not prove who
approved.

The approving device also sends its pins, sealed to the new device's encryption
key and covered by the MAC: key log pins with their verification marks and
accepted epochs, membership log pins, the channel generation pins it has loaded,
and the pinned person of each direct chat. The new device adds only pins it does
not already have, and generation pins only rise, so the import never loosens a
check the new device made itself. The server refuses pin sets larger than 2 MB;
a device with more pins links without sending them.

A new device keeps its key, the session ID, and the secret in its key store
until the approval arrives, so an app that restarts meanwhile can still finish
the link. This is separate from the pending keys of setup and restore, because a
linked device key is used only after the MAC checks out.

QR codes are rendered with QRCoder. Scanning uses the browser's
`BarcodeDetector` and falls back to the vendored jsQR library. The Android host
requests camera permission when scanning starts, and the Apple hosts declare
camera usage in their property lists.

## Channel keys

### Generations

Each channel has numbered key **generations**. A generation is a random 32-byte
secret. The member who creates a generation signs a record with the planet,
channel, number, rotation reason, a public key derived from the secret, the
index generation (see [Search and automod](#search-and-automod)), and the
previous generation's secret encrypted under the new one. The record also
carries its **terms**: the channel's policy, whether new members may read
history, and the sequence number of the newest membership log entry the creator
had verified. The server stores the record and a sealed box of the secret for
each member's user key.

Record times never go backwards. The creating device dates a record no earlier
than the previous one, and the server refuses a record dated more than ten
minutes ahead of its own clock or earlier than the previous record. A previous
record dated further ahead than that limit is not held against the next one, so
a single bad clock cannot block a channel.

The rotation reasons are `Initial`, `MemberRemoved`, `Manual`,
`NewMemberWithoutHistory`, `IndexRotation`, `ServerSealing`, and
`KeyUnavailable`. A generation whose previous-secret field is empty does not
unlock earlier keys. It always has its own search key, because members who only
hold it cannot reach the earlier one. A channel gets its first generation from
the first member who sends a message there, unless the server already created
one (see [Server-created first keys](#server-created-first-keys)).

[`ChannelKeyChain`](../Valour/Sdk/E2ee/ChannelKeys.cs) describes how generations
link. Holding a generation unlocks every earlier one up to the last break. The
newest generation of each earlier chain is a **head** that has to be shared on
its own. Breaks made to hide history from new members are not heads, because
those members must not receive earlier keys. The server only accepts a
generation that unlocks the previous one from a member who holds the previous
one.

### Keys a device sends with

Every message is signed by its author, so a device reads with any generation
whose record carries a valid signature from one of the creator's devices. It
**sends** only with a generation it trusts. It trusts a generation when:

- a member created it, not the server;
- the creator's signing device is still active in the creator's key log, and
  the record is dated within that device's time on the account;
- the device has the previous generation's record, and this record is not dated
  before it;
- the channel's policy allows the creator. In a direct chat the creator is this
  user or the pinned other person, with a key epoch no newer than the one this
  person accepted. In a governed channel the membership log admits the epoch of
  the creator's device, with a matching key ID. In an open planet any member may
  create keys.

A record that fails the date check is still used to read. A member who wants to
send replaces an untrusted key first. This keeps a faked key reset, or a key
made by a removed device, from introducing a key the server or someone else
knows. Each device also remembers the newest generation it saw in each channel
and stops sending if the server later reports an older one.

### Server-created first keys

The server sometimes has to seal a message into a channel that has no key yet:
a webhook post, an automod response, or history written before encryption. It
then creates the channel's first generation itself, with the reason
`ServerSealing`, and signs the record with its attestation key.
[`E2eeChannelKeyService.EnsureSealingGenerationAsync`](../Valour/Server/Services/E2ee/E2eeChannelKeyService.cs)
does this. The messages sealed with it are described in
[Server-sealed messages](#server-sealed-messages).

The server seals the new key to up to 200 recently active viewers who have set
up encryption, and in governed channels only to viewers the membership log
admits. Until `HeldKeyMinimumHolders` members (three by default) can open it,
the server also keeps a copy protected with ASP.NET Data Protection, because a
single member who resets their keys would otherwise take the only copy with
them. A member counts when they hold the key, or a later generation that unlocks
it, in a box they can still open. A channel with fewer viewers than that needs
every viewer. While the server keeps its copy, it seals the key to each viewer
who loads the channel's keys and cannot open it yet. In a planet that hides
history, it does this only while the key is still the channel's newest, so later
members do not receive earlier history this way. Once enough members hold the
key, the server deletes its copy, and other members get the key from members who
hold it, in the usual way.

Clients accept a server-created generation only as generation 1 and only with a
valid signature from the server's published attestation key. Because the server
knew this key, members must replace it before sending: the next member to send
creates generation 2 with a new search key, then sends. The server-created key
therefore only ever protects text the server already had.

### Distributing keys

Apart from the server-created first key, keys are distributed by members'
devices.

**When a key is created**, the creating device seals it to the other members
right away: every member of a direct or group chat, or a planet channel's
viewers who have set up encryption, up to the 1,000 most recently active, from
`api/e2ee/channels/{id}/key-candidates`. Each is checked against the channel's
policy on the device. The first 200 boxes travel with the new generation and the
rest follow in batches of 100. Members therefore do not wait for anyone to share
a new key.

**A member who joins later** records a key request. Devices that hold keys check
requests when they open a channel or receive a realtime notice. For each
requester the server lists the generations they are missing: the newest one and
any head they cannot reach. In a channel that hides history from new members it
lists only the newest one, since nobody may share the earlier heads. A box
sealed to a user key from before the requester's last key reset does not count
as holding that generation, because it can never be opened again. The holder
verifies the requester against the channel's policy and uploads a sealed box for
each missing generation it holds. A request stays open until the member holds
every key they may receive, and requests older than 30 days are removed.

The server refuses boxes for users its own records say cannot view the channel,
and a member can only share generations it holds directly or through a later
generation that unlocks them. In governed channels it also checks its own
verified copy of the membership log: it accepts a new key or shared boxes only
from a member the log admits with their current keys, and boxes only for
admitted members.

A member's user key changes when they sign out of a device or reset, and other
devices may still have the old one for a few minutes. When a new generation or a
set of shared boxes includes boxes sealed to a user key its recipient has since
replaced, the server stores the rest and leaves those out. The response names
the affected users in `StaleRecipients` (on `ChannelKeyStateDto` for a new
generation, and on `ChannelKeyShareResultDto` for shared boxes), and the sending
device reloads their keys and shares with them again. The creator's own box must
be current, or the new generation is refused.

In a planet channel, a key request reaches members who have the channel open and
up to 25 members who most recently received its newest key, so a quiet channel
still gets an answer from someone online. In a direct or group chat it reaches
every other member. A request repeated within 30 seconds of the previous one is
not announced again, and a device does not send the same request more often.

A device asks as soon as a channel it may post in opens (`PrepareToSendAsync`),
not when the person sends, so the key usually arrives while they are still
typing. A channel without a key is left alone when it opens; its first key is
made when someone sends.

**If nobody shares the key within five seconds**, a member who is about to send
starts a new generation with the reason `KeyUnavailable` (or
`NewMemberWithoutHistory` in a channel that hides history) and seals it to the
other members as above. They can send immediately and everyone can read it.
Messages from before it stay unreadable to them until a member who holds the
earlier key comes online and shares it. A planet with automod word or command
triggers allows this only for moderators, because the new search key has no
automod term hashes until a moderator's app computes them, and a moderator's
app computes them right away. Other members wait for someone to share the key,
and the send fails with an explanation. A moderator can therefore also recover a
channel whose newest key nobody online holds.

Only members who may send messages in a channel can publish a new generation.
Everyone sends with the newest key, so a read-only viewer could otherwise
publish one sealed only to themselves and stop everyone else from sending. In a
governed channel, the server also refuses a generation whose terms name a
membership log entry that does not exist yet.

### Who receives keys

| Policy | Who receives keys |
| --- | --- |
| `Direct` | The two members of a direct chat |
| `Group` | Members admitted by the group's signed membership log |
| `PlanetOpen` | Anyone the planet's permissions let view the channel |
| `PlanetInviteOnly` | Viewers the planet's signed membership log admits |

Outside a planet, the device decides the policy itself: a direct chat is
always `Direct`, and a chat this device has used as a direct chat stays one
whatever the server reports. A planet policy never applies outside a planet. A
group chat follows its membership log.

A public planet is open and a private planet is invite-only once its owner's
device has signed its membership log (see [Planet settings](#planet-settings)).
In open planets the server's permission data decides who can receive keys, so
membership is trusted to the server. A planet channel's encryption details
explain who can read it, public or private, without listing people, since that
follows the planet's members and permissions. The encryption details of direct
and group chats list the members with their security codes.

Group chats and invite-only planets are **governed** by a membership log. A
device treats a planet channel as governed when the planet or the channel's key
state reports invite-only, when it has pinned the planet's membership log, or
when any member-made key record in the channel was signed under a governed
policy. A new device that never pinned the log therefore still follows it, and
the server cannot relabel the planet as open.

The exception is a log its owner opened when making the planet public (see
[Making a planet public again](#making-a-planet-public-again)). A device that
verifies the owner's `Open` entry treats the planet's channels as open, as long
as the server also reports the planet as open and no key record in the channel
names a log entry at or after the `Open` entry. Such a record shows that the log
continued, for example with a restart that made the planet private again, so the
device keeps the channel governed. A governed channel whose log was opened
admits nobody, so no keys are shared until the log is loaded again.
`GetPlanetGovernanceAsync` in
[`E2eeService.Access.cs`](../Valour/Sdk/Services/E2ee/E2eeService.Access.cs)
makes this decision. When a device sees a planet's log start or stop governing,
it loads the keys of that planet's channels again the next time it uses them.

A device pins the other person of each direct chat the first time it sees the
chat. If the server later lists different members, the device neither shares
keys with nor trusts keys from anyone else, and sending pauses with an
explanation.

The SDK keeps each channel's keys under its planet ID and channel ID together.
Channel IDs are only unique within one node, but planet IDs are global and
direct chats live on the hub.

### History

When a planet shares history, a holder gives a new member the newest secret and
any earlier heads, and the chains of previous secrets unlock the rest. When a
planet does not share history, a new member triggers a
`NewMemberWithoutHistory` generation with no link to earlier secrets and its own
search key, and holders only ever give out the newest key. In such a channel a
holder serves only members who are missing the newest key. Direct and group
chats always share history among their members.

In an open planet the history setting comes from the server, which refuses key
records whose setting does not match the planet's. In a governed channel it
comes from the key records instead. The first trusted record sets it, and later
records change it only when the membership log's owner created them. Other
members carry the signed setting forward, and the owner changes it only through
the planet's Privacy settings (`SetPlanetPrivacyAsync`, which creates new keys
with `RotateChannelKeyAsync` and `sharesHistory`).
The server accepts a `NewMemberWithoutHistory` key in a governed channel
whatever the planet's current setting says, because members' devices check it
against the signed setting.

**Unused keys are reused in open planets.** In a planet that hides history, each
newcomer would otherwise cause a `NewMemberWithoutHistory` generation. The
server records whether any message uses a generation. When the newest generation
does not unlock earlier keys and no message uses it yet, a holder in an open
planet gives it to the next newcomer instead of creating another, because it
reveals nothing that newcomer should not read. The first message sent with it
ends the reuse. In an invite-only planet the server could claim that a used key
is unused, or that a newcomer already had earlier keys, so holders there give
each newcomer a new key.

### Rotation

**The server's rotation flag.** When someone who holds the newest key can no
longer view the channel, the server marks rotation as required. It does not have
a new key to give. The next member who sends a message, or edits one, receives
`E2EE_ROTATION_REQUIRED`, creates a `MemberRemoved` generation for the remaining
members, and resends. A send against an outdated generation receives
`E2EE_STALE_GENERATION` and is retried with the newest key. The server also
requires rotation when:

- a holder's box was sealed to a user key that has since been replaced, because
  a device was removed or the keys were reset, since the removed device could
  still open that box;
- in a governed channel, a holder is no longer in the membership log;
- the newest key is server-created.

**The sending device's own checks.** The device also replaces the newest key
before sending when:

- the server created it, or the device does not trust it (see
  [Keys a device sends with](#keys-a-device-sends-with));
- in a governed channel, the key was made before the membership log applied,
  for example while the planet was public;
- in a governed channel, the membership log removed someone after the entry the
  key's terms name, or the terms name an entry this device has not verified;
- this user, the other person of a direct chat, or a member of a group chat
  removed a device or reset their keys after the key was made. Each such change
  causes one new key. In planets, the device checks only its own user and relies
  on the server's flag for other members.

The server therefore cannot keep members on a key it knows, or one a removed
member holds, by withholding the flag. A governed channel gets no new key while
its membership log cannot be verified; sending fails until it can be. Before a
device shares or sends with a governed channel's keys, its copy of the log must
reach every entry that the channel's trusted keys name. If the server does not
return those entries, the device neither shares keys nor sends, so the server
cannot hide a removal that another member's key already reflects.

**Large open planets.** Open planets with at least `LargePlanetMembers` members
replace keys on a schedule instead: when rotation would be required, the key
stays in use until `LargePlanetRotationDays` (at least one day) have passed
since it was created. This applies to departures and to replaced user keys.
Members leave large planets constantly, and anyone can join an open planet
again, so replacing the key every time would cost a new key per departure for
little benefit. Invite-only planets, group chats, and smaller open planets
always rotate.

### Keeping key data small

A busy channel collects many generations over time. Three things keep the data a
device downloads and the server stores from growing with them.

**Records load on demand.** `api/e2ee/channels/{id}/keys` returns the records of
the newest 32 generations, the generations the member holds boxes for, and the
search key generations those use. When a device needs an older secret, for
example to read old history, it fetches the records between that generation and
the nearest one it holds from `api/e2ee/channels/{id}/generations?from=&to=`, up
to 200 per request, and follows the chain of previous secrets down. It checks
each record's signature as it arrives.

**Superseded boxes are removed.** A member who holds a generation can unlock
every earlier one it links to. The server cannot check those links, so the
member's own device asks it to delete their boxes for earlier generations,
through `api/e2ee/channels/{id}/boxes/prune`, after it has opened that part of
the chain itself. A member of a channel with a long unbroken chain keeps one
box. A device that cannot open a box deletes it
(`DELETE api/e2ee/channels/{id}/boxes/{generation}`) so another member can share
a working one. A device replaced by a reset holds only the old user key, so
once the member's new device prunes the old boxes, the replaced device can no
longer open those earlier channel keys.

**Unused keys are reused** in open planets that hide history (see
[History](#history)).

## Messages

### Encrypted messages

A message with `EncryptionVersion` 1 carries an **envelope**:

- A signed header with the channel, planet, generation, a random message nonce,
  revision, author, author device, reply target, timestamp, franking
  commitment, and search terms hash.
- A body encrypted with a key derived from the generation's content key, the
  message nonce, and the revision. It contains the text, the embed, a random
  franking key, a digest of each attached file (see
  [Attachments and link previews](#attachments-and-link-previews)), and any
  [extensions](#payload-extensions).
- An Ed25519 signature by the author's device over the header and a hash of the
  body.

The server checks the header against the channel and author, requires the
newest generation, verifies the signature against the author's key log, and
checks the uploaded search terms against the header's hash. The signing device
must be active, and a recovery key cannot send. The server stores the terms but
never relays them, and its `Content` column for these messages is empty.

The server refuses:

- a post or edit whose `EncryptionVersion` is not 1, with an error that begins
  with `E2EE_REQUIRED` and points bots to the .NET SDK and webhooks;
- an envelope that is missing, larger than 24 KB, or does not verify;
- an encrypted message that also carries plain text, or an embed outside the
  envelope.

The server keeps at most 50 mentions and five preview links from a message and
refuses more than 100 custom emoji IDs. The payload lists at most 64
attachments. Text is limited to 2,048 characters, the same limit the server
applies to text it can read, so every message can be reported with its text.

Edits keep the message nonce and increase the revision by one, and they go
through the same checks, including the rotation and stale-generation codes. An
edit cannot change the reply target.

### Payload extensions

The body ends with a list of **extensions**, so later versions of Valour can add
data to messages without breaking earlier apps. Each extension has a positive
number (its tag), a required flag, and a value. Tags appear in increasing order,
at most 32 of them, so a body has only one encoding. The server cannot read
extensions, and the franking commitment does not cover them, so a report cannot
prove an extension's value. A feature whose data must be provable in reports
needs its own commitment.

An app skips an optional extension it does not know and shows the rest of the
message. An extension whose absence would make the message appear wrong, such as
the keys to encrypted files, is marked required. An app that does not know a
required extension still checks the signature and commitment first, so a forged
message is still marked `Invalid`. A message that passes those checks is marked
`NeedsNewerVersion` and shown as "Update Valour to see this message" instead of
its text. `MessagePayload.SupportedExtensions` lists the tags this version reads,
and the list is currently empty. A body in the format without extensions
(`VMP2`) still reads, with no extensions.

### Reading messages

Recipients apply their own checks and mark a message that fails one `Invalid`:

- The signature must come from a device that was active when the message was
  sent. A signature from a device the recipient does not know yet, such as one
  the author just added, makes the SDK load the author's key log again, at most
  once per author every 10 seconds, before deciding.
- The header's planet must match the message, and the server must not have
  added a reply target the header does not have. Reply IDs are otherwise not
  compared, because messages get new IDs when a planet moves between nodes, and
  the server clears the reference when the target is deleted or its author is
  blocked.
- Text longer than 2,048 characters is invalid, and the sender's SDK refuses to
  send it.
- A message whose search terms do not match its text is hidden. The SDK raises
  `TamperedMessageDetected`, and the app offers to report it.
- A verified message with a required extension this version does not read is
  marked `NeedsNewerVersion` rather than `Invalid` (see
  [Payload extensions](#payload-extensions)).

When the key log or the channel's keys cannot be loaded, the message waits and
is tried again on a timer, from 15 seconds up to 5 minutes, instead of being
marked `Invalid`. A realtime notice that new keys are available lets the next
request load them right away, even shortly after a failed attempt. Realtime
events can arrive out of order, so the SDK's cache never replaces a decrypted
revision with an earlier one.

### Safeguards the server cannot apply

The server checks what it can read. For encrypted content, clients apply the
same checks after decrypting:

- **Markdown.** The server escapes `[](` and `]()` in text it writes, so an
  empty link cannot load an unproxied image or hide text. The SDK applies the
  same [`MessageMarkdownSafety`](../Valour/Shared/Utilities/MessageMarkdownSafety.cs)
  escaping after decrypting. The unescaped text is kept in `SignedContent` for
  reports.
- **Embeds.** [`EmbedSafety`](../Valour/Sdk/Models/Embeds/EmbedSafety.cs) parses
  and validates an embed and checks that its media come from allowed sources
  ([`MediaUriHelper`](../Valour/Sdk/Cdn/MediaUriHelper.cs)), which keeps third
  parties from learning readers' IP addresses. The sender's SDK refuses to send
  an embed that fails, and every recipient hides one that fails.
- **Permissions.** The sender's SDK refuses an embed without the channel's
  Embed permission, and custom planet emojis without the planet's Use Custom
  Emojis permission or outside planets. It lists the emoji IDs with the
  message, the way it lists mentions, and the server checks them. A recipient
  hides an embed whose author is still a planet member but lacks the Embed
  permission.

### Attachments and link previews

Files attached to a message, such as uploaded images, are stored beside the
ciphertext, so the server could otherwise add or change them. The sender lists a
digest of each file in the encrypted body
([`AttachmentBinding`](../Valour/Sdk/E2ee/MessageEnvelope.cs)). A digest covers
the file's type, location, MIME type, file name, image size, spoiler flag,
hosting, reported hash, and type-specific data. The sender sends an empty MIME
type or file name rather than none, because the server fills in missing values
for uploaded files.

A recipient shows only files the body lists, each once. When the server deletes
or quarantines a file it replaces it with an "Attachment not found"
placeholder, and a recipient shows at most as many placeholders as listed files
it did not receive. If the server returned any other file, the recipient hides
it and the message says that some attachments were withheld
(`Message.AttachmentsWithheld`). Apps show no attachments on an encrypted
message until it is decrypted.

The server generates link previews from the links the sender lists, so it
chooses what the preview of a listed link shows. A recipient keeps a preview
only when it could have come from a link in the decrypted text
([`InlinePreviewBinding`](../Valour/Sdk/E2ee/MessageEnvelope.cs)): its location
or Open Graph URL is the link itself, its location is the content CDN's copy of
the link's media, or its type is the player or card for the link's site. Each
link accounts for one preview, and a preview of a link inside a spoiler is shown
as a spoiler. Other previews are hidden.

### Live embed updates

A bot updates the embed on a planet message it sent with
`Message.SendEmbedUpdateAsync`.
[`EmbedUpdateCrypto`](../Valour/Sdk/E2ee/EmbedUpdateCrypto.cs) encrypts the
update with a key derived from the channel key, bound to the channel, message,
recipient, and revision. `api/embed/update` checks that the
sender is the bot that wrote the message and that the update names an existing
key generation, and relays only the ciphertext, up to 80 KB. Recipients decrypt
the update and run `EmbedSafety` before showing it.

Updates are not signed, so anyone who holds the key they use could write one. A
recipient therefore accepts an update only for a message it has, and only when
the update's key generation is at least the message's, was created by a member
rather than the server, and is one this device trusts for sending
(`E2eeService.EmbedUpdateViolation`). A removed member who kept an older key
cannot use it to change newer messages.

### Server-sealed messages

The server writes some messages itself: webhook messages, automod responses and
other system messages such as welcome messages, and text sent before encryption.
It seals these to the channel generation's public key, which it can use but not
reverse, creating the channel's first key if it has none. These have
`EncryptionVersion` 2 and a kind of `Webhook`, `System`, or `Legacy`. New
webhook and system messages are sealed when they are written, before anything
such as a notification can copy the text. `Legacy` messages are sealed later by
the maintenance worker (see [Plain-text history](#plain-text-history)).

The server signs an attestation over a salted hash of the text with its own
Ed25519 key, published at `api/e2ee/server-keys`. It keeps the attestation and
discards the salt and text. The header binds the planet and channel and a random
nonce, but not the message ID, because messages receive new IDs when a planet
moves between nodes. A sealed message holds up to five embeds, and its
attestation covers them as one value: the embed's JSON for one embed, or a JSON
array of the embeds' JSON for several. Webhook edits replace the whole sealed
message, so they must include both the text and the embeds. An author can
replace their own `Legacy` message with an encrypted edit; webhook and system
messages cannot be edited by users.

Because the server knows the text of these messages, recipients limit what it
can claim with them and mark a message that breaks a rule `Invalid`:

- `Webhook` and `System` messages must name Victor, the system account, as their
  author, so the server cannot post as a person with them.
- A `Legacy` message must be sealed with generation 1, where the server seals
  all history from before encryption. It must also be older than the channel's
  first member-created generation, whose creator signed the time it was
  created: generation 1 when a member created it, or generation 2 when the
  server created generation 1. A server-created generation proves nothing about
  time. An hour's allowance covers a slow clock on the creator's device and
  plain text an older server wrote during an upgrade. Until a member has created
  a key, nothing dates the start of encryption, so the time is not checked. The
  device loads the record it needs when it does not have it and counts the
  newest generation it has seen, so the server cannot skip the check by hiding
  generation 2.
- The message's time must match the attested time within 2 seconds.

Apps label `Legacy` messages (`Message.SealedKind`, `Message.IsLegacySealed`) so
they are not mistaken for messages their author encrypted. The chat always shows
the label, and never groups a server-sealed message under the message before it
or a message under a server-sealed one, so each sealed message shows its author
and label.

### Plain-text history

Messages written before encryption stay plain text in the database until an
operator turns on `SealLegacyMessages` (see
[Configuration and rollout](#configuration-and-rollout)). Apps treat plain text
like a sealed `Legacy` message, because the server could return any message
without encryption: it carries the same label (`Message.IsPlainTextHistory`,
`Message.IsFromBeforeEncryption`), must be older than the first key a member
created in its channel, and a plain copy never replaces a message the app holds
encrypted. Plain text is grouped only with other plain text.

When sealing is on,
[`E2eeMaintenanceWorker`](../Valour/Server/Workers/E2eeMaintenanceWorker.cs)
seals them with
[`E2eeMaintenanceService`](../Valour/Server/Services/E2ee/E2eeMaintenanceService.cs):

- **One server seals.** The worker starts once the server has finished starting
  and `LegacySealStartDelaySeconds` have passed, so an instance that is about to
  replace another one does not start early. Only the server holding a
  PostgreSQL advisory lock seals, so several instances sharing a database do not
  repeat each other's work. The lock is held on a dedicated connection with TCP
  keepalives, so a connection a network device dropped is noticed and the lock
  is released for another server.
- **Channel by channel.** The worker stays on one channel until its plain text
  is gone, in batches of `LegacySealBatchSize`, then moves to the next channel in
  ID order. It runs a batch every quarter second while work remains and checks
  again every minute once none is left. The partial index
  `ix_messages_legacy_plaintext` on `messages (channel_id, id)` finds the next
  channel and its messages.
- **Hosting.** A node seals planet channels only for planets it hosts, because
  it needs the planet's members to share the key. A planet assigned to another
  live node, or being migrated, is passed over until the next sweep. A planet no
  node hosts is taken on, since its history could not be sealed otherwise.
- **The oldest key.** Text is sealed into the channel's generation 1, creating
  it if the channel has no key. Members who hold a later generation that unlocks
  generation 1 can read it, and in a planet that hides history, members who
  joined after a break cannot.
- **What changes.** One statement replaces each batch, and it replaces a message
  only if it is still plain text and unchanged since the worker read it, so a
  concurrent edit is never lost. Embeds move into the sealed copy, and their
  attachment rows are deleted in the same transaction. A sealed message holds at
  most five embeds, so a message with more keeps its first five and loses the
  rest. The worker removes the channel's messages from the chat cache and
  replaces message text in the channel's notifications with "Encrypted message".
- **Open reports.** Before text leaves the database, the worker copies it into
  the evidence of every unreviewed report or planet report that points to the
  message, recorded as `ServerAttested`, so moderators can still read it.
- **Deleted channels.** Messages in deleted channels and deleted planets lose
  their text instead of being sealed, since nobody can read them again. They are
  marked as server-sealed without an envelope so the worker does not return to
  them.

Until the worker reaches a message, that message remains plain text.

## Search and automod

### Search terms

A client folds text into tokens and computes keyed hashes with the channel's
**index key** for each word (`W`), word prefix (`P`), word suffix (`S`), and
leading command (`C`). Prefixes and suffixes are limited to 12 characters, and a
message may have up to 400 terms. Common stop words such as "the" and "by" have
no terms. Hashes are truncated to 32 bits and stored in `messages.search_terms`
with a GIN index.

Folding uses fixed tables instead of culture or ICU behavior, so browsers and
native apps compute the same terms. It lowercases text, removes accents and
zero-width characters, maps lookalike letters from other alphabets, maps digits
used as letters, collapses long repeated letters, and splits CJK text into
single characters plus each overlapping pair of characters.

The terms hash is in the signed header, and recipients recompute terms from the
decrypted text. A sender who uploads different terms to avoid automod produces a
message recipients hide and can report.

The index key changes only with an `IndexRotation`, when members replace a
server-created key, or with a key that does not unlock earlier ones, so ordinary
key rotations do not interrupt search or automod. Only members with the planet's
Manage permission may replace a search key that a member created.

### Search

Message search is always encrypted. The client hashes the query and asks
`api/e2ee/channels/{id}/search` for candidates. Earlier query words must match
whole words and the last word may be a prefix. Truncated hashes can collide, so
the client decrypts candidates and checks each against the query. Search covers
messages indexed under the channel's newest 32 search keys. For each one the
client sends two term sets, one for members' messages and one for server-sealed
messages (see [Indexing server-sealed messages](#indexing-server-sealed-messages)),
in random order. A request carries at most 64 term sets of at most 32 terms each
and returns up to 100 candidates. A longer query is cut to 32 terms, which still
finds every match because the client checks the results.
[`E2eeLimits`](../Valour/Sdk/E2ee/E2eeLimits.cs) holds these limits for the SDK
and the server.

### Automod

Word and command triggers keep working. A moderator's client hashes each
trigger's alternatives with each channel's index key and uploads them to
`api/e2ee/planets/{id}/automod/terms`. A single word matches as a whole word,
the start of a word, or the end of a word. A phrase matches when all its words
other than stop words are present. The server matches uploaded message terms
before storing a message. Server-sealed messages are not matched, since the
server checked their text before sealing it.

When a new search key appears, or a trigger has no hashes for a channel, the
server asks moderators' apps to compute them (`AutomodTermsNeeded`), at most once
every two minutes per planet. A new or edited trigger starts working in a
channel once a moderator who can read it has been online; until then word
filters do not apply to messages under a new search key. Thread posts and
comments are not encrypted and are matched against their text.

**Approved triggers in invite-only planets.** Hashing text with a channel's
index key reveals whether that text appears in messages, so a moderator's app in
an invite-only planet does not hash whatever trigger list the server sends. When
an admin in the membership log saves a word or command trigger, their app
appends an `ApproveAutomodTriggers` entry with a hash of the trigger's type and
text (`E2eeService.ApproveAutomodTriggersAsync`, called by `AutomodService`).
Moderators' apps only compute terms for triggers whose current text matches an
approval. The server checks its own copy of the log too: it lists only approved
triggers when it asks moderators' apps for terms, and does not ask again about a
trigger that has no approval. A trigger saved by someone who is not a log admin,
or saved before the planet became invite-only, does not apply in encrypted
channels until an admin saves it again. Approvals name triggers by ID, and
moving the planet keeps trigger IDs (see [Moving a planet](#moving-a-planet)).
Open planets trust the server with membership, so their triggers need no
approval.

### Indexing server-sealed messages

Server-sealed messages have no terms when sealed. Members' clients index them
through `api/e2ee/channels/{id}/unindexed` and `terms`, up to 100 messages per
request, newest first. The partial index `ix_messages_unindexed_sealed` on
`messages (channel_id, id)` for sealed messages without terms serves that
request. The server cannot check these terms, so the first member to upload
terms for a message sets them for good. A member who uploads wrong terms can
keep that message out of search results, but cannot change what anyone reads.

The server chooses the text of sealed messages and could return made-up ones.
If clients hashed that text with the index key, the server would learn the terms
for any word it chose and could then find those words in members' messages.
Clients therefore hash sealed messages only with a separate **sealed index key**
derived from the index key (`ChannelKeySecret.SealedIndexKey`), and only index
sealed messages of the channel they asked about. Terms under that key match only
other sealed messages, whose text the server knew anyway.

This protects members' messages, not search queries against sealed history. A
query is also hashed with the sealed index key, so the server can learn which
words a person searches for by fabricating sealed messages and watching which
of their terms the query matches.

`IndexSealedHistoryAsync` indexes up to five batches per call. Apps call it
themselves; the Valour app calls it when a chat window opens a channel. It
visits a channel at most every 10 minutes, and when only messages it cannot read
are left, it waits longer each time, up to 6 hours. It does not decrypt a
message it could not read again until the channel's keys change.

## Reporting

When the reporting device can read the reported message's text, the app always
includes it in the report as **evidence**: the message text and the value that
proves it. The app has no option to leave it out. A report of a message the
device cannot read carries only the reporter's description.

- For end-to-end messages the proof is the franking key. The header's commitment
  is an HMAC of the text under that key, and the header is signed by the
  author's device. Evidence that opens the commitment is recorded as
  `AuthorSigned`.
- For server-sealed messages the proof is the salt. Evidence that matches the
  server's attestation is recorded as `ServerAttested`. Text the server copies
  from its own records before sealing a reported message is also
  `ServerAttested` (see [Plain-text history](#plain-text-history)).
- Anything else is recorded as `Unverified`.

Evidence carries the embeds exactly as the author signed or the server attested
them, from the decrypted payload (`Message.SignedEmbeds`), not the embeds on
screen, which safety checks may hide and live updates may change.
`E2eeService.BuildEvidence` builds it.

When a message is edited or deleted, the server keeps a `MessageProof` row for
`ProofRetentionDays`: the header, body hash, and signature of an end-to-end
message, or the sealed header with its attestation for a server-sealed one.
Proofs contain no text. A recipient can still prove an earlier revision or a
deleted message during that time. Proofs of reported messages are kept as long
as the report's evidence, which is stored in `report_evidence`. Moderators see
evidence in the planet reports view and staff see it in report details.

## Membership logs

Invite-only planets and group chats have a signed **membership log** that
decides who receives keys. The code and the API call it the access log
(`AccessLogState`, `api/e2ee/access-logs`).

Entries are `Genesis`, `AddMembers`, `RemoveMembers`, `SetAdmin`,
`CreateInvite`, `RevokeInvite`, `RedeemInvite`, `TransferOwnership`,
`ConfirmEpoch`, `Restart`, `Checkpoint`, `ApproveAutomodTriggers`, and `Open`.
Each is signed by one of a user's devices, chains to the previous entry, and is
checked against the signer's key log and the signer's role in the log. A
`RedeemInvite` entry is signed by the joining user, who is not a member yet. An
`Open` entry ends the log's authority over a planet that became public (see
[Planet settings](#planet-settings)).

The server stores and relays the log but cannot sign entries. Joining a planet
through the server does not admit anyone to the log, so the server cannot give
itself or anyone else keys.

### Admission with keys

The log lists each member, the admins, and the owner with a user ID, a key
epoch, and a **key ID**: the first 16 bytes of a SHA-256 hash over the user ID,
the epoch, and both public keys of that epoch's first user key
(`UserKeyState.EpochKeyIds`). The security number comes from the same keys. A
member counts only when the key log a device verified for them has the admitted
epoch with the same key ID (`AccessLogState.IsMember`). A key a member's device
created is trusted when the log admitted that device's epoch or a later one and
the admitted key ID matches (`AdmitsDevice`). The verifier checks the key ID of
every signer against the signer's key log. The keys a signer names for someone
else, in `Genesis`, `AddMembers`, `SetAdmin`, `ConfirmEpoch`, and
`TransferOwnership` entries, are checked by each device when it decides whom to
share keys with or whose keys to use. So a server that shows some devices a
different key log for a member, even one with the same epoch number, does not
gain an admitted member.

Nobody is admitted before they have keys. Starting a log, admitting members,
and adding people to an encrypted group skip anyone who has not set up
encryption. In a planet they stay on the list of people waiting to be admitted,
marked as not set up yet, until they sign in and an admin admits them. In a
group chat, any member can admit them from the chat's encryption details once
they have keys.

### Roles

- **Planets.** Only the owner and admins in the log admit or remove members,
  create or revoke invites, approve automod triggers, and sign checkpoints. Only
  the owner makes or removes admins. The planet settings list people waiting to
  be admitted, members whose keys changed and need confirming, and people the
  log admits who are no longer in the planet. Kicking or banning through the app
  or SDK also removes the person from the log when the moderator is a log admin.
- **Group chats.** A group cannot send messages until it has a log, so any
  member can start it and becomes the log's owner. Any member can add people,
  and only the log's owner can remove others or restart the log.
- **Leaving.** Any member can remove themselves; leaving an invite-only planet
  through the SDK does this. The owner cannot be removed.
- **Invites.** An admin can create a signed invite with an expiry and use limit.
  Its secret is carried in the invite link's fragment (`#e2ee=...`), which
  browsers do not send to the server. Someone who joins with the link redeems it
  and is admitted without waiting for an admin. When an admin creates an
  ordinary invite for a private planet on a verified device, the app signs one
  for it (`E2eeService.SignPlanetInviteAsync`): the signed invite is named after
  the invite code, expires with it, and has no use limit, since invite codes
  have none. The creating device keeps the secret in its key store, keyed by
  planet and invite code, so copying the code from the invite list on that
  device gives the full link again (`E2eeService.GetSavedInviteFragmentAsync`).
  Other devices and other admins copy the plain link. Device linking does not
  pass these secrets to a new device, which keeps them on the device that made
  the invite. The device forgets a secret when the code is deleted, its signed
  invite is revoked, or it expires, and forgets all of them when its keys are
  removed or started over. Invites created by anyone else, or on a device that
  is not verified, are plain: people who use them wait for an admin, and the
  app says so. Deleting a signed code (`E2eeService.DeletePlanetInviteAsync`) revokes its
  signed invite first, so the secret cannot admit anyone who joins with another
  code. Only an admin of the log on a verified device can do that, so the app
  refuses to delete such a code for anyone else.
- **Reset members.** A member who resets their keys is not trusted with the new
  epoch until another member appends `ConfirmEpoch`: an admin in a planet, or
  any other member in a group chat. Nobody can confirm their own keys. The app
  asks the confirming person to compare the security number it shows with the
  member, and the confirmation applies only to the keys behind that number. A
  member must also have their current keys admitted before they can become an
  admin or the owner.
- **Ownership.** A planet's log names its own owner, and only the owner's device
  can sign `TransferOwnership`. The planet settings transfer ownership through
  `E2eeService.TransferPlanetOwnershipAsync`, which signs that entry, naming the
  new owner's admitted keys, and sends it with the transfer request. The server
  appends it in the same transaction that changes the planet's owner, and
  refuses to transfer an invite-only planet without it unless the log already
  names the new owner. It also refuses `TransferOwnership` entries for planets
  sent any other way, so the planet's owner and the log's owner stay the same
  account. This includes a public planet whose log the owner opened, since only
  the log's owner can make the planet private again. While the log is open the
  entry names the new owner's current keys without requiring them to be a
  member, because an opened log has no members. The one exception is described
  in [Making a planet public again](#making-a-planet-public-again).

### Pins and history

Devices pin the newest membership log entry they verify. A later copy must
extend it, so the server cannot hide the log, roll it back to undo a removal, or
replace it. Pins are also why the server cannot make a private planet public:
members' devices keep enforcing the log regardless of what the server reports,
until they verify an `Open` entry the log's owner signed.

Only the log's current owner, with the keys the log names, can sign a `Restart`,
so the server cannot name an account it controls as a planet's owner and start
the log over. After an `Open` entry, the owner's `Restart` and
`TransferOwnership` are the only entries the log accepts (see
[Making a planet public again](#making-a-planet-public-again)). The
verified state records the latest entry that removed members or restarted the
log; channel keys made before it are replaced before anyone sends (see
[Rotation](#rotation)).

Entry times never go backwards: an entry cannot be dated before the entry it
follows, and the server refuses entries dated more than ten minutes ahead of
its clock. An invite's expiry is checked against the time of the entry that
redeems it, so once any entry is dated after the expiry, the invite cannot be
redeemed.

### Removed devices

Membership logs name members by account and key epoch, not by device, so on
its own a log cannot tell a removed device's signature from one by the same
person's other devices. Each signature is checked against the device's time on
the account, but the signer chooses an entry's time. Without more, a copy of a
removed device's keys, together with a server willing to append its entries,
could still let people in by dating an entry before the removal.

The removal therefore records a **cutoff** for each membership log the account
belongs to: the newest entry the removed device may have signed there
(`AccessLogCutoff` in [`UserKeyLog.cs`](../Valour/Sdk/E2ee/UserKeyLog.cs)). The
`RevokeDevice` entry carries the cutoffs and is signed by one of the account's
remaining devices. It is the only key log entry written with the newer `VKL2`
magic, so other entries stay readable by apps from before cutoffs existed. The
account's key log is pinned by everyone who checks it, so the server cannot
take a removal back from a device that has seen it.

- **Removing this device**, including when logging out, loads no logs. Each
  log this device verified is cut off at its pin, which includes every entry it
  signed, merged with pins other tabs saved. Every other private planet and
  group chat the account belongs to, read from the account's planet and chat
  lists, is cut off at -1, because a device signs only in logs it verified. If the server finds an entry the
  device signed past its cutoff (`E2EE_REMOVAL_CUTOFF_STALE`), for example one
  whose pin was never saved, the device loads the logs and tries once more.
- **Removing another device** loads every membership log the account belongs
  to (private planets it joined or owns, and group chats) and uses each one's
  verified end. A log that cannot be loaded is left out. A removal lists at
  most 1,000 logs.
- **Checking entries.** An entry a removed device signed after its cutoff stays
  in the log's chain, so the log keeps working, but it has no effect: nobody it
  names is let in or removed. A checkpoint signed that way cannot start a log;
  readers who receive one read the whole log instead
  (`AccessLogVerifier.CanStartFrom`). Logs a removal does not list, and removals
  recorded before cutoffs existed, are checked only against the removal time.
- **Starting over.** A `Reset` entry carries no cutoffs. Only the new device
  signs it, so the server could forge one, and cutoffs on it would let the
  server undo entries members already accepted. Devices of the earlier keys
  are checked against the reset's time, and in each log their authority ends
  once the log confirms the account's new keys (`ConfirmEpoch`).
- **The server.** It holds a per-account lock while it stores a key log entry
  and while it stores a membership log entry signed by that account, so a
  device being removed cannot sign an entry between the removal's checks and
  its storage. It takes new membership log entries only from devices that are
  active at that moment. For the logs it keeps, it refuses a removal whose
  cutoff is past a log's end or before an entry the removed device signed. A
  community node's logs are checked by the removing device, which loads them
  first.

Cutoffs apply to entries a device checks after it has seen the removal. A
server can still withhold a key log update from someone indefinitely, as it
can for any removal, and that person's device then accepts such an entry. A
device that applied one before it saw the removal keeps its state. If that
device belongs to an admin who then signs a checkpoint, devices that replay the
log find the checkpoint does not match their state and refuse the log.

**Stored state.** A device stores the state it verified (members, admins,
invites, whether the owner opened the log, and the newest entry) next to its
pin. Later it asks
`api/e2ee/access-logs/{scope}/{id}?known=` only for entries after the ones it
has and applies them to the stored state, so it does not download and check the
whole log again. A device that has a pin but lost its stored state, such as a
new device that received pins while linking, asks with `&full=true` for the
whole log, page by page, and checks it against the pin. A log the server does
not return in full is refused.

**Checkpoints.** An admin's device appends a `Checkpoint` entry once 256 entries
have passed since the last one. A checkpoint holds the full state at that point:
the owner, members, admins, every invite with its use count and whether it was
revoked, automod approvals, the time of the last restart, and the latest
removal. Devices that replay the log check that the checkpoint matches the state
they computed and refuse the log if it does not, so an admin cannot use a
checkpoint to change membership. A device that has never seen the log receives
entries from the newest checkpoint onward, starts from its state, and checks
that an admin in that state signed it. Such a device trusts the newest
checkpoint the same way it would otherwise trust the first entry, and devices
that watched the log grow check that it continues it. The server refuses a
checkpoint unless at least 16 entries have followed the previous one.

**Size limits.** A `Genesis`, `Restart`, or `Checkpoint` entry carries the whole
membership and may be up to 1 MB. An entry that adds or removes members may be
up to 256 KB, and any other entry up to 64 KB. Each listed member takes 28
bytes, so apps put at most 30,000 members in a start entry and 8,000 in each
`AddMembers` entry, and admit the rest in further entries. One response carries
up to 8 MB of entries; a device that receives fewer entries than exist continues
from the last one it verified the next time it refreshes the log. Planets being
migrated to another node refuse new entries until the move finishes.

## Planet settings

A planet's **Public** setting, on the Privacy page of its settings, decides who
can join it and who receives its keys:

| Setting | Encryption mode | Who can join | Who receives keys |
| --- | --- | --- | --- |
| Public | Open | Anyone | Anyone the planet's permissions let view a channel |
| Private | Invite-only | People with an invite link | Members the membership log admits |

The invite page shows a private planet to someone who holds one of its invite
codes. A vanity invite link only works for a public planet, so making a planet
private turns it off. The owner also chooses whether new members can read
earlier messages (see [History](#history)).

Only the planet's owner changes whether it is public, because each change
carries a membership log entry the owner's device signs.
`E2eeService.SetPlanetPrivacyAsync` signs it and sends it to
`PUT api/planets/{id}/privacy`, which refuses anyone but the owner. A general
planet update (`PUT api/planets/{id}`) cannot change `Public`, for the owner
either. [`PlanetEncryptionService`](../Valour/Server/Services/E2ee/PlanetEncryptionService.cs)
appends the entry in the same transaction that changes the planet and never
stores a planet that is both public and invite-only. Imported snapshots, and
migrations that restore a moved planet's visibility, keep an invite-only planet
private. The app shows other admins that only the owner can change the setting,
and asks the owner to verify their device first when it is not ready.

### Making a planet private

The owner's device signs a `Genesis` entry, or a `Restart` when the planet was
private before, admitting every current member who has set up encryption.
Members who have not set up encryption wait to be admitted. The device then
replaces keys in the channels it can see, so later messages reach only admitted
members. Changing whether new members can read history in a private planet also
replaces keys, carrying the new setting in the key records.

### Making a planet public again

The owner's device signs an `Open` entry. Only the log's owner can sign it, with
the keys the log admitted as the owner's, so neither the server nor an account
it controls can make one, including after a key reset nobody confirmed. The
entry clears the members, admins, and invites but keeps the owner; the log then
admits nobody and accepts only the owner's `Restart` or `TransferOwnership`. Members' devices treat the planet as open once
they verify the entry (see [Who receives keys](#who-receives-keys)). Keys are
not replaced, since everyone who holds them could already read those messages.
Anyone who joins afterwards can read new messages, and earlier ones too if new
members can read history.

Only the log's owner can make the planet private again, with a `Restart` signed
by the keys the log names. An account the server names as the planet's owner
therefore cannot restart the log and admit itself to later messages. When
ownership moves while the planet is public, the owner's device signs a
`TransferOwnership` entry handing the log to the new owner's current keys, and
the server refuses the transfer without it.

An owner who starts their encryption over while the planet is public no longer
holds the keys the log names, and an opened log has no admins left to confirm
new ones. The planet can stay public, but it can no longer be made private again
under that log. Nobody can sign a `TransferOwnership` entry either, so the
planet moves to a new owner without one in this case only: the server checks
that the log is open and that the current owner's keys, as their key log shows
them, are not the ones the log names. The log keeps naming the earlier keys, so
the new owner cannot make the planet private either. The Privacy settings
explain this to the owner, and to a later owner, instead of offering the change
(`E2eeService.GetMakePrivateBlockerAsync`).

A device that saw the log opened but not a later restart relies on the server to
return the restart, as a device that never saw the log relies on it for the
first entry. Key records made after the restart name its entry, so once the
device loads one of them it stops treating the planet as open, even if the
server withholds the restart.

### Private planets the owner has not signed yet

A planet whose Public setting is off but whose membership log the owner's device
has not signed is still open. Planets created as private start this way, and so
does every planet that was private before encryption, because the server cannot
sign for the owner. `E2eeService.IsPrivacyPending` reports this state.

The app signs the log right after the owner creates a private planet on a
verified device. Otherwise, when the owner opens such a planet on a verified
device, the app asks them once per session to finish making it private, and the
Privacy settings show the same prompt. Finishing
(`E2eeService.FinishPrivatePlanetAsync`) is the same as making the planet
private: every current member who has set up encryption is admitted, so nobody
loses access. Other members see no prompt.

## Community nodes

Planets on community nodes are encrypted like planets on the hub. Key logs stay
on the hub, where users change them. A node keeps a verified copy of the key
logs it needs so it can check message signatures and membership logs: when its
copy of a user's log is more than a minute old, it asks the hub's
node-authenticated `api/federation/e2ee/key-logs` endpoint for newer entries and
stores them only if they extend the log it already accepted. It asks about up to
500 users per request and makes as many requests as it needs. If the hub is
unreachable, the node uses its copies and waits before asking again, from
5 seconds after the first failure up to 5 minutes after repeated ones, so an
outage does not slow every request. Nodes refuse key log changes, and clients
always read and change key logs through the hub. Key logs hold only public keys
and device names, and any signed-in user can read any account's log through
`api/e2ee/users/logs`, so the node endpoint returns the logs of any users a
verified node asks about.

The hub and nodes must speak the same federation protocol version, which is 6.
They refuse each other's credentials when the versions differ, and the hub logs
which node must be updated.

A node reports the client protocol it speaks in its instance manifest. Clients
check it before sending in a node's planets, so a node that has not been
upgraded for encryption gives a clear error instead of failing to load keys. See
[upgrading for end-to-end encryption](Federation.md#upgrading-for-end-to-end-encryption)
for the order in which to upgrade the hub and nodes.

A node publishes its own server attestation keys. Clients resolve a sealed
message's key from the node that serves the channel, and a community node may
only send encryption events for planets it hosts.

### Moving a planet

A planet moved between the hub and a community node keeps its channel IDs,
because messages and keys are signed over the planet and channel. It also keeps
its automod trigger IDs, because an invite-only planet's membership log approves
triggers by ID. The import fails if a channel ID or trigger ID is already taken
at the destination. The
[snapshot](../Valour/Shared/Models/PlanetSnapshot.cs) carries each message's
envelope and search terms, the channel key generations and members' sealed
boxes, the planet's membership log, automod term hashes, and the source server's
public attestation keys. A server-created key that no member holds yet travels
in the authenticated migration and is protected again by the destination. Key
requests and proofs of edited or deleted messages are deleted at the source and
do not move. Chat caches for the moved channels are cleared on both sides. An
invite-only planet arrives private even when its snapshot says it is public.

## Configuration and rollout

The `E2ee` section of the server configuration maps to
[`E2eeConfig`](../Config/Configs/E2eeConfig.cs):

| Setting | Default | Meaning |
| --- | --- | --- |
| `ProofRetentionDays` | 180 | Days to keep proofs of edited and deleted messages (at least 1) |
| `SealLegacyMessages` | false | Seal plain-text history written before encryption |
| `LegacySealBatchSize` | 500 | Messages sealed per batch, kept between 10 and 5,000 |
| `LegacySealStartDelaySeconds` | 120 | Seconds after startup before sealing begins |
| `HeldKeyMinimumHolders` | 3 | Members who must hold a server-created key before the server deletes its copy |
| `LargePlanetMembers` | 1000 | Open planets at least this large replace keys on a schedule |
| `LargePlanetRotationDays` | 7 | Days after its creation that a key in a large open planet may stay in use once rotation is due (at least 1) |

### Data Protection KEK

Every server needs a key encryption key (KEK) for ASP.NET Data Protection. The
server protects two kinds of secrets with Data Protection: its attestation
signing key, which vouches for server-sealed messages, and channel keys it
creates (for webhook, automod, system, and welcome messages, and for sealed
history) until enough members hold them (see
[Server-created first keys](#server-created-first-keys)). Without a KEK, the
Data Protection key ring is stored in the same database, so a copy of the
database would open those channel keys and the attestation key.

Configure `DataProtection:Kek` (a base64-encoded 32-byte key) or
`DataProtection:KekFile`, the same on every instance that shares a database.
Keep it stable, and back it up apart from database backups; changing or losing
it makes the protected keys unreadable. A server refuses to start without a KEK
when its host environment is `Production`, when `SealLegacyMessages` is on, or
when a federation role is enabled. In other cases it logs a warning and starts.
The provided `docker-compose.yml` runs in `Production` and refuses to start
until `DATAPROTECTION__KEK` is set in `.env`. The
[deployment guide](Deployment/README.md#data-protection-kek) explains how to
create one.

### Migrations

Migrations run in this order:

| Migration | What it does |
| --- | --- |
| `AddMessageChannelIdIdIndex` | Replaces the index of messages by channel with one on channel and ID, which history pages read in order. The old index is dropped only once the new one is valid |
| `EndToEndEncryption` | Adds the encryption tables and the encryption columns on `planets` and `channels` |
| `MessageEncryptionColumns` | Adds the encryption columns on `messages`: the encryption version, envelope, key generation, and search terms |
| `MessageSearchTermsIndex` | Builds the GIN index on search terms that encrypted search and automod use |
| `LegacyPlaintextIndex` | Builds the partial index of plain-text messages left to seal |
| `NotificationChannelIndex` | Builds the index of notifications by channel |
| `UnindexedSealedMessagesIndex` | Builds the partial index of server-sealed messages without search terms, which `api/e2ee/channels/{id}/unindexed` reads |
| `AutomodTermsPlanetIndex` | Adds the index of encrypted automod terms by planet |
| `DeviceLinkApproval` | Adds the columns device linking uses to relay the approving device's proof and pins |
| `PrivatePlanetsAreNotPublic` | Makes every planet that is both public and invite-only private and turns off its vanity invite, since only the owner's signed entry makes such a planet public. It deletes the invite codes of planets that are not public and not yet invite-only, because those planets had invites turned off |

Adding a column needs a brief exclusive lock on its table. The lock request
waits behind any long-running query, and every later query on the table waits
behind the lock request. `EndToEndEncryption`, `MessageEncryptionColumns`, and
`PrivatePlanetsAreNotPublic` therefore set a lock timeout of five seconds, so other queries wait at most that long. If the lock is not
granted in time, the server fails to start without having changed anything, and
the migration runs again on the next start. `MessageEncryptionColumns` runs in a
transaction of its own, so its lock on `messages` is never held together with
the locks on `planets` and `channels`, and it keeps columns that already exist,
so it is safe to run again.

The indexes on large tables are built with `CREATE INDEX CONCURRENTLY`, which
does not block writes but cannot run in a transaction, so each is in a migration
of its own. A build that is interrupted leaves an invalid index; running the
migration again removes it and builds it again, and a valid index is kept. A
concurrent build waits for every transaction that was open when it started, so a
long-running query or an idle open transaction delays it. The server allows
migrations up to 30 minutes per command, so the first start after upgrading a
large database can take a while. The server answers health checks only after
migrating, so a deployment must wait for it. The supplied
[deployment script](Deployment/README.md#bluegreen-deployment) waits up to
1,350 checks, two seconds apart, and stops early only if the container exits.
On a large `messages` table, the indexes can also be built ahead of the upgrade
by running each migration's `CREATE INDEX CONCURRENTLY IF NOT EXISTS` statement
with `psql`; the migrations then keep the valid indexes they find.

Only one server applies migrations at a time. Before migrating, a server takes a
PostgreSQL advisory lock, asking for it every two seconds and logging that it is
waiting while another server holds it. Without the lock, a second server could
take the first one's unfinished concurrent index for an invalid one and drop it.
Several instances can therefore start together, and the later ones wait until
the first has finished.

Reverting `MessageEncryptionColumns` drops the envelope and search term columns,
which destroys every encrypted and sealed message. Reverting `EndToEndEncryption`
drops the key logs, channel keys, membership logs, proofs, and report evidence,
so the messages that remain can no longer be read. Restore a backup instead of
reverting them.

### Clients

The instance manifest's `MinimumClientProtocol` tells clients the oldest client
this server works with. A client whose `InstanceManifest.CurrentClientProtocol`
is lower shows a notice that cannot be dismissed, asking the person to update.
Clients built before the manifest had this field see the server's
`E2EE_REQUIRED` error when they try to send.

### Sealing history

New messages are encrypted from the first start. Messages written before the
upgrade stay plain text until an operator sets `E2ee:SealLegacyMessages` to
true. Sealing cannot be undone: the plain text is removed from the database and
exists only in the sealed copies. Before turning it on:

1. Let the upgrade run for a while and confirm that people can send and read
   messages and that no rollback to an earlier release is needed. An earlier
   release cannot read sealed history, so rolling back after sealing leaves it
   blank.
2. Take a database backup. It is the only copy of the plain text if something
   goes wrong.
3. Configure a Data Protection KEK (see above). Sealing creates a key for every
   channel with history, and a server with sealing turned on and no KEK refuses
   to start in any environment.

Sealing rewrites every old message row in batches of `LegacySealBatchSize`, one
`UPDATE` statement per batch, so the `messages` table and its indexes grow until
autovacuum reclaims the old row versions. Autovacuum usually keeps up. If the
table stays large after sealing finishes, run `VACUUM (ANALYZE) messages`. Index
bloat is not reclaimed by vacuum; if the indexes on `messages` stay much larger
than before, rebuild them one at a time with `REINDEX INDEX CONCURRENTLY`, which
does not block writes.

### Background cleanup

Every six hours, the maintenance worker on each server removes message proofs
older than `ProofRetentionDays` (except proofs of reported messages), link
sessions that expired more than an hour ago, and key requests older than 30
days.

### Rate limits

Two policies in
[`RateLimitPolicies`](../Valour/Server/Utilities/RateLimitPolicies.cs) limit the
encryption routes per account:

| Policy | Limit | Routes |
| --- | --- | --- |
| `e2ee` | 300 per minute | Key requests, new key generations, key candidates, planet member lists, encrypted search, key sharing, key log and user key box changes, and membership log entries |
| `e2ee-read` | 1,200 per minute | Channel keys, older key records, key holders, key logs, membership logs, direct chat key requests, sealed-history indexing, automod term work and uploads, removing the caller's own boxes, and the server's attestation keys |

Fetching this account's own user key box, creating a link session, and joining
one use the login policy (`auth`, 10 per minute per address), since they are
steps of signing in a device. Viewing, approving, and denying link sessions and
changing whether a planet is public have no rate limit of their own.

The limiter runs before authentication. It counts a request under its account
once it has looked up the account behind the token, which it does in the
background the first time it sees a token; until then the request counts under
the token. Requests without a token count under the client's address.
`RateLimiting:Disabled` turns these limits off along with the others.

### Server caches

The server checks whether a channel's key must be rotated for every message
sent, so each server process keeps the result for each channel. The result is
reused until the channel's newest generation, the planet's permissions or
membership, a direct or group chat's members, the channel's membership log, or
the process's count of replaced user keys changes, and for at most a minute.
That count rises whenever the process stores a key log entry that replaces a
user key (a reset, a device removal, or a user key rotation), whether a user
appended it there or a community node copied it from the hub. Other processes
sharing the database do not see the count change, so after a device is removed
through one server, another can take up to a minute to require rotation. Senders
also check for removed devices themselves (see [Rotation](#rotation)).

The server's other encryption caches, such as verified key logs and membership
logs, are bounded in size and can always be rebuilt from the database.

## API routes

Most routes are in [`E2eeApi.cs`](../Valour/Server/Api/Dynamic/E2eeApi.cs). The
privacy and ownership routes are in
[`PlanetApi.cs`](../Valour/Server/Api/Dynamic/PlanetApi.cs), live embed updates
are in [`EmbedAPI.cs`](../Valour/Server/Api/EmbedAPI.cs), and the key log route
for community nodes is in
[`FederationApi.cs`](../Valour/Server/Api/Dynamic/FederationApi.cs).
Routes under `api/e2ee/channels/{id}` take a `planetId` query parameter for
planet channels, which the server uses to find the channel in its planet.

| Route | Purpose |
| --- | --- |
| `GET api/e2ee/users/{userId}/log?known=` | A user's key log after the first `known` entries |
| `POST api/e2ee/users/logs` | Several users' key logs, up to 500 per request |
| `POST api/e2ee/users/me/log` | Append to this account's key log, with user key boxes |
| `GET api/e2ee/users/me/boxes/{recipientId}?generation=` | A user key box for this account's device or recovery key |
| `POST api/e2ee/users/me/boxes` | Store user key boxes |
| `POST api/e2ee/link-sessions` and `GET .../pending`, `GET .../{id}`, `POST .../{id}/join`, `.../approve`, `.../deny` | Device linking |
| `GET api/e2ee/server-keys` | The server's public attestation keys |
| `GET api/e2ee/channels/{id}/keys` | Policy, newest generation, records, and this member's boxes |
| `POST api/e2ee/channels/{id}/generations` | Publish a new generation with boxes |
| `GET api/e2ee/channels/{id}/generations?from=&to=` | Older records, up to 200 |
| `POST api/e2ee/channels/{id}/boxes` | Share boxes, up to 200 per request |
| `POST api/e2ee/channels/{id}/boxes/prune?through=&floor=` | Remove the caller's superseded boxes |
| `DELETE api/e2ee/channels/{id}/boxes/{generation}` | Remove a box the caller could not open |
| `GET api/e2ee/channels/{id}/recipients` | Members waiting for keys, and the key holders |
| `GET api/e2ee/channels/{id}/key-candidates?limit=` | Members to seal a new key to, up to 1,000 |
| `POST api/e2ee/channels/{id}/key-requests` | Ask for the channel's keys |
| `GET api/e2ee/key-requests` | Requests in direct and group chats where the caller holds keys |
| `POST api/e2ee/channels/{id}/search` | Encrypted search |
| `GET api/e2ee/channels/{id}/unindexed?count=` and `POST .../terms` | Index server-sealed messages |
| `GET api/e2ee/access-logs/{scope}/{id}?known=&full=` and `POST` | Read or append to a membership log |
| `PUT api/planets/{id}/privacy` | Make a planet public or private, with the owner's signed membership log entry, and set its history setting |
| `POST api/planets/{id}/transfer-ownership` | Hand a planet to a new owner, with the owner's signed membership log entry when the planet has a log |
| `POST api/embed/update` | Send an encrypted live embed update |
| `POST api/federation/e2ee/key-logs` | Community nodes fetch users' key logs from the hub, authenticated as a node |
| `GET api/e2ee/planets/{planetId}/member-ids` | Planet members, for admitting them to the log |
| `GET api/e2ee/planets/{planetId}/automod/work` and `POST .../automod/terms` | Compute and upload automod term hashes |

## SDK usage

`E2eeService.KeyStore` holds the device's private keys and pins. Set it before
login:

| Host | Store |
| --- | --- |
| Browser | `AppStorageE2eeKeyStore` in local storage, which also holds the login token |
| MAUI apps | `SecureE2eeKeyStore` in the platform's secure storage |
| Bots and other .NET programs | `FileE2eeKeyStore` in `.valour-e2ee` in the working directory |
| Tests | `MemoryE2eeKeyStore` |

In the browser, the keys are lost if the site's data is cleared, for example by
the person or by a browser that clears storage for sites it has not seen
recently. Once a device is verified, the web app asks the browser to keep the
site's storage (`navigator.storage.persist()`), so it is not cleared to free
space. When that browser has been the account's only device for three days, the
app also tells the person once that clearing its data would leave only their
recovery code, and suggests saving the code or signing in to a native app as
well.

On Linux and macOS, `FileE2eeKeyStore` makes its directory readable only by its
owner (mode 0700) and each key file too (mode 0600). On every platform it writes
each file through a uniquely named temporary file that it flushes to disk before
renaming. The directory must survive restarts; a bot in a container keeps it on
a volume. `DeviceName` sets the name other devices see for this one.

**Setup and recovery.** `AutoSetUp` sets up an account without keys at login.
`StoreRecoveryCode` keeps the recovery code the service creates in the key store
(read it with `GetStoredRecoveryCodeAsync`). When the device has no working
keys, `AutoRecover` restores them with the stored recovery code or with
`RecoveryCode`, which defaults to the `VALOUR_E2EE_RECOVERY_CODE` environment
variable. `AutoSetUp`, `AutoRecover`, and `StoreRecoveryCode` all default to
true. The service resets the keys without asking only when `AllowAutomaticReset`
is true, which it is not by default, and only for a definite cause: no stored
device key and no working recovery code, or a stored device the key log no
longer lists as active. A failure to reach the server never causes a reset. When
an account with keys finds no device key in a `FileE2eeKeyStore`, the service
logs an error that names the directory. Apps set `AutoRecover` and
`StoreRecoveryCode` to false and guide the person instead, using
`RestoreWithRecoveryCodeAsync`, `CreateNewRecoveryCodeAsync`, `ResetKeysAsync`,
`RevokeDeviceAsync`, and the linking methods (`StartLinkingAsync`,
`JoinLinkAsync`, `ShowLinkCodeAsync`, `MatchScannedLinkCodeAsync`,
`ApproveLinkAsync`).

**Initializing and signing out.** `ValourClient.InitializeUser` and
`BotService.InitializeBot` load the account and initialize encryption; a
program that logs in with `AuthService.LoginAsync` directly calls
`E2eeService.InitializeAsync` itself. `AuthService.LogoutAsync` does not remove
the device from the key log. A program that wants to sign the device out of
encryption calls `E2eeService.SignOutDeviceAsync` before logging out.

**Messages.** `Message.PostAsync` and `UpdateAsync` encrypt on the device and
return the cached message. Messages from the SDK's services are decrypted before
they reach events. Check `Message.DecryptionState`:

| State | Meaning |
| --- | --- |
| `NotAttempted` | The message is plain text, or decryption has not run yet |
| `Decrypted` | The message was decrypted and its signature and commitment checked |
| `WaitingForKey` | Another member has not shared the key yet, or something it needs could not be loaded yet |
| `DeviceNotVerified` | This device is not set up, so it cannot read encrypted messages |
| `Invalid` | The message failed verification and is not shown |

`DecryptionChanged` fires on a message when a waiting message becomes readable,
and `MessageService.MessageDecrypted` fires once for each message or edit that
arrived unreadable and became readable, so a bot that reacts to text handles
both `MessageReceived` and `MessageDecrypted`. The SDK tracks waiting messages
by ID in the model cache, so they are updated even after the object first
received is gone. `RetryUnverifiedMessagesAsync` decrypts cached messages again
after a device becomes verified.

**Other operations.** `SearchAsync` searches a channel, `IndexSealedHistoryAsync`
indexes its server-sealed messages (see
[Indexing server-sealed messages](#indexing-server-sealed-messages)), and
`RequestKeysAsync` asks online members for a channel's keys, and
`PrepareToSendAsync` loads a channel's keys when it opens and asks for the
current one if this device does not hold it. `StatusChanged`,
`ChannelKeysChanged`, `RecoveryCodeChanged`, `ContactWarning`,
`KeysResetElsewhereDetected`, `LinkSessionUpdated`, `LinkFailed`, and
`TamperedMessageDetected` report changes an app may show.

**Performance.** Decrypting a page of history loads the authors' key logs in one
request and yields between messages so a browser stays responsive. Search terms
are computed only up to the 400-term limit, and repeated words are hashed once.
A failed request for a channel's keys or a server's attestation keys is not
repeated for 10 seconds, then 20, then 30.

## Tests

- [`E2eeCoreTests`](../Valour/Tests/Sdk/E2ee/E2eeCoreTests.cs) cover the
  cryptographic formats, device link codes and approval MACs, server-created
  keys, embed update encryption and key checks, key logs and key IDs,
  membership logs, pin merging, search folding, the separate key for sealed
  messages' terms, attachment and link preview binding, the rules for sealed
  history, and markdown escaping without a server.
  [`PlanetPrivacyLogTests`](../Valour/Tests/Sdk/E2ee/PlanetPrivacyLogTests.cs)
  cover the `Open` entry and the owner's restart and ownership transfer that
  can follow it.
  [`DeviceRemovalCutoffTests`](../Valour/Tests/Sdk/E2ee/DeviceRemovalCutoffTests.cs)
  cover removed devices' cutoffs: backdated entries after a cutoff, removals
  without a cutoff for a log, starting over without undoing earlier entries,
  checkpoints that cannot start a log, and the key log formats.
- [`PlanetPrivacyLiveTests`](../Valour/Tests/Apis/PlanetPrivacyLiveTests.cs)
  make planets private, public, and private again under a new owner, finish
  planets that are private but not signed yet, check that only the owner's
  signed entry changes a planet's privacy or hands its log to a new owner
  (except after the owner started over while it was public), and check that
  admins' invite links admit people right away, copy again only on the device
  that made them, are revoked and forgotten when deleted, and that plain links
  wait for an admin. They also check that a linked device receives a private
  planet's log pin, and that removing a device records the log's end and stops
  the server from taking entries signed with its keys.
- [`E2eeLiveTests`](../Valour/Tests/Apis/E2eeLiveTests.cs) run several SDK
  clients against the real server and check stored data, including device
  linking in both directions, sealing history, moving a planet between nodes,
  scheduled rotation in large planets, pruned boxes and on-demand records,
  reused keys, stale recipients, and starting from a checkpoint. Other server
  tests post messages through [`EncryptedChat`](../Valour/Tests/EncryptedChat.cs).
  Follow the [isolated test instructions](../Valour/Tests/Browser/README.md#isolated-c-regression).
- [`e2ee-device-linking.mjs`](../Valour/Tests/Browser/e2ee-device-linking.mjs)
  drives first-login setup, an encrypted direct message, and device linking with
  a typed code in real browsers. See the
  [browser test guide](../Valour/Tests/Browser/README.md#end-to-end-encryption).
