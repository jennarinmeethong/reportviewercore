# Reusable Tools

Index project-local tools here. Each entry should include purpose, runtime, command, inputs, and expected output.

## `validate_v2_artifacts.py`

- Purpose: validate all seven v2 `.nupkg`/`.snupkg` archives and the seven-file cross-platform smoke output.
- Runtime: Python 3 standard library only.
- Command: `python3 .devbuddy/tools/validate_v2_artifacts.py --packages artifacts/nuget-v2-loop-hyperlink-whitespace --smoke artifacts/cross-platform-loop-hyperlink-whitespace`
- Expected output: package archive count and smoke artifact count, or a non-zero exit with the failing archive/path.

## Test result artifacts

- Purpose: keep a shareable VSTest result beside generated smoke files.
- Command: `dotnet test tests/ReportViewerCore.Rendering.Tests --results-directory artifacts/test-results --logger "trx;LogFileName=ReportViewerCore.Rendering.Tests.trx"`
- Expected output: `artifacts/test-results/ReportViewerCore.Rendering.Tests.trx`; compiled binaries remain under the test project's `bin/<Configuration>/net10.0/` directory.

Validate the saved evidence with `python3 .devbuddy/tools/validate_v2_artifacts.py --test-results artifacts/test-results/ReportViewerCore.Rendering.Tests.trx`; this checks that the TRX is non-empty, valid XML, contains test cases, and has no non-passed outcomes.

Validate fixture content and test-output copying with `python3 .devbuddy/tools/validate_v2_artifacts.py --fixtures tests/fixtures/engine --fixture-output tests/ReportViewerCore.Rendering.Tests/bin/Debug/net10.0/fixtures/engine`; this checks every RDLC is non-empty, well-formed, rooted at `Report`, and that source/output fixture names match exactly. The coverage index is in `tests/fixtures/README.md`.
