# Attachment storage

Every account has an allowance for the files it uploads to Valour's own storage. This
guide describes what counts toward it, how uploads are checked against it, and how the
Cleanup tool lets a person free space safely.

## Allowance

The allowance depends on the account's Stargazer plan. Sizes are binary gigabytes.

| Plan | Allowance |
| --- | --- |
| No plan | 5 GB |
| Stargazer | 50 GB |
| Stargazer Plus | 100 GB |
| Stargazer Pro | 200 GB |

The values live in `UserSubscriptionType.StorageQuotaBytes` in
`Valour/Shared/Models/ISharedUserSubscription.cs`. `UserSubscriptionTypes.GetStorageQuotaBytes`
returns the default for an account with no active plan.

Only files attached to messages and threads count. Profile pictures, planet icons, emoji,
theme assets, and files kept on a planet's own storage are not counted.

Usage is the sum of `SizeBytes` over the account's `cdn_bucket_items` rows. When two people
upload the same file, the bytes are stored once, but each person's row counts for that
person. Uploading a file the account already has costs nothing.

The allowance only limits new uploads. It never deletes anything, and an account over its
allowance, for example after a plan ends, keeps every file it has.

## Checking an upload

The message upload routes (`/upload/image` and `/upload/file`) check the allowance twice.
Before reading the file they refuse the upload when usage already reaches the allowance.
`CdnBucketService.Upload` then checks that the new file fits, after confirming the account
does not already have it.

The check is a soft limit. Uploads that arrive at the same moment can each pass it, so an
account can end slightly over its allowance.

A refused upload returns status 413 with a `UserStorageInfo` body whose `Code` is
`storage_full`. It carries the usage, the allowance, the plan, and `RequestedBytes`. The
upload client reports the status and body as `413: {json}`, and `FileUploadComponent` turns
that into the out-of-space dialog. `GET api/attachments/storage` returns the same
`UserStorageInfo` without an error code.

## Where people see it

- The attach menu in the composer shows a compact meter.
- Settings, then Attachments, shows the meter next to the Cleanup button.
- The out-of-space dialog offers Manage Attachments and, below the largest plan, a link to
  Subscriptions.

The meter turns amber at 80 percent and red when full.

## Cleanup

Cleanup removes many attachments at once. `UserStorageService` implements it, and
`AttachmentApi` exposes it under `api/attachments/cleanup`.

### Choosing files

`AttachmentCleanupCriteria` selects files by scope (all files, files in chosen planets and
direct messages, or files that no message or thread uses), age, content type, and minimum
size. `AsOf` fixes the moment the criteria were built, so files uploaded while a cleanup is
running are never included. Files held for safety review are never included and cannot be
deleted, because they must be preserved.

`cleanup/places` lists the planets and direct messages that hold files, and
`cleanup/preview` counts what the criteria currently match.

### Backup and deletion

1. `POST cleanup/archive` creates a ticket for a zip of the matching files and returns an
   unguessable id. The ticket is kept in memory for 30 minutes.
2. `GET cleanup/archive/{id}` streams the zip. A browser download cannot send a token
   header, so the id is the credential. It works once. Each file is stored without
   compression, and a `manifest.json` lists every file and whether it was included. A file
   whose stored object cannot be read after three attempts is skipped and counted in
   `SkippedCount`.
3. `GET cleanup/archive/{id}/status` reports `Pending`, `Streaming`, `Completed`,
   `Failed`, or `Expired`. The archive becomes `Completed` only after the last byte was
   written.
4. `POST cleanup/delete` removes up to `MaxItems` matching files (at most 200) and reports
   how many remain. The client repeats the call until none remain.

Deletion requires the id of a `Completed` archive built from the same criteria, unless the
request sets `SkipBackup`. Without one the server answers 409. The client therefore starts
the download through `IDownloadService` (see [Downloads](Downloads.md)), waits for `Completed`,
and only then deletes. If any file was skipped, it
stops and deletes nothing.

Deleting a file leaves the message text in place. Wherever the file was used, readers see
the "Attachment not found" placeholder.

## Tests

`Valour/Tests/Apis/AttachmentStorageLiveTests.cs` covers the allowance, the 413 answer, the
archive, and the rule that deletion waits for a finished backup.
