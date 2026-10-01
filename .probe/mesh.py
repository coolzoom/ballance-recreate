import sys,struct; from nmo import load
objs=load(sys.argv[1]); by={o['name']:o for o in objs}
def mesh(o):
    c=o['chunk']; r={}
    if c.seek(0x2000) is not None: r['flags']=hex(c.u32())
    if c.seek(0x100000) is not None:
        n=c.u32(); r['mtl']=[(c.u32(),c.u32()) for _ in range(n)]
    if c.seek(0x10000) is not None:
        n=c.u32(); f=[]
        for i in range(n):
            a=c.u32(); b=c.u32(); f.append((a&0xffff,a>>16,b&0xffff,b>>16))
        r['faces']=f[:4]; r['nf']=n
    if c.seek(0x20000) is not None:
        n=c.u32(); fl=c.u32(); sz=c.u32(); r['nv']=n; r['vflags']=hex(fl); r['sz']=sz
        r['p0']=[c.f32() for _ in range(3)]
    return r
for name in sys.argv[2:]:
    o=by[name]; c=o['chunk']
    if o['cid']==32: print(name, mesh(o))
    elif o['cid']==30:
        c.seek(0x1000); print(name,'mat',[hex(c.u32()) for _ in range(4)], c.f32(), [hex(c.u32()) for _ in range(6)])
    elif o['cid']==31:
        for i,a,b in c.idents():
            c.pos=a; print(name,hex(i),c.data[a:min(b,a+8)])
        c.seek(0x10000); n=c.u32(); print([c.string() for _ in range(n)])
    else: print(name,[(hex(i),c.data[a:b]) for i,a,b in c.idents()])
