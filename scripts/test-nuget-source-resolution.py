#!/usr/bin/env python3
"""Run dependency-free C# resolver tests against the collector's actual class."""
import json
import os
import zipfile
from pathlib import Path
import subprocess
import tempfile

source = Path(__file__).with_name('collect-nuget-documents.cs').read_text()
marker = '\nclass SourceResolver'
assert marker in source, 'SourceResolver behavior is not implemented yet'
tests = Path(__file__).with_name('test-nuget-source-resolution.cs').read_text()
with tempfile.TemporaryDirectory(prefix='nuget-tests-', dir=os.environ.get('TMPDIR')) as directory:
    script = Path(directory) / 'tests.cs'
    download = source[source.index('static async Task<(byte[]? Bytes, int? Status, string? Error)> Download'):source.index('static void SafePath')]
    script.write_text(tests.replace('// DOWNLOAD_FUNCTION', download) + source[source.index(marker):])
    subprocess.run(['dotnet', 'run', '--file', str(script)], check=True)

    # Exercise the complete collector and exit status without network access.
    root = Path(directory)
    cache = root / 'cache'
    for name, bundled in [('Bundled', True), ('Unsafe', False)]:
        package = cache / name.lower() / '1.0.0' / (name.lower() + '.1.0.0.nupkg')
        package.parent.mkdir(parents=True)
        with zipfile.ZipFile(package, 'w') as archive:
            archive.writestr(name + '.nuspec', '<package><metadata><id>' + name + '</id><version>1.0.0</version><repository url="https://example.org/not-github" /></metadata></package>')
            if bundled:
                archive.writestr('LICENSE', 'Original bundled license')
    inventory = root / 'inventory.json'
    inventory.write_text(json.dumps({'projects': [{'frameworks': [{'topLevelPackages': [
        {'id': name, 'resolvedVersion': '1.0.0'} for name in ['Bundled', 'Unsafe']]}]}]}))
    output = root / 'output'
    run = subprocess.run(['dotnet', 'run', '--file', str(Path(__file__).with_name('collect-nuget-documents.cs')), '--',
                          str(inventory), str(cache), str(output)])
    assert run.returncode == 2, 'unresolved collection must exit 2'
    manifest = json.loads((output / 'manifest.json').read_text())
    bundled, unsafe = manifest
    assert bundled['status'] == 'original-license-collected' and bundled['sourceResolution'] is None
    assert (output / 'packages/bundled/1.0.0/LICENSE').read_text() == 'Original bundled license'
    assert unsafe['status'] == 'error' and unsafe['sourceAttempts'] == []
    assert {e['code'] for e in unsafe['errors']} == {'unsafe-or-unsupported-repository', 'unresolved-original-license'}
    print('PASS full collector bundled license + unsafe repository + unresolved exit 2')
