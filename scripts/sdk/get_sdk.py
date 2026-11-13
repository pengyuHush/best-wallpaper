import json, sys, urllib.request

INDEX = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json'


def get(url, timeout=40):
    req = urllib.request.Request(url, headers={'User-Agent': 'best-wallpaper-build'})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read()


def main():
    idx = json.loads(get(INDEX))
    chans = idx.get('releases-index', [])
    print('available channels: ' + ', '.join(c.get('channel-version', '?') for c in chans))
    want = sys.argv[1] if len(sys.argv) > 1 else '10.0'
    pick = [c for c in chans if c.get('channel-version') == want]
    if not pick:
        pick = [c for c in chans if c.get('latest-sdk', '').startswith(want)]
    if not pick:
        print('NO CHANNEL MATCH for ' + want)
        return 1
    ch = pick[0]
    rel = json.loads(get(ch['releases.json']))
    latest = rel['releases'][0]
    sdkver = latest['sdk']['version']
    files = latest['sdk'].get('files', [])
    win = [f for f in files if f.get('rid') == 'win-x64']
    if not win:
        print('NO win-x64 FILE for sdk ' + sdkver)
        return 1
    f = win[0]
    out = {
        'channel': ch['channel-version'],
        'sdk_version': sdkver,
        'url': f['url'],
        'size': f.get('size'),
        'hash': f.get('hash'),
        'release_date': latest.get('release-date'),
    }
    print(json.dumps(out, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
