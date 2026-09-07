#!/usr/bin/env python3
"""Start a published Village QA server only after validating and copying local configuration."""
import argparse
import ipaddress
import json
import os
import shutil
import subprocess
from pathlib import Path
from urllib.parse import urlsplit


def loopback(value):
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


def local_environment(config, output, port):
    sections = {'cdn', 'database', 'email', 'notifications', 'node', 'redis', 'stripe',
                'cloudflare', 'voice', 'mediasafety', 'hosting', 'bootstrap', 'federation', 'sentry'}
    env = {key: value for key, value in os.environ.items()
           if key.replace(':', '__').split('__')[0].lower() not in sections
           and not key.upper().startswith('TEST_DB')
           and key.upper() not in {'TEST_REDIS', 'NODE_NAME', 'SENTRY_DSN'}}
    db = config['Database']
    env.update({
        'ASPNETCORE_ENVIRONMENT': 'Production', 'DOTNET_ENVIRONMENT': 'Production',
        'ASPNETCORE_URLS': f'http://localhost:{port}',
        'TEST_DB_HOST': db['Host'], 'TEST_DB': db['Database'],
        'TEST_DB_USER': db['Username'], 'TEST_DB_PASS': db['Password'],
        'Redis__ConnectionString': config['Redis']['ConnectionString'],
        'Sentry__Dsn': '', 'SENTRY_DSN': '',
        'Cdn__StorageMode': 'filesystem', 'Cdn__FileSystemPath': str(output / 'qa-media'),
    })
    return env


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', required=True, type=Path)
    parser.add_argument('--output', type=Path, default=Path('/tmp/valour-village-release'))
    parser.add_argument('--port', type=int, default=5100)
    parser.add_argument('--publish', action='store_true')
    parser.add_argument('--publish-only', action='store_true')
    args = parser.parse_args()
    args.config = args.config.resolve()
    args.output = args.output.resolve()
    config = json.loads(args.config.read_text())
    db = config.get('Database', {})
    redis = config.get('Redis', {}).get('ConnectionString', '')
    redis_endpoints = [part for part in redis.split(',') if part.strip() and '=' not in part]
    if not loopback(db.get('Host', '')) or not redis_endpoints or not all(map(loopback, redis_endpoints)):
        raise SystemExit('QA server requires loopback PostgreSQL and Redis.')
    if not all(db.get(key) for key in ['Database', 'Username', 'Password']) or not any(word in db['Database'].lower() for word in ['test', 'qa', 'local']):
        raise SystemExit('QA server requires complete credentials for a dedicated test, qa or local database.')
    if not 1024 <= args.port <= 65535:
        raise SystemExit('Choose a non-privileged local port.')
    voice = config.get('Voice', {})
    if any(value and not loopback(value) for key, value in voice.items() if 'Url' in key and isinstance(value, str)):
        raise SystemExit('QA voice endpoints must be local.')
    root = Path(__file__).resolve().parents[2]
    args.output.mkdir(parents=True, exist_ok=True)
    if args.publish or args.publish_only:
        try:
            subprocess.run(['dotnet', 'publish', str(root / 'Valour/Server/Valour.Server.csproj'), '-c', 'Release', '--no-restore', '--nologo', '-clp:ErrorsOnly', '-v:q', '-o', str(args.output)], cwd=root, check=True)
        finally:
            shutil.copy2(args.config, args.output / 'appsettings.json')
    else:
        shutil.copy2(args.config, args.output / 'appsettings.json')
    if json.loads((args.output / 'appsettings.json').read_text()) != config:
        raise SystemExit('QA configuration copy did not match; refusing startup.')
    if args.publish_only:
        return
    os.chdir(args.output)
    env = local_environment(config, args.output, args.port)
    os.execvpe('dotnet', ['dotnet', str(args.output / 'Valour.Server.dll')], env)

if __name__ == '__main__':
    main()
