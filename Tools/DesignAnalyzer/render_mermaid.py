#!/usr/bin/env python3
import argparse
import os
import shutil
import subprocess
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

KNOWN_OUTPUT_FORMATS = ("svg", "png", "pdf")


def run_command(
    command: list[str], timeout: int | None = None
) -> subprocess.CompletedProcess:
    return subprocess.run(
        command,
        text=True,
        capture_output=True,
        check=False,
        timeout=timeout,
    )


def build_mmdc_command(file: Path, output: Path, args) -> list[str]:
    command = [
        "mmdc",
        "-i",
        str(file),
        "-o",
        str(output),
        "-e",
        args.format,
        "-t",
        args.theme,
        "-b",
        args.background,
        "-w",
        str(args.width),
        "-H",
        str(args.height),
        "-q",
    ]

    # Chỉ thực sự hữu ích cho PNG
    if args.format == "png":
        command.extend(["--scale", str(args.scale)])

    if args.config:
        command.extend(["-c", str(Path(args.config).resolve())])

    if args.puppeteer_config:
        command.extend(["-p", str(Path(args.puppeteer_config).resolve())])

    return command


def find_existing_outputs(file: Path) -> list[Path]:
    existing = []
    for ext in KNOWN_OUTPUT_FORMATS:
        candidate = file.with_suffix(f".{ext}")
        if candidate.exists():
            existing.append(candidate)
    return existing


def should_skip(file: Path, output: Path, args) -> tuple[bool, str]:
    if args.force:
        return False, ""

    if args.skip_existing_mode == "target":
        if output.exists():
            return True, f"skipped {output.name} (already exists)"
        return False, ""

    if args.skip_existing_mode == "any":
        existing = find_existing_outputs(file)
        if existing:
            names = ", ".join(p.name for p in existing)
            return True, f"skipped {file.name} (existing output(s): {names})"
        return False, ""

    return False, ""


def render_one(file: Path, root: Path, args) -> tuple[str, str]:
    output = file.with_suffix(f".{args.format}")

    skip, skip_reason = should_skip(file, output, args)
    if skip:
        return "skipped", skip_reason

    command = build_mmdc_command(file, output, args)
    last_error = ""

    for attempt in range(1, args.retries + 2):
        try:
            result = run_command(command, timeout=args.timeout)
        except subprocess.TimeoutExpired:
            result = None
            last_error = f"Timeout after {args.timeout}s"

        if result is not None and result.returncode == 0:
            return "rendered", f"rendered {output.relative_to(root)}"

        if result is not None:
            last_error = (
                result.stderr.strip() or result.stdout.strip() or "Unknown error"
            )

        if attempt <= args.retries:
            time.sleep(args.retry_delay)

    return "failed", f"failed {file.relative_to(root)}\n{last_error}"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Render all Mermaid .mmd files with mmdc in high quality."
    )

    parser.add_argument(
        "directory",
        nargs="?",
        default="artifacts/design",
        help="Root directory to scan for .mmd files.",
    )

    parser.add_argument(
        "--format",
        default="png",
        choices=("svg", "png", "pdf"),
        help="Output format. Default: png.",
    )

    parser.add_argument(
        "--theme",
        default="default",
        help="Mermaid theme. Default: default.",
    )

    parser.add_argument(
        "--background",
        default="white",
        help="Background color. Default: white.",
    )

    parser.add_argument(
        "--scale",
        type=float,
        default=2,
        help="PNG scale factor. Higher = sharper but larger/slower. Default: 2.",
    )

    parser.add_argument(
        "--width",
        type=int,
        default=3200,
        help="Browser viewport width for rendering. Default: 3200.",
    )

    parser.add_argument(
        "--height",
        type=int,
        default=2400,
        help="Browser viewport height for rendering. Default: 2400.",
    )

    parser.add_argument(
        "-c",
        "--config",
        default=None,
        help="Optional Mermaid config JSON file.",
    )

    parser.add_argument(
        "-p",
        "--puppeteer-config",
        default=None,
        help="Optional Puppeteer config JSON file.",
    )

    parser.add_argument(
        "-j",
        "--jobs",
        type=int,
        default=min(8, os.cpu_count() or 1),
        help="Number of parallel render jobs. Default: min(3, CPU count).",
    )

    parser.add_argument(
        "--retries",
        type=int,
        default=1,
        help="Retry count for failed renders. Default: 1.",
    )

    parser.add_argument(
        "--retry-delay",
        type=float,
        default=0.5,
        help="Delay between retries in seconds. Default: 0.5.",
    )

    parser.add_argument(
        "--timeout",
        type=int,
        default=180,
        help="Timeout per diagram in seconds. Default: 180.",
    )

    parser.add_argument(
        "--skip-existing-mode",
        choices=("target", "any"),
        default="any",
        help=(
            "Skip files already rendered. "
            "'target' = skip only if the target output file exists. "
            "'any' = skip if any sibling output (.svg/.png/.pdf) exists. "
            "Default: any."
        ),
    )

    parser.add_argument(
        "--force",
        action="store_true",
        help="Force re-render even if output files already exist.",
    )

    args = parser.parse_args()

    if not shutil.which("mmdc"):
        print("mmdc not found in PATH.")
        print("Install it with:")
        print("  npm install -g @mermaid-js/mermaid-cli")
        return 1

    if args.jobs < 1:
        print("--jobs must be >= 1")
        return 1

    if args.scale < 1:
        print("--scale must be >= 1")
        return 1

    root = Path(args.directory).resolve()
    files = sorted(root.rglob("*.mmd"))

    if not files:
        print(f"No .mmd files found under {root}")
        return 0

    print(f"Found {len(files)} .mmd file(s)")
    print("Renderer: mmdc")
    print(f"Format: {args.format}")
    print(f"Viewport: {args.width}x{args.height}")
    print(f"Scale: {args.scale if args.format == 'png' else 'not used for svg/pdf'}")
    print(f"Parallel jobs: {args.jobs}")
    print(
        f"Skip existing mode: {'disabled by --force' if args.force else args.skip_existing_mode}"
    )
    print("")

    rendered = 0
    skipped = 0
    failed = 0

    with ThreadPoolExecutor(max_workers=args.jobs) as executor:
        futures = [executor.submit(render_one, file, root, args) for file in files]

        for future in as_completed(futures):
            status, message = future.result()
            print(message)

            if status == "rendered":
                rendered += 1
            elif status == "skipped":
                skipped += 1
            else:
                failed += 1

    print("")
    print(f"Rendered: {rendered}")
    print(f"Skipped:  {skipped}")
    print(f"Failed:   {failed}")

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
