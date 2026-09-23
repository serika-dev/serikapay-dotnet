# Changelog

All notable changes to this library are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-09-23

First release.

### Added
- Payments: create, retrieve, list and refund.
- Balance retrieval (EUR by default) and transaction listing.
- Webhook signature verification and event parsing (`Serika-Signature`, HMAC-SHA256).
- Automatic retries with exponential backoff for network errors, 429 and 5xx on
  idempotent requests; payment creation always sends an idempotency key.
- Typed API errors carrying the error `type`, message and HTTP status.

[1.0.0]: https://github.com/serika-dev/serikapay-dotnet/releases/tag/v1.0.0
