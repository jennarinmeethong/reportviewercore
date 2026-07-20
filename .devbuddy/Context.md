# Context

## Architecture

The legacy solution contains a decompiled ReportViewer engine. Its headless and chart paths still use System.Drawing/GDI/Uniscribe, while WinForms is Windows-only. A v2 migration has started with a backend-neutral rendering boundary in `ReportViewerCore.Rendering.Abstractions` and a SkiaSharp/HarfBuzz implementation in `ReportViewerCore.Rendering.Skia`.

## Modules

The new rendering slice exposes geometry, font, text shaping, image codec/resolver, subreport resolver, bar-chart, canvas, report document, and renderer contracts without System.Drawing types. `ReportViewerCore.Engine` parses constrained multi-tablix/subreport/page-header-footer/textbox-hyperlink/nested-item/styled-text and mixed textbox/image/rectangle/line/basic-chart report items, keeps other body items alongside tablixes, applies tablix sorting, partitions composite group scopes, renders nested group headers and group-footer templates, and evaluates `CountRows`, `Sum`, `Avg`, `Min`, and `Max` during pagination, applies parent offsets, binds enumerable rows into pages, maps text color/writing mode, applies RDLC parameter defaults, resolves allow-listed field/parameter expressions including concatenation, `IIF`, comparisons, and `Format` in body and tablix cells, maps explicit parent parameters into child subreports, and exposes `IReportPageSource`/`ReportPageSourceAdapter` for paginated legacy-to-portable bridging. `ReportViewerCore.Headless.LocalReport` owns the local workflow and `ServerReport` delegates remote rendering to an explicit transport; `HttpReportServerTransport` uses URL access and `IReportServerAuthenticator` for explicit credentials. SkiaSharp handles PNG/PDF on every RID, `ReportViewerCore.Rendering.Html` handles SVG-backed HTML, and `ReportViewerCore.Rendering.OpenXml` handles text/hyperlink/PNG-image XLSX/DOCX baselines, rectangle/line shape parts, and native chart parts. `ReportViewerCore.Rendering.Windows` is a display-only WinForms adapter over the same Skia pages, exposed from the legacy assembly as net10-only `Microsoft.Reporting.WinForms.PortableReportViewer`. Both legacy `Microsoft.Reporting.NETCore.LocalReport` and `Microsoft.Reporting.WinForms.LocalReport` expose net10-only `CreatePortableDocument`/`RenderPortable` bridges; legacy `Render` remains unchanged. Existing legacy controls remain available for compatibility.

## Data Models

## Dependencies

The new backend pins SkiaSharp 2.88.9 and HarfBuzzSharp 8.3.1.1, including Linux native assets. The final v2 package must validate native assets for `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, and `win-x64`.

## Runtime Behavior

## Test Strategy

`tests/ReportViewerCore.Rendering.Tests` currently covers bitmap rendering, Latin/Thai/Arabic/RTL/CJK/vertical text shaping, PDF routing, semantic HTML/security behavior, OpenXML package structure, RDLC field binding/pagination, and the LocalReport/ServerReport facades. GitHub Actions validates the portable tests and sample publish for `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`, and `win-x64`, plus Windows-targeted adapter/legacy bridge builds and smoke samples; only matching hosted runners execute Windows-targeted binaries.

## DevBuddy

Resolved memory root: `/Users/jmmac/Desktop/MyFiles/Codes/reportviewercore/.devbuddy`
