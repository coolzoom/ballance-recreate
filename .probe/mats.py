import sys,glob; from nmo import load
seen={}
for f in sorted(glob.glob("../Ballance.Build.12799282/3D Entities/Level/*.NMO"))+glob.glob("../Ballance.Build.12799282/3D Entities/PH/*.nmo"):
    objs=load(f)
    for o in objs:
        if o['cid']==30:
            c=o['chunk']
            if c.seek(0x1000) is None: continue
            d=c.data[c.pos:c.pos+9]
            tex=objs[d[5]]['name'] if d[5]<len(objs) else d[5]
            k=(o['name'])
            if k not in seen: seen[k]=(hex(d[0]),hex(d[7]),hex(d[8]),tex)
for k,v in sorted(seen.items()): print(k,v)
