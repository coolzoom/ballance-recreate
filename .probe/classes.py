import glob,sys; from nmo import load
from collections import Counter
for f in sorted(glob.glob("../Ballance.Build.12799282/3D Entities/**/*.nmo",recursive=True)+glob.glob("../Ballance.Build.12799282/3D Entities/Level/*.NMO")):
    try:
        objs=load(f); print(f.split('/')[-1], dict(Counter(o['cid'] for o in objs)), len(objs))
    except Exception as e: print(f, 'ERR', e)
