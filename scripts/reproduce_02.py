"""Run current production rendering regressions without launching SPAM.

Original 0.1 failures: analysis-local/debug-v2 and
artifacts/rendering-replay-before.log.
"""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.exit(subprocess.call([
    'dotnet', 'run', '--project', str(ROOT / 'tests/CoreTests.csproj'),
    '--', '--replay-v2',
], cwd=ROOT))
