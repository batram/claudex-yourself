"""With an already controlled renderer, verify launch exits despite persistent workers.
Run: python tests/BackgroundWorkerSmoke.py ./bin/claudex-yourself
"""
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor

executable = sys.argv[1] if len(sys.argv) > 1 else './bin/claudex-yourself'
def check_open(arguments):
    started = time.monotonic()
    result = subprocess.run([executable, *arguments], capture_output=True, text=True, timeout=15)
    assert result.returncode == 0, (arguments, result.returncode, result.stdout, result.stderr)
    assert 'Activated' in result.stdout or 'activation' in result.stdout, result.stdout
    return f"{' '.join(arguments)}: {time.monotonic() - started:.3f}s"

print(check_open(['launch']))
if sys.platform == 'win32':
    print(check_open(['taskbar', 'claudex']))
    # Different entry points must share a lock without deadlocking nested launch.
    with ThreadPoolExecutor(max_workers=3) as pool:
        for timing in pool.map(check_open, [['launch'], ['launch'], ['taskbar', 'claudex']]):
            print('Concurrent ' + timing)
print('Captured opens returned while background workers remained independent.')
