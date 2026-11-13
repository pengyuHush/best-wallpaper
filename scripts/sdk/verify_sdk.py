import hashlib, json, os, zipfile

ZIP = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_dl', 'dotnet-sdk-10.0.403-win-x64.zip')


def main():
    rel = json.loads(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '_dl', 'releases.json'), 'rb').read()) \
        if os.path.exists(os.path.join(os.path.dirname(os.path.abspath(__file__)), '_dl', 'releases.json')) else None
    if rel is None:
        import urllib.request
        rel = json.loads(urllib.request.urlopen(urllib.request.Request(
            'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json',
            headers={'User-Agent': 'x'}), timeout=40).read())
    sdk = rel['releases'][0]['sdk']
    entry = [f for f in sdk['files'] if f.get('name') == 'dotnet-sdk-win-x64.zip'][0]
    print('expected name :', entry['name'])
    print('expected hash :', entry['hash'])
    print('expected size :', entry.get('size'))
    print('actual size   :', os.path.getsize(ZIP))

    h = hashlib.sha512()
    with open(ZIP, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    print('actual hash   :', h.hexdigest())
    print('HASH MATCH    :', h.hexdigest() == entry['hash'])

    try:
        with zipfile.ZipFile(ZIP) as z:
            bad = z.testzip()
            names = z.namelist()
            print('zip testzip   :', bad or 'OK')
            print('zip entries   :', len(names))
            print('has dotnet.exe:', 'dotnet.exe' in names)
            print('sample        :', ', '.join(names[:8]))
    except Exception as e:
        print('ZIP OPEN FAIL :', repr(e))


if __name__ == '__main__':
    main()
