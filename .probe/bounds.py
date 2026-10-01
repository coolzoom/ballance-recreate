import sys,struct; from nmo import load
objs=load(sys.argv[1]); 
for o in objs:
    if o['cid']!=32: continue
    if len(sys.argv)>2 and not any(k in o['name'] for k in sys.argv[2:]): continue
    c=o['chunk']
    if c.seek(0x20000) is None: continue
    n=c.u32(); fl=c.u32(); sz=c.u32()
    if fl&0x10: continue
    p=[c.f32() for _ in range(3*n)]
    xs,ys,zs=p[0::3],p[1::3],p[2::3]
    print(o['name'],n,hex(fl),'x',round(min(xs),2),round(max(xs),2),'y',round(min(ys),2),round(max(ys),2),'z',round(min(zs),2),round(max(zs),2))
