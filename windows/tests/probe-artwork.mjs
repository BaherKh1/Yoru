import fs from 'node:fs';
const settings=await (await fetch('https://firestore.googleapis.com/v1/projects/animewitcher-1c66d/databases/(default)/documents/Settings/constants')).json();
const fields=settings.fields.search_settings.mapValue.fields;
const app=fields.app_id_v4.stringValue,key=fields.api_key.stringValue;
const a=await (await fetch(`https://${app.toLowerCase()}-dsn.algolia.net/1/indexes/series/query`,{method:'POST',headers:{'X-Algolia-Application-Id':app,'X-Algolia-API-Key':key,'Content-Type':'application/json'},body:JSON.stringify({params:'query=Arcane&hitsPerPage=10&analytics=false'})})).json();
fs.writeFileSync('evidence/v7-arcane.json',JSON.stringify(a,null,2));
const urls=['https://anime.atsit.in/l/wp-content/uploads/2023/07/solo-leveling-trailer-chinh-thuc-thu-2.jpg'];
for(const x of a.hits){console.log(JSON.stringify({name:x.name,poster:x.poster,poster_uri:x.poster_uri,cover_uri:x.cover_uri}));urls.push(x.poster?.medium,x.poster?.large,x.poster_uri)}
const checks=await Promise.all([...new Set(urls.filter(Boolean))].map(async url=>{try{const r=await fetch(url,{signal:AbortSignal.timeout(20000)});const b=Buffer.from(await r.arrayBuffer()); return {url,status:r.status,mime:r.headers.get('content-type'),bytes:b.length,signature:b.subarray(0,16).toString('hex')};}catch(e){return {url,error:e.message}}}));
fs.writeFileSync('evidence/v7-source-probes.json',JSON.stringify(checks,null,2)); console.log(checks);
