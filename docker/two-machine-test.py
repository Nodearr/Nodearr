#!/usr/bin/env python3
"""
The two-machine test for Nodearr, as commands.

Run it once on each of two machines that are on different networks:

    python3 two-machine-test.py send              on the machine that will have the file
    python3 two-machine-test.py receive CODE      on the other, with the code "send" printed

"send" starts Nodearr and Radarr, adds Night of the Living Dead (a film in the public domain),
makes a test video for it, imports that, and prints an invite code. "receive" starts the same
pair with the film missing, pairs with the code, checks how the two nodes are connected, has
Radarr search and grab, and reports what happened.

    python3 two-machine-test.py fetch             on the receiving side, to try the transfer again
    python3 two-machine-test.py status            where the web pages are, and anything Nodearr warns about
    python3 two-machine-test.py down              remove everything this script made on this machine

It needs Docker with the compose plugin, the "nodearr" image, and Python 3.8 or newer. Nothing
else: no settings are clicked, no sign-in is set up, and nothing is installed on the machine.
Everything lives in Docker volumes and in ~/nodearr-test.

    python3 two-machine-test.py send --native     Nodearr straight on this machine, not in a container

Docker Desktop, on Windows and macOS, puts its own address translation between a container and
the router, and it gives a different public port to every destination. That makes a node in a
container there hard to reach directly, whatever the router is like. --native runs Nodearr itself
outside Docker, from a build of this repository, and only Radarr in a container. It needs
"dotnet build src/Nodearr.sln" and a built transport (--transport-bin says where). Use it to see
what the machine's own network allows.

Nodearr and Radarr are started with signing in turned off and bound to this machine only, so
their pages are reachable from this machine and from nowhere else. Do not publish their TCP
ports. The only port another machine ever needs is Nodearr's UDP port.
"""

import argparse
import json
import os
import platform
import secrets
import shutil
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

MOVIE_TMDB_ID = 10331
MOVIE_TITLE = 'Night of the Living Dead'
MOVIE_RELEASE = 'Night.of.the.Living.Dead.1968.720p.WEB-DL.AAC2.0.H.264-NODEARR'

# A film is not imported if it is short enough to be a sample.
MOVIE_SECONDS = 660

RADARR_IMAGE = 'lscr.io/linuxserver/radarr:6.4.4.10685-ls319@sha256:7dfd049e79c00b16fbc29c3f5d96a9e7b9e73a23930b4c5b3c4541d60b366814'
FFMPEG_IMAGE = 'mwader/static-ffmpeg:7.1'


class Stop(Exception):
    """Something went wrong that the person running this can act on."""


def say(text=''):
    print(text, flush=True)


def step(text):
    say(f'\n== {text}')


# --- Docker --------------------------------------------------------------------------------------

_docker = None


def docker_command():
    """How Docker is run here: directly, or through sudo where the user is not in the docker group."""
    global _docker

    if _docker is not None:
        return _docker

    try:
        direct = subprocess.run(['docker', 'info'], capture_output=True, text=True)
    except FileNotFoundError:
        raise Stop('Docker is not installed. On Ubuntu: sudo apt-get update && sudo apt-get install -y docker.io docker-compose-v2')

    if direct.returncode == 0:
        _docker = ['docker']
    elif 'permission denied' in (direct.stderr or '').lower() and subprocess.run(['sudo', '-n', 'true'], capture_output=True).returncode == 0:
        _docker = ['sudo', 'docker']
    else:
        raise Stop('Docker is not answering:\n' + (direct.stderr or direct.stdout or '').strip())

    return _docker


def docker(*args, check=True, quiet=True):
    result = subprocess.run(docker_command() + list(args), capture_output=quiet, text=True)

    if check and result.returncode != 0:
        raise Stop(f"docker {' '.join(args[:3])} ... failed:\n" + ((result.stderr or '') + (result.stdout or '')).strip())

    return (result.stdout or '').strip()


def uses_docker_desktop():
    return 'docker desktop' in docker('info', '--format', '{{.OperatingSystem}}').lower()


# --- State ---------------------------------------------------------------------------------------

def load(folder):
    path = folder / 'state.json'

    return json.loads(path.read_text()) if path.exists() else None


def save(folder, state):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / 'state.json').write_text(json.dumps(state, indent=2))


def find_side(args):
    """The side this machine was set up as, for the commands that are not told."""
    root = Path(args.dir).expanduser()
    found = [name for name in ('send', 'receive') if (root / name / 'state.json').exists()]

    if args.side:
        return args.side

    if len(found) == 1:
        return found[0]

    if not found:
        raise Stop('Nothing has been set up on this machine yet. Run "send" or "receive CODE" first.')

    raise Stop('Both sides are set up on this machine. Say which: --side send or --side receive.')


def host_path(folder, name):
    """A folder of this script's, the way Docker and Nodearr on this machine both spell it."""
    return str(Path(folder) / name)


