# Security policy

## Reporting a vulnerability

Please **don't** open a public issue for security problems. Report them privately through
GitHub: go to the **Security** tab of this repository and choose **Report a vulnerability**.

Include what you found, how to reproduce it and the impact you expect. We'll acknowledge the
report, keep you updated while we work on a fix, and credit you in the release notes if you'd like.

## Supported versions

Security fixes go into the latest release. Upgrade to the newest version to receive them.

## Handling API keys

- Secret keys (`sk_live_…`, `sk_test_…`) belong on your server only — never in browser code,
  mobile apps or a public repository.
- If a key leaks, revoke it in your wallet under **Developers → API keys**. It stops working immediately.
- Always verify webhook signatures with the raw request body before acting on an event.
