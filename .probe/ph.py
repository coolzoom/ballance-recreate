import sys,glob; from nmo import load
for f in sorted(glob.glob("../Ballance.Build.12799282/3D Entities/PH/*.nmo"))+["../Ballance.Build.12799282/3D Entities/Balls.nmo"]:
    objs=load(f); print('###',f.split('/')[-1])
    for o in objs:
        if o['cid'] in (41,23,32,46,33,30,31):
            c=o['chunk']; extra=''
            if o['cid']==41 and c.seek(0x4000) is not None:
                extra='mesh=%d'%c.u32()
                if c.seek(0x100000) is not None:
                    fl=c.u32(); mf=c.u32(); m=[round(c.f32(),2) for _ in range(12)]; extra+=' mf=%x pos=%s'%(mf,m[9:])
            if o['cid']==23: extra=str(c.data[c.seek(0xfffff) and c.pos: c.pos+12] if c.seek(0xfffff) is not None else '')
            if o['cid']==31 and c.seek(0x10000) is not None:
                n=c.u32(); extra=c.string()
            print(' ',o['idx'],o['cid'],o['name'],extra, 'ver',c.data_ver, 'ids' if c.ids else '')
