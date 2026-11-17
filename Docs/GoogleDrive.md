# Google Drive storage

A person can let Valour keep their attachments in their own Google Drive. Drive becomes
the place a file lives. Valour keeps its own copy for a while so files open quickly, then
removes it. That copy is internal: the screens show a file as being in Google Drive and never
mention the cached copy or when it expires. This guide explains how the connection works,
how files move, what happens when something goes wrong, and how to turn the feature on for
a server.

## Turning it on for a server

Google Drive uses the Google OAuth client from `ExternalAuth:Google`, the same one used
for Google sign-in. Two things are needed in the Google Cloud project:

1. Enable the Google Drive API for the project.
2. Add the `https://www.googleapis.com/auth/drive.file` scope to the OAuth consent screen.
   This scope only reaches files and folders the app created, and Google lists it as a
   non-sensitive scope, so it does not need a security review.

The redirect address is the one Google sign-in already uses,
`/api/auth/external/google/callback`. Google requires apps that handle Google user data to
describe that use in their privacy policy, including Google's limited use statement.

The `GoogleDrive` section of the server settings tunes the feature:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Turns the feature off even when Google sign-in is configured. |
| `RetentionDays` | `30` | Days Valour keeps its own copy after a file reaches Drive. |
| `SyncBatchSize` | `20` | Files copied per worker pass. |
| `SyncIntervalSeconds` | `30` | Seconds between worker passes. |
| `AttachGraceMinutes` | `10` | Minutes a new file waits so it can be filed under the planet or conversation it was sent to. |
| `MaxAttempts` | `6` | Failed copies before a file is marked as failed. |
| `EmulatorUrl` | empty | For testing only. See [Testing](#testing). |

`GoogleDriveConfig` is in `Config/Configs/GoogleDriveConfig.cs`. Servers without a Google
client simply do not offer the feature: `DriveConnectionInfo.Available` is false and the
screens hide it.

### Community nodes

Google Drive is not shared across servers. The refresh tokens are stored in the connecting
server's own database, encrypted with that server's keys, and the Google client secret never
leaves the server's configuration. Valour's Google credentials are therefore never given to a
community node. A node operator who wants the feature creates their own Google Cloud project,
sets `ExternalAuth:Google` on the node, and members of that node connect their Drive to the
node. A node with no Google client does not offer it.

The Attachments settings in the app talk to the hub, so they manage the files stored on the
hub. Files uploaded to a community node are limited by that node's own settings (see
[Attachment storage](AttachmentStorage.md#servers-that-set-their-own-limits)). Files on a
planet with its own storage are never copied to Drive.

## Connecting

Connecting is a separate consent from signing in. Signing in with Google asks only for
`openid email profile` and never for Drive. The permission is requested when a person
presses Connect in Settings, under Attachments, or in the out-of-space dialog.

The flow reuses the sign-in machinery in `ExternalAuthService` with the
`ExternalAuthIntent.ConnectDrive` intent, so web, Android, desktop, and Apple clients
already know how to run it. For this intent `GoogleAuthProvider` asks for the Drive scope
with `access_type=offline` and `prompt=select_account consent`, so Google always returns a
refresh token and the person can pick a different Google account than the one they sign in
with. `include_granted_scopes` is off, so the grant is only for Drive.

After the consent page the server:

1. Exchanges the code and checks that Google granted `drive.file`. Google lets people untick
   individual permissions, so a missing scope is an error.
2. Reads the Google account's ID and email.
3. Parks the refresh token in the ticket store for a few minutes. Like every sign-in grant,
   it can only be redeemed with the verifier of the client that started the flow, by the
   same signed-in user.

The client then calls `POST api/attachments/drive/connect` with the ticket and verifier.
`UserDriveService.ConnectAsync` checks that the token works, encrypts it with ASP.NET Data
Protection (purpose `Valour.GoogleDrive.RefreshToken`), and stores a
`user_drive_connections` row. Connecting the same Google account again keeps everything
Valour already put in it. A different account starts fresh.

## How files move

Uploads do not change. `CdnBucketService.Upload` stores the file on Valour's storage after
the usual safety scan. `DriveSyncWorker` then does the rest in the background:

1. **Pick files.** Files uploaded since the connection's `AutoSyncFrom` time, and files a
   person queued by hand, wait for the grace period and then get copied. Choosing Valour
   storage as the default clears `AutoSyncFrom`, so new uploads are left alone.
2. **Choose a folder.** `DriveSyncService.ResolvePlacementAsync` files a file by where it
   was first used:
   `Valour/Planets/<planet name>/<yyyy-MM>/`,
   `Valour/Direct Messages/<names>/<yyyy-MM>/`, or `Valour/Other/` for files that no
   message or thread uses.
3. **Upload.** The file goes up with a resumable upload. Its Drive record carries
   `appProperties` with the SHA-256 hash and the planet ID.
4. **Expire Valour's copy.** The record gets `StorageExpiresAt`, `RetentionDays` from now.
   When it passes, the worker sets `StoragePurgedAt` and deletes the stored object. Stored
   objects are shared by hash, so the object is only deleted when no other record still
   needs it.

### Folders

Valour finds its folders by the IDs saved in `user_drive_folders`, never by name, so a
folder a person renames in Drive keeps receiving files. When a planet is renamed, its
folder is renamed the next time a file goes in. If a person deletes or trashes a folder,
the next upload notices, forgets that folder and everything inside it, and creates it
again. Folder names have control characters removed and are cut to 100 characters.

### Reading a file back

A file whose Valour copy has expired still opens normally. `ContentApi` sees
`StoragePurgedAt`, asks `DriveSyncService.RestoreAsync` to read the file from the owner's
Drive, checks that its SHA-256 hash still matches, puts it back on Valour's storage, and
starts a new expiry. Concurrent requests for one file share a single read from Drive. The
signed address routes return the direct content route for these files, so they take the
same path. The Cleanup backup archive restores files the same way.

If the file cannot be read back, because the connection is gone, the person deleted the
file in Drive, or the Drive copy was changed, the content route answers 404 and readers
see the same "Attachment not found" tile as any deleted file.

## Choosing where new uploads go

While Drive is connected, Settings, under Attachments, lets the person choose where new
uploads go: Google Drive, or Valour storage. Google Drive is the default when connecting.
The choice is `UserDriveConnection.AutoSyncFrom`: a time means uploads from then on go to
Drive, and null means they stay on Valour. Switching back to Drive only covers files
uploaded after the switch, so files kept on Valour in between are not swept up.
`POST api/attachments/drive/default` changes it.

With Valour storage chosen, the account has its allowance and file limit again (the plan's, or
the operator's when the server sets its own), and new files count toward the allowance. Drive stays connected, and files can still be moved by
hand.

## Moving a file by hand

Each row in Attachments settings shows where its file lives (`AttachmentStorageLocation`)
and one button that moves it the other way:

- **Send to Drive** for a file on Valour. `DriveSyncService.SendToDriveAsync` queues the file
  with `DriveReleaseOnSync` set, so the worker copies it and Valour's copy expires at once
  instead of after the retention days. A file that is already in Drive with a cached Valour
  copy loses that copy at the next pass. A file that was moved back earlier and still has its
  Drive copy reuses it instead of uploading again.
- **Move to Valour** for a file in Drive. `DriveSyncService.MoveToValourAsync` reads a
  Drive-only file back, after checking that the plan's allowance has room for it (answering
  413 otherwise), and marks it `Pinned`. A pinned file is never copied or expired
  automatically, and its older Drive copy is left alone.

Both routes check that the file belongs to the signed-in user and is not quarantined.

## Quota and limits

While Drive is the default for new uploads, the account is not turned away for lack of space.
`UserStorageService.GetQuotaBytesAsync` returns no limit, and the per-file limit is at
least 100 MB (`GoogleDriveLimits.ConnectedMaxUploadBytes`), or the plan's limit when that
is higher. Usage counts only files that still have a copy on Valour, so files that live
only in Drive are free. The meter still shows the plan's allowance, which is what the
account returns to if Drive is disconnected.

Clients ask `AttachmentService.GetMaxUploadBytesAsync` for the file limit, which remembers
whether Drive is the default for a few minutes.

## When something goes wrong

| Situation | What happens |
| --- | --- |
| Google revokes or expires the grant | The connection is marked `NeedsReconnect`. New files stay on Valour, nothing is purged, and the account is limited by its plan again. Reconnecting retries failed files. |
| The Drive is full | Files wait and are retried after an hour. The card shows the reason. This does not count as a failed attempt. |
| A temporary error or Google throttling | The file is retried with a delay of 2, 4, 8, and so on minutes, up to six hours, and marked failed after `MaxAttempts`. Valour keeps its copy. A person can retry failed files from the card. |
| A worker stops partway through a file | The file is claimed for 20 minutes. After that another pass takes it. |
| The encryption key ring is lost | The saved token cannot be read, so the connection is marked `NeedsReconnect`. |

Several servers can run the worker at once. A file is claimed with an atomic update, so only
one copies it.

## Deleting and disconnecting

Deleting an attachment or running Cleanup removes the Valour copy and the record. It does
not delete the file in the person's Drive, which is theirs. A Drive copy is also a backup.

Disconnecting revokes the grant with Google and removes the connection and folder records.
Files that still have a Valour copy become ordinary files again. Files that live only in
Drive keep their Drive record, so connecting the same Google account again makes them open
again. Until then they show as not found, and the disconnect dialog warns about it when files are in Drive.

## Safety

Drive does not change what is scanned. Uploads are checked for known abuse material before
they are stored, and quarantined files are never copied to Drive. A file that is
quarantined later is not removed from a Drive it already reached. Attachments are not end
to end encrypted, so the Drive copy is the plain file.

## Routes

All routes are under `api/attachments/drive` and need a signed-in user.

| Route | Purpose |
| --- | --- |
| `GET api/attachments/drive` | `DriveConnectionInfo`: connection, counts, and the last error. |
| `POST api/attachments/drive/connect` | Finish connecting with a ticket and verifier. |
| `DELETE api/attachments/drive` | Disconnect. |
| `POST api/attachments/drive/default` | Choose whether new uploads go to Drive or stay on Valour. |
| `POST api/attachments/{category}/{hash}/to-drive` | Send one file to Drive and remove its Valour copy. |
| `POST api/attachments/{category}/{hash}/to-valour` | Make Valour the home of one file in Drive. |
| `POST api/attachments/drive/copy-existing` | Queue files uploaded before Drive was connected. |
| `POST api/attachments/drive/retry` | Try failed files again. |

`GET api/attachments/storage` also reports `DriveConnected`, `DriveIsDefault`, and
`DriveAvailable`.

## Data

- `user_drive_connections`: one row per connected account, with the encrypted refresh
  token, a `Status` of `Active` or `NeedsReconnect`, and `AutoSyncFrom`.
- `user_drive_folders`: the Drive folder IDs Valour made, keyed by what they hold.
- `cdn_bucket_items`: `DriveState`, `DriveFileId`, `DriveSyncedAt`, `DriveAttempts`,
  `DriveNextAttemptAt`, `DriveError`, `DriveReleaseOnSync`, `StorageExpiresAt`, and
`StoragePurgedAt`. `DriveState` is `None`, `Pending`, `Syncing`, `Synced`, `Failed`, or
`Pinned`.

## Testing

`Valour/Tests/Apis/GoogleDriveStorageTests.cs` runs the sync, expiry, restore, and failure
paths against the real server with an in-memory Drive (`FakeGoogleDriveApi`).

To exercise the real HTTP calls, set `GoogleDrive:EmulatorUrl` to the address of a stand-in
for Google. Every Google address (consent page, token, user info, revoke, and the Drive
files and upload endpoints) is then served from that base address with the same path, as
listed in `GoogleEndpoints`. Never set it in production.
