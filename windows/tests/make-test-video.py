"""Generate a two-second MJPEG AVI fixture; no third-party video is downloaded."""
from PIL import Image, ImageDraw
from pathlib import Path
import io, struct
def chunk(name, data): return name+struct.pack('<I',len(data))+data+(b'\0' if len(data)%2 else b'')
def listing(kind, data): return chunk(b'LIST',kind+data)
w,h,fps,count=320,180,10,20
frames=[]
for n in range(count):
    image=Image.new('RGB',(w,h),(30+n*5,20,70))
    ImageDraw.Draw(image).rectangle((n*10,40,n*10+50,110),fill=(150,230,255))
    stream=io.BytesIO();image.save(stream,'JPEG');frames.append(stream.getvalue())
avih=struct.pack('<14I',1000000//fps,max(map(len,frames))*fps,0,0x10,count,0,1,max(map(len,frames)),w,h,0,0,0,0)
strh=struct.pack('<4s4sIHHIIIIIIIIhhhh',b'vids',b'MJPG',0,0,0,0,1,fps,0,count,max(map(len,frames)),0xffffffff,0,0,0,w,h)
strf=struct.pack('<IiiHH4sIiiII',40,w,h,1,24,b'MJPG',w*h*3,0,0,0,0)
hdrl=listing(b'hdrl',chunk(b'avih',avih)+listing(b'strl',chunk(b'strh',strh)+chunk(b'strf',strf)))
movi=b'';index=b'';offset=4
for frame in frames:
    entry=chunk(b'00dc',frame);index+=struct.pack('<4sIII',b'00dc',0x10,offset,len(frame));movi+=entry;offset+=len(entry)
body=b'AVI '+hdrl+listing(b'movi',movi)+chunk(b'idx1',index)
Path('tests/decoder-fixture.avi').write_bytes(b'RIFF'+struct.pack('<I',len(body))+body)
print('Generated 20-frame MJPEG decoder fixture')
