from pathlib import Path
import urllib.request,zipfile,hashlib,json,concurrent.futures
root=Path(__file__).resolve().parent.parent;tc=root/'toolchain';sdk=tc/'android-sdk';archives=tc/'downloads';archives.mkdir(exist_ok=True)
def install(item):
 file=archives/item['url'].rsplit('/',1)[1]
 if not file.exists():
  op=urllib.request.build_opener(urllib.request.ProxyHandler({}))
  with op.open(item['url'],timeout=180) as response,file.open('wb') as output:
   while chunk:=response.read(1024*1024):output.write(chunk)
 assert file.stat().st_size==item['size']
 digest=hashlib.sha1()
 with file.open('rb') as source:
  while chunk:=source.read(1024*1024):digest.update(chunk)
 assert digest.hexdigest()==item['sha1'],'Checksum mismatch'
 destination=sdk/Path(*item['path'].split(';'))
 destination.mkdir(parents=True,exist_ok=True)
 with zipfile.ZipFile(file) as z:
  for member in z.infolist():
   parts=Path(member.filename).parts[1:]
   if not parts or member.is_dir():continue
   path=destination.joinpath(*parts);assert path.resolve().is_relative_to(destination.resolve());path.parent.mkdir(parents=True,exist_ok=True)
   with z.open(member) as source,path.open('wb') as output:
    while chunk:=source.read(1024*1024):output.write(chunk)
 print('Installed Android SDK',item['path'],flush=True)
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
 for result in pool.map(install,[item for item in json.loads((tc/'android-downloads.json').read_text()) if item['path']!='emulator']):pass
