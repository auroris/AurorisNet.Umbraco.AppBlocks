# Anti-spam

An editor protects a form by placing an **Anti-spam** block (`appAntiSpam`) inside it and choosing a service. AppBlocks renders the service's widget and script, and checks every create and update on the server before the form's class is called. The Anti-spam section of the [business rules guide](business-rules.md#anti-spam) has the full reference: the order of checks, the hosts a Content-Security-Policy must allow, and how to register another service.

## Services

| Service | Needs |
|---|---|
| Honeypot | Nothing. It loads no script |
| Cloudflare Turnstile | A site key and a secret key |
| Google reCAPTCHA v2 | A site key and a secret key |
| Google reCAPTCHA v3 | A site key, a secret key and a score threshold, 0.5 by default |
| hCaptcha | A site key and a secret key |

A failed check returns a message to the visitor and the class is not called. When the service cannot be reached or a key is missing, the submission is refused with a message to try later. The problem is logged once and shown on the settings screen.

## Keys

A site key is public and can go on the block. A secret key stays on the server: in configuration, as `AppBlocks:AntiSpam:<service>:SecretKey`, or encrypted in **Settings → AppBlocks → Anti-spam**. Secret keys are kept out of block content, exports, API responses and logs.

## Host check

Turnstile, reCAPTCHA and hCaptcha say which host a token was solved on. AppBlocks refuses a token solved on a host other than the one the form was sent to, so a token from another site with the same keys fails.

Set `AppBlocks:AntiSpam:VerifyHostname` to `false` in two cases:

- The site cannot see its public host name, behind a proxy that does not forward it.
- The site uses a service's published test keys, whose answers name the service's own test host.

## Third-party scripts

Turnstile, reCAPTCHA and hCaptcha load third-party scripts in the visitor's browser and may need cookie consent. Consent handling stays with the site. A site with its own Content-Security-Policy must allow the scripts.

## Rate limits

**The Anti-spam block is not a rate limit.** Anyone can call a public form as often as they like. A site exposed to the internet should also limit the form endpoints, `/api/forms` unless `AppBlocks:ApiRoot` moves them. The business rules guide has a [recipe for ASP.NET Core's rate limiter](business-rules.md#limiting-how-often).

A form submission is limited to 1 MB. `AppBlocks:Forms:MaxBodySize` changes the limit.

## Testing with the real services

The ordinary tests stand in for the services and need no network. These commands try the real services with the test keys each service publishes:

```powershell
dotnet run --project tests/AppBlocks.Tests -- -noLogo -explicit only -class AppBlocks.Tests.AntiSpamRealServiceTests
$env:APPBLOCKS_ANTISPAM_REAL = "1"; node --test tests/ui/anti-spam.browser.test.mjs
```
