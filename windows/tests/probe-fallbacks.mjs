import fs from 'node:fs'; import crypto from 'node:crypto';
const a=JSON.parse(fs.readFileSync('evidence/v7-arcane.json'));const original=a.hits.find(x=>x.name==='Arcane');
const urls=[original.cover_uri,original.poster_uri.split('/revision/')[0],'https://cdn.myanimelist.net/images/anime/1801/142390l.jpg','https://cdn.myanimelist.net/images/anime/9/39495.jpg'];
const out=await Promise.all(urls.map(async url=>{try{const r=await fetch(url,{signal:AbortSignal.timeout(15000)});const b=Buffer.from(await r.arrayBuffer()); if(r.ok){fs.writeFileSync('evidence/design-assets/'+crypto.createHash('sha256').update(url).digest('hex')+'.img',b)}return {url,status:r.status,mime:r.headers.get('content-type'),bytes:b.length}}catch(e){return {url,error:e.message}}}));
fs.writeFileSync('evidence/v7-fallback-probes.json',JSON.stringify(out,null,2));console.log(out);
