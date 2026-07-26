#!/usr/bin/env python3
"""Safely preview or remove generated repository artifacts.

The command is intentionally a dry run unless ``--apply`` is supplied.  It
only accepts the repository's exact ``artifacts`` directory and never removes
that directory itself.

Examples:
    python .devbuddy/tools/clear_artifacts.py --summary
    python .devbuddy/tools/clear_artifacts.py --apply
    python .devbuddy/tools/clear_artifacts.py --apply --keep test-results
"""

from __future__ import annotations

import argparse
import os
import shutil
import stat
import sys
from pathlib import Path


REPARSE_POINT = 0x400


def repository_root() -> Path:
	return Path(__file__).resolve().parents[2]


def resolve_artifacts_root(repo_root: Path, requested: Path | None) -> Path:
	"""Resolve the artifacts directory and reject paths outside the repository."""

	repo_root = repo_root.expanduser().resolve()
	artifacts_root = (requested if requested is not None else repo_root / "artifacts").expanduser().resolve()
	if artifacts_root.parent != repo_root or artifacts_root.name != "artifacts":
		raise ValueError("artifacts directory must be the repository's exact 'artifacts' child")
	if not artifacts_root.is_dir():
		raise ValueError(f"artifacts directory does not exist: {artifacts_root}")
	return artifacts_root


def validate_keep_names(artifacts_root: Path, names: list[str]) -> set[str]:
	"""Allow only immediate child names, preventing path traversal or ambiguity."""

	keep: set[str] = set()
	for name in names:
		candidate = Path(name)
		if candidate.is_absolute() or len(candidate.parts) != 1 or candidate.parts[0] in {".", ".."}:
			raise ValueError(f"--keep must name an immediate artifacts child: {name!r}")
		if not (artifacts_root / candidate).exists():
			raise ValueError(f"--keep entry does not exist: {name}")
		keep.add(candidate.name.casefold())
	return keep


def is_reparse_point(path: Path) -> bool:
	"""Detect links/junctions without following them."""

	try:
		attributes = getattr(os.lstat(path), "st_file_attributes", 0)
		return stat.S_ISLNK(os.lstat(path).st_mode) or bool(attributes & REPARSE_POINT)
	except OSError:
		return True


def cleanup_entries(artifacts_root: Path, keep: set[str], apply: bool, quiet: bool) -> tuple[int, int, int]:
	"""Preview or remove immediate children; return (removed, kept, unsafe)."""

	removed = kept = unsafe = 0
	for entry in sorted(artifacts_root.iterdir(), key=lambda item: item.name.casefold()):
		if entry.name.casefold() in keep:
			kept += 1
			if not quiet:
				print(f"KEEP       {entry.name}")
			continue

		if is_reparse_point(entry):
			unsafe += 1
			print(f"SKIP LINK  {entry.name}", file=sys.stderr)
			continue

		if apply:
			if entry.is_dir():
				shutil.rmtree(entry)
			else:
				entry.unlink()
			if not quiet:
				print(f"REMOVED    {entry.name}")
		else:
			if not quiet:
				print(f"DRY-RUN    {entry.name}")
		removed += 1

	return removed, kept, unsafe


def parse_args(argv: list[str] | None) -> argparse.Namespace:
	parser = argparse.ArgumentParser(description=__doc__)
	parser.add_argument("--repo-root", type=Path, default=repository_root(), help="repository root (defaults to this script's repository)")
	parser.add_argument("--artifacts-dir", type=Path, help="exact repository artifacts directory; alternate paths are rejected")
	parser.add_argument("--keep", action="append", default=[], metavar="NAME", help="preserve an immediate child of artifacts; repeatable")
	parser.add_argument("--apply", action="store_true", help="perform deletion; without this flag the command is a dry run")
	parser.add_argument("--summary", action="store_true", help="print only the final summary")
	return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
	args = parse_args(argv)
	try:
		root = resolve_artifacts_root(args.repo_root, args.artifacts_dir)
		keep = validate_keep_names(root, args.keep)
		if (root / ".gitkeep").exists():
			keep.add(".gitkeep")
		removed, kept, unsafe = cleanup_entries(root, keep, args.apply, args.summary)
	except (OSError, ValueError) as error:
		print(f"error: {error}", file=sys.stderr)
		return 2

	mode = "removed" if args.apply else "would remove"
	print(f"{mode} {removed} artifact entr{'y' if removed == 1 else 'ies'}; kept {kept}; skipped links {unsafe}")
	if unsafe:
		return 1
	if not args.apply:
		print("dry run only; pass --apply to delete generated entries")
	return 0


if __name__ == "__main__":
	raise SystemExit(main())
