#!/usr/bin/env python3
"""Build and launch the native iOS offline-upload harness using caller-provided credentials."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--device', required=True, help='Booted iOS simulator UUID')
parser.add_argument('--duration', type=int, default=25, help='Seconds to record before sealing the final chunk')
parser.add_argument('--skip-build', action='store_true')
args = parser.parse_args()
for name in ('ANSIGHT_CAPTURE_UPLOAD_URL', 'ANSIGHT_CAPTURE_UPLOAD_KEY'):
    if not os.environ.get(name):
        parser.error(f'{name} must be provided in the environment')
root = Path(__file__).resolve().parents[1]
if not args.skip_build:
    log = Path(tempfile.gettempdir()) / 'ansight-offline-harness-build.log'
    with log.open('w') as output:
        result = subprocess.run(['xcodebuild', '-project', 'test-apps/core/ios/AnsightNativeHarness.xcodeproj',
            '-scheme', 'AnsightNativeHarness', '-sdk', 'iphonesimulator', '-configuration', 'Debug',
            '-derivedDataPath', str(Path(tempfile.gettempdir()) / 'ansight-offline-harness'),
            'CODE_SIGNING_ALLOWED=NO'], cwd=root, stdout=output, stderr=subprocess.STDOUT)
    if result.returncode:
        raise SystemExit(f'Harness build failed. See {log}')
app = root / 'test-apps/core/ios/Build/Products/Debug-iphonesimulator/AnsightNativeHarness.app'
subprocess.run(['xcrun', 'simctl', 'install', args.device, str(app)], check=True)
environment = dict(os.environ)
for name in ('ANSIGHT_CAPTURE_UPLOAD_URL', 'ANSIGHT_CAPTURE_UPLOAD_KEY'):
    environment['SIMCTL_CHILD_' + name] = environment[name]
environment['SIMCTL_CHILD_ANSIGHT_CAPTURE_TEST_DURATION_SECONDS'] = str(args.duration)
subprocess.run(['xcrun', 'simctl', 'launch', '--terminate-running-process', args.device,
    'ai.ansight.ios.native-harness'], env=environment, check=True)
print('Harness started. Upload credentials are not displayed or saved by this launcher.')
