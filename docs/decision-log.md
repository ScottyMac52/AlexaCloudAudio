# Architecture Decision Log

This document records the decisions accepted for the AlexaCloudAudio MVP. A decision may be superseded later, but changes must be explicit and preserve the security and dependency boundaries defined in `architecture.md`.

## ADR-001: Use .NET 10 and a layered solution

**Status:** Accepted

**Decision:** Target .NET 10 and use separate Domain, Application, Infrastructure, and Skill projects. Nullable reference types and warnings-as-errors are mandatory.

**Rationale:** .NET 10 is the current LTS baseline for this new project. Layering keeps Alexa, Microsoft Graph, hosting, and transcoding dependencies outside stable business rules and supports focused TDD.

**Consequences:** Cloud SDK objects must be mapped into provider-neutral models. Dependency direction is enforced by project references and architecture tests.

## ADR-002: Host the MVP on AWS Lambda with an ASP.NET Core-compatible entry point

**Status:** Accepted

**Decision:** Deploy the initial Alexa skill and playback endpoint to AWS Lambda behind HTTPS, using ASP.NET Core-compatible hosting where practical.

**Rationale:** Lambda is a natural Alexa hosting target, avoids operating an always-on server, and supports managed scaling. Keeping the endpoint as ordinary ASP.NET Core HTTP behavior preserves portability.

**Consequences:** Cold-start performance, package size, writable temporary storage, FFmpeg packaging, response streaming, and Lambda/API Gateway timeout limits must be tested. A conventional ASP.NET Core host remains a supported future deployment option rather than an MVP target.

## ADR-003: Model Alexa requests with owned DTOs at the boundary

**Status:** Accepted

**Decision:** Use small, owned request/response DTOs for the Alexa request types required by the MVP, with serialization tests against representative Alexa payloads. A third-party Alexa library may be used only if it is actively maintained, does not weaken validation, and remains isolated in the Skill project.

**Rationale:** The MVP needs a limited set of intents and AudioPlayer events. Owned boundary models reduce dependency risk and prevent SDK-specific types from entering application contracts.

**Consequences:** The project owns compatibility tests for supported Alexa payloads and must deliberately add new request types.

## ADR-004: Use Microsoft OAuth authorization-code flow and least-privilege Graph access

**Status:** Accepted

**Decision:** Use OAuth 2.0 authorization-code flow with refresh support for the configured Microsoft account. Request the narrowest Graph scopes that satisfy reading the selected OneDrive audio folder.

**Rationale:** Personal OneDrive content is private and must not be exposed through permanent public links. Refresh support allows unattended playback after initial account linking.

**Consequences:** Token storage requires encryption or a managed secret store. Account relinking and revoked-consent behavior must be handled explicitly. Tokens and authorization data are prohibited from logs.

## ADR-005: Use a provider-neutral catalog and OneDrive-first implementation

**Status:** Accepted

**Decision:** Define `IAudioLibraryProvider` and provider-neutral audio metadata in the application/domain layers. Implement Microsoft OneDrive first; defer Google Drive to a later provider.

**Rationale:** The product should not couple voice handling, matching, preparation, or playback to Microsoft Graph. OneDrive-first keeps the MVP small while preserving an extension point.

**Consequences:** Provider-specific IDs and version metadata are mapped into stable internal values. Google Drive must conform to the same observable provider contract when added.

## ADR-006: Resolve names deterministically, not fuzzily

**Status:** Accepted

**Decision:** Match spoken names case-insensitively and independently of file extension after Unicode, punctuation, separator, and whitespace normalization. Support validated aliases. Prefer exact normalized matches and return ambiguity rather than choosing arbitrarily.

**Rationale:** Deterministic behavior is easy to test and avoids surprising playback. Aliases handle common spoken variations without introducing opaque ranking.

**Consequences:** Fuzzy matching, phonetic scoring, and recommendations are deferred. Duplicate canonical names or aliases must be surfaced during catalog validation.

## ADR-007: Deliver prepared MP3 audio through AlexaCloudAudio URLs

**Status:** Accepted

**Decision:** Treat WAV and other approved formats as source content. Prepare MP3 output using documented Alexa-compatible encoding parameters, unless later Alexa endpoint testing proves another delivery format is required. Alexa receives only a signed AlexaCloudAudio HTTPS URL.

**Rationale:** Source WAV characteristics vary, and direct cloud URLs expose short-lived credentials and provide inconsistent playback behavior. A controlled output format simplifies validation, caching, and streaming.

**Consequences:** FFmpeg is an infrastructure dependency and must be packaged for the selected host. Licensing and distribution notices must be reviewed before release. Transcoding limits and cleanup are mandatory.

## ADR-008: Cache prepared audio by source identity and version

**Status:** Accepted

**Decision:** Cache prepared output using provider identity plus immutable source-version metadata or a content hash. Do not cache Microsoft Graph preauthenticated download URLs as catalog state.

**Rationale:** Transcoding on every request is slow and expensive, while source-version keys make invalidation deterministic.

**Consequences:** Cache storage and retention are configurable. Concurrent requests for the same uncached version must share one preparation operation. Old cache entries require bounded cleanup.

## ADR-009: Use signed, expiring playback grants and HTTP range requests

**Status:** Accepted

**Decision:** The playback endpoint validates a tamper-resistant grant with a short expiry and prepared-audio identity. It supports valid `Range` requests and returns correct content headers. Grants fail closed when invalid, expired, or altered.

**Rationale:** Alexa needs an HTTPS media URL, but it must not receive cloud credentials or durable source links. Range support improves media-client compatibility and resumability.

**Consequences:** Signing keys are managed secrets and support rotation. Raw grants and complete signed URLs are excluded from logs. Tests must cover expiry, tampering, ranges, and unauthorized access.

## ADR-010: Use managed secrets in deployment and user-secrets locally

**Status:** Accepted

**Decision:** Local development uses .NET user-secrets or environment variables. AWS deployment uses AWS Secrets Manager or an equivalent approved managed store. Configuration committed to GitHub contains placeholders only.

**Rationale:** Credentials must be separated from source code and deployment packages while remaining practical for local development.

**Consequences:** Startup validates required configuration without echoing secret values. Rotation and least-privilege access policies are part of deployment documentation.

## ADR-011: Enforce TDD-oriented quality gates

**Status:** Accepted

**Decision:** Use xUnit and automated coverage collection. CI must enforce at least 80% line coverage and 80% branch coverage. Security-sensitive behavior requires direct tests even when aggregate coverage is already satisfied.

**Rationale:** The project contains authentication, signed URLs, external API boundaries, and media processing where regressions can be costly or unsafe.

**Consequences:** Coverage is a floor, not a substitute for meaningful tests. Pull requests remain small and tied to issues.
