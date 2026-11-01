export function staticRequestAllowed(request,origin,paths){
 try{
  const url=new URL(request.url);
  return request.method==='GET'&&url.origin===origin&&!url.search&&!url.hash&&
   !request.headers.has('Authorization')&&!request.headers.has('Range')&&
   /^\/bilateral-control\/(?:$|(?:[a-z0-9-]+\.(?:html|mjs|css|webmanifest))$|icons\/[a-z0-9-]+\.png$)/.test(url.pathname)&&
   !url.pathname.endsWith('.test.mjs')&&paths.includes(url.pathname);
 }catch{return false;}
}
