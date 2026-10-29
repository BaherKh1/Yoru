// Bounded, read-only protocol check. Never prints service keys or media links.
const base = 'https://firestore.googleapis.com/v1/projects/animewitcher-1c66d/databases/(default)/documents';
function decode(v) {
  if (v.mapValue) return fields(v.mapValue.fields);
  if (v.arrayValue) return (v.arrayValue.values || []).map(decode);
  return Object.values(v)[0];
}
function fields(v = {}) { return Object.fromEntries(Object.entries(v).map(([k,v]) => [k, decode(v)])); }
async function get(url, init) {
  const r = await fetch(url, {...init, signal: AbortSignal.timeout(25000)});
  if (!r.ok) throw new Error(`HTTP ${r.status} from ${new URL(url).hostname}`);
  return r.json();
}
try {
  const c = fields((await get(`${base}/Settings/constants`)).fields);
  const s = c.search_settings;
  if (!s?.app_id_v4 || !s?.api_key) throw new Error('Missing Algolia configuration');
  const headers = {'content-type':'application/json','X-Algolia-Application-Id':s.app_id_v4,'X-Algolia-API-Key':s.api_key};
  const algolia = `https://${s.app_id_v4}-dsn.algolia.net/1/indexes`;
  const catalog = await get(`${algolia}/series/query`, {method:'POST', headers, body:JSON.stringify({params:'query=&hitsPerPage=2&analytics=false&clickAnalytics=false'})});
  const searchTerm = process.argv.find(x=>x.startsWith('--search='))?.slice(9) || 'Naruto';
  const search = await get(`${algolia}/series/query`, {method:'POST', headers, body:JSON.stringify({params:`query=${encodeURIComponent(searchTerm)}&hitsPerPage=12&analytics=false&clickAnalytics=false`})});
  if (process.argv.includes('--titles')) console.log(JSON.stringify(search.hits.map(x=>({id:x.objectID,name:x.name,year:x.details?.year}))));
  console.log(JSON.stringify({settings:true, catalogRows:catalog.hits.length,total:catalog.nbHits,searchRows:search.hits.length,searchEnabled:s.is_search_active,serverMode:c.load_servers_type}));
  const selectedId = process.argv.find(x=>x.startsWith('--id='))?.slice(5);
  const hit = (selectedId && search.hits.find(x=>x.objectID===selectedId)) || search.hits[0] || catalog.hits[0];
  const id = encodeURIComponent(hit.objectID);
  const detail = await get(`${algolia}/series/${id}`, {headers});
  const summaryResponse = await fetch(`${base}/anime_list/${id}/episodes_summery/summery`);
  let eps;
  if(summaryResponse.ok) eps = fields((await summaryResponse.json()).fields).episodes;
  if (!eps?.length) eps = (await get(`${base}/anime_list/${id}/episodes?pageSize=2&orderBy=doc_id`)).documents?.map(d=>({...fields(d.fields),doc_id:d.name.split('/').at(-1)}));
  console.log(JSON.stringify({detail:!!detail.name,episodeCount:eps?.length||0,episodeFields:Object.keys(eps?.[0]||{}),posterShape:typeof detail.poster,story:!!detail.story}));
  if(eps?.length) {
    const ep = encodeURIComponent(eps[0].doc_id);
    let servers;
    if(c.load_servers_type==='summary') servers = fields((await get(`${base}/anime_list/${id}/episodes/${ep}/servers2/all_servers`)).fields).servers;
    else {
      const r = await get(`${base}/anime_list/${id}/episodes/${ep}:runQuery`,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({structuredQuery:{from:[{collectionId:'servers'}],where:{compositeFilter:{op:'AND',filters:[{fieldFilter:{field:{fieldPath:'visible'},op:'EQUAL',value:{booleanValue:true}}},{fieldFilter:{field:{fieldPath:'name'},op:'NOT_EQUAL',value:{stringValue:''}}}]}},limit:20}})});
      servers=r.filter(x=>x.document).map(x=>fields(x.document.fields));
    }
    console.log(JSON.stringify({serverCount:servers?.length||0,serverFields:Object.keys(servers?.[0]||{}),direct:servers?.filter(x=>x.direct_link).length||0,browser:servers?.filter(x=>x.open_browser).length||0}));
    if (process.argv.includes('--hosts')) console.log(JSON.stringify(servers.map(x=>({name:x.name,quality:x.quality,host:new URL(x.link).hostname,browser:x.open_browser,direct:x.direct_link}))));
  }
} catch (e) {console.error(e.message); process.exitCode=1;}
