# Security Policy

## Supported Scope

Security-sensitive areas in this repository include:

- local file access and workspace ingestion
- permission storage and authorization state
- OpenAI and Ollama provider configuration
- VS Code / Codex process launching

## Reporting a Vulnerability

If you find a vulnerability:

1. Do not post exploit details in a public issue first.
2. Prepare a minimal reproduction and affected files.
3. Contact the maintainers privately if possible.
4. If private contact is not available yet, open a public issue without exploit details and request a secure follow-up path.

## Disclosure Expectations

- keep reports reproducible
- avoid leaking local user data in examples
- include environment details when the issue depends on Windows, WPF, or local process execution
