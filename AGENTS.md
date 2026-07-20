# Repository Guidelines

## Project Structure & Module Organization

`ReportViewerCore.sln` contains legacy libraries, v2 packages, samples, and tests. Legacy processing is under `Microsoft.ReportViewer.Common/`; APIs are in `Microsoft.ReportViewer.NETCore/` and `Microsoft.ReportViewer.WinForms/`. The latter retains the old control and adds net10-only `PortableReportViewer` and `LocalReport` bridge methods. V2 code is split across `ReportViewerCore.Rendering.Abstractions/`, `Engine/`, `Headless/`, `Rendering.Skia/`, `Rendering.Windows/`, `Rendering.Html/`, and `Rendering.OpenXml/`. Samples are under `ReportViewerCore.Sample.*`; fixtures and tests are under `tests/`.

## Build, Test, and Development Commands

Run these commands from the repository root:

```bash
dotnet restore ReportViewerCore.sln -p:EnableWindowsTargeting=true
dotnet build ReportViewerCore.sln -p:EnableWindowsTargeting=true
dotnet run --project ReportViewerCore.Sample.Console
dotnet run --project ReportViewerCore.Sample.AspNetCore
dotnet run --project ReportViewerCore.Sample.CrossPlatform -- ./artifacts/cross-platform
dotnet test tests/ReportViewerCore.Rendering.Tests
dotnet publish ReportViewerCore.Sample.CrossPlatform -r osx-arm64 --self-contained false
dotnet pack ReportViewerCore.Headless/ReportViewerCore.Headless.csproj -c Release -o ./artifacts/nuget-v2 -m:1 -p:UseSharedCompilation=false
dotnet build ReportViewerCore.Sample.WinForms.V2 -p:EnableWindowsTargeting=true
dotnet run --project ReportViewerCore.Sample.LegacyBridge -- ./artifacts/windows-legacy-bridge
dotnet run --project ReportViewerCore.Sample.WinForms.LegacyBridge -- ./artifacts/windows-winforms-legacy-bridge
```

The solution build validates legacy projects and needs Windows targeting enabled on macOS. The cross-platform sample writes PNG/PDF/HTML/OpenXML and RDLC-engine smoke artifacts; the xUnit project validates rendering primitives and the portable engine. WinForms samples require Windows.

GitHub Actions repeats the portable test/publish gate for `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`, and `win-x64`; Windows-only adapter and bridge samples run on `windows-latest`.

## Coding Style & Naming Conventions

Use C# with tabs for indentation and retain the surrounding project’s established formatting, especially in the decompiled reporting sources. Use PascalCase for types, methods, and public members; camelCase for local variables and parameters; and preserve the existing `Microsoft.Reporting*` namespace layout. There is no repository formatter or linter configured, so avoid broad formatting-only changes.

## Testing Guidelines

Name tests after the behavior, for example `PdfDocument_WritesPdfHeader`. Add RDLC fixtures and output snapshots under `tests/fixtures/` when coverage needs report inputs. For rendering changes, run the xUnit tests plus the cross-platform smoke sample; compare semantic output and visual output with an explicit tolerance. Keep WinForms coverage on Windows.

## Commit & Pull Request Guidelines

Keep commits small and action-oriented, matching history such as `Removed ...`, `Switched ...`, or `Fixed #233 - ...`. Pull requests should explain the behavior, affected targets, verification results, and any RDLC/sample used. Include screenshots for WinForms or layout changes and call out compatibility impacts.

## Security & Configuration Tips

Do not load or execute reports from untrusted sources; expression sandboxing and code security are not provided. Never commit credentials, report-server URLs containing secrets, generated binaries, or local `bin/` and `obj/` output.
