# Channel Activity Notifications

## Problem

Users report two related problems:

1. They don't know when channels are active — a channel can be having a great
   conversation and nothing in the UI pulls anyone in.
2. Outside of pings (mentions, replies, role pings), Valour generates almost no
   notifications. The platform is *too quiet*. A member who isn't being directly
   mentioned can go days without a reason to open the app.

The fix cannot be "notify on every message." Discord's small-server default is
exactly that ("All Messages" is the default notification setting on
non-community servers), and the result is the universal mute reflex — users
bulk-mute servers and channels until the product is quiet again, which is
worse than where we started. Slack avoids this by defaulting to
mentions/DMs/keywords only, but that recreates our current problem: quiet
platforms feel dead.

## Core model: budget + allocation

We split the design into two independent mechanisms:

- **Budget** — a structural cap on how many activity notifications a user can
  receive. At most one notification per channel per cooldown period, plus a
  short global gap between any two activity notifications regardless of
  channel. Volume is bounded *by construction*; no configuration mistake or
  hyperactive community can turn the feature into a firehose.
- **Allocation** — when more activity is happening than the budget allows
  through, an **interest ranking** decides which channels win the limited
  slots. The user hears about the channels they demonstrably care about most.

This makes it safe to be *loud by default*. Because the ceiling is structural,
the default can lean toward "informed" rather than "silent" without
recreating Discord's failure mode. Per-message notification delivery does not
exist anywhere in this design, at any setting.

## Interest signals

Interest is inferred, not declared. All v1 signals already exist in the schema:

| Signal | Source | Weight |
|---|---|---|
| Recently viewed the channel | `UserChannelState.LastViewedTime` | Base signal. More recent = higher interest. |
| Favorited the channel | `ChannelFavorite` | Strong boost — explicit intent. Favoriting doubles as "follow". |
| Recently spoke in the channel | recent chatters | Boost (v1.1) — you talk there, you care about replies. |

The interest score maps to a **cooldown multiplier** rather than a separate
scheduler: high-interest channels notify at the base cadence, low-interest
channels at a stretched cadence, and channels below the floor don't notify at
all.

v1 bands:

| Band | Effective cooldown |
|---|---|
| Favorited, or viewed within 24h | 1× base |
| Viewed within 7 days | 2× base |
| Viewed within 14 days | 4× base |
| Older / never viewed, not favorited | No activity notifications |

Known weakness: recency ranking is a feedback loop (notified → viewed →
re-ranked to top). Favorites and authorship dampen it. A v2 negative-feedback
rule (repeatedly ignored channel → decayed score) is planned but not blocking.

Cold start: a user with no view history in a planet gets no activity
notifications from it. v1 accepts this (they still get mentions); a v2
fallback can rank by planet-wide channel popularity for new members.

## Activity detection: buckets, not messages

Per-channel rolling window (Redis, ~5 minutes) tracking **message count** and
**distinct author count**. A channel is *active* when it crosses a threshold —
distinct authors matter more than raw messages (12 messages from one person is
a monologue; 5 messages from 3 people is a conversation).

Threshold crossing enqueues one evaluation per channel per evaluation-debounce
period (~60s) onto a background worker. The message hot path only increments
Redis counters; candidate resolution, ranking, and delivery all happen off the
hot path (same pattern as role-mention push batching).

Two notification flavors from the same bucket data:

- **Conversation start** — the channel was quiet (no activity for ≥30 min) and
  just crossed the threshold: *"💬 #dev is picking up"*.
- **Ongoing activity** — the channel stays active past a user's cooldown:
  *"14 messages from 4 people in #dev"*.

Inbox entries **coalesce**: one unread activity notification per channel,
updated in place as the burst grows. Never a stack of entries per channel.

## Delegation of defaults (the intentional version of Discord's model)

Planet owners know their community's temperament, so they set the default
cadence — but the unit they control is *how often*, never *whether every
message notifies*:

| Cadence | Base per-channel cooldown | Intent |
|---|---|---|
| Off | — | No activity notifications by default |
| Quiet | 60 min | Low-touch communities |
| Standard (default) | 15 min | Most planets |
| Lively | 5 min | High-energy communities |

