import glob; from nmo import load
from collections import Counter
allg=Counter()
for f in sorted(glob.glob("../Ballance.Build.12799282/3D Entities/Level/*.NMO")):
    objs=load(f)
    g=[o['name'] for o in objs if o['cid']==23]
    allg.update(g)
print(sorted(allg.items()))
