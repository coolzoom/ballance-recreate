import sys; from nmo import load
objs=load(sys.argv[1]); idx={o['idx']:o for o in objs}
from collections import Counter
for o in objs:
    if o['cid'] in (23,) : print('GROUP',o['idx'],o['name'], o['chunk'] and len(o['chunk'].data))
names=set(sys.argv[2:])
for o in objs:
    if o['name'] in names:
        c=o['chunk']; print('==',o['idx'],o['cid'],o['name'],[(hex(i),a,b) for i,a,b in c.idents()], 'ids',c.ids[:20], 'opt',c.opt,'ver',c.data_ver,c.chunk_ver)
        print(c.data[:60])
