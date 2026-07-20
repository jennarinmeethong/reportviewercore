# Reusable Tools

Index project-local tools here. Each entry should include purpose, runtime, command, inputs, and expected output.

## `validate_v2_artifacts.py`

- Purpose: validate all seven v2 `.nupkg`/`.snupkg` archives and the seven-file cross-platform smoke output.
- Runtime: Python 3 standard library only.
- Command: `python3 .devbuddy/tools/validate_v2_artifacts.py --packages artifacts/nuget-v2-loop-hyperlink-whitespace --smoke artifacts/cross-platform-loop-hyperlink-whitespace`
- Expected output: package archive count and smoke artifact count, or a non-zero exit with the failing archive/path.
