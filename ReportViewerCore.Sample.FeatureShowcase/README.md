# ReportViewerCore v2 Feature Showcase

This console project renders both a backend-neutral two-page `ReportDocument` and a representative `FeatureShowcase.rdlc` through every portable output path:

- PNG, one file per page
- PDF
- HTML/SVG
- XLSX
- DOCX

The generated `feature-showcase-manifest.json` lists the canvas feature coverage. The `rdlc-feature-showcase/` directory contains the RDLC source, its rendered formats, a page PNG, and a second manifest for the engine feature coverage. Run it from the repository root:

```powershell
dotnet run --project ReportViewerCore.Sample.FeatureShowcase -- artifacts/feature-showcase
python .devbuddy/tools/validate_v2_artifacts.py --showcase artifacts/feature-showcase
```

The showcase intentionally includes the recently added variable page sizes, table spans, chart kinds, image crop metadata, vector clipping cases, and RDLC grouping/aggregate/header/footer/visibility/hyperlink/image coverage.