def native_compose_file(state):
    """Radarr alone. Nodearr runs outside Docker and shares two folders with it."""
    project = state['project']
    folder = state['folder']

    def bind(name):
        return host_path(folder, name).replace('\\', '/')

    return ''.join([
        '# Written by two-machine-test.py. Change the script, not this file.\n',
        f'name: {project}\n\n',
        'services:\n',
        '  radarr:\n',
        f'    image: {RADARR_IMAGE}\n',
        f'    container_name: {project}-radarr\n',
        '    restart: unless-stopped\n',
        '    environment:\n',
        '      PUID: "1000"\n',
        '      PGID: "1000"\n',
        '      TZ: "Etc/UTC"\n',
        f'      RADARR__AUTH__APIKEY: "{state["radarr_key"]}"\n',
        '      RADARR__AUTH__METHOD: "External"\n',
        '      RADARR__LOG__ANALYTICSENABLED: "false"\n',
        '    volumes:\n',
        '      - radarr-config:/config\n',
        f'      - "{bind("downloads")}:/downloads"\n',
        f'      - "{bind("movies")}:/movies"\n',
        '    ports:\n',
        f'      - "127.0.0.1:{state["radarr_port"]}:7878"\n',
        # Radarr reaches the Nodearr on this machine by this name. Docker Desktop has it already.
        '    extra_hosts:\n',
        '      - "host.docker.internal:host-gateway"\n',
        '\nvolumes:\n  radarr-config:\n',
    ])


def find_native_files(args):
    """The Nodearr build and the transport binary a native run starts."""
    repository = Path(__file__).resolve().parent.parent
    dll = Path(args.nodearr_dll) if args.nodearr_dll else repository / '_output' / 'net10.0' / 'Nodearr.Console.dll'

    if not dll.exists():
        raise Stop(f'There is no Nodearr build at {dll}.\n  Build it from the repository root:   dotnet build src/Nodearr.sln\n  Or say where one is:                --nodearr-dll PATH')

    name = 'nodearr-transport.exe' if os.name == 'nt' else 'nodearr-transport'
    candidates = [args.transport_bin, os.environ.get('NODEARR_TRANSPORT_BIN')]

    if os.environ.get('CARGO_TARGET_DIR'):
        candidates.append(str(Path(os.environ['CARGO_TARGET_DIR']) / 'release' / name))

    candidates += [str(repository / 'transport' / 'target' / 'release' / name), str(dll.parent / name)]
    transport = next((Path(c) for c in candidates if c and Path(c).exists()), None)

    if transport is None:
        raise Stop(
            'The transport binary was not found.\n'
            '  Build it from the repository root:   cargo build --release --manifest-path transport/Cargo.toml\n'
            f'  Or say where one is:                --transport-bin PATH/{name}')

    return str(dll), str(transport)


def port_in_use(port):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.settimeout(1)

        return probe.connect_ex(('127.0.0.1', port)) == 0


def start_nodearr_process(folder, state):
    """Nodearr on this machine itself, left running after this script ends."""
    environment = os.environ.copy()
    environment.update({
        'Nodearr__Server__Port': str(state['http_port']),
        'Nodearr__Server__BindAddress': '127.0.0.1',
        'Nodearr__Auth__Method': 'External',
        'Nodearr__Auth__ApiKey': state['nodearr_key'],
        'Nodearr__Log__AnalyticsEnabled': 'false',
        'Nodearr__Transport__BinaryPath': state['transport_bin'],
        'Nodearr__Transport__NodeName': state['name'],
        'Nodearr__Transport__Port': str(state['udp_port']),
        'Nodearr__Transport__DownloadFolder': host_path(folder, 'downloads'),
    })

    log = open(folder / 'nodearr.log', 'ab')
    options = {'creationflags': subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW} if os.name == 'nt' else {'start_new_session': True}

    try:
        process = subprocess.Popen(
            ['dotnet', state['nodearr_dll'], '-nobrowser', f'-data={host_path(folder, "nodearr-data")}'],
            env=environment, stdin=subprocess.DEVNULL, stdout=log, stderr=log, **options)
    except FileNotFoundError:
        raise Stop('"dotnet" was not found. A native run needs the .NET SDK that builds Nodearr.')

    state['pid'] = process.pid
    save(folder, state)


def stop_nodearr_process(state):
    pid = state.get('pid')

    if not pid:
        return

    if os.name == 'nt':
        # /T takes the transport with it.
        subprocess.run(['taskkill', '/PID', str(pid), '/T', '/F'], capture_output=True)
    else:
        try:
            os.killpg(pid, 15)
        except (ProcessLookupError, PermissionError):
            pass


