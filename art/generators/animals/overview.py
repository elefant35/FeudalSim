"""Contact sheets of the final family; species order follows the brief."""
import pathlib,bpy,numpy as np
ROOT=pathlib.Path(__file__).resolve().parents[3];out=ROOT/'art/previews/animals'
names=['red_deer_stag','red_deer_hind','wild_boar','hare','grey_wolf','brown_bear','fox','wild_goat']
def save(name,canvas):
 h,w,_=canvas.shape;im=bpy.data.images.new(name,w,h);im.pixels.foreach_set(canvas.ravel());im.filepath_raw=str(out/(name+'.png'));im.file_format='PNG';im.save()
for suffix in ['turntable','silhouette']:
 canvas=np.ones((1024,2048,4),np.float32)
 for i,name in enumerate(names):
  im=bpy.data.images.load(str(out/(name+'_'+suffix+'.png')),check_existing=False);im.scale(512,512);pixels=np.empty(512*512*4,np.float32);im.pixels.foreach_get(pixels);x=i%4*512;y=(1-i//4)*512;canvas[y:y+512,x:x+512]=pixels.reshape(512,512,4);bpy.data.images.remove(im)
 save('family_'+suffix,canvas)
canvas=np.ones((640,1280,4),np.float32)
for i,name in enumerate(names):
 im=bpy.data.images.load(str(out/(name+'_motion.png')),check_existing=False);pixels=np.empty(1280*1920*4,np.float32);im.pixels.foreach_get(pixels);row=pixels.reshape(1920,1280,4)[320:640]
 row=row.reshape(160,2,640,2,4).mean(axis=(1,3));x=i%2*640;y=(3-i//2)*160;canvas[y:y+160,x:x+640]=row;bpy.data.images.remove(im)
save('family_sleep',canvas)
