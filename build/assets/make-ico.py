"""Packs PNG files into a Windows .ico (PNG-compressed entries, Vista+)."""
import struct, sys
out, pngs = sys.argv[1], sys.argv[2:]
entries = []
for p in pngs:
    data = open(p, 'rb').read()
    w, h = struct.unpack('>II', data[16:24])
    entries.append((w, h, data))
header = struct.pack('<HHH', 0, 1, len(entries))
offset = 6 + 16 * len(entries)
dirs, blobs = b'', b''
for w, h, data in entries:
    dirs += struct.pack('<BBBBHHII', w % 256, h % 256, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
    blobs += data
open(out, 'wb').write(header + dirs + blobs)