def compose_file(state):
    """One Nodearr and one Radarr sharing /downloads and /movies, with nothing kept outside Docker."""
    if state.get('native'):
        return native_compose_file(state)

    project = state['project']
    host_network = state['network'] == 'host'

    nodearr_environment = {
        'PUID': '1000',
        'PGID': '1000',
        'TZ': 'Etc/UTC',

        # Nothing asks anyone to sign in, and the keys are known to this script. That is only
        # all right because neither page is reachable from another machine.
        'Nodearr__Auth__Method': 'External',
        'Nodearr__Auth__ApiKey': state['nodearr_key'],
        'Nodearr__Log__AnalyticsEnabled': 'false',
        'Nodearr__Transport__NodeName': state['name'],
    }
    radarr_environment = {
        'PUID': '1000',
        'PGID': '1000',
        'TZ': 'Etc/UTC',
        'RADARR__AUTH__APIKEY': state['radarr_key'],
        'RADARR__AUTH__METHOD': 'External',
        'RADARR__LOG__ANALYTICSENABLED': 'false',
    }

    if host_network:
        nodearr_environment.update({
            'Nodearr__Server__Port': str(state['http_port']),
            'Nodearr__Server__BindAddress': '127.0.0.1',
            'Nodearr__Transport__Port': str(state['udp_port']),
        })
        radarr_environment.update({
            'RADARR__SERVER__PORT': str(state['radarr_port']),
            'RADARR__SERVER__BINDADDRESS': '127.0.0.1',
        })

    def block(environment):
        return ''.join(f'      {key}: "{value}"\n' for key, value in environment.items())

    lines = [
        '# Written by two-machine-test.py. Change the script, not this file.\n',
        f'name: {project}\n\n',
        'services:\n',
        '  nodearr:\n',
        f'    image: {state["image"]}\n',
        f'    container_name: {project}-nodearr\n',
        '    restart: unless-stopped\n',
        '    environment:\n',
        block(nodearr_environment),
        '    volumes:\n',
        '      - nodearr-config:/config\n',
        '      - downloads:/downloads\n',
        '      - movies:/movies:ro\n',
    ]

    if host_network:
        lines.append('    network_mode: host\n')
    else:
        lines += [
            '    ports:\n',
            f'      - "127.0.0.1:{state["http_port"]}:6633/tcp"\n',
            f'      - "{state["udp_port"]}:6633/udp"\n',
        ]

        if state.get('join_network'):
            lines += ['    networks:\n', '      - default\n', '      - joined\n']

    lines += [
        '\n  radarr:\n',
        f'    image: {RADARR_IMAGE}\n',
        f'    container_name: {project}-radarr\n',
        '    restart: unless-stopped\n',
        '    environment:\n',
        block(radarr_environment),
        '    volumes:\n',
        '      - radarr-config:/config\n',
        '      - downloads:/downloads\n',
        '      - movies:/movies\n',
    ]

    if host_network:
        lines.append('    network_mode: host\n')
    else:
        lines += ['    ports:\n', f'      - "127.0.0.1:{state["radarr_port"]}:7878"\n']

    lines.append('\nvolumes:\n  nodearr-config:\n  radarr-config:\n  downloads:\n  movies:\n')

    if state.get('join_network') and not host_network:
        lines.append(f'\nnetworks:\n  joined:\n    name: {state["join_network"]}\n    external: true\n')

    return ''.join(lines)


def compose(folder, state, *args, check=True):
    return docker('compose', '-p', state['project'], '-f', str(folder / 'docker-compose.yml'), *args, check=check)


# --- The two APIs ---------------------------------------------------------------------------------

def call(base, key, method, path, body=None, timeout=60):
    request = urllib.request.Request(
        base + path,
        method=method,
        headers={'X-Api-Key': key, 'Content-Type': 'application/json'},
        data=json.dumps(body).encode() if body is not None else None)

    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            text = response.read()

            return json.loads(text) if text else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors='replace')[:600]

        raise Stop(f'{method} {base}{path} answered {error.code}: {detail}')


class App:
    def __init__(self, base, key):
        self.base = base
        self.key = key

    def get(self, path, **kw):
        return call(self.base, self.key, 'GET', path, **kw)

    def post(self, path, body=None, **kw):
        return call(self.base, self.key, 'POST', path, body if body is not None else {}, **kw)

    def put(self, path, body, **kw):
        return call(self.base, self.key, 'PUT', path, body, **kw)

    def delete(self, path, body=None, **kw):
        return call(self.base, self.key, 'DELETE', path, body, **kw)


def apps(state):
    nodearr = App(f'http://127.0.0.1:{state["http_port"]}/api/v5', state['nodearr_key'])
    radarr = App(f'http://127.0.0.1:{state["radarr_port"]}/api/v3', state['radarr_key'])

    return nodearr, radarr


def wait_until(what, condition, timeout=120, every=2):
    deadline = time.time() + timeout
    last_error = None

    while time.time() < deadline:
        try:
            value = condition()

            if value:
                return value
        except (Stop, urllib.error.URLError, ConnectionError, TimeoutError, OSError) as error:
            last_error = error

        time.sleep(every)

    raise Stop(f'Gave up after {timeout}s waiting for {what}.' + (f' Last answer: {last_error}' if last_error else ''))


def run_command(app, body, timeout=300):
    queued = app.post('/command', body)

    def done():
        state = app.get(f'/command/{queued["id"]}')

        if state['status'] == 'failed':
            raise Stop(f'{body["name"]} failed: {state.get("message")}')

        return state['status'] == 'completed'

    wait_until(f'{body["name"]} to finish', done, timeout=timeout, every=1)


