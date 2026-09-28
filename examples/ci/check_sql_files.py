"""Check SQL files with an installed tsqlformat tool (exit 0/1/2)."""

import argparse
from pathlib import Path
import shutil
import subprocess
import sys


def main() -> int:
    parser = argparse.ArgumentParser(description="Check SQL formatting without editing files.")
    parser.add_argument("files", nargs="*", type=Path, help="SQL files (for pre-commit)")
    parser.add_argument("--directory", type=Path, help="Recursively check SQL files in a directory")
    parser.add_argument("--tool", type=Path, help="Path to tsqlformat (otherwise searched on PATH)")
    arguments = parser.parse_args()

    if arguments.directory is not None:
        if not arguments.directory.is_dir():
            print(f"SQL directory not found: {arguments.directory}", file=sys.stderr)
            return 2
        files = sorted(path for path in arguments.directory.rglob("*")
                       if path.is_file() and path.suffix.lower() == ".sql")
        if not files:
            print(f"No SQL files found in: {arguments.directory}", file=sys.stderr)
            return 2
        files.extend(arguments.files)
    else:
        files = arguments.files

    if not files:
        print("No SQL files specified.", file=sys.stderr)
        return 2

    if arguments.tool is not None and not arguments.tool.is_file():
        print(f"tsqlformat tool not found: {arguments.tool}", file=sys.stderr)
        return 2
    executable = (str(arguments.tool.resolve()) if arguments.tool is not None
                  else shutil.which("tsqlformat"))
    if executable is None:
        print("tsqlformat is not on PATH; install the local .NET tool first.", file=sys.stderr)
        return 2

    result = 0
    for path in files:
        completed = subprocess.run([executable, str(path), "--check"], check=False)
        if completed.returncode == 1:
            print(f"Formatting needed: {path}", file=sys.stderr)
            result = max(result, 1)
        elif completed.returncode != 0:
            print(f"Formatting check failed ({completed.returncode}): {path}", file=sys.stderr)
            result = 2
    return result


if __name__ == "__main__":
    raise SystemExit(main())
