from pathlib import Path
import urllib.request,json,zipfile,concurrent.futures,os,subprocess
root=Path(__file__).resolve().parent.parent;tc=root/'toolchain';feed=tc/'offline-feed';feed.mkdir(exist_ok=True)
def download(item):
 name=item['id'].lower();version=item['version'].lower();destination=feed/f'{name}.{version}.nupkg'
 if destination.exists():return
 url=f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg'
 op=urllib.request.build_opener(urllib.request.ProxyHandler({}));temporary=destination.with_suffix('.part')
 with op.open(url,timeout=120) as response,temporary.open('wb') as output:
  while chunk:=response.read(1024*1024):output.write(chunk)
 with zipfile.ZipFile(temporary) as z:assert z.testzip() is None
 temporary.replace(destination);print('Cached',item['id'],item['version'],round(destination.stat().st_size/2**20,1),'MiB',flush=True)
items=json.loads((tc/'workload-packages.json').read_text())
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
 for result in pool.map(download,items):pass
env=os.environ.copy();env.update({'DOTNET_ROOT':str(tc/'dotnet'),'DOTNET_CLI_HOME':str(tc/'cli'),'NUGET_PACKAGES':str(tc/'packages'),'DOTNET_NOLOGO':'1','DOTNET_CLI_TELEMETRY_OPTOUT':'1'})
with (tc/'workload-offline.log').open('w',encoding='utf-8') as log:
 code=subprocess.call([str(tc/'dotnet/dotnet.exe'),'workload','install','android','--skip-manifest-update','--source',str(feed)],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
print('Offline workload exit',code,flush=True);print((tc/'workload-offline.log').read_text()[-2500:]);raise SystemExit(code)