# --- Setting a side up ---------------------------------------------------------------------------

def start(folder, state):
    nodearr, radarr = apps(state)

    if state.get('native'):
        return start_native(folder, state, nodearr, radarr)

    if not docker('image', 'ls', '-q', state['image']):
        raise Stop(
            f'There is no "{state["image"]}" image on this machine.\n'
            '  Build it from the repository root:   docker build -t nodearr .\n'
            '  Or load one made elsewhere:          docker load -i nodearr-image.tar')

    (folder / 'docker-compose.yml').write_text(compose_file(state))

    step('Starting Nodearr and Radarr')
    compose(folder, state, 'up', '-d')

    wait_until('Radarr to start', lambda: radarr.get('/system/status', timeout=5), timeout=300)
    identity = wait_until(
        'Nodearr and its transport to start',
        lambda: (lambda found: found if found.get('transportRunning') else None)(nodearr.get('/nodes/identity', timeout=5)),
        timeout=180)

    say(f'  Nodearr is up as "{state["name"]}".')

    return nodearr, radarr, identity


def start_native(folder, state, nodearr, radarr):
    # Radarr will not take a path mapping to a folder that is not there yet.
    for name in ('movies', 'downloads/complete', 'nodearr-data'):
        (folder / name).mkdir(parents=True, exist_ok=True)

    def answering():
        try:
            return nodearr.get('/nodes/identity', timeout=3)
        except (Stop, urllib.error.URLError, ConnectionError, TimeoutError, OSError):
            return None

    already = answering()

    if already is None:
        for what, port in (("Nodearr's page", state['http_port']), ("Radarr's page", state['radarr_port'])):
            if port_in_use(port) and not (what.startswith('Radarr') and docker('ps', '-q', '--filter', f'name={state["project"]}-radarr')):
                raise Stop(
                    f'Port {port}, for {what}, is already in use on this machine.\n'
                    '  If the container version of this test is still up, run "down" where you started it.\n'
                    '  Or choose other ports: --http-port, --udp-port, --radarr-port.')

    (folder / 'docker-compose.yml').write_text(compose_file(state))

    step('Starting Radarr in a container, and Nodearr straight on this machine')
    compose(folder, state, 'up', '-d')

    if already is None:
        start_nodearr_process(folder, state)

        if os.name == 'nt':
            say('  If Windows asks whether to allow nodearr-transport or .NET through the firewall, say yes.')

    wait_until('Radarr to start', lambda: radarr.get('/system/status', timeout=5), timeout=300)
    identity = wait_until(
        f'Nodearr and its transport to start (its log is {folder / "nodearr.log"})',
        lambda: (lambda found: found if found and found.get('transportRunning') else None)(answering()),
        timeout=180)

    say(f'  Nodearr is up as "{state["name"]}".')

    return nodearr, radarr, identity


def configure(state, nodearr, radarr):
    """The film in Radarr, and Radarr in Nodearr. Safe to run again."""
    project = state['project']

    native = bool(state.get('native'))

    step(f'Adding {MOVIE_TITLE} to Radarr')
    # A new volume belongs to root, and Radarr imports as its own user.
    docker('exec', f'{project}-radarr', 'chown', 'abc:abc', '/movies', check=not native)

    if not any(folder['path'].rstrip('/') == '/movies' for folder in radarr.get('/rootfolder')):
        radarr.post('/rootfolder', {'path': '/movies'})

    movie = next((m for m in radarr.get('/movie') if m.get('tmdbId') == MOVIE_TMDB_ID), None)

    if movie is None:
        found = wait_until('Radarr to look the film up', lambda: radarr.get(f'/movie/lookup?term=tmdb:{MOVIE_TMDB_ID}'), timeout=90, every=5)[0]
        found.update({
            'qualityProfileId': 1,
            'rootFolderPath': '/movies',
            'monitored': True,
            'minimumAvailability': 'released',
            'addOptions': {'searchForMovie': False},
        })
        movie = radarr.post('/movie', found)

    step('Telling Nodearr about Radarr')
    if not any(application.get('implementation') == 'Radarr' for application in nodearr.get('/applications')):
        host_network = state['network'] == 'host'
        addresses = {
            'nodearrUrl': f'http://localhost:{state["http_port"]}' if host_network else 'http://nodearr:6633',
            'baseUrl': f'http://localhost:{state["radarr_port"]}' if host_network else 'http://radarr:7878',
            'apiKey': state['radarr_key'],
        }

        if native:
            # Radarr is in a container and Nodearr is not, so each needs the other's address from
            # where it stands, and each needs telling where the other's folders are.
            addresses.update({
                'nodearrUrl': f'http://host.docker.internal:{state["http_port"]}',
                'baseUrl': f'http://127.0.0.1:{state["radarr_port"]}',
            })

            if not any(mapping.get('remotePath', '').rstrip('/') == '/movies' for mapping in nodearr.get('/remotepathmapping')):
                nodearr.post('/remotepathmapping', {'host': '127.0.0.1', 'remotePath': '/movies/', 'localPath': host_path(state['folder'], 'movies') + os.sep})

            if not radarr.get('/remotepathmapping'):
                complete = host_path(state['folder'], 'downloads') + os.sep + 'complete' + os.sep
                radarr.post('/remotepathmapping', {'host': 'host.docker.internal', 'remotePath': complete, 'localPath': '/downloads/complete/'})
        definition = next(schema for schema in nodearr.get('/applications/schema') if schema['implementation'] == 'Radarr')

        for field in definition['fields']:
            if field['name'] in addresses:
                field['value'] = addresses[field['name']]

        definition.update({'name': 'Radarr', 'syncLevel': 'fullSync'})
        nodearr.post('/applications', definition)

    wait_until(
        'Nodearr to add itself to Radarr as an indexer and a download client',
        lambda: any(i['name'] == 'Nodearr' for i in radarr.get('/indexer')) and any(c['name'] == 'Nodearr' for c in radarr.get('/downloadclient')),
        timeout=90)
    say('  Radarr has Nodearr as an indexer and as a download client.')

    return movie


