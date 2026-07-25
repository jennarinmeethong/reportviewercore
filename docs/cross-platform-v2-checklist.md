# Cross-platform v2 Migration Checklist

Use this document as the handoff plan for the next coding session. The current implementation is a working portable vertical slice, not yet full v1/RPL parity.

## Completed

- [x] .NET 10 package boundaries: Abstractions, Engine, Headless, Skia, HTML, OpenXML, Windows.
- [x] Backend-neutral `ReportDocument`/`IRenderCanvas` contracts with no `System.Drawing` in portable packages.
- [x] SkiaSharp/HarfBuzz PNG/PDF rendering on macOS, Linux, and Windows.
- [x] HTML SVG output; XLSX/DOCX output with images, links, charts, rectangles, lines, RTL, and vertical text.
- [x] Constrained RDLC engine: tablixes, sorting, multi-level group scopes, headers/footers, subtotals, images, charts, hyperlinks, nested items, subreports, parameters, expressions, and pagination.
- [x] `IReportPageSource`/`ReportPageSourceAdapter` seam for legacy RPL/SPB pagination.
- [x] Windows display adapter and opt-in legacy `RenderPortable` bridges.
- [x] 129 regression tests, 62 content-complete RDLC fixtures, RID publish workflow, seven `2.0.0-preview.1` packages, samples, and migration docs.
- [x] Feature-showcase console project exports every portable file type plus manifests covering all positive canvas and RDLC-engine showcase features, including pagination, table spans, image crop, vector clipping, grouping, aggregates, headers/footers, visibility, hyperlinks, embedded images, and charts.
- [x] Compare direct-canvas and RDLC showcase semantics across HTML/PDF/DOCX/XLSX, require matching page counts, parse every SVG, verify that every embedded Office page preview is byte-identical to its PNG source, and assert hyperlink targets across HTML/PDF/DOCX/XLSX plus native Office image/shape/chart metadata.
- [x] Preserve basic charts, rectangles, and lines placed inside tablix cells with cell-column and repeated-row offsets.
- [x] Preserve bar/column/line/area/pie/doughnut chart semantics through the shared fixture and native backend output parts; unsupported Radar remains an explicit negative case.

## Remaining implementation phases

### 1. Legacy pagination bridge

- [x] Identify the v1 RPL/SPB page object and operation types exposed by the legacy processing host (`RPLReport`, `RPLPageContent`, `RPLReportSection`, and `RPLItemMeasurement`).
- [x] Implement a Windows-only adapter that maps text, images, lines, rectangles, links, dynamic chart/map streams, and page decorations to `IReportPageSource`; tablix cells and nested containers are traversed while snapshotting lazy RPL queues.
- [x] Keep the adapter isolated from portable packages; the net10 WinForms `CreatePortableDocument` path invokes legacy RPL only after the constrained engine throws `NotSupportedException` or `InvalidDataException`.
- [x] Add golden legacy-bridge comparison fixtures; both samples assert required semantic text and golden portable page counts, and Windows runs also compare legacy page counts. The WinForms case uses an explicitly loaded subreport to exercise RPL fallback.
- [ ] Run the real legacy-vs-v2 semantic/page-count comparisons on `windows-latest`; builds and CI artifact upload are wired, execution remains environment-dependent.
- [x] Add golden legacy-vs-v2 semantic/page-count expectations shared by both bridge samples; Windows execution remains a hosted-runner check.
- [x] Execute both legacy bridge comparisons on the current Windows host; the hosted `windows-latest` job remains the final independent confirmation.

### 2. RDLC parity

- [x] Support constrained three-or-more-level row-group prefix headers/scopes using matching row templates.
- [x] Traverse nested `TablixMember` group expressions in document order for constrained scopes.
- [x] Honor constrained grouped `PageBreak` metadata only when the configured member-scope prefix changes.
- [x] Resolve parameter- and field-disabled group page breaks at materialized group scope and prove the enabled/disabled page counts match PNG, HTML, PDF, DOCX, and XLSX outputs.
- [x] Support shape-matched recursive `TablixMember` trees with static wrappers, multiple nested dynamic children, scoped leading/trailing rows, recursive headers/details, subtotals, totals, and supported page breaks.
- [x] Support arbitrary nested `TablixMember` row layouts through a hierarchy-first renderer when each member has an explicit body template; legacy compact header/detail layouts retain their compatible renderer path.
- [x] Support terminal sibling row-group branches and nested dynamic branch trees, including single-root nested sibling trees, with independent group/detail sections, optional static headers, explicit static wrapper members with recursive dynamic → static → dynamic children, child `End` boundaries before wrapper subtotals, and nested child `StartAndEnd` breaks, scoped leading/trailing static child rows, explicit root-level interstitial static members, an optional root-level trailing total row, and branch-specific `Between`/`Start`/`End`/`StartAndEnd` page breaks; linear dynamic members with static leaf children repeat the first leaf as detail and emit later leaves as scoped footers; other static layouts and break locations remain deferred.
- [x] Reject branching row-member trees and unsupported grouped page-break locations explicitly; linear nested groups support `Between`, `Start`, `End`, and `StartAndEnd` while other member layouts remain constrained.
- [x] Add scoped `First`, `Last`, and `Count` aggregates through the allow-listed expression host.
- [x] Add allow-listed conditional visibility for standalone report items and tablix-cell text/images.
- [x] Add a regression proving unsupported `Code.*` expressions remain inert and do not execute arbitrary report code.
- [x] Add allow-listed `IsNothing` null checks plus boolean `Not`/`And`/`Or` composition without enabling arbitrary report code.
- [x] Add prefix-scoped nested `Sum` across constrained row-group levels.
- [x] Define static output policy for `ToggleItem`: honor the initial `Hidden` state.
- [x] Define toggle behavior for interactive renderers: current renderers are draw-only, so they honor initial `Hidden` state and defer expand/collapse metadata until an interactive contract exists.
- [ ] Expand the allow-listed expression host only through tests and security review; never execute arbitrary report code.
- [x] Add grouped empty-data and null-key fixtures with scoped aggregate assertions, culture-specific decimal-comma sorting, allow-listed multi-value `Join`, and multi-value default coverage.

