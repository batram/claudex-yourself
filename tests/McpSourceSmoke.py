"""Cross-platform MCP protocol/source-tool smoke test; --live also checks the renderer.
Run: python3 tests/McpSourceSmoke.py ./bin/claudex-yourself [--live]
"""
import json
import subprocess
import sys

live = '--live' in sys.argv
executable = next((arg for arg in sys.argv[1:] if arg != '--live'), './bin/claudex-yourself')
requests = [
    {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {'protocolVersion': '2025-03-26'}},
    {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list'},
    {'jsonrpc': '2.0', 'id': 3, 'method': 'tools/call', 'params': {'name': 'get_source_status', 'arguments': {}}},
]
if live:
    for name in ['claudex_status', 'get_userscript_runtime_status']:
        requests.append({'jsonrpc': '2.0', 'id': len(requests) + 1, 'method': 'tools/call', 'params': {'name': name, 'arguments': {}}})
result = subprocess.run([executable, 'mcp'], input='\n'.join(map(json.dumps, requests)) + '\n', capture_output=True, text=True, timeout=45, check=True)
responses = [json.loads(line) for line in result.stdout.splitlines()]
assert [item['id'] for item in responses] == [item['id'] for item in requests]
for item in responses:
    assert 'error' not in item and not item['result'].get('isError'), item
names = {tool['name'] for tool in responses[1]['result']['tools']}
assert {'get_source_status', 'check_claudex_update', 'check_userscript_update', 'set_update_source', 'preview_userscript_url', 'install_userscript_url', 'set_update_schedule'} <= names
status = responses[2]['result']['structuredContent']
assert status['version'] == responses[0]['result']['serverInfo']['version']
assert isinstance(status['scripts'], list)
assert 'onStartup' in status['defaultSchedule'] and 'intervalMinutes' in status['defaultSchedule']
assert all('schedule' in script and 'scheduleInherited' in script for script in status['scripts'])
if live:
    assert responses[3]['result']['structuredContent']['controlled'], responses[3]
print(f'MCP source smoke passed: {len(names)} tools, version {status["version"]}, live={live}.')
