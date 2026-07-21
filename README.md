# AlexaCloudAudio

An Alexa custom skill that securely finds and plays personal audio files stored in OneDrive, with Google Drive support planned behind the same provider-neutral architecture.

## Vision

The goal is to make personal sound files available through natural Alexa voice commands without exposing cloud credentials or relying on permanent public file links.

Example:

> Alexa, ask Vyper Audio to play General Quarters.

## MVP scope

The first release will provide:

- An Alexa custom skill using the invocation name **Vyper Audio**.
- OneDrive integration through Microsoft Graph.
- Provider-neutral abstractions so Google Drive can be added later.
- Case-insensitive, extension-independent sound-name matching.
- Aliases for convenient spoken names.
- Secure, expiring playback URLs.
- WAV files accepted as source content and transcoded to an Alexa-compatible MP3 delivery format when required.
- Play, stop, pause, resume, help, not-found, and ambiguous-match behavior.
- Automated tests with minimum 80% line and 80% branch coverage.

## Architecture

The accepted MVP architecture is documented in:

- [MVP architecture](docs/architecture.md)
- [Architecture decision log](docs/decision-log.md)

```text
Alexa Echo
    |
    |  "Alexa, ask Vyper Audio to play General Quarters"
    v
Alexa Custom Skill
    |
    v
AlexaCloudAudio Skill Endpoint
    |
    +--> Audio Catalog / Name Resolver
    |
    +--> OneDrive Provider --> Microsoft Graph
    |         |
    |         v
    |     Source audio file
    |
    +--> Audio Preparation Service
    |         |
    |         +--> validation
    |         +--> FFmpeg transcoding when required
    |         +--> version-aware cache
    |
    +--> Signed Playback URL Service
              |
              v
       Secure HTTPS Audio Stream
              |
              v
          Alexa AudioPlayer
```

Key accepted decisions include:

- .NET 10 with Domain, Application, Infrastructure, and Skill layers.
- AWS Lambda as the initial deployment target with an ASP.NET Core-compatible endpoint.
- Owned Alexa boundary DTOs with compatibility tests.
- Microsoft OAuth authorization-code flow with refresh support and least-privilege Graph scopes.
- OneDrive-first implementation behind `IAudioLibraryProvider`.
- Deterministic normalized-name and alias matching.
- FFmpeg-based MP3 preparation with version-aware caching.
- Signed, expiring playback grants and HTTP range support.
- Managed secrets in deployment and .NET user-secrets locally.

## Security model

AlexaCloudAudio must never expose Microsoft or Google access tokens, authorization headers, or durable cloud-storage URLs to Alexa.

The service will:

- Validate Alexa signatures, timestamps, and expected skill identity.
- Use least-privilege OAuth scopes.
- Store OAuth credentials only in an approved encrypted or managed secret store.
- Resolve cloud download URLs only when required.
- Never persist short-lived preauthenticated cloud URLs as catalog data.
- Return signed, tamper-resistant, expiring playback URLs.
- Redact credentials, signed URLs, and authorization headers from logs.
- Apply file-size, duration, processing-time, and concurrency limits.
- Support HTTP range requests for reliable audio playback.

## Planned solution structure

```text
src/
  AlexaCloudAudio.Domain/
  AlexaCloudAudio.Application/
  AlexaCloudAudio.Infrastructure/
  AlexaCloudAudio.Skill/

tests/
  AlexaCloudAudio.Domain.Tests/
  AlexaCloudAudio.Application.Tests/
  AlexaCloudAudio.Infrastructure.Tests/
  AlexaCloudAudio.Skill.Tests/
```

Cloud SDKs and hosting concerns must remain outside the domain and application contracts.

## Engineering standards

- Test-driven development where practical.
- SOLID and DRY design.
- Nullable reference types enabled.
- Warnings treated as errors.
- Deterministic builds.
- Central package management.
- xUnit for automated tests.
- Minimum 80% line coverage.
- Minimum 80% branch coverage.
- No secrets, tokens, or developer-specific paths in source control.
- Small, reviewable pull requests tied to GitHub issues.

## Roadmap

1. [Define MVP architecture and technical decisions](https://github.com/ScottyMac52/AlexaCloudAudio/issues/1)
2. [Create .NET solution and TDD quality foundation](https://github.com/ScottyMac52/AlexaCloudAudio/issues/2)
3. [Implement domain model and provider-neutral audio catalog](https://github.com/ScottyMac52/AlexaCloudAudio/issues/3)
4. [Implement Alexa skill request handling and interaction model](https://github.com/ScottyMac52/AlexaCloudAudio/issues/4)
5. [Implement OneDrive provider with Microsoft Graph](https://github.com/ScottyMac52/AlexaCloudAudio/issues/5)
6. [Implement secure audio preparation and streaming](https://github.com/ScottyMac52/AlexaCloudAudio/issues/6)
7. [Add CI/CD, security checks, and deployment documentation](https://github.com/ScottyMac52/AlexaCloudAudio/issues/7)
8. [Deliver OneDrive-backed Alexa playback MVP](https://github.com/ScottyMac52/AlexaCloudAudio/issues/8)

## Deferred features

The following are intentionally outside the first MVP:

- Google Drive provider implementation.
- Playlists and randomized playback.
- Browser-based catalog administration.
- Public Alexa skill certification.
- Multi-user commercial onboarding.

## Project status

MVP architecture decisions are complete. The next implementation step is [issue #2](https://github.com/ScottyMac52/AlexaCloudAudio/issues/2), which creates the .NET solution and TDD quality foundation.

## License

This project is licensed under the MIT License.
