# Secure audio preparation and streaming

## Playback format

AlexaCloudAudio delivers prepared audio as MP3 (`audio/mpeg`) encoded by FFmpeg with the `libmp3lame` codec, a 128 kbps audio bitrate, and a 44.1 kHz sample rate by default. A source that is already an MP3 with MIME type `audio/mpeg` may pass through without transcoding. WAV and other approved source formats are transcoded.

FFmpeg is invoked directly with `ProcessStartInfo.ArgumentList`; shell interpolation is not used. Processing occurs in a randomly generated controlled temporary directory, and temporary input/output files are deleted after success or failure. The deployment package must provide an FFmpeg executable compatible with the target runtime and review its licensing/distribution requirements.

## Limits

`AudioPreparationOptions` bounds source size, processing time, and concurrent preparation work. The initial defaults are:

- Maximum source size: 50 MiB
- Processing timeout: 2 minutes
- Maximum concurrent preparations: 2

Host configuration should reduce these limits when required by Lambda/API Gateway memory and timeout constraints. Source files are treated as untrusted input.

## Cache behavior

Prepared output is identified by a SHA-256 cache key derived from:

- provider item ID;
- provider source version;
- preparation profile (`mp3-v1`).

An unchanged item reuses its cached prepared output. A changed source version creates a different key, so stale output is not selected. Concurrent requests for the same uncached key share one in-flight preparation. A failed preparation removes any partial cache entry before the next attempt.

`MemoryPreparedAudioCache` is suitable for local development and tests. A deployed adapter should use bounded durable/object cache storage appropriate for the host while implementing the same `IPreparedAudioCache` contract.

## OneDrive source boundary

`OneDriveAudioSourceContentProvider` asks `IOneDriveDownloadUrlProvider` for a fresh short-lived OneDrive URL only when content is needed. It downloads the source into trusted service memory and returns only a stream to the preparation service. The URL is not placed in catalog data, prepared metadata, playback grants, Alexa directives, or logs.

## Playback grants

`HmacPlaybackGrantService` issues an HMAC-SHA-256 signed token containing only:

- the internal prepared-audio cache key;
- an absolute expiry time.

The signing key must contain at least 32 random bytes and must come from user-secrets locally or a managed secret store in deployment. Raw tokens and complete signed playback URLs must not be logged. Validation uses fixed-time signature comparison and fails closed for malformed, altered, or expired tokens.

## HTTP streaming adapter

`AudioStreamingService` returns host-neutral response information for an HTTPS endpoint adapter:

- `200 OK` for a full response;
- `206 Partial Content` for a valid single byte range;
- content type and content length;
- the selected range so the host can emit `Content-Range: bytes START-END/TOTAL` and `Accept-Ranges: bytes`.

Malformed ranges, unknown cache entries, and invalid or expired grants return no stream and disclose no internal details. The hosting endpoint should map an invalid range to `416 Range Not Satisfiable` and invalid authorization to a generic `404` or `403` according to deployment policy, without distinguishing token failure causes.

## Security verification

Tests cover cache reuse and invalidation, concurrent single-flight preparation, failure cleanup, source-size rejection, MP3 preparation behavior, HMAC signing, expiry, tampering, full streaming, suffix/open/closed byte ranges, invalid ranges, and fail-closed token handling.