def make_film(state, radarr, movie, size_mb):
    """A test video the right length to be taken for the film, imported the way a download is."""
    project = state['project']

    if radarr.get(f'/movie/{movie["id"]}').get('hasFile'):
        say('  Radarr already has a file for it.')

        return

    step(f'Making a test video of about {size_mb} MB (colour bars and noise, {MOVIE_SECONDS // 60} minutes long)')
    kilobits = max(200, int(size_mb * 8192 / MOVIE_SECONDS) - 64)
    seed = f'/downloads/seed/{MOVIE_RELEASE}'

    downloads = host_path(state['folder'], 'downloads').replace('\\', '/') if state.get('native') else f'{project}_downloads'

    docker('exec', f'{project}-radarr', 'mkdir', '-p', seed)
    docker(
        'run', '--rm', '-v', f'{downloads}:/downloads', FFMPEG_IMAGE,
        '-hide_banner', '-loglevel', 'error', '-y',
        '-f', 'lavfi', '-i', 'testsrc2=size=1280x720:rate=12,noise=alls=40:allf=t',
        '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000',
        '-t', str(MOVIE_SECONDS),
        '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p',
        '-b:v', f'{kilobits}k', '-minrate', f'{kilobits}k', '-maxrate', f'{kilobits}k', '-bufsize', f'{kilobits * 2}k',
        '-c:a', 'aac', '-b:a', '64k',
        f'{seed}/{MOVIE_RELEASE}.mkv')
    docker('exec', f'{project}-radarr', 'chown', '-R', 'abc:abc', '/downloads/seed', check=not state.get('native'))

    step('Importing it into Radarr')
    run_command(radarr, {'name': 'DownloadedMoviesScan', 'path': seed, 'importMode': 'move'})
    wait_until('Radarr to hold the file', lambda: radarr.get(f'/movie/{movie["id"]}').get('hasFile'), timeout=120)


def file_summary(state, radarr, movie):
    current = radarr.get(f'/movie/{movie["id"]}')
    file = current.get('movieFile')

    if not file:
        return None

    digest = docker('exec', f'{state["project"]}-radarr', 'sha256sum', file['path'], check=False).split(' ')[0]

    return {'path': file['path'], 'size': file['size'], 'sha256': digest}


def network_summary(identity):
    if identity.get('udp') is False:
        return 'could not confirm that UDP gets out'

    if identity.get('mappingVaries'):
        return 'a different public port for every destination (direct connections usually need a forwarded port)'

    if identity.get('udp'):
        return 'UDP works and the public port stays the same'

    return 'not known yet'


def warnings(nodearr):
    return [item['message'] for item in nodearr.get('/health') or []]


# --- The commands --------------------------------------------------------------------------------

def new_state(args, side, folder):
    docker_desktop = uses_docker_desktop()
    network = args.network or ('bridge' if docker_desktop or platform.system() != 'Linux' else 'host')
    native = {}

    if args.native:
        dll, transport = find_native_files(args)
        native = {'native': True, 'network': 'native', 'nodearr_dll': dll, 'transport_bin': transport}

    return {
        'folder': str(folder),
        'side': side,
        'project': args.project or f'nodearr-test-{side}',
        'image': args.image,
        'name': args.name or ('Sender' if side == 'send' else 'Receiver'),
        'network': network,
        'http_port': args.http_port,
        'udp_port': args.udp_port,
        'radarr_port': args.radarr_port,
        'join_network': args.join_network,
        'nodearr_key': secrets.token_hex(16),
        'radarr_key': secrets.token_hex(16),
        **native,
    }


def state_for(args, side):
    """This side's notes, made on the first run and kept for the ones after."""
    folder = Path(args.dir).expanduser().resolve() / side
    state = load(folder)

    if state is None:
        state = new_state(args, side, folder)
    elif bool(args.native) != bool(state.get('native')) and (args.native or args.network):
        raise Stop(f'This side is already set up {"without" if args.native else "with"} --native. Run "down" first to change that.')

    save(folder, state)

    return folder, state


