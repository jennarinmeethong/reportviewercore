# Cross-platform v2 status

The repository now contains the first portable rendering boundary:

- `ReportViewerCore.Rendering.Abstractions` owns backend-neutral geometry, font, text, image resolver, bar-chart, canvas, and document contracts.
- `ReportViewerCore.Rendering.Skia` implements PNG/image, PDF, font metrics, and HarfBuzz text shaping using SkiaSharp and HarfBuzzSharp.
- `ReportViewerCore.Rendering.Html` emits standalone SVG-backed HTML with escaped text, safe hyperlinks, and embedded PNG images.
- `ReportViewerCore.Rendering.OpenXml` emits valid XLSX and DOCX packages with multi-page text, external hyperlinks, embedded PNG image parts, native rectangle/line shape parts, and native chart parts for basic bar charts.
- `ReportViewerCore.Engine` now parses tested RDLC multi-tablix, explicit subreport, page header/footer, textbox hyperlinks, nested report items, and styled text, plus mixed textbox/image/rectangle/line/basic-bar-chart report-item paths; it keeps non-tablix body items alongside tablixes, applies tablix `SortExpressions` before pagination, partitions rows by composite `TablixRowHierarchy` group expressions, renders constrained nested group headers plus group-footer/subtotal templates when present, evaluates group-scope `CountRows`, `Sum`, `Avg`, `Min`, and `Max`, applies parent offsets, binds `IEnumerable` rows, maps color/writing mode, applies RDLC parameter defaults without overriding caller values, expands allow-listed field/parameter expressions including string concatenation, `IIF`, comparison, and `Format`, resolves expression-driven and embedded images in body and tablix cells through an injected `IImageResolver`, and splits overflowing rows into `ReportDocument` pages.
- `ReportViewerCore.Headless.LocalReport` exposes the first v2 local workflow; `ServerReport` exposes an explicit `IReportServerTransport` boundary, with `HttpReportServerTransport` providing URL-access rendering and caller-injected authentication.
- `ReportViewerCore.Engine` also exposes `IReportPageSource`/`ReportPageSourceAdapter` as the RPL/SPB migration seam: a legacy pagination adapter can render each page into the same `ReportDocument` consumed by every backend without leaking legacy or System.Drawing types.
- `ReportViewerCore.Headless` selects an injected renderer for a backend-neutral `ReportDocument`; it does not depend on System.Drawing.
- `ReportViewerCore.Rendering.Windows` is a Windows-only WinForms display adapter. It displays pages rendered by the same SkiaSharp backend; it does not introduce a second GDI report renderer. `Microsoft.ReportViewer.WinForms` exposes this as the explicit net10-only `PortableReportViewer` control while retaining the legacy `ReportViewer` control.
- `ReportViewerCore.Sample.CrossPlatform` is a native smoke sample that writes PNG, PDF, HTML, XLSX, DOCX, and RDLC-engine output.
- `ReportViewerCore.Sample.WinForms.V2` demonstrates the Windows adapter with the same `LocalReport`/RDLC workflow.
- `ReportViewerCore.Sample.LegacyBridge` demonstrates the explicit `Microsoft.Reporting.NETCore.LocalReport.RenderPortable` bridge for existing Windows applications.
- `ReportViewerCore.Sample.WinForms.LegacyBridge` verifies the equivalent `Microsoft.Reporting.WinForms.LocalReport` bridge on Windows.
- `tests/ReportViewerCore.Rendering.Tests` verifies the backend without loading `System.Drawing`, including Thai, Arabic/RTL, CJK and vertical shaping, body/tablix images and image expressions, composite/function/aggregate expressions, sorted/grouped tablixes, charts, links, subreports, multi-tablix layout, and OpenXML package parts.
- The seven v2 libraries have explicit NuGet identities and share the `2.0.0-preview.1` package line. Pack them individually into a release artifact directory; legacy package identities are intentionally unchanged.

CI runs the portable tests and sample publish for `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`, and `win-x64`. A separate Windows job builds the WinForms v2 adapter and both legacy bridge samples, then executes the bridge smoke tests. Native smoke execution runs on matching hosted runners; the `linux-arm64` job validates restore and publish without trying to execute an ARM binary on an x64 runner.

This is an incremental migration. The legacy `Microsoft.ReportViewer.NETCore` and `Microsoft.ReportViewer.WinForms` packages remain compatibility lines and still contain the old GDI/System.Drawing/RPL path, while net10 callers can opt into v2 with `CreatePortableDocument`/`RenderPortable`; the old `Render` path is unchanged. New Windows applications should use the v2 headless API plus `ReportViewerCore.Rendering.Windows`; the WinForms adapter is display-only and receives backend-neutral pages. Subreports use an explicit resolver, support parent-to-child parameter mappings, and have a nesting guard. The RPL/SPB seam is now an explicit page-source adapter, while constrained grouped tablixes render nested header templates, subtotals, and composite group scopes. Legacy internal pagination-to-canvas mapping, deeper recursive member features, advanced chart/map and printer output remain separate compatibility tasks.

Use the smoke sample from the repository root:

```bash
dotnet run --project ReportViewerCore.Sample.CrossPlatform -- ./artifacts/cross-platform
```

The output directory is intentionally caller-controlled so generated artifacts do not pollute the repository. The sample also writes `rdlc-engine.html` and `rdlc-engine.pdf` from the bundled RDLC/data fixture. HTML and PDF links allow only relative, `http`, `https`, and `mailto` URLs. OpenXML preserves vertical/RTL text through native styles and maps basic rectangles/lines, but advanced chart types and full chart/RPL/SPB mapping are later phases.

Run the local equivalent of the portable gate with `dotnet test tests/ReportViewerCore.Rendering.Tests` and `dotnet publish ReportViewerCore.Sample.CrossPlatform -r osx-arm64 --self-contained false`.
For a local v2 package smoke, run `dotnet pack ReportViewerCore.Headless/ReportViewerCore.Headless.csproj -c Release -o ./artifacts/nuget-v2 -m:1 -p:UseSharedCompilation=false` and repeat for the other `ReportViewerCore.*` v2 library projects. The single-node setting avoids MSBuild project-reference contention during pack.
On Windows, run `dotnet run --project ReportViewerCore.Sample.LegacyBridge -- ./artifacts/windows-legacy-bridge` and `dotnet run --project ReportViewerCore.Sample.WinForms.LegacyBridge -- ./artifacts/windows-winforms-legacy-bridge` to verify both legacy API bridges.
