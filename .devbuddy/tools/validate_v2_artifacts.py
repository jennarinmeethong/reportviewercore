#!/usr/bin/env python3
"""Validate the local v2 NuGet archives, smoke outputs, and feature showcase."""

from __future__ import annotations

import argparse
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path


PORTABLE_PREFIXES = (
    "ReportViewerCore.Engine.",
    "ReportViewerCore.Headless.",
    "ReportViewerCore.Rendering.Abstractions.",
    "ReportViewerCore.Rendering.Html.",
    "ReportViewerCore.Rendering.OpenXml.",
    "ReportViewerCore.Rendering.Skia.",
)


def fail(message: str) -> "NoReturn":
    raise SystemExit(f"validation failed: {message}")


def validate_archive(path: Path, require_pdb: bool = False) -> None:
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                fail(f"{path} contains duplicate entries")
            if archive.testzip() is not None:
                fail(f"{path} contains a corrupt entry")
            if require_pdb and not any(name.endswith(".pdb") for name in names):
                fail(f"{path} contains no PDB")
            if not require_pdb:
                if not any(name.endswith("README.md") for name in names):
                    fail(f"{path} contains no README.md")
                if not any(name.endswith("cross-platform-v2-checklist.md") for name in names):
                    fail(f"{path} contains no linked migration checklist")
                nuspecs = [name for name in names if name.endswith(".nuspec")]
                if not nuspecs or not any(b"<repository" in archive.read(name) for name in nuspecs):
                    fail(f"{path} contains no repository metadata")
    except zipfile.BadZipFile as error:
        fail(f"{path} is not a valid zip archive: {error}")


def validate_packages(directory: Path) -> None:
    packages = sorted(directory.glob("*.nupkg"))
    symbols = sorted(directory.glob("*.snupkg"))
    if len(packages) != 7 or len(symbols) != 7:
        fail(f"expected 7 nupkg and 7 snupkg, found {len(packages)} and {len(symbols)}")

    for package in packages:
        validate_archive(package)
    for symbol in symbols:
        validate_archive(symbol, require_pdb=True)

    for package in packages:
        if package.name.startswith(PORTABLE_PREFIXES):
            with zipfile.ZipFile(package) as archive:
                nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
                nuspec = b"\n".join(archive.read(name) for name in nuspecs)
                if b"System.Drawing.Common" in nuspec or b"Microsoft.WindowsDesktop" in nuspec:
                    fail(f"portable package {package} contains a Windows/System.Drawing dependency")

    windows = [package for package in packages if package.name.startswith("ReportViewerCore.Rendering.Windows.")]
    if len(windows) != 1:
        fail("could not identify the Windows adapter package")
    with zipfile.ZipFile(windows[0]) as archive:
        nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
        nuspec = b"\n".join(archive.read(name) for name in nuspecs)
        if b"Microsoft.WindowsDesktop" not in nuspec:
            fail("Windows adapter package has no Microsoft.WindowsDesktop dependency marker")

    print(f"validated package archives: {len(packages)} nupkg, {len(symbols)} snupkg")


def validate_smoke(directory: Path) -> None:
    files = [path for path in directory.iterdir() if path.is_file()]
    if len(files) != 7:
        fail(f"expected 7 smoke files, found {len(files)}")

    for suffix in (".xlsx", ".docx"):
        matches = list(directory.glob(f"*{suffix}"))
        if len(matches) != 1:
            fail(f"expected one {suffix} smoke artifact")
        validate_zip_only(matches[0])

    pngs = list(directory.glob("*.png"))
    pdfs = list(directory.glob("*.pdf"))
    if len(pngs) != 1 or pngs[0].read_bytes()[:8] != b"\x89PNG\r\n\x1a\n":
        fail("PNG smoke artifact is missing or invalid")
    if len(pdfs) != 2 or any(path.read_bytes()[:5] != b"%PDF-" for path in pdfs):
        fail("PDF smoke artifacts are missing or invalid")
    print(f"validated smoke artifacts: {len(files)} files")