def pages(state):
    say(f'  Nodearr:  http://localhost:{state["http_port"]}')
    say(f'  Radarr:   http://localhost:{state["radarr_port"]}')


def send(args):
    folder, state = state_for(args, 'send')

    nodearr, radarr, identity = start(folder, state)
    movie = configure(state, nodearr, radarr)
    make_film(state, radarr, movie, args.size_mb)

    step('Reading the library into Nodearr')
    run_command(nodearr, {'name': 'CatalogRefresh'})
    files = wait_until(
        'Nodearr to list the file as readable',
        lambda: [f for f in nodearr.get('/catalog/local/moviefile?pageSize=50')['records'] if f.get('readable')],
        timeout=60)
    file = file_summary(state, radarr, movie)
    say(f'  {len(files)} file, {file["size"] / 1048576:.0f} MB, SHA-256 {file["sha256"]}')

    step('Creating an invite that shares everything')
    invite = nodearr.post('/nodes/invites', {'validHours': 24, 'label': 'two-machine test', 'shareAll': True})

    identity = nodearr.get('/nodes/identity')
    state.update({'sha256': file['sha256'], 'size': file['size']})
    save(folder, state)

    say('\n' + '=' * 78)
    say('This side is ready.')
    pages(state)
    say(f'  This network: {network_summary(identity)}')

    for message in warnings(nodearr):
        say(f'  Nodearr warns: {message}')

    say(f'\nOther nodes reach this one on UDP port {state["udp_port"]}. Nothing else needs to be open.')
    say('\nOn the other machine, run this. The code works once and for 24 hours:\n')
    say(f"  python3 two-machine-test.py receive '{invite['code']}'")
    say('\nThe file it should end up with has this SHA-256:')
    say(f'  {file["sha256"]}')
    say('=' * 78)


def receive(args):
    folder, state = state_for(args, 'receive')

    nodearr, radarr, _ = start(folder, state)
    movie = configure(state, nodearr, radarr)

    trusted = [peer for peer in nodearr.get('/nodes/peers') if peer['state'] == 'trusted']

    if args.code.strip() and args.code.strip() != state.get('code'):
        step('Pairing with the other node')
        peer = nodearr.post('/nodes/invites/redeem', {'code': args.code.strip()})
        say(f'  Paired with {peer["name"]}.')

        # A new code is a new sending side. This test talks to one node at a time, and a node
        # left over from an earlier run would offer the same film and no longer be there.
        for other in nodearr.get('/nodes/peers'):
            if other['id'] != peer['id']:
                if other['state'] == 'trusted':
                    nodearr.post(f'/nodes/peers/{other["id"]}/revoke')

                if other['state'] != 'blocked':
                    nodearr.delete(f'/nodes/peers/{other["id"]}')

        state.update({'code': args.code.strip(), 'peer_node_id': peer['nodeId']})
        save(folder, state)
    elif trusted:
        say(f'\n  Already paired with {trusted[0]["name"]}. The code is not needed again.')
    else:
        raise Stop('"receive" needs the invite code that "send" printed on the other machine.')

    step('Waiting for what the other node shares')
    count = wait_until(
        'the other node to send its catalog',
        lambda: nodearr.get('/catalog/peers/items?pageSize=50')['totalRecords'],
        timeout=120)
    say(f'  It offers {count} file(s).')

    fetch_once(folder, state, nodearr, radarr, movie, args)


def fetch(args):
    side = find_side(args)

    if side != 'receive':
        raise Stop('"fetch" is for the receiving side. On the sending side there is nothing to fetch.')

    folder = Path(args.dir).expanduser() / side
    state = load(folder)
    nodearr, radarr = apps(state)
    movie = next(m for m in radarr.get('/movie') if m.get('tmdbId') == MOVIE_TMDB_ID)

    fetch_once(folder, state, nodearr, radarr, movie, args)