### 3. Rendering parity

- [x] Add line/area/pie and richer chart contracts with semantic HTML/PDF/OpenXML output.
- [x] Explicitly reject unsupported map/vector-style report items with clear constrained-engine errors; full map/vector contracts remain future work.
- [ ] Improve OpenXML layout, merged cells, styles, pagination, floating shapes, and hyperlink/image fidelity.
- [x] Preserve OpenXML text family/size/weight/style/color, whitespace, hyperlink cell references, worksheet dimensions, document page sizes, and horizontal text/image/chart offsets.
- [x] Propagate constrained RDLC tablix `ColSpan`/`RowSpan` metadata through the shared canvas and emit bounded SpreadsheetML merged ranges with matching worksheet dimensions.
- [x] Emit page-relative DOCX floating anchors with preserved image/chart positions and extents.
- [x] Preserve backend-neutral page boundaries as DOCX `nextPage` sections with per-page sizes and XLSX worksheets.
- [x] Clip OpenXML images that cross page boundaries, preserving visible anchors and source crop metadata in DOCX/XLSX.
- [x] Clip OpenXML vector rectangles and lines that cross page boundaries, preserving visible bounds and line endpoints in DOCX/XLSX.
- [x] Clip OpenXML text, hyperlinks, and charts that cross page boundaries; fully off-page semantic objects and hyperlink relationships are omitted, while partially visible horizontal and vertical text/link/chart bounds are constrained before DOCX/XLSX emission.
- [ ] Broaden OpenXML hyperlink/image fidelity beyond the current bounded anchors, crop metadata, and URL-policy coverage.
- [x] Validate platform font fallback and caller-owned registered-font policy for Latin, Thai, Arabic, CJK, RTL, and vertical text; unknown families fail closed, and OpenXML references font families without silently embedding or substituting font bytes.
- [x] Define explicit registered-font failure behavior; missing caller-supplied font files fail before rendering, while platform fallback remains runtime/RID-specific.

### 4. Windows compatibility

- [ ] Run WinForms v2 and both legacy bridge samples on `windows-latest`.
- [ ] Validate printer, TIFF, EMF, BIFF8, Word97, Map, and legacy preview behavior.
- [ ] Keep v1 packages unchanged; document every behavior difference before a v2 stable release.

### 5. Release gate

- [x] Run targeted tests and solution build with Windows targeting enabled.
- [x] Publish/execute `osx-arm64` locally.
- [x] Publish `osx-x64`, `linux-x64`, `linux-arm64`, and `win-x64`; verify each published payload contains the app and RDLC fixture.
- [ ] Execute each RID on its matching runner; `linux-arm64` remains publish-only on x64 CI hosts.
- [x] Validate seven v2 `.nupkg`/`.snupkg` archives with `unzip -t`, README plus linked checklist, symbols, repository metadata, and Skia/HarfBuzz native dependency markers.
- [x] Verify the six portable packages have no Windows desktop/System.Drawing dependency; keep that dependency isolated to `ReportViewerCore.Rendering.Windows`.
- [x] Independently inspect all seven release symbol archives with a Portable PDB reader: each reports `SourceLink=true` and three `EmbeddedSource` records; the projects enable `PublishRepositoryUrl` and `EmbedUntrackedSources`.
- [x] Review local security boundaries, API compatibility notes, changelog, migration guide, and preview release notes; keep unsupported report code and Windows-only behavior explicit.
- [x] Pin the legacy compatibility packages to `System.Security.Cryptography.Xml` `10.0.10` in project and nuspec dependencies; this removes the known NU1903 advisory without changing the v1 rendering/API path.
- [ ] Complete licensing/legal review before stable release.

## Next-session start

```bash
dotnet test tests/ReportViewerCore.Rendering.Tests/ReportViewerCore.Rendering.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
dotnet build ReportViewerCore.sln --no-restore -c Release -p:EnableWindowsTargeting=true -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
rg -n "RPL|SPB|TablixMember|GroupExpressions|NotSupported|TODO" ReportViewerCore.Engine Microsoft.ReportViewer.Common docs
```

Portable work does not require a Windows computer. Windows or GitHub Actions `windows-latest` is required for WinForms, GDI legacy bridges, printer/TIFF/EMF, and actual v1 RPL/SPB validation.
