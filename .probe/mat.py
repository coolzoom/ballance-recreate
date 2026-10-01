import sys,struct; from nmo import load
objs=load(sys.argv[1])
for o in objs:
    if o['cid'] in (30,31) and o['idx']<120:
        c=o['chunk']
        for i,a,b in c.idents():
            print(o['idx'],o['cid'],o['name'],hex(i),b-a,[hex(x) for x in c.data[a:min(b,a+30)]])
        if o['cid']==31:
            print('   raw', c.raw[ (c.idents()[0][1])*4 : (c.idents()[0][1])*4+80])