def fetch_once(folder, state, nodearr, radarr, movie, args):
    """Checks the connection, has Radarr search and grab, and reports how it ended."""
    result = {'when': time.strftime('%Y-%m-%d %H:%M:%S %Z')}

    step('Checking how the two nodes are connected (up to 30 seconds)')
    trusted = [p for p in nodearr.get('/nodes/peers') if p['state'] == 'trusted']
    peer = next((p for p in trusted if p['nodeId'] == state.get('peer_node_id')), trusted[0] if trusted else None)

    if peer is None:
        raise Stop('This node trusts no other node. Run "receive CODE" with a new code from the sending side.')

    checked = nodearr.post(f'/nodes/peers/{peer["id"]}/ping', timeout=90)
    path = checked.get('connectionPath')
    result['connection'] = {'direct': 'Direct', 'relay': 'Relay only'}.get(path, 'Could not reach the other node')
    result['address'] = checked.get('connectionAddress')
    result['checkError'] = checked.get('lastError')
    say(f'  {result["connection"]}' + (f' ({result["address"]})' if result['address'] else '') + (f': {result["checkError"]}' if result['checkError'] else ''))

    if path == 'relay':
        say('  The nodes can talk, but a file needs a direct connection. The transfer is tried anyway, to see it reported.')

    identity = nodearr.get('/nodes/identity')
    result['thisNetwork'] = network_summary(identity)

    # A run before this one may have left the film in place, a failed download, or the release on
    # Radarr's blocklist. Any of those would stop Radarr grabbing it again.
    current = radarr.get(f'/movie/{movie["id"]}')

    if current.get('hasFile'):
        radarr.delete(f'/moviefile/{current["movieFile"]["id"]}')

        # Radarr goes on saying it has the file for a moment after it is deleted. Searching in
        # that moment would look like an import that had already happened.
        wait_until('Radarr to notice the film is gone', lambda: not radarr.get(f'/movie/{movie["id"]}').get('hasFile'), timeout=60, every=1)

    for transfer in nodearr.get('/transfers'):
        nodearr.delete(f'/transfers/{transfer["downloadId"]}')

    blocked = [item['id'] for item in radarr.get('/blocklist?pageSize=200').get('records', [])]

    if blocked:
        radarr.delete('/blocklist/bulk', {'ids': blocked})

    # Radarr keeps a download it has just imported in its queue until it next looks at its
    # download clients, and turns down a release it thinks is still in the queue.
    def queue_is_clear():
        records = radarr.get('/queue?pageSize=50').get('records', [])

        if args.debug:
            say(f'  [debug] queue before searching: {[(q.get("status"), q.get("trackedDownloadState")) for q in records]}')

        if records:
            radarr.post('/command', {'name': 'RefreshMonitoredDownloads'})

        return not records

    wait_until('Radarr to finish with the last download', queue_is_clear, timeout=120, every=3)

    step('Having Radarr search for the film and grab it')
    started = time.time()
    radarr.post('/command', {'name': 'MoviesSearch', 'movieIds': [movie['id']]})

    seen = None
    deadline = started + args.timeout
    last_shown = 0

    while time.time() < deadline:
        time.sleep(2)
        transfers = nodearr.get('/transfers')

        if args.debug:
            now = radarr.get(f'/movie/{movie["id"]}')
            say(f'  [debug] {time.time() - started:5.1f}s transfers={[(t["state"], t.get("bytesDone")) for t in transfers]} hasFile={now.get("hasFile")} fileId={(now.get("movieFile") or {}).get("id")}')

        if transfers:
            seen = transfers[0]

            if seen['state'] == 'failed':
                break

            if time.time() - last_shown >= 10 and seen.get('size'):
                last_shown = time.time()
                say(f'  {seen["state"]}: {seen.get("bytesDone", 0) / 1048576:.0f} of {seen["size"] / 1048576:.0f} MB')

        if radarr.get(f'/movie/{movie["id"]}').get('hasFile'):
            break

        if seen is None and time.time() - started > 120:
            break

        if int(time.time() - started) % 10 < 2:
            radarr.post('/command', {'name': 'RefreshMonitoredDownloads'})

    elapsed = time.time() - started
    imported = file_summary(state, radarr, movie)

    if args.debug:
        history = radarr.get(f'/history/movie?movieId={movie["id"]}')
        say('  [debug] history, newest first: ' + ', '.join(f'{h["eventType"]}@{h["date"][11:19]}' for h in history[:8]))
        say(f'  [debug] queue: {[(q.get("status"), q.get("trackedDownloadState")) for q in radarr.get("/queue?pageSize=20").get("records", [])]}')
        say(f'  [debug] commands: {[(c["name"], c["status"]) for c in radarr.get("/command") if c["name"] in ("MoviesSearch", "RefreshMonitoredDownloads")][-4:]}')

    if imported:
        result.update({'transfer': 'Completed and imported', 'size': imported['size'], 'sha256': imported['sha256'], 'seconds': round(elapsed)})
    elif seen is not None and seen['state'] == 'failed':
        result.update({'transfer': f'Failed: {seen.get("failure")}', 'message': seen.get('failureMessage')})
    elif seen is None:
        releases = [r for r in radarr.get(f'/release?movieId={movie["id"]}') if r.get('indexer') == 'Nodearr']
        reasons = sorted({reason for release in releases for reason in release.get('rejections', [])})
        result.update({
            'transfer': 'Radarr did not grab anything',
            'message': ('Radarr found it and turned it down: ' + '; '.join(reasons)) if releases else 'The Nodearr indexer returned nothing for the film.',
        })
    else:
        result.update({'transfer': f'Not finished after {args.timeout}s', 'message': f'{seen["state"]}, {seen.get("bytesDone", 0)} of {seen.get("size", 0)} bytes'})

    result['warnings'] = warnings(nodearr)
    (folder / 'result.json').write_text(json.dumps(result, indent=2))

    say('\n' + '=' * 78)
    say('What to write down')
    say(f'  When:                 {result["when"]}')
    networking = {'host': "in a container on the host's own network", 'native': 'straight on this machine, not in a container'}.get(
        state['network'], 'in a container on a bridge network with published ports')
    say(f'  How Nodearr is run:   {networking}')
    say(f"  This side's network:  {result['thisNetwork']}")
    say(f'  "Check connection":   {result["connection"]}' + (f' ({result["address"]})' if result['address'] else ''))
    say(f'  The transfer:         {result["transfer"]}')

    if imported:
        megabytes = imported['size'] / 1048576
        say(f'  Size and time:        {megabytes:.0f} MB in {result["seconds"]}s, search and import included')
        say(f'  SHA-256 here:         {imported["sha256"]}')
        say('                        It should be the one the sending side printed.')

    if result.get('message'):
        say(f'  Said why:             {result["message"]}')

    for message in result['warnings']:
        say(f'  Nodearr warns:        {message}')

    say(f'\nSaved to {folder / "result.json"}. To try again after changing something:  python3 two-machine-test.py fetch')
    say('=' * 78)


