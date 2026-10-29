from pathlib import Path
import json,urllib.request,hashlib,zipfile
root=Path(__file__).resolve().parent.parent;tc=root/'toolchain';tc.mkdir(exist_ok=True);info=json.loads((root/'scripts/sdk-download.json').read_text());archive=tc/'dotnet-sdk.zip';destination=tc/'dotnet'
if (destination/'dotnet.exe').exists():raise SystemExit(0)
if not archive.exists():
 opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
 with opener.open(info['url'],timeout=180) as response,archive.open('wb') as output:
  while chunk:=response.read(1024*1024):output.write(chunk)
digest=hashlib.sha512()
with archive.open('rb') as source:
 while chunk:=source.read(1024*1024):digest.update(chunk)
assert digest.hexdigest().lower()==info['hash'].lower(),'SDK checksum mismatch'
with zipfile.ZipFile(archive) as z:
 for member in z.infolist():assert (destination/member.filename).resolve().is_relative_to(destination.resolve())
 z.extractall(destination)
print('Checksum-verified isolated .NET SDK installed')