def validate_showcase(directory: Path) -> None:
    expected = {
        "feature-showcase-page-1.png",
        "feature-showcase-page-2.png",
        "feature-showcase.pdf",
        "feature-showcase.html",
        "feature-showcase.xlsx",
        "feature-showcase.docx",
        "feature-showcase-manifest.json",
    }
    files = {path.name for path in directory.iterdir() if path.is_file()}
    if files != expected:
        fail(f"feature showcase files differ; missing={sorted(expected - files)}, extra={sorted(files - expected)}")

    try:
        manifest = json.loads((directory / "feature-showcase-manifest.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"feature showcase manifest is invalid: {error}")

    if set(manifest.get("Files", [])) != expected:
        fail("feature showcase manifest does not enumerate the exact output files")
    if set(manifest.get("OutputFormats", [])) != {"png", "pdf", "html", "xlsx", "docx"}:
        fail("feature showcase manifest does not enumerate every portable output format")
    features = " ".join(manifest.get("Features", []))
    for marker in ("Text", "hyperlink", "PNG", "ColSpan", "RowSpan", "Bar", "doughnut", "page sizes"):
        if marker.lower() not in features.lower():
            fail(f"feature showcase manifest is missing feature marker: {marker}")

    for name in ("feature-showcase-page-1.png", "feature-showcase-page-2.png"):
        if (directory / name).read_bytes()[:8] != b"\x89PNG\r\n\x1a\n":
            fail(f"feature showcase PNG is invalid: {name}")
    if (directory / "feature-showcase.pdf").read_bytes()[:5] != b"%PDF-":
        fail("feature showcase PDF is invalid")

    validate_html_svg(directory / "feature-showcase.html", "feature showcase")

    for name, required in (
        ("feature-showcase.xlsx", ("xl/workbook.xml", "xl/drawings/drawing1.xml")),
        ("feature-showcase.docx", ("word/document.xml",)),
    ):
        path = directory / name
        try:
            with zipfile.ZipFile(path) as archive:
                if archive.testzip() is not None:
                    fail(f"feature showcase archive is corrupt: {name}")
                missing = [entry for entry in required if entry not in archive.namelist()]
                if missing:
                    fail(f"feature showcase archive {name} is missing: {', '.join(missing)}")
        except zipfile.BadZipFile as error:
            fail(f"feature showcase archive is not valid: {name}: {error}")

    validate_rdlc_showcase(directory / "rdlc-feature-showcase")
    print(f"validated feature showcase: {len(files)} direct files, RDLC showcase validated, and {len(manifest.get('Features', []))} canvas feature markers")


def validate_rdlc_showcase(directory: Path) -> None:
    expected = {
        "rdlc-feature-showcase.rdlc",
        "rdlc-feature-showcase-page-1.png",
        "rdlc-feature-showcase.pdf",
        "rdlc-feature-showcase.html",
        "rdlc-feature-showcase.xlsx",
        "rdlc-feature-showcase.docx",
        "rdlc-feature-showcase-manifest.json",
    }
    if not directory.is_dir():
        fail(f"RDLC feature showcase directory does not exist: {directory}")
    files = {path.name for path in directory.iterdir() if path.is_file()}
    if files != expected:
        fail(f"RDLC feature showcase files differ; missing={sorted(expected - files)}, extra={sorted(files - expected)}")

    try:
        root = ET.parse(directory / "rdlc-feature-showcase.rdlc").getroot()
    except (ET.ParseError, OSError) as error:
        fail(f"RDLC feature showcase definition is invalid: {error}")
    if root.tag.rsplit("}", 1)[-1] != "Report":
        fail("RDLC feature showcase definition has an unexpected root")

    try:
        manifest = json.loads((directory / "rdlc-feature-showcase-manifest.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"RDLC feature showcase manifest is invalid: {error}")
    if set(manifest.get("Files", [])) != expected:
        fail("RDLC feature showcase manifest does not enumerate the exact output files")
    if set(manifest.get("OutputFormats", [])) != {"png", "pdf", "html", "xlsx", "docx"}:
        fail("RDLC feature showcase manifest does not enumerate every portable output format")
    features = " ".join(manifest.get("Features", []))
    for marker in ("header", "footer", "CountRows", "Sum", "Avg", "Min", "Max", "ColSpan", "RowSpan", "hyperlink", "embedded image", "doughnut"):
        if marker.lower() not in features.lower():
            fail(f"RDLC feature showcase manifest is missing feature marker: {marker}")

    if (directory / "rdlc-feature-showcase-page-1.png").read_bytes()[:8] != b"\x89PNG\r\n\x1a\n":
        fail("RDLC feature showcase PNG is invalid")
    if (directory / "rdlc-feature-showcase.pdf").read_bytes()[:5] != b"%PDF-":
        fail("RDLC feature showcase PDF is invalid")
    validate_html_svg(directory / "rdlc-feature-showcase.html", "RDLC feature showcase")

    for name, required in (
        ("rdlc-feature-showcase.xlsx", ("xl/workbook.xml",)),
        ("rdlc-feature-showcase.docx", ("word/document.xml",)),
    ):
        try:
            with zipfile.ZipFile(directory / name) as archive:
                if archive.testzip() is not None:
                    fail(f"RDLC feature showcase archive is corrupt: {name}")
                missing = [entry for entry in required if entry not in archive.namelist()]
                if missing:
                    fail(f"RDLC feature showcase archive {name} is missing: {', '.join(missing)}")
        except zipfile.BadZipFile as error:
            fail(f"RDLC feature showcase archive is not valid: {name}: {error}")


def validate_html_svg(path: Path, label: str) -> None:
    try:
        html = path.read_text(encoding="utf-8")
    except OSError as error:
        fail(f"{label} HTML cannot be read: {error}")
    svg_parts = re.findall(r"<svg\b.*?</svg>", html, flags=re.DOTALL | re.IGNORECASE)
    if not svg_parts:
        fail(f"{label} HTML is missing SVG content")
    for index, svg in enumerate(svg_parts, start=1):
        try:
            ET.fromstring(svg)
        except ET.ParseError as error:
            fail(f"{label} HTML SVG page {index} is malformed: {error}")
    if "Doughnut" not in html:
        fail(f"{label} HTML is missing chart content")


def validate_test_results(path: Path) -> None:
    if not path.is_file():
        fail(f"test result file does not exist: {path}")
    if path.stat().st_size == 0:
        fail(f"test result file is empty: {path}")

    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as error:
        fail(f"test result file is not valid XML: {path}: {error}")

    test_cases = [element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == "UnitTestResult"]
    if not test_cases:
        fail(f"test result file contains no test cases: {path}")
    failures = [element for element in test_cases if element.attrib.get("outcome") != "Passed"]
    if failures:
        failed_names = ", ".join(element.attrib.get("testName", "<unnamed>") for element in failures[:5])
        fail(f"test result file contains non-passed tests ({len(failures)}): {failed_names}")
    print(f"validated test results: {len(test_cases)} passed tests")


def validate_fixtures(source_directory: Path, output_directory: Path | None = None) -> None:
    source_files = sorted(source_directory.glob("*.rdlc"))
    if not source_files:
        fail(f"no RDLC fixtures found in {source_directory}")

    for path in source_files:
        if path.stat().st_size == 0:
            fail(f"fixture is empty: {path}")
        try:
            root = ET.parse(path).getroot()
        except (ET.ParseError, OSError) as error:
            fail(f"fixture is not valid XML: {path}: {error}")
        if root.tag.rsplit("}", 1)[-1] != "Report":
            fail(f"fixture has an unexpected root element: {path}")

    if output_directory is not None:
        output_files = {path.name for path in output_directory.glob("*.rdlc")}
        missing = [path.name for path in source_files if path.name not in output_files]
        if missing:
            fail(f"test output is missing fixtures: {', '.join(missing)}")
        extras = sorted(output_files - {path.name for path in source_files})
        if extras:
            fail(f"test output contains fixtures not present in source: {', '.join(extras)}")

    print(f"validated RDLC fixtures: {len(source_files)} source files")


def validate_zip_only(path: Path) -> None:
    try:
        with zipfile.ZipFile(path) as archive:
            if len(archive.namelist()) != len(set(archive.namelist())):
                fail(f"{path} contains duplicate entries")
            if archive.testzip() is not None:
                fail(f"{path} contains a corrupt entry")
    except zipfile.BadZipFile as error:
        fail(f"{path} is not a valid zip archive: {error}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--packages", type=Path)
    parser.add_argument("--smoke", type=Path)
    parser.add_argument("--showcase", type=Path)
    parser.add_argument("--test-results", type=Path)
    parser.add_argument("--fixtures", type=Path)
    parser.add_argument("--fixture-output", type=Path)
    arguments = parser.parse_args()
    if arguments.fixture_output is not None and arguments.fixtures is None:
        parser.error("--fixture-output requires --fixtures")
    if arguments.packages is None and arguments.smoke is None and arguments.showcase is None and arguments.test_results is None and arguments.fixtures is None:
        parser.error("provide --packages, --smoke, --showcase, --test-results, and/or --fixtures")
    if arguments.packages is not None:
        validate_packages(arguments.packages)
    if arguments.smoke is not None:
        validate_smoke(arguments.smoke)
    if arguments.showcase is not None:
        validate_showcase(arguments.showcase)
    if arguments.test_results is not None:
        validate_test_results(arguments.test_results)
    if arguments.fixtures is not None:
        validate_fixtures(arguments.fixtures, arguments.fixture_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