def status(args):
    side = find_side(args)
    folder = Path(args.dir).expanduser() / side
    state = load(folder)
    nodearr, _ = apps(state)

    say(f'Set up as the {"sending" if side == "send" else "receiving"} side, "{state["name"]}".')
    pages(state)
    say(f'  Other nodes reach this one on UDP port {state["udp_port"]}.')

    identity = nodearr.get('/nodes/identity')
    say(f'  This network: {network_summary(identity)}')

    for peer in nodearr.get('/nodes/peers'):
        say(f'  Node {peer["name"]}: {peer["state"]}, last connection check: {peer.get("connectionPath", "unknown")}')

    for message in warnings(nodearr):
        say(f'  Nodearr warns: {message}')


def down(args):
    root = Path(args.dir).expanduser()
    sides = [args.side] if args.side else [name for name in ('send', 'receive') if (root / name / 'state.json').exists()]

    if not sides:
        say('Nothing to remove.')

    for side in sides:
        folder = root / side
        state = load(folder)

        if state is None:
            continue

        compose(folder, state, 'down', '-v', check=False)

        if state.get('native'):
            stop_nodearr_process(state)
            time.sleep(2)

        # Only ever the folder this script made and wrote its notes in.
        shutil.rmtree(folder, ignore_errors=True)
        say(f'Removed the {side} side: its containers, its volumes and {folder}.')


def main():
    parser = argparse.ArgumentParser(description='The two-machine test for Nodearr, as commands.', epilog='Run "send" on one machine and "receive CODE" on the other.')
    parser.add_argument('command', choices=['send', 'receive', 'fetch', 'status', 'down'])
    parser.add_argument('code', nargs='?', help='the invite code, for "receive"')
    parser.add_argument('--name', help='what the other side sees this node called')
    parser.add_argument('--size-mb', type=int, default=100, help='how big a test video "send" makes (default 100)')
    parser.add_argument('--timeout', type=int, default=900, help='how long "receive" and "fetch" wait for the transfer, in seconds (default 900)')
    parser.add_argument('--http-port', type=int, default=6633, help="Nodearr's web page, on this machine only (default 6633)")
    parser.add_argument('--udp-port', type=int, default=6633, help='the UDP port other nodes connect to (default 6633)')
    parser.add_argument('--radarr-port', type=int, default=7878, help="Radarr's web page, on this machine only (default 7878)")
    parser.add_argument('--network', choices=['host', 'bridge'], help="default: the host's network on Linux, a bridge with published ports on Docker Desktop")
    parser.add_argument('--image', default='nodearr', help='the Nodearr image to run (default nodearr)')
    parser.add_argument('--native', action='store_true', help='run Nodearr straight on this machine, from a build of this repository, and only Radarr in a container')
    parser.add_argument('--nodearr-dll', help='with --native: the built Nodearr.Console.dll (default: _output/net10.0 in this repository)')
    parser.add_argument('--transport-bin', help='with --native: the built nodearr-transport binary')
    parser.add_argument('--dir', default='~/nodearr-test', help='where this script keeps its notes (default ~/nodearr-test)')
    parser.add_argument('--side', choices=['send', 'receive'], help='which side, when both are set up on one machine')
    # For trying both sides on one machine: another name for the containers, and a network the
    # two Nodearrs share so that they have a direct route to each other.
    parser.add_argument('--project', help=argparse.SUPPRESS)
    parser.add_argument('--debug', action='store_true', help=argparse.SUPPRESS)
    parser.add_argument('--join-network', help=argparse.SUPPRESS)
    args = parser.parse_args()

    try:
        if args.native and 'microsoft' in platform.release().lower():
            raise Stop('--native has to start a Windows program, so run it from PowerShell, not from WSL:\n  python docker\\two-machine-test.py send --native')

        if args.command == 'send':
            send(args)
        elif args.command == 'receive':
            if not args.code and not load(Path(args.dir).expanduser() / 'receive'):
                raise Stop('"receive" needs the invite code that "send" printed on the other machine.')

            args.code = args.code or ''
            receive(args)
        elif args.command == 'fetch':
            fetch(args)
        elif args.command == 'status':
            status(args)
        else:
            down(args)
    except Stop as stop:
        say(f'\nStopped: {stop}')
        sys.exit(1)
    except KeyboardInterrupt:
        sys.exit(130)


if __name__ == '__main__':
    main()
