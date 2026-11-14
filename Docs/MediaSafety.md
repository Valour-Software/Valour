# Media safety scanning

Valour can check uploaded images against known child sexual abuse material
(CSAM) with Microsoft PhotoDNA. This guide explains what is checked, what
happens when an image matches, and how to turn scanning on. The published
policy that this supports is [CHILD_SAFETY.md](../CHILD_SAFETY.md).

## What is checked

Every image upload route calls
[`MediaSafetyService.HashMatchImageUploadAsync`](../Valour/Server/Cdn/MediaSafetyService.cs)
before the image is stored: message images, avatars, banners, backgrounds, planet
images, emoji, app images, and theme assets. The generic file route also checks
any file whose bytes are an image, whatever its name or declared type, so an
image cannot avoid the check by being uploaded as a file.

PhotoDNA accepts JPEG, PNG, GIF, BMP, and TIFF. Images in other formats, such as
WebP, are converted to PNG for the check only. The stored file is unchanged.

The result is recorded on the file's `cdn_bucket_items` row
(`safety_hash_match_state`, `safety_match_id`, and related columns).

## When an image matches

In `Enforce` mode a match blocks the upload, and
[`MediaSafetyIncidentService`](../Valour/Server/Cdn/MediaSafetyIncidentService.cs)
then:

1. Stores the file in private storage and marks it quarantined. Quarantined
   files are never served, and account deletion keeps them, because the law
   requires the material to be preserved for investigators.
2. Quarantines every other stored copy of the same file, including copies
   uploaded before scanning was turned on.
3. Disables the uploader's account through the same path staff use, which also
   disables the account's bots and ends its sessions. The staff action log
   records Victor as the actor.
4. Files a report in the staff review queue with the reason Minor Sexual
   Content. The report lists the match ID, the time, the file's SHA-256 hash,
   and any other accounts that uploaded the same file.
5. Emails the same summary to `MediaSafety:AlertEmail`, if it is set. The email
   never contains the image.

Each step runs even if an earlier one fails, and a failure is logged at the
critical level for staff to finish by hand. The uploader only sees that the
image could not be uploaded.

Staff then confirm the match and file a CyberTipline report with NCMEC.

## Responses that are not a clean result

PhotoDNA reports a processed request with status code 3000 and an `IsMatch`
value. Any other status, a response without `IsMatch`, an HTTP error, or a
timeout is recorded as an error, never as a clean result. With
`FailClosed: false` the upload continues after an error, so an outage at
PhotoDNA does not stop image uploads. With `FailClosed: true` the upload is
rejected instead.

## Configuration

Scanning is off unless the `MediaSafety` section turns it on
([MediaSafetyConfig.cs](../Config/Configs/MediaSafetyConfig.cs)):

```json
"MediaSafety": {
  "Enabled": true,
  "Mode": "Enforce",
  "FailClosed": false,
  "PhotoDnaEndpoint": "https://api.microsoftmoderator.com/photodna/v1.0/Match?enhance=false",
  "PhotoDnaSubscriptionKey": "<subscription key>",
  "AlertEmail": "<address that receives match alerts>"
}
```

`Mode` is `Enforce` (block and act on matches), `Shadow` (record matches but
store and serve the file as usual), or `Off`. Use `Shadow` only to check that
the connection works. A matched image in `Shadow` mode is served like any other
upload, and the server only logs the match.

To check a new configuration, upload an ordinary image and confirm that its
`cdn_bucket_items` row has `safety_hash_match_state` 1 (no match) rather than 3
(error). An error state includes PhotoDNA's response in `safety_details`.
