# OneDrive provider setup

## Microsoft identity and account linking

Register AlexaCloudAudio as a Microsoft Entra application and use the OAuth 2.0 authorization-code flow with refresh support. The account-linking callback exchanges the authorization code at the trusted service boundary; browser and Alexa responses must never receive Graph access or refresh tokens.

Store token material only in the selected encrypted or managed secret store. Local development may use .NET user-secrets. Deployed environments should use AWS Secrets Manager or an equivalent managed store with least-privilege access. Never log tokens, authorization headers, authorization codes, or Graph preauthenticated download URLs.

## Least-privilege Graph scopes

Prefer delegated `Files.Read` plus `offline_access` for the single linked user's files. Do not request write scopes. `Files.Read.All` should be used only when a documented deployment requirement cannot be satisfied with `Files.Read`, and it requires a separate security review.

The configured OneDrive folder is supplied through `OneDriveOptions.RootFolder`. Nested traversal is explicit through `IncludeNestedFolders`; it defaults to `true`.

## Graph boundary

`OneDriveProvider` depends on `IOneDriveGraphClient`. A production adapter is responsible for:

- resolving the configured folder path to a drive-item ID;
- listing children while returning Graph `@odata.nextLink` values unchanged to the provider;
- mapping throttling and transient HTTP failures to `GraphRequestException` without including authorization data or preauthenticated URLs in exception messages;
- obtaining a fresh download URL only when source content is needed.

The provider deliberately does not retain download URLs. Every `GetDownloadUriAsync` call reacquires one through the Graph boundary, so an expired URL is replaced rather than cached indefinitely.

## Opt-in integration test

Integration tests are intentionally excluded from normal CI and require a dedicated non-production Microsoft account.

1. Register a test Entra application and grant delegated `Files.Read` and `offline_access`.
2. Create a OneDrive test folder containing supported audio files, one unsupported file, and one nested folder.
3. Store the client ID, tenant, encrypted refresh-token material, and test folder path in user-secrets or the CI secret store. Do not place them in repository files.
4. Set `ALEXA_CLOUD_AUDIO_RUN_ONEDRIVE_INTEGRATION_TESTS=true`.
5. Run the integration-test project or filtered test category locally.
6. Verify enumeration, paging with a sufficiently large folder, nested-folder behavior, rename/delete refresh behavior, and fresh download-URL acquisition.
7. Revoke the test account consent and remove test secrets when testing is complete.

Normal unit tests use a mocked `IOneDriveGraphClient` and require no live credentials.
