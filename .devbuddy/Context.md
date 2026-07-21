# Context

## Architecture

The legacy solution contains a decompiled ReportViewer engine. Its headless and chart paths still use System.Drawing/GDI/Uniscribe, while WinForms is Windows-only. A v2 migration has started with a backend-neutral rendering boundary in `ReportViewerCore.Rendering.Abstractions` and a SkiaSharp/HarfBuzz implementation in `ReportViewerCore.Rendering.Skia`. The net10 WinForms compatibility assembly now contains the Windows-only `RplPortableDocumentAdapter`, which snapshots the internal v1 RPL page model into `IReportPageSource`; the opt-in WinForms portable bridge falls back to this adapter only when the constrained RDLC engine rejects the report.

## Modules

The new rendering slice exposes geometry, font, text shaping, image codec/resolver, subreport resolver, chart, canvas, report document, and renderer contracts without System.Drawing types. `ReportViewerCore.Engine` parses constrained multi-tablix/subreport/page-header-footer/textbox-hyperlink/nested-item/styled-text and mixed textbox/image/rectangle/line/bar/column/line/area/pie chart report items, keeps other body items alongside tablixes, applies tablix sorting, resolves dataset names case-insensitively, traverses nested `TablixMember` group expressions in document order, partitions composite group scopes, renders nested group headers and group-footer templates, honors constrained grouped page breaks, and evaluates `CountRows`, `Count`, `First`, `Last`, `Sum`, `Avg`, `Min`, and `Max` during pagination, applies allow-listed conditional visibility to standalone items and tablix-cell text/images, preserves initial `Hidden` state for static `ToggleItem` reports, applies parent offsets, binds enumerable rows into pages, maps text color/writing mode, applies RDLC parameter defaults, resolves allow-listed field/parameter expressions including concatenation, `IIF`, comparisons, `Format`, and pure string functions in body and tablix cells, maps explicit parent parameters into child subreports, and exposes `IReportPageSource`/`ReportPageSourceAdapter` for paginated legacy-to-portable bridging. `ReportViewerCore.Headless.LocalReport` owns the local workflow and `ServerReport` delegates remote rendering to an explicit transport; `HttpReportServerTransport` uses URL access and `IReportServerAuthenticator` for explicit credentials. SkiaSharp handles PNG/PDF on every RID, `ReportViewerCore.Rendering.Html` handles SVG-backed HTML, and `ReportViewerCore.Rendering.OpenXml` handles text/hyperlink/PNG-image XLSX/DOCX baselines, worksheet dimensions, report page-sized DOCX sections, rectangle/line shape parts, and native bar/column/line/area/pie chart parts. `ReportViewerCore.Rendering.Windows` is a display-only WinForms adapter over the same Skia pages, exposed from the legacy assembly as net10-only `Microsoft.Reporting.WinForms.PortableReportViewer`. Both legacy `Microsoft.Reporting.NETCore.LocalReport` and `Microsoft.Reporting.WinForms.LocalReport` expose net10-only `CreatePortableDocument`/`RenderPortable` bridges; legacy `Render` remains unchanged. Existing legacy controls remain available for compatibility.

## Data Models

## Dependencies

The new backend pins SkiaSharp 2.88.9 and HarfBuzzSharp 8.3.1.1, including Linux native assets. The seven v2 projects now package a migration README and symbol archive with repository metadata. The final v2 package must validate native assets for `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, and `win-x64`.

## Runtime Behavior

## Test Strategy

`tests/ReportViewerCore.Rendering.Tests` currently covers bitmap rendering, Latin/Thai/Arabic/RTL/CJK/vertical text shaping, PDF routing, semantic HTML/security behavior, OpenXML package structure, RDLC field binding/pagination, fixture-driven mixed bar/column/line/area/pie charts, fixture-driven international text styles/directions, and the LocalReport/ServerReport facades. GitHub Actions validates the portable tests and sample publish for `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`, and `win-x64`, plus Windows-targeted adapter/legacy bridge builds and smoke samples; local validation has published all five RID payloads and executed `osx-arm64`, while only matching hosted runners execute Windows-targeted binaries.
The chart fixture now also covers Doughnut, while Radar remains the explicit unsupported chart regression; all six supported chart kinds are asserted across HTML, PDF, XLSX, and DOCX paths.
The aggregate fixture now carries `Min`/`Max` content, and the OpenXML fixture test parses every emitted XML part to catch malformed package structure.
The test suite now also covers allow-listed `Len`/`Trim`/`UCase`/`LCase`, case-insensitive dataset lookup, OpenXML internal relationship targets, worksheet dimensions, report-sized DOCX sections, and invalid legacy page-source inputs.
Shareable VSTest results belong in `artifacts/test-results/`; cross-platform smoke outputs belong in `artifacts/cross-platform/`, while build/test binaries remain generated under each project's `bin/<Configuration>/net10.0/` folder.
The reusable `.devbuddy/tools/validate_v2_artifacts.py` tool also validates TRX outcomes and verifies that every source RDLC fixture is non-empty, well-formed, and copied into the test output directory.
The fixture coverage index lives in `tests/fixtures/README.md`; source and test-output RDLC filenames must match exactly, and the current set contains 36 fixtures. The empty-tablix `NoRowsMessage` path is covered by `no-rows-message.rdlc` and a semantic HTML assertion.

## DevBuddy

Resolved memory root: `/Users/jmmac/Desktop/MyFiles/Codes/reportviewercore/.devbuddy`

## Next-session handoff

Use [`docs/cross-platform-v2-checklist.md`](../docs/cross-platform-v2-checklist.md) as the source of truth for completed work, remaining phases, verification commands, and Windows-only validation. The RPL/SPB adapter is compile-verified for net10 WinForms, and the WinForms legacy bridge sample now contains a subreport fallback comparison fixture; actual execution remains pending on `windows-latest`. The next implementation priority is deeper RDLC member parity, followed by advanced renderer contracts.
