"""Numerical body-space stance contacts and first-person wrist camera clearance."""
import pathlib,sys,json,math
ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/art'));sys.path.insert(0,str(pathlib.Path(__file__).parent))
import bpy,fsart
import combat_motion as human_motion
from mathutils import Vector
fsart.reset();arm=human_motion.base.create_skeleton();report={'gait':{},'first_person':{},'kneel':{}}
for name,speed,period,duty in [('walk',1.6,.9,.5),('jog',4,19/30,.36),('sprint',6.5,16/30,.29)]:
 maxerr=0; speeds=[];prev={}
 for i in range(301):
  t=i/300;human_motion.pose(arm,name,t)
  for side,phase,sign in [('Left',t%1,1),('Right',(t+.5)%1,-1)]:
   f,z=human_motion.base.footcycle(phase,speed,period,duty);actual=arm.pose.bones[side+'Foot'].head.copy();target=Vector((sign*.11,-f,z))
   if phase<duty:
    maxerr=max(maxerr,(actual-target).length)
    if side in prev and phase>prev[side][0]:speeds.append((actual.y-prev[side][1].y)/(period/300))
    prev[side]=(phase,actual)
   else:prev.pop(side,None)
 report['gait'][name]={'target_m_s':speed,'stance_max_target_error_m':maxerr,'stance_speed_min_m_s':min(speeds),'stance_speed_max_m_s':max(speeds)}
for name in ('unarmed_ready','punch_a','punch_b','block','shove'):
 mindepth=100;count=0;mincamera=100
 for i in range(61):
  human_motion.pose(arm,name,i/60);head=arm.pose.bones['Head'];deform=head.matrix@head.bone.matrix_local.inverted();eye=deform@Vector((0,-.10,1.634));forward=deform.to_quaternion()@Vector((0,-1,0));up=deform.to_quaternion()@Vector((0,0,1));right=forward.cross(up)
  visible=0
  for side in ('Left','Right'):
   delta=arm.pose.bones[side+'Hand'].head-eye;depth=delta.dot(forward);mindepth=min(mindepth,depth);mincamera=min(mincamera,delta.length)
   if depth>0 and abs(delta.dot(up))/depth<.674 and abs(delta.dot(right))/depth<.9:visible+=1
  count+=visible>0
 report['first_person'][name]={'min_wrist_camera_distance_m':mincamera,'min_forward_depth_m':mindepth,'frames_at_least_one_wrist_in_frustum':count,'sample_frames':61}
for name in ('warm_hands_loop','forage_pick_loop','drink_kneel_loop'):
 human_motion.pose(arm,name,0);report['kneel'][name]={b:list(arm.pose.bones[b].head) for b in ('Hips','RightLowerLeg','RightFoot','LeftFoot')}
p=ROOT/'art/previews/animations_p2/pose_measurements.json';p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(report,indent=2)+'\n');fsart.result(ok=True,report=report)
