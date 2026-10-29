// Capture actual public catalog records/artwork for deterministic UI checks, never API credentials.
import fs from 'node:fs/promises';
import crypto from 'node:crypto';
const base='https://firestore.googleapis.com/v1/projects/animewitcher-1c66d/databases/(default)/documents';
const decode=v=>v.mapValue?fields(v.mapValue.fields):v.arrayValue?(v.arrayValue.values||[]).map(decode):Object.values(v)[0];
const fields=(v={})=>Object.fromEntries(Object.entries(v).map(([k,v])=>[k,decode(v)]));
const response=await fetch(`${base}/Settings/constants`,{signal:AbortSignal.timeout(20000)});if(!response.ok)throw Error(`Settings HTTP ${response.status}`);
const settings=fields((await response.json()).fields).search_settings;
const endpoint=`https://${settings.app_id_v4}-dsn.algolia.net/1/indexes/series/query`;
const headers={'content-type':'application/json','X-Algolia-Application-Id':settings.app_id_v4,'X-Algolia-API-Key':settings.api_key};
const catalogResponse=await fetch(endpoint,{method:'POST',headers,body:JSON.stringify({params:'query=&hitsPerPage=30&analytics=false&clickAnalytics=false'}),signal:AbortSignal.timeout(20000)});
if(!catalogResponse.ok)throw Error(`Catalog HTTP ${catalogResponse.status}`);
const catalog=await catalogResponse.json();
await fs.mkdir('evidence/design-assets',{recursive:true});
await fs.writeFile('evidence/design-catalog.json',JSON.stringify({capturedAt:new Date().toISOString(),source:'Yoru Algolia series, empty query, page 0',total:catalog.nbHits,hits:catalog.hits},null,2));
console.log(JSON.stringify({records:catalog.hits.length,total:catalog.nbHits,firstKeys:Object.keys(catalog.hits[0]),details:catalog.hits[0]?.details,coverCount:catalog.hits.filter(x=>x.cover_uri).length}));
try {
 const id=encodeURIComponent(catalog.hits[0].objectID);
 const r=await fetch(`${base}/anime_list/${id}/episodes_summery/summery`,{signal:AbortSignal.timeout(20000)});
 if(r.ok){const episodes=fields((await r.json()).fields).episodes||[];await fs.writeFile('evidence/design-episodes.json',JSON.stringify({animeId:catalog.hits[0].objectID,episodes:episodes.slice(0,30)}));console.log('Captured real episode rows: '+Math.min(30,episodes.length));
  if(episodes.length){const sr=await fetch(`${base}/anime_list/${id}/episodes/${encodeURIComponent(episodes[0].doc_id)}/servers2/all_servers`,{signal:AbortSignal.timeout(20000)});if(sr.ok)await fs.writeFile('evidence/design-servers.json',JSON.stringify(fields((await sr.json()).fields)));}
 }
}catch{console.log('Episode preview unavailable')}
const urls=[...new Set(catalog.hits.flatMap(x=>[x.poster?.medium||x.poster?.large||x.poster_uri,x.cover_uri]).filter(x=>/^https?:\/\//.test(x||'')))];
let downloaded=0,failed=0;
for(let i=0;i<urls.length;i+=5)await Promise.all(urls.slice(i,i+5).map(async url=>{
  try{const file='evidence/design-assets/'+crypto.createHash('sha256').update(url).digest('hex')+'.img';try{await fs.access(file);downloaded++;return}catch{}
    const r=await fetch(url,{signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error(r.status);const bytes=Buffer.from(await r.arrayBuffer());if(bytes.length>8_000_000)throw Error('Oversized');await fs.writeFile(file,bytes);downloaded++;
  }catch{failed++}
}));
console.log(JSON.stringify({artworkDownloaded:downloaded,artworkUnavailable:failed}));
