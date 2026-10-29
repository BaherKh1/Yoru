// Diagnostic reads for the reported Jujutsu Kaisen S1E1 regression. No page scripts execute.
import fs from 'node:fs/promises';
const base='https://firestore.googleapis.com/v1/projects/animewitcher-1c66d/databases/(default)/documents';
const decode=v=>v.mapValue?fields(v.mapValue.fields):v.arrayValue?(v.arrayValue.values||[]).map(decode):Object.values(v)[0];
const fields=(v={})=>Object.fromEntries(Object.entries(v).map(([k,v])=>[k,decode(v)]));
const get=async u=>{const r=await fetch(u,{signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error(`HTTP ${r.status}`);return fields((await r.json()).fields)};
const id=encodeURIComponent(process.argv[2]||'Jujutsu Kaisen');
const ep=(await get(`${base}/anime_list/${id}/episodes_summery/summery`)).episodes[0];
const servers=(await get(`${base}/anime_list/${id}/episodes/${encodeURIComponent(ep.doc_id)}/servers2/all_servers`)).servers;
const output={episode:ep.name,servers:[]};
for(const name of ['MF','KF','PD','ST']){
  const server=servers.find(x=>x.name===name);if(!server)continue;
  try{
    const words=await get(`${base}/Settings/servers/servers/${name}`).catch(()=>null);
    const r=await fetch(server.link,{signal:AbortSignal.timeout(20000)});
    const reader=r.body.getReader();let text='';while(text.length<2_000_000){const {done,value}=await reader.read();if(done)break;text+=new TextDecoder().decode(value)}await reader.cancel();
    output.servers.push({server,words,status:r.status,contentType:r.headers.get('content-type'),url:r.url,html:text});
    console.log(JSON.stringify({name,episode:ep.name,status:r.status,contentType:r.headers.get('content-type'),bytes:text.length,words,anchors:[...text.matchAll(/<a\b[^>]*(?:downloadButton|download-link|download_link)[^>]*>/gi)].map(x=>x[0].replace(/https?:[^"'\s]+/g,'[URL]')).slice(0,3),hasVideo: /<video|<source|robotlink|videolink|download-token/.test(text)}));
  }catch(e){console.log(JSON.stringify({name,error:e.message}))}
}
await fs.writeFile('tests/host-probe.local.json',JSON.stringify(output));