Members can override with their own cooldown preference. Precedence, most
specific wins:

1. **User per-channel setting** — "Activity alerts: Auto / Off" on the
   channel (stored on `UserChannelState`). Off is a hard mute for activity
   notifications only; mentions still behave normally.
2. **User per-planet setting** — same Auto/Off toggle at the planet level
   (stored in `user_planet_settings`), in the planet context menu.
3. **User global cooldown preference** — overrides every planet's cadence.
4. **Planet cadence** — owner's default, applies when the user has set nothing.
5. **Platform default** — Standard.

Additionally the whole feature is a `NotificationSource` (`ChannelActivity`)
in the existing global preference mask, so one toggle in notification settings
kills it everywhere. It defaults **on** — including for existing users (the
rollout migration flips the bit on for already-initialized preference masks).

## Anti-annoyance rules (non-negotiable)

- **Never notify a user who is looking at the channel** (existing
  `ChannelWatchingService` suppression, same as push today), and never notify
  a user whose read state advanced during the current burst window — advancing
  read state means they saw the messages, even if their watch lease lapsed.
- **The cooldown is a hard TTL budget.** Viewing the channel marks the
  coalesced notification read, but never resets the cooldown: read-state
  updates fire constantly while a client has a channel open, and resetting
  from them degrades "once per cooldown period" into "once per evaluation"
  (this shipped briefly and manifested as grouped push spam).
- **OS push fires only when a new inbox entry is created.** Coalescing works
  in the in-app inbox (entry updated in place), but each push renders as a
  separate entry in the OS notification shade — so updates to an existing
  unread entry are relayed in-app only, never re-pushed. At most one shade
  entry per channel until the user reads it.
- **Global gap** (~60s) between any two activity notifications for a user, so
  simultaneous bursts across channels can't stack. When channels compete,
  higher interest wins the slot (in practice: lower effective cooldown wins).
- **Bounded fan-out**: candidate users per evaluation capped (top N by
  `LastViewedTime`).
- Activity notifications are always visually distinct from mentions — they use
  the normal notification inbox and (optional) push, but never the red mention
  badge. The mention badge stays sacred.

## What this is not

- Not per-message notifications — the option does not exist.
- Not a replacement for mentions/pings — those paths are untouched.
- Not engagement-bait: no notification fires for a channel the user has never
  shown interest in, and every knob a planet owner has is bounded by the
  user's own budget.

## Phasing

1. **v1 (this doc)**: bucketing, interest-banded cooldowns, conversation-start
   + ongoing flavors, coalesced inbox, planet cadence, per-channel and
   per-planet off switches, global source toggle + cooldown preference.
2. **v1.1**: authorship boost in ranking; sidebar ambient liveness (avatar
   stack / glow on active channels, reusing `ChannelWatchingService` counts —
   passive awareness with zero notification cost); push shade entry updated
   in place via notification tag replacement.
3. **v2**: ignored-channel decay, cold-start popularity fallback, quiet hours,
   keyword highlights, digest for lapsed users.

## Implementation map

- `NotificationSource.ChannelActivity = 0x20000` (+ preference-mask backfill
  migration for initialized masks).
- `Planet.ActivityNotificationCadence` (enum column).
- `UserChannelState.ActivityAlerts` (Auto/Off) — per-channel override rides the
  existing read-state row.
- `user_planet_settings` (user, planet, activity_alerts) — per-planet override;
  deliberately not on `PlanetMember`, which is synced to other members.
- `UserPreferences.ActivityCooldownSeconds` (nullable → inherit planet).
- `ChannelActivityService` (server): Redis buckets + gating + candidate
  resolution. `ChannelActivityWorker`: queue consumer, calls
  `NotificationService.SendChannelActivityNotificationsAsync` (coalescing).
- Hook: `MessageService.PostMessageAsync` bumps the bucket.
- Client: notification-settings toggle + cooldown preset, planet settings
  cadence dropdown, channel + planet context-menu "Activity alerts" toggles.
