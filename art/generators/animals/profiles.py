"""Species proportions in metre space, facing -Y; original authored profiles.
Front/rear limb landmarks are shoulder/hip, elbow/stifle, wrist/hock, sole.
"""
PROFILES={
 'red_deer_stag':dict(kind='deer',height=1.35,length=1.72,width=.27,coat='russet',belly='wool_brown',head=(0,-.96,1.63),muzzle=(0,-1.30,1.51),neck=(0,-.53,1.19),front=(-.53,1.22,-.38,.72,-.55,.32,-.59,.065),hind=(.59,1.18,.36,.71,.62,.33,.57,.065),ear=.18,tail=.14,antlers=True),
 'red_deer_hind':dict(kind='deer',height=1.17,length=1.53,width=.225,coat='wood_light',belly='wool_brown',head=(0,-.88,1.48),muzzle=(0,-1.16,1.37),neck=(0,-.48,1.08),front=(-.47,1.10,-.35,.63,-.48,.28,-.52,.06),hind=(.50,1.05,.29,.61,.55,.28,.48,.06),ear=.17,tail=.14,antlers=False),
 'wild_boar':dict(kind='boar',height=.88,length=1.50,width=.34,coat='hair_dark',belly='wool_brown',head=(0,-.76,.66),muzzle=(0,-1.13,.47),neck=(0,-.50,.67),front=(-.43,.68,-.30,.40,-.43,.20,-.48,.065),hind=(.49,.66,.31,.37,.54,.20,.47,.065),ear=.12,tail=.22),
 'hare':dict(kind='hare',height=.39,length=.56,width=.12,coat='wool_brown',belly='linen',head=(0,-.245,.36),muzzle=(0,-.37,.295),neck=(0,-.17,.28),front=(-.17,.30,-.12,.16,-.20,.075,-.25,.023),hind=(.19,.32,.03,.15,.22,.09,.08,.023),ear=.235,tail=.055),
 'grey_wolf':dict(kind='canid',height=.82,length=1.40,width=.225,coat='wool_grey',belly='linen',head=(0,-.78,.87),muzzle=(0,-1.08,.76),neck=(0,-.47,.72),front=(-.45,.73,-.33,.43,-.45,.22,-.51,.06),hind=(.47,.70,.26,.42,.56,.22,.46,.06),ear=.15,tail=.52),
 'brown_bear':dict(kind='bear',height=1.06,length=1.93,width=.41,coat='wool_brown',belly='hair_brown',head=(0,-.94,.95),muzzle=(0,-1.24,.77),neck=(0,-.60,.84),front=(-.60,.83,-.50,.45,-.63,.18,-.73,.072),hind=(.61,.81,.43,.45,.69,.20,.57,.072),ear=.13,tail=.065),
 'fox':dict(kind='canid',height=.41,length=.76,width=.105,coat='brannoch_rust',belly='linen',head=(0,-.40,.45),muzzle=(0,-.60,.395),neck=(0,-.23,.355),front=(-.235,.365,-.18,.21,-.24,.105,-.28,.034),hind=(.255,.35,.16,.20,.30,.10,.24,.034),ear=.115,tail=.42),
 'wild_goat':dict(kind='goat',height=.86,length=1.23,width=.245,coat='wool_grey',belly='wool_brown',head=(0,-.64,1.13),muzzle=(0,-.85,.98),neck=(0,-.37,.76),front=(-.36,.78,-.27,.46,-.38,.23,-.43,.055),hind=(.40,.74,.23,.43,.46,.23,.39,.055),ear=.15,tail=.12),
}
