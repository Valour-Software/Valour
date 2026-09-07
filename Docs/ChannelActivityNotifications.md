# Channel activity notifications

Activity notifications tell members when a channel they follow has an active
conversation. The server measures recent messages, finds eligible members, and
limits how often each member can receive an alert. These notifications appear
in the inbox and can produce push notifications. They do not add mention badges.

## Detecting a conversation

`MessageService` calls `ChannelActivityService.RecordMessageAsync` when a message
is posted. Redis sorted sets track message IDs and each author's most recent
message time in a five-minute window. A channel qualifies when the window contains
at least three messages from at least two authors.

A Redis key limits evaluation to once per channel every 60 seconds. The message
path updates Redis and queues the evaluation; `ChannelActivityWorker` handles
membership lookup, preferences, permissions, and delivery. A separate active key
tracks whether the conversation follows a 30-minute quiet period for logging.
Both starting and continuing conversations use a message preview in their alert.

## Choosing recipients

An evaluation considers up to 500 candidates. It first reads channel view states
from the last 14 days, ordered by most recent view, then adds favorite-only users
while space remains. Favorites qualify without recent view history. A member
with neither a recent view nor a favorite does not qualify.

The service excludes authors in the queued conversation snapshot and the author
of the message chosen for the preview. It also requires planet membership and
channel access. Members watching the channel, or whose read state advanced within
the activity window, are excluded because they have already seen the conversation.

Interest changes the cooldown for each user and channel:

| Interest | Cooldown multiplier |
| --- | --- |
| Favorite, or viewed less than 24 hours ago | 1 |
| Viewed less than seven days ago | 2 |
| Viewed within 14 days | 4 |
| No qualifying view or favorite | No alert |

Each channel evaluation tries to acquire the user's global gap key. The first
evaluation to acquire it gets the available slot. There is no separate scheduler
that ranks competing channels by interest.

## Preferences and frequency

The planet's cadence supplies the base cooldown:

| Cadence | Base cooldown |
| --- | --- |
| Off | No planet default alerts |
| Quiet | 60 minutes |
| Standard | 15 minutes |
| Lively | Five minutes |

A user's `ActivityCooldownSeconds`, when set, replaces the planet cadence,
including an Off cadence. The server clamps this preference to 60 through 86,400
seconds before applying the interest multiplier.

Three independent switches can prevent delivery: the global
`NotificationSource.ChannelActivity` preference, the channel's
`UserChannelState.ActivityAlerts`, and the planet's `user_planet_settings` row.
Either per-channel or per-planet Off setting is a hard mute for activity alerts.
Auto follows the cooldown settings; it does not override another mute.

Redis enforces the resulting per-channel cooldown and a 60-second gap between
activity alerts across all channels for each user. Viewing a channel marks its
activity notification read without clearing either frequency limit.

## Notification content and delivery

The worker looks up recent message IDs from Redis together with the triggering
message ID and selects the newest matching database message. Webhook identity
overrides take precedence over planet nicknames and avatars, followed by the
user's identity. Mention tags become readable names.

A title can read `Alex in Valour Central (+3 others)`. The body normalizes
whitespace and limits the preview to 180 characters. An attachment-only message
uses an attachment count. The click URL opens the selected message.

`NotificationService.SendChannelActivityNotificationsAsync` maintains one unread
activity inbox entry per channel and recipient. Further alerts update that entry
and reach connected clients through SignalR. Push is sent only when a new entry
is created, so updating an unread entry does not add another OS notification.

## Source files

- `Valour/Shared/Models/ChannelActivityPreferences.cs`: thresholds and cooldowns.
- `Valour/Server/Services/ChannelActivityService.cs`: activity tracking and eligibility.
- `Valour/Server/Workers/ChannelActivityWorker.cs`: queued evaluations.
- `Valour/Server/Services/NotificationService.cs`: inbox updates and push delivery.
