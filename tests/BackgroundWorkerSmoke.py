"""With an already controlled renderer, verify launch exits despite persistent workers.
Run: python tests/BackgroundWorkerSmoke.py ./bin/claudex-yourself
"""
import subprocess
import sys

executable = sys.argv[1] if len(sys.argv) > 1 else './bin/claudex-yourself'
result = subprocess.run([executable, 'launch'], capture_output=True, text=True, timeout=15)
assert result.returncode == 0, (result.returncode, result.stdout, result.stderr)
assert 'Activated' in result.stdout or 'activation' in result.stdout, result.stdout
print('Captured launch returned while background workers remained independent.')
