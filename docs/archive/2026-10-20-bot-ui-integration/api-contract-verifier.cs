using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
static List<string> Read(string path) {
 using var f=File.OpenRead(path);using var pe=new PEReader(f);var r=pe.GetMetadataReader();var result=new List<string>();
 var a=r.GetAssemblyDefinition();result.Add($"assembly:{r.GetString(a.Name)}:{a.Version}");
 foreach(var h in r.TypeDefinitions) {var t=r.GetTypeDefinition(h);result.Add($"type:{r.GetString(t.Namespace)}:{r.GetString(t.Name)}:{t.Attributes}");
 foreach(var m in t.GetMethods()) {var x=r.GetMethodDefinition(m);result.Add($"method:{r.GetString(x.Name)}:{x.Attributes}:{Convert.ToHexString(r.GetBlobBytes(x.Signature))}:"+(x.RelativeVirtualAddress==0?"":Convert.ToHexString(pe.GetMethodBody(x.RelativeVirtualAddress).GetILBytes()!)));}
 foreach(var m in t.GetFields()) {var x=r.GetFieldDefinition(m);result.Add($"field:{r.GetString(x.Name)}:{x.Attributes}:{Convert.ToHexString(r.GetBlobBytes(x.Signature))}");}}
 return result;
}
var old=Read(args[0]);foreach(var path in args.Skip(1)){if(!old.SequenceEqual(Read(path)))throw new Exception("API/IL mismatch: "+path);}Console.WriteLine(JsonSerializer.Serialize(new {status="PASS", original=args[0],compared=args.Skip(1),methodFieldAssemblyAndIlEqual=true}));
