# Sign-in methods

An account can sign in with a password, a Google account, a Discord account,
and, in the Android app, a fingerprint. People manage these in Settings, under
Connections. This guide explains how the pieces fit together and how to turn
Google and Discord sign-in on for a server.

## Credentials

Every sign-in method is a row in the `credentials` table
([Credential.cs](../Valour/Database/Credential.cs)). The `credential_type`
column says which kind it is:

| Type | `identifier` | `secret` | `display_name` |
| --- | --- | --- | --- |
| `Password` | The account's email | PBKDF2 hash | Not used |
| `Google` | Google's account ID (`sub`) | Not used | The Google email |
| `Discord` | The Discord user ID | Not used | `@username` |
| `DeviceKey` | A random key ID the device keeps | DER public key (P-256) | The device's name |

A unique index on type and identifier keeps a Google account, Discord account,
or device key tied to one Valour account. Passwords are excluded from the index
because their identifier is the email, which is already unique.

An account must keep at least one password, Google, or Discord method, because
those work on a new device. A device key is lost with its device, so it never
counts as the last way in. `SignInMethodService.RemoveMethodAsync` enforces
this, and Connections disables the buttons that would break it.

## Signing in

Every method ends at `api/users/token`. The request carries email and
password, a sign-in ticket from Google or Discord, or a device key signature.
After identifying the account, all three share the same checks: disabled
accounts, email verification, the authenticator code, and a new session.
Fingerprint sign-in skips the authenticator code, because the key only signs
after a fingerprint check on the device that holds it.

A session from signing in to Valour lasts seven days from its last use.
`TokenService` moves the expiry forward at most once an hour, and never past 90
days after sign-in, so even a session used every day ends eventually. Tokens
for bots, OAuth apps, and federation keep their fixed expiry.

## Google and Discord

Each provider is a class that extends
[`ExternalAuthProvider`](../Valour/Server/Services/ExternalAuth/ExternalAuthProvider.cs):
`GoogleAuthProvider` and `DiscordAuthProvider`. A provider supplies its OAuth
addresses and reads its own account data. `ExternalAuthService` runs the flow
around them, so adding a provider means adding one class and registering it in
`Program.cs`.

The server runs the OAuth authorization code flow itself, so client secrets
never reach an app:

1. The client creates a random verifier and calls
   `api/auth/external/{provider}/begin` with its hash, the intent (sign in,
   link, or confirm identity), and where it receives the result.
2. The client opens the returned provider page. Google does not allow sign-in
   inside embedded web views, so apps use the system browser.
3. The provider redirects to `api/auth/external/{provider}/callback`. The server
   exchanges the code, reads the account, and issues a ticket for the outcome:
   sign in, start registration, link, or confirm identity. Otherwise it reports
   an error.
4. The result goes back to the client. The Android app receives it on
   `gg.valour.app://auth`, the Windows app on a temporary loopback port, and the
   web app polls `api/auth/external/result`, because provider pages can cut a
   popup's link to the window that opened it.
5. The client redeems the ticket with its verifier: at `api/users/token` to
   sign in, at `api/users/register` with `ExternalTicket` to create an account,
   at `api/users/me/signin-methods/link` to link, or at `api/users/me/reauth`
   to confirm identity. Linking and confirming identity also need the session
   of the user who started the flow, so nothing changes on an account until
   that app redeems the ticket.

Flow state, tickets, and proofs live in Redis
([AuthTicketStore.cs](../Valour/Server/Services/AuthTicketStore.cs)) for a few
minutes, so any node can finish a flow another node started. A ticket is useless
without the verifier, so a result intercepted on its way back can't be used.

Web flows are also bound to the browser that started them. The begin response
sets an HttpOnly cookie named `valour-ext-{flowId}`, scoped to
`/api/auth/external`, and the callback rejects the flow if that cookie is
missing or wrong. Without this, someone could start a flow, send the provider
link to another person, and collect that person's sign-in or link their account
by polling for the result. The app sends the begin request with credentials
included, and the cookie uses `SameSite=Lax`, so the web app and the API must
be served from the same site. Android and Windows deliver results straight to
the app on the device, so they don't use the cookie. A provider link sent to
another person there leaves the result on that person's device, where the
sender's verifier and session are missing. The Windows app listens on a
loopback port that any local program can reach, so it accepts only the request
that carries its own flow ID.

New accounts need a verified email from the provider. If a verified Valour
account already has that email, the person is asked to sign in with their
password and link the provider in Connections, so a provider account can never
take over an existing Valour account. Discord accounts can suggest a username,
and both providers can supply the new account's avatar, which goes through the
same upload checks as any avatar.

## Confirming identity

Linking an account, removing a sign-in method, adding a password, turning on
fingerprint sign-in, changing the username, removing two-factor
authentication, and deleting the account all need recent confirmation. A
person confirms with their password, by signing in again with a linked
account, or with their fingerprint. Each of these produces a proof from
`api/users/me/reauth`, which these changes accept for five minutes. A proof
works only with the session that created it, so a leaked proof is useless to
another session. Accounts without a password rely on these proofs.

When a password, linked account, or fingerprint key is added, the account's
email address gets a notice, so a method added by someone else doesn't go
unnoticed.

## Fingerprint sign-in

The Android app creates a P-256 key in the Android keystore that needs a strong
biometric check for every signature and stops working when fingerprints change
([AndroidDeviceKeyService.cs](../Valour/Client.Maui/Platforms/Android/AndroidDeviceKeyService.cs)).
The server stores the public key. To sign in, the app asks
`api/auth/device/challenge` for a one-time challenge, signs it after the
fingerprint check, and sends the signature to `api/users/token`. A password
reset removes every device key, because the account may have been taken over.

Because fingerprint sign-in skips the authenticator code, turning it on for an
account with two-factor authentication requires a current authenticator code.
Otherwise a stolen session and password could add a key that gets around the
second factor.

## Turning on Google and Discord

Create an OAuth client with each provider and add the credentials to the
`ExternalAuth` section of `appsettings.json` (see
[appsettings.helper.json](../Config/appsettings.helper.json)). A provider appears
on the sign-in page only when both values are set.

- **Google:** in Google Cloud Console, create an OAuth client of type "Web
  application" with the scopes `openid`, `email`, and `profile`.
- **Discord:** in the Discord Developer Portal, create an application and use
  its OAuth2 client ID and secret. It uses the `identify` and `email` scopes.

Register this redirect URL with each provider, replacing the host with the
server's public API address:

```
https://api.valour.gg/api/auth/external/google/callback
https://api.valour.gg/api/auth/external/discord/callback
```
