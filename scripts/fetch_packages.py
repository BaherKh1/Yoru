from pathlib import Path
import urllib.request,zipfile,xml.etree.ElementTree as E,re,concurrent.futures,json
root=Path(__file__).resolve().parent.parent;feed=root/'toolchain/offline-feed';feed.mkdir(exist_ok=True)
ns={'n':'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
def fetch(item):
 name,version=item;name=name.lower();version=version.lower();file=feed/f'{name}.{version}.nupkg'
 if file.exists() and not zipfile.is_zipfile(file):file.unlink()
 if not file.exists():
  op=urllib.request.build_opener(urllib.request.ProxyHandler({}));url=f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg'
  with op.open(url,timeout=120) as response,file.open('wb') as output:
   while chunk:=response.read(1024*1024):output.write(chunk)
 with zipfile.ZipFile(file) as z:
  nuspec=E.fromstring(z.read(next(n for n in z.namelist() if n.endswith('.nuspec'))))
 dependencies=next((el for el in nuspec.iter() if el.tag.split('}')[-1]=='dependencies'),None)
 result=[]
 if dependencies is not None:
  groups=[g for g in dependencies if g.tag.split('}')[-1]=='group']
  if groups:
   def rank(g):
    tf=g.attrib.get('targetFramework','').lower()
    return (10 if 'net10.0-android' in tf else 9 if 'net9.0-android' in tf else 8 if 'net8.0-android' in tf else 5 if 'netstandard' in tf else 2 if not tf else 0,tf)
   chosen=max(groups,key=rank);children=list(chosen)
  else:children=list(dependencies)
  for child in children:
   if child.tag.split('}')[-1]!='dependency':continue
   v=child.attrib['version'].strip('[]() ').split(',')[0].strip()
   if not v:continue
   result.append((child.attrib['id'].lower(),v.lower()))
 print('Cached dependency',name,version,flush=True);return result
queue=[('Microsoft.NET.ILLink.Tasks','10.0.14'),('Xamarin.AndroidX.AppCompat','1.8.0'),('Xamarin.AndroidX.RecyclerView','1.4.0.6'),('Xamarin.AndroidX.Media3.ExoPlayer','1.11.1'),('Xamarin.AndroidX.Media3.ExoPlayer.Hls','1.11.1'),('Xamarin.AndroidX.Media3.ExoPlayer.Dash','1.11.1'),('Xamarin.AndroidX.Media3.UI','1.11.1')];seen=set()
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
 while queue:
  batch=list(dict.fromkeys(item for item in queue if item not in seen));queue=[]
  for item in batch:seen.add(item)
  for dependencies in pool.map(fetch,batch):queue.extend(item for item in dependencies if item not in seen)
(root/'toolchain/dependencies.json').write_text(json.dumps(sorted(seen),indent=2));print('Dependency packages:',len(seen))
