# Direct and group calls

Direct calls belong to private conversations. `DirectCall` records the call attempt
and `DirectCallMember` records each participant's state. Audio and video use the
instance provider selected by `VoiceCoordinator`: Cloudflare RealtimeKit or
LiveKit. A planet's voice configuration does not apply to private calls.

## Conversations and participants

`DirectChat` is a two-person conversation. `GroupChat` has a name and supports
three through 25 members. `ChannelMember.IsAdmin` controls group renaming,
invitations, and removal of other members.

Adding someone during a two-person call creates a separate group conversation.
The call retains its ID and provider room, and its `ChannelId` points to that
group. The added person does not receive the original direct-message history.

## Lifecycle

A call has Ringing, Active, or Ended state. Each participant has Invited, Joined,
Declined, or Left state. The caller starts Joined, and the first acceptance makes
the call Active. Ringing invitations expire after 45 seconds. A background worker
records missed calls and closes expired provider rooms.

Users can have only one ringing or active call, including planet voice presence.
The service checks blocks and the user's call privacy preference, which is separate
from direct-message privacy and defaults to FriendsOnly.

Updates reach each user's primary-node SignalR group through the inter-node relay.
Participants can therefore receive ringing and membership updates while connected
to different application nodes.

## Media credentials and cleanup

Provider rooms are keyed by `DirectCall.Id`. A temporary in-memory channel model
selects the provider's audio/video preset. Joined membership is validated before
each token issuance, and direct-call LiveKit tokens expire after five minutes.
RealtimeKit cleanup removes both the active peer and its meeting participant
record so its reusable credential cannot reconnect.

The direct-call worker owns call expiry and room teardown. Planet voice cleanup
skips active direct-call rooms, whose presence does not use planet voice Redis
keys. RealtimeKit orphan cleanup also preserves explicitly tracked rooms.

## HTTP routes

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `api/direct-calls` | Start a voice or video call |
| GET | `api/direct-calls/current` | Restore the user's current calls |
| GET | `api/direct-calls/{callId}` | Read a call |
| POST | `api/direct-calls/{callId}/accept` | Accept an invitation |
| POST | `api/direct-calls/{callId}/decline` | Decline an invitation |
| POST | `api/direct-calls/{callId}/leave` | Leave a call |
| POST | `api/direct-calls/{callId}/end` | End a call |
| POST | `api/direct-calls/{callId}/participants` | Invite participants |
| POST | `api/direct-calls/{callId}/token` | Issue media credentials |
| POST | `api/channels/group` | Create a group conversation |
| POST | `api/channels/group/{id}/members` | Add group members |
| PUT | `api/channels/group/{id}` | Rename a group |
| DELETE | `api/channels/group/{id}/members/{userId}` | Leave or remove a member |

## Verification

`LiveKitTokenTests` checks token grants and lifetime. `RealtimeKitReconciliationTests`
checks participant credential cleanup without external calls. `DirectCallServiceTests`
and `DirectCallApiTests` cover lifecycle, privacy, authorization, busy users, missed
calls, and participant expansion. Group-conversation tests in `ChannelServiceTests`
cover administration and preservation of the original direct history.

Run database-backed tests with the
[isolated runner](../Valour/Tests/Browser/README.md#isolated-c-regression). Provider
unit tests do not verify real device capture, network traversal, or deployed call
quality; those require calls through the intended provider and devices.
