# Cross-platform v2 Migration Checklist

Use this document as the handoff plan for the next coding session. The current implementation is a working portable vertical slice, not yet full v1/RPL parity.

## Completed

- [x] .NET 10 package boundaries: Abstractions, Engine, Headless, Skia, HTML, OpenXML, Windows.
- [x] Backend-neutral `ReportDocument`/`IRenderCanvas` contracts with no `System.Drawing` in portable packages.
- [x] SkiaSharp/HarfBuzz PNG/PDF rendering on macOS, Linux, and Windows.
- [x] HTML SVG output; XLSX/DOCX output with images, links, charts, rectangles, lines, RTL, and vertical text.
- [x] Constrained RDLC engine: tablixes, sorting, two-level groups, headers/footers, subtotals, images, charts, hyperlinks, nested items, subreports, parameters, expressions, and pagination.
- [x] `IReportPageSource`/`ReportPageSourceAdapter` seam for legacy RPL/SPB pagination.
- [x] Windows display adapter and opt-in legacy `RenderPortable` bridges.
- [x] 40 regression tests, RID publish workflow, seven `2.0.0-preview.1` packages, samples, and migration docs.

## Remaining implementation phases

### 1. Legacy pagination bridge

- [x] Identify the v1 RPL/SPB page object and operation types exposed by the legacy processing host (`RPLReport`, `RPLPageContent`, `RPLReportSection`, and `RPLItemMeasurement`).
- [x] Implement a Windows-only adapter that maps text, images, lines, rectangles, links, dynamic chart/map streams, and page decorations to `IReportPageSource`; tablix cells and nested containers are traversed while snapshotting lazy RPL queues.
- [x] Keep the adapter isolated from portable packages; the net10 WinForms `CreatePortableDocument` path invokes legacy RPL only after the constrained engine throws `NotSupportedException` or `InvalidDataException`.
- [x] Add legacy-bridge comparison fixtures; both bridge samples assert semantic text and portable-vs-legacy page counts, while the WinForms case uses an explicitly loaded subreport to exercise RPL fallback.
- [ ] Run the real legacy-vs-v2 semantic/page-count comparisons on `windows-latest`; builds and CI artifact upload are wired, execution remains environment-dependent.
- [ ] Add golden legacy-vs-v2 semantic/page-count fixtures.

### 2. RDLC parity

- [x] Support constrained three-or-more-level row-group prefix headers/scopes using matching row templates.
- [x] Traverse nested `TablixMember` group expressions in document order for constrained scopes.
- [x] Honor constrained grouped `PageBreak` metadata between materialized group scopes.
- [ ] Support arbitrary nested `TablixMember` trees, recursive headers, subtotals, totals, and page breaks.
- [x] Add scoped `First`, `Last`, and `Count` aggregates through the allow-listed expression host.
- [x] Add allow-listed conditional visibility for standalone report items and tablix-cell text/images.
- [x] Add prefix-scoped nested `Sum` across constrained row-group levels.
- [x] Define static output policy for `ToggleItem`: honor the initial `Hidden` state.
- [ ] Define toggle behavior for interactive renderers.
- [ ] Expand the allow-listed expression host only through tests and security review; never execute arbitrary report code.
- [ ] Add data-region grouping/sorting fixtures for empty groups, null keys, culture-specific numbers, and multi-value parameters.

### 3. Rendering parity

- [ ] Add line/area/pie and richer chart contracts with semantic HTML/PDF/OpenXML output.
- [ ] Define map/vector-graphics contracts or explicitly defer them with clear errors.
- [ ] Improve OpenXML layout, merged cells, styles, pagination, floating shapes, and hyperlink/image fidelity.
- [ ] Validate font fallback and embedded-font policy for Latin, Thai, Arabic, CJK, RTL, and vertical text.

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
- [x] Independently inspect all seven release symbol archives: each contains a PDB with SourceLink document data; the projects enable `PublishRepositoryUrl` and `EmbedUntrackedSources`.
- [ ] Publish/execute the remaining RIDs and verify embedded-source records with a dedicated symbol reader.
- [ ] Review security, licensing, API compatibility, changelog, migration guide, and release notes.

## Next-session start

```bash
dotnet test tests/ReportViewerCore.Rendering.Tests/ReportViewerCore.Rendering.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
dotnet build ReportViewerCore.sln --no-restore -c Release -p:EnableWindowsTargeting=true -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false
rg -n "RPL|SPB|TablixMember|GroupExpressions|NotSupported|TODO" ReportViewerCore.Engine Microsoft.ReportViewer.Common docs
```

Portable work does not require a Windows computer. Windows or GitHub Actions `windows-latest` is required for WinForms, GDI legacy bridges, printer/TIFF/EMF, and actual v1 RPL/SPB validation.
