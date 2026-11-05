#!/usr/bin/env python3
"""Run the client journey tests against the server in this checkout.

Publishes the current server, starts it against an isolated PostgreSQL database
and Redis, then runs Valour/Tests.Compatibility once for each client: the SDK in
this checkout ("current") and each released SDK version given. By default the
released version is the one recorded in the wire contract baseline, so the
tests show whether people still running that release can use this server.
See Docs/ClientCompatibility.md.
"""
import argparse
import base64
import ipaddress
import json
import os
import secrets
import shutil
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[2]
SUITE = ROOT / 'Valour/Tests.Compatibility/Valour.Tests.Compatibility.csproj'
SERVER = ROOT / 'Valour/Server/Valour.Server.csproj'
BASELINE = ROOT / 'Valour/Tests/Contracts/baseline.json'

# Settings sections the server reads. Inherited variables for them are dropped
# so a shell that points at shared services cannot leak into the test server.
SERVER_SECTIONS = {
    'appstore', 'bootstrap', 'cdn', 'cloudflare', 'database', 'dataprotection', 'e2ee', 'email',
    'externalauth', 'federation', 'googleplay', 'hosting', 'mediasafety', 'node', 'notifications',
    'ratelimiting', 'redis', 'sentry', 'stripe', 'voice',
}


def is_private_host(value):
    """True for localhost and loopback or private network addresses."""
    host = urlsplit(value if '://' in value else '//' + value).hostname or ''
    if host == 'localhost':
        return True
    try:
        address = ipaddress.ip_address(host)
    except ValueError:
        return False
    return address.is_loopback or address.is_private


def baseline_version():
    return json.loads(BASELINE.read_text())['version']


def free_port():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


def publish_server(output):
    subprocess.run(
        ['dotnet', 'publish', str(SERVER), '-c', 'Release', '-o', str(output),
         '--nologo', '-v:q', '-clp:ErrorsOnly'],
        cwd=ROOT, check=True)

    # A local appsettings.json can hold credentials for live services. The
    # test server reads its settings only from the environment set below.
    for settings in output.glob('appsettings*.json'):
        settings.unlink()


def server_environment(args, output, port):
    env = {key: value for key, value in os.environ.items()
           if key.replace(':', '__').split('__')[0].lower() not in SERVER_SECTIONS
           and not key.upper().startswith('TEST_')
           and key.upper() not in {'NODE_NAME', 'SENTRY_DSN', 'ASPNETCORE_URLS'}}

    kek = output / 'kek'
    if not kek.exists():
        kek.write_text(base64.b64encode(secrets.token_bytes(32)).decode())
        kek.chmod(0o600)

    env.update({
        'ASPNETCORE_ENVIRONMENT': 'Production',
        'DOTNET_ENVIRONMENT': 'Production',
        'ASPNETCORE_URLS': f'http://localhost:{port}',
        'TEST_DB_HOST': args.db_host,
        'TEST_DB': args.db,
        'TEST_DB_USER': args.db_user,
        'TEST_DB_PASS': args.db_password,
        'TEST_REDIS': f'{args.redis},abortConnect=False',
        'NODE_NAME': 'compatibility',
        'RateLimiting__Disabled': 'true',
        'Cdn__StorageMode': 'filesystem',
        'Cdn__FileSystemPath': str(output / 'media'),
        'DataProtection__KekFile': str(kek),
    })
    return env


def wait_until_ready(url, process, log, timeout):
    # The server answers only after applying migrations, which take a while
    # on an empty database.
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if process.poll() is not None:
            hint = ''
            if '3D000' in log.read_text(errors='replace'):
                hint = ('\nThe database does not exist. Create it first, for example with '
                        'psql -c "CREATE DATABASE valour_compat".')
            raise SystemExit(f'The server exited with code {process.returncode}.\n{tail(log)}{hint}')
        try:
            with urllib.request.urlopen(f'{url}/healthz', timeout=5) as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, ConnectionError, TimeoutError):
            pass
        time.sleep(2)
    raise SystemExit(f'The server was not ready after {timeout} seconds.\n{tail(log)}')


def tail(log, lines=80):
    if not log.exists():
        return ''
    text = log.read_text(errors='replace').splitlines()
    return '\n'.join(['--- server log ---', *text[-lines:]])


def run_suite(client, url):
    print(f'\n=== Client {client} against {url} ===', flush=True)
    command = ['dotnet', 'test', str(SUITE), '--nologo', '--logger', 'console;verbosity=normal']
    if client != 'current':
        command.append(f'-p:ClientSdkVersion={client}')
    env = {**os.environ, 'VALOUR_COMPAT_SERVER': url}
    return subprocess.run(command, cwd=ROOT, env=env).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--client', action='append',
                        help='"current" or a released SDK version such as 0.9.20; repeatable '
                             '(default: current and the baseline release)')
    parser.add_argument('--server-url', help='use a server that is already running instead of starting one')
    parser.add_argument('--db-host', default='localhost:5432')
    parser.add_argument('--db', default='valour_compat', help='an existing database the server may migrate')
    parser.add_argument('--db-user', default='test_user')
    parser.add_argument('--db-password', default='test_password_123')
    parser.add_argument('--redis', default='localhost:6379')
    parser.add_argument('--port', type=int, default=0, help='server port (default: any free port)')
    parser.add_argument('--output', type=Path, default=Path('/tmp/valour-compatibility'))
    parser.add_argument('--ready-timeout', type=int, default=900)
    args = parser.parse_args()

    clients = args.client or ['current', baseline_version()]

    if args.server_url:
        failures = [client for client in clients if run_suite(client, args.server_url.rstrip('/')) != 0]
        if failures:
            raise SystemExit(f'Client journey tests failed for: {", ".join(failures)}')
        return

    # The server applies migrations to this database and writes to this Redis,
    # so both must be dedicated test services on this machine or network.
    if not any(word in args.db.lower() for word in ('test', 'compat', 'local')):
        raise SystemExit('Use a database whose name marks it as a test database, such as valour_compat.')
    for name, value in (('database', args.db_host), ('Redis', args.redis)):
        if not is_private_host(value):
            raise SystemExit(f'The {name} host must be local or on a private network: {value}')

    output = args.output.resolve()
    server_dir = output / 'server'
    if server_dir.exists():
        shutil.rmtree(server_dir)
    output.mkdir(parents=True, exist_ok=True)
    (output / 'media').mkdir(exist_ok=True)

    print('Publishing the server...', flush=True)
    publish_server(server_dir)

    port = args.port or free_port()
    url = f'http://localhost:{port}'
    log = output / 'server.log'
    with open(log, 'w') as log_file:
        process = subprocess.Popen(['dotnet', str(server_dir / 'Valour.Server.dll')], cwd=server_dir,
                                   env=server_environment(args, output, port),
                                   stdout=log_file, stderr=subprocess.STDOUT)
        try:
            print(f'Waiting for the server at {url}...', flush=True)
            wait_until_ready(url, process, log, args.ready_timeout)
            failures = [client for client in clients if run_suite(client, url) != 0]
        finally:
            process.terminate()
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                process.kill()

    if failures:
        print(tail(log), file=sys.stderr)
        raise SystemExit(f'Client journey tests failed for: {", ".join(failures)}')
    print(f'\nClient journey tests passed for: {", ".join(clients)}')


if __name__ == '__main__':
    main()
