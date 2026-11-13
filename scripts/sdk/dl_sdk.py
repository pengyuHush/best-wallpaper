import hashlib, os, sys, time, urllib.request

URL = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-win-x64.zip'
EXPECTED_SHA512 = 'cca8d82788252a8d13e778150a5e8393e204af274a12d19de7da10a0d98370a4edd5361ce5da73d0db13997fe5191e5db3a69c9665c6ddd6fba0d77845046f3c'
DEST = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_dl', 'dotnet-sdk-10.0.403-win-x64.zip')


def main():
    os.makedirs(os.path.dirname(DEST), exist_ok=True)
    total_expected = 300745255
    resume = os.path.getsize(DEST) if os.path.exists(DEST) else 0
    if resume >= total_expected:
        print(f'already complete: {resume} bytes')
        return verify()
    headers = {'User-Agent': 'best-wallpaper-build'}
    if resume:
        headers['Range'] = f'bytes={resume}-'
        print(f'resuming at {resume}')
    req = urllib.request.Request(URL, headers=headers)
    t0 = time.time()
    with urllib.request.urlopen(req, timeout=120) as r, open(DEST, 'ab') as f:
        last = time.time()
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
            resume += len(chunk)
            if time.time() - last > 5:
                last = time.time()
                mb = resume / 1048576
                rate = mb / max(1e-6, time.time() - t0)
                print(f'{mb:.1f} MB / {total_expected/1048576:.1f} MB  ({rate:.2f} MB/s)', flush=True)
    print(f'download finished: {os.path.getsize(DEST)} bytes in {time.time()-t0:.0f}s')
    return verify()


def verify():
    h = hashlib.sha512()
    with open(DEST, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    got = h.hexdigest()
    ok = got == EXPECTED_SHA512
    print(('SHA512 OK  ' if ok else 'SHA512 MISMATCH  ') + got)
    return 0 if ok else 2


if __name__ == '__main__':
    sys.exit(main())
