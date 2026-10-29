// Test transport for hosts reachable by Node in the agent environment.
// Bounded reads only; no page scripts execute and no response contents are logged.
let input='';for await(const chunk of process.stdin)input+=chunk;
try {
  const request=JSON.parse(input);
  const response=await fetch(request.url,{method:request.method,headers:request.headers,body:request.body??undefined,signal:AbortSignal.timeout(25000)});
  const type=response.headers.get('content-type')||'';
  const limit=/^(?:video|audio)\//.test(type)||type==='application/octet-stream'?4096:4_000_000;
  const chunks=[];let size=0;
  if(response.body){const reader=response.body.getReader();while(size<limit){const {done,value}=await reader.read();if(done)break;const selected=value.subarray(0,limit-size);chunks.push(Buffer.from(selected));size+=selected.length}await reader.cancel();}
  process.stdout.write(JSON.stringify({status:response.status,url:response.url,headers:Object.fromEntries(response.headers),data:Buffer.concat(chunks).toString('base64')}));
}catch(e){process.stderr.write(e.message);process.exitCode=1}
