import struct
import sys


def main():
    if len(sys.argv) < 3:
        print('usage: png_to_ico.py <in.png> <out.ico>')
        return 1
    src, dst = sys.argv[1], sys.argv[2]
    with open(src, 'rb') as f:
        png = f.read()

    # ICONDIR
    header = struct.pack('<HHH', 0, 1, 1)
    # 0 width / 0 height means 256
    entry = struct.pack('<BBBBHHII', 0, 0, 0, 0, 1, 32, len(png), 6 + 16)
    with open(dst, 'wb') as f:
        f.write(header + entry + png)
    print('ico bytes:', 6 + 16 + len(png))
    return 0


if __name__ == '__main__':
    sys.exit(main())
