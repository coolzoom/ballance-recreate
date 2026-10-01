import struct, sys, zlib

class Chunk:
    def __init__(s, buf):
        s.data_ver = buf[0]; s.cls = buf[1]; s.chunk_ver = buf[2]; s.opt = buf[3]
        dw = struct.unpack_from('<I', buf, 4)[0]
        s.data = list(struct.unpack_from('<%dI' % dw, buf, 8))
        s.raw = buf[8:8 + dw * 4]
        p = 8 + dw * 4
        s.ids = s.chunks = s.mgrs = []
        if s.opt & 1:
            n = struct.unpack_from('<I', buf, p)[0]; p += 4
            s.ids = list(struct.unpack_from('<%dI' % n, buf, p)); p += 4 * n
        s.pos = 0

    def idents(s):
        out = []; pos = 0
        if len(s.data) < 2: return out
        while True:
            nxt = s.data[pos + 1]
            end = nxt if nxt else len(s.data)
            out.append((s.data[pos], pos + 2, end))
            if nxt == 0 or nxt + 1 >= len(s.data): break
            pos = nxt
        return out

    def seek(s, ident):
        for i, a, b in s.idents():
            if i == ident:
                s.pos = a; return b - a
        return None

    def u32(s):
        v = s.data[s.pos]; s.pos += 1; return v
    def f32(s):
        v = struct.unpack_from('<f', s.raw, s.pos * 4)[0]; s.pos += 1; return v
    def raw_bytes(s, n):
        b = s.raw[s.pos * 4:s.pos * 4 + n]; s.pos += (n + 3) // 4; return b
    def string(s):
        n = s.u32()
        if n == 0: return ''
        return s.raw_bytes(n).rstrip(b'\0').decode('latin1')


def load(path):
    d = open(path, 'rb').read()
    sig, crc, ckver, fver, fver2, mode, h1pack = struct.unpack_from('<8sIIIIII', d, 0)
    dpack, dunpack, mgrc, objc, maxid, pver, pbuild, h1unpack = struct.unpack_from('<8I', d, 32)
    p = 64
    h1 = d[p:p + h1pack]; p += h1pack
    if h1pack != h1unpack: h1 = zlib.decompress(h1)
    dat = d[p:p + dpack]
    if dpack != dunpack: dat = zlib.decompress(dat)
    objs = []; q = 0
    for i in range(objc):
        oid, cid, fidx, nl = struct.unpack_from('<4I', h1, q); q += 16
        name = h1[q:q + nl].decode('latin1'); q += nl
        objs.append(dict(id=oid, cid=cid, name=name, idx=i))
    q = 0
    for i in range(mgrc):
        g1, g2, sz = struct.unpack_from('<3I', dat, q); q += 12 + sz
    for o in objs:
        sz = struct.unpack_from('<I', dat, q)[0]; q += 4
        o['chunk'] = Chunk(dat[q:q + sz]) if sz else None; q += sz
    return objs

if __name__ == '__main__':
    objs = load(sys.argv[1])
    from collections import Counter
    print(Counter(o['cid'] for o in objs))
    for o in objs[:int(sys.argv[2]) if len(sys.argv) > 2 else 400]:
        c = o['chunk']
        print(o['idx'], hex(o['id']), o['cid'], o['name'], c and [hex(i) for i, a, b in c.idents()], c and len(c.ids))
