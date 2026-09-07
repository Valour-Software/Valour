#!/usr/bin/env python3
"""Run C# regression with copied binaries and explicitly local data services."""
import argparse
import ipaddress
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[2]


def is_local_endpoint(value):
    value = value.strip()
    if value == '::1':
        return True
    host = urlsplit(value if '://' in value else '//' + value).hostname
    if host == 'localhost':
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', required=True, type=Path, help='Private QA appsettings.json')
    parser.add_argument('--binaries', type=Path, default=ROOT / 'Valour/Tests/bin/Debug/net11.0')
    parser.add_argument('--full', action='store_true', help='Run the complete C# suite')
    parser.add_argument('--serial', action='store_true', help='Serialize collections that share application configuration')
    parser.add_argument('--filter', default='FullyQualifiedName~Village|FullyQualifiedName~IdManagerTests')
    parser.add_argument('--results', type=Path, default=ROOT / 'TestResults')
    args = parser.parse_args()
    config = json.loads(args.config.read_text())
    db = config.get('Database', {})
    redis = config.get('Redis', {}).get('ConnectionString', '')
    redis_endpoints = [part for part in redis.split(',') if part.strip() and '=' not in part]
    if not is_local_endpoint(db.get('Host', '')) or not redis_endpoints or not all(map(is_local_endpoint, redis_endpoints)):
        parser.error('Database and every Redis endpoint must be localhost or a loopback address.')
    if not all(db.get(key) for key in ['Database', 'Username', 'Password']):
        parser.error('Provide complete dedicated QA database credentials.')
    if not any(word in db['Database'].lower() for word in ['test', 'qa', 'local']):
        parser.error('Use a dedicated database with test, qa or local in its name.')
    if not (args.binaries / 'Valour.Tests.dll').is_file():
        parser.error('Build Valour/Tests/Valour.Tests.csproj first.')

    # xUnit v3 launches the apphost as a child process. A symlinked apphost can
    # resolve configuration beside its original binary despite a copied DLL.
    work = Path(tempfile.mkdtemp(prefix='valour-village-tests-'))
    shutil.copytree(args.binaries, work, dirs_exist_ok=True, symlinks=False,
                    ignore=shutil.ignore_patterns('appsettings*.json'))
    config['Sentry'] = {'Dsn': ''}
    config.setdefault('Cdn', {}).update({'StorageMode': 'filesystem', 'FileSystemPath': str(work / 'media-storage')})
    (work / 'appsettings.json').write_text(json.dumps(config, indent=2))
    if args.serial:
        (work / 'xunit.runner.json').write_text(json.dumps({'parallelizeTestCollections': False}))
    if any(path.is_symlink() for path in work.rglob('*')):
        raise RuntimeError('The isolated test directory must not contain symlinks.')
    env = os.environ.copy()
    env.update({
        'TEST_DB_HOST': db['Host'], 'TEST_DB': db['Database'],
        'TEST_DB_USER': db['Username'], 'TEST_DB_PASS': db['Password'],
        'Redis__ConnectionString': redis, 'Sentry__Dsn': '', 'SENTRY_DSN': '',
        'Cdn__StorageMode': 'filesystem', 'Cdn__FileSystemPath': str(work / 'media-storage'),
    })
    args.results.mkdir(parents=True, exist_ok=True)
    command = ['dotnet', 'test', str(work / 'Valour.Tests.dll'), '--results-directory', str(args.results.resolve()),
               '--logger', 'trx;LogFileName=villages-local-full.trx' if args.full else 'trx;LogFileName=villages-local.trx']
    if not args.full:
        command += ['--filter', args.filter]
    print(f'Test binaries/configuration copied to {work}', flush=True)
    print(f'Data services: {db["Host"]}; {", ".join(redis_endpoints)}', flush=True)
    raise SystemExit(subprocess.run(command, cwd=work, env=env).returncode)


if __name__ == '__main__':
    main()
