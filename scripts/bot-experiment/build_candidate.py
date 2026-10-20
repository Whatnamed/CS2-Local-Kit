"""Assemble the frozen 2026-10-20 experiment from verified offline build outputs.

Reads the old evidence worktree; only writes this worktree temp/ and a new release.
No network restore/build or real installation is performed.
"""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid
import zipfile
import xml.etree.ElementTree as ET

import bot_experiment as tx


def git(repo,*args):
    return subprocess.check_output(['git','-c','core.longpaths=true','-C',str(repo),*args])


def tracked(repo,ref,path):
    return git(repo,'show',ref+':'+path)


def save(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(data)


def build(evidence_worktree,archive,cs2_root,releases_root,dotnet):
    tx.ensure_closed()
    repo=tx.REPO;freeze=tx.read_json(repo/'experiments/bot-experiment/candidate-20261020.json')
    evidence=tx.no_links(evidence_worktree);upstream=evidence/'temp/upstream'
    tx.require(git(upstream,'rev-parse','HEAD').decode().strip()==freeze['botRef'],'Wrong upstream HEAD')
    for path,ref in freeze['submodules'].items():
        tx.require(git(upstream/path,'rev-parse','HEAD').decode().strip()==ref,'Wrong component gitlink: '+path)
        subprocess.run(['git','-c','core.longpaths=true','-C',str(upstream/path),'diff','--exit-code','--quiet','--ignore-submodules=all','HEAD'],check=True)
    for entry in freeze['recursiveSubmodules']:
        tx.require(git(upstream/entry['path'],'rev-parse','HEAD').decode().strip()==entry['ref'],'Recursive ref drift: '+entry['path'])
    tx.require(tx.sha(archive)==freeze['metamod']['archiveSha256'],'Official MetaMod archive hash mismatch')
    root=tx.validate_root(cs2_root,False)
    old=tx.read_json(repo/'docs/archive/2026-10-20-bot-offline/local-facts.json')
    for e in old['acceptedFiles']:
        tx.require(tx.sha(root/'game/csgo'/e['path'])==e['expected'],'Accepted framework/Human drift: '+e['path'])
    for e in old['gameBinaries']:
        tx.require(tx.sha(root/'game'/e['path'])==e['sha256'],'Target binary drift: '+e['path'])
    tx.gameinfo_bytes(root)
    destination=tx.no_links(releases_root)/('bot-experiment-e18c029-'+uuid.uuid4().hex[:12])
    tx.require(not destination.exists(),'Release already exists')
    stage=repo/'temp/bot-experiment-build'/destination.name;stage.mkdir(parents=True)
    release=stage/'bundle';release.mkdir()
    files=[];components=[]
    def asset(source,target,surface,data=None):
        tx.require(tx.payload_allowed('game/csgo/'+target,surface),'Unexpected payload target: '+target)
        destination=release/surface/target
        save(destination,Path(source).read_bytes() if data is None else data)
        files.append({'target':'game/csgo/'+target,'source':surface+'/'+target,'sha256':tx.sha(destination),'surface':surface})
    # Inspect the complete official archive, extract only the CS2 x64 loader pair.
    archive_entries=[];seen=set()
    with zipfile.ZipFile(archive) as z:
        for info in z.infolist():
            name=info.filename.rstrip('/');tx.relative(name)
            tx.require(name.casefold() not in seen,'ZIP case/path collision');seen.add(name.casefold())
            tx.require(not ((info.external_attr>>16)&0o170000)==0o120000,'ZIP link refused')
            data=z.read(info) if not info.is_dir() else b''
            archive_entries.append({'path':info.filename,'length':info.file_size,'sha256':tx.digest(data) if not info.is_dir() else None,'selected':name in tx.MM_FILES})
            if name in tx.MM_FILES:asset(None,name,'framework',data)
    tx.require({f['target'][len('game/csgo/'):] for f in files}==tx.MM_FILES,'Official archive misses CS2 x64 pair')
    save(release/'sources/metamod-1481-archive-inventory.json',tx.json_bytes(archive_entries))
    # Native outputs and managed implementation DLLs must match the accepted offline evidence.
    native=tx.read_json(repo/'docs/archive/2026-10-20-bot-offline/native-builds.json')
    for row in native['native']:
        component=row['component'];dll=evidence/row['output']['path'].replace('\\','/')
        tx.require(tx.sha(dll)==row['output']['sha256'],'Native output drift: '+component)
        source=upstream/'addons'/component;ref=freeze['submodules']['addons/'+component]
        version=re.search(rb'project\(\w+ VERSION ([0-9.]+)',tracked(source,ref,'CMakeLists.txt'))[1].decode()
        package=evidence/'temp/native-build'/component/'package'
        for path,value in tx.inventory(package).items():
            if value['kind']!='file':continue
            p=package/path
            if path.endswith('.dll'):
                tx.require(tx.sha(p)==row['output']['sha256'],'Unexpected native DLL')
                data=p.read_bytes()
            elif path.endswith('.vdf'):
                data=p.read_bytes()
                tx.require(('addons/'+component+'/bin/win64/'+component).encode() in data,'Wrong native VDF')
            else:
                data=tracked(source,ref,'configs/'+path)
                tx.require(tx.digest(data)==tx.sha(p),'Staged native config drift: '+path)
            asset(None,path,'bot',data)
        components.append({'name':component,'ref':ref,'version':version,'nativeSha256':row['output']['sha256']})
    managed=tx.read_json(repo/'docs/archive/2026-10-20-bot-offline/managed-builds.json')
    for row in managed['builds']:
        spec=row['outputs'][0];dll=upstream/spec['path'].replace('\\','/')
        tx.require(tx.sha(dll)==spec['sha256'],'Managed output drift: '+row['component'])
        name=dll.stem
        project=upstream/row['project']
        xml=ET.fromstring(project.read_text(encoding='utf-8-sig'))
        for p in sorted(dll.parent.iterdir()):
            if p.is_file() and p.suffix in {'.dll','.json'} and not p.name.startswith(('CounterStrikeSharp.API','Microsoft.','System.')):
                if p.suffix=='.json' and not p.name.endswith(('.deps.json','.runtimeconfig.json')):
                    source=project.parent
                    data=tracked(source,'HEAD',p.name)
                    tx.require(data.replace(b'\r\n',b'\n')==p.read_bytes().replace(b'\r\n',b'\n'),'Managed data output drift: '+p.name)
                    asset(None,'addons/counterstrikesharp/plugins/'+name+'/'+p.name,'bot',data)
                else:
                    asset(p,'addons/counterstrikesharp/plugins/'+name+'/'+p.name,'bot')
        components.append({'name':name,'ref':freeze['submodules'][next(p for p in freeze['submodules'] if p.endswith('/'+row['component']))],'managedSha256':spec['sha256'],'targetFramework':xml.findtext('.//TargetFramework'),'apiReferences':[n.attrib['Version'] for n in xml.findall('.//PackageReference') if n.attrib.get('Include')=='CounterStrikeSharp.API']})
    # API outputs were not all hashed in the earlier report. Rebuild their exact source locally.
    env=os.environ.copy();env.update(DOTNET_CLI_HOME=str(stage/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',DOTNET_CLI_UI_LANGUAGE='en')
    for component in ['BotController','BotHider']:
        source=upstream/'addons'/component;ref=freeze['submodules']['addons/'+component]
        local=stage/component
        for entry in git(source,'ls-tree','-r','--name-only',ref).decode().splitlines():
            if entry.startswith('csharp/') and '/bin/' not in entry and '/obj/' not in entry or entry=='CMakeLists.txt':
                save(local/entry,tracked(source,ref,entry))
        api=component+'Api';project=local/f'csharp/{api}/{api}.csproj'
        commands=[[str(dotnet),'restore',str(project),'--source',str(Path.home()/'.nuget/packages'),'-p:NuGetAudit=false','-p:RestoreFallbackFolders='],[str(dotnet),'build',str(project),'-c','Release','--no-restore','-p:SourceRevisionId='+ref]]
        for i,command in enumerate(commands):
            r=subprocess.run(command,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
            save(release/f'build-evidence/{api}-{i}.log',r.stdout)
            tx.require(r.returncode==0,'Exact API build failed: '+api)
        outputs=list((local/f'csharp/{api}/bin/Release').rglob(api+'.dll'));tx.require(len(outputs)==1,'API output missing')
        for p in outputs[0].parent.iterdir():
            if p.suffix in {'.dll','.json'}:asset(p,f'addons/counterstrikesharp/shared/{api}/{p.name}','bot')
        components.append({'name':api,'ref':ref,'sha256':tx.sha(outputs[0]),'commands':commands})
    harmony=upstream/'addons/BotHider/csharp/BotHiderImpl/bin/Release/net10.0/shared/0Harmony/0Harmony.dll'
    cache=Path.home()/'.nuget/packages/lib.harmony/2.4.2/lib/net10.0/0Harmony.dll'
    tx.require(tx.sha(harmony)==tx.sha(cache),'Harmony dependency drift')
    asset(harmony,'addons/counterstrikesharp/shared/0Harmony/0Harmony.dll','bot')
    save(release/'licenses/Lib.Harmony-2.4.2-LICENSE',(cache.parents[2]/'LICENSE').read_bytes())
    components.append({'name':'Lib.Harmony','version':'2.4.2','sha256':tx.sha(harmony),'source':'existing local NuGet cache; MIT license included'})
    # Runtime data and configs are serialized from immutable Git blobs, not a dirty working copy.
    excluded_bindings=[];ref=freeze['botRef']
    for entry in git(upstream,'ls-tree','-r','--name-only',ref).decode().splitlines():
        if entry.startswith('addons/counterstrikesharp/data/NadeSystem/') or entry.startswith('cfg/') and entry.endswith('.cfg') and 'rush' not in entry.lower():
            data=tracked(upstream,ref,entry)
            if entry.startswith('cfg/'):
                text=data.decode('utf-8-sig')
                cleaned=re.sub(r'(?im)^[ \t]*bind\s+[^\r\n]*subclass_create[^\r\n]*(?:\r?\n|$)','',text)
                if cleaned!=text:excluded_bindings.append(entry)
                tx.require('subclass_create' not in cleaned.lower(),'Unremoved subclass_create command')
                data=cleaned.encode('utf-8')
            asset(None,entry,'bot',data)
    # Medium only: no live difficulty mutation and no Rush behavior trees.
    db=stage/'Medium/botprofile.db';save(db,tracked(upstream,ref,'overrides/Medium/botprofile.db'))
    vpk=stage/'Medium/botprofile.vpk'
    helper=stage/'pack-profile.ps1'
    save(helper,('param([string]$Common,[string]$Db,[string]$Out)\n. $Common\nNew-BotProfileVpk -DbPath $Db -OutPath $Out\nif (-not (Test-BotProfileVpkEmbedsDb $Out $Db)) { throw "VPK verification failed" }\n').encode())
    subprocess.run(['pwsh','-NoProfile','-File',str(helper),'-Common',str(repo/'scripts/bot-baseline/BotBaseline.Common.ps1'),'-Db',str(db),'-Out',str(vpk)],check=True)
    asset(vpk,'overrides/botprofile.vpk','bot')
    # Corresponding sources and licenses: deduplicate exact recursive repositories/refs.
    sources=[{'path':'','ref':ref}]+freeze['recursiveSubmodules'];seen_sources={}
    for item in sources:
        source=upstream/item['path'];source_ref=item['ref']
        url=git(source,'remote','get-url','origin').decode().strip()
        key=(url,source_ref)
        if key in seen_sources:continue
        filename=url.rsplit('/',1)[-1].removesuffix('.git')+'-'+source_ref[:12]+'.zip'
        output=release/'sources'/filename;output.parent.mkdir(parents=True,exist_ok=True)
        subprocess.run(['git','-c','core.longpaths=true','-C',str(source),'archive','--format=zip','-o',str(output),source_ref],check=True)
        seen_sources[key]=filename
        for entry in git(source,'ls-tree','--name-only',source_ref).decode().splitlines():
            if entry.upper().startswith(('LICENSE','COPYING')):
                try:save(release/'licenses'/(filename.removesuffix('.zip')+'-'+entry),tracked(source,source_ref,entry))
                except subprocess.CalledProcessError:raise ValueError('License must be a regular source file: '+entry)
    save(release/'sources/source-index.json',tx.json_bytes([{'url':u,'ref':r,'archive':n} for (u,r),n in seen_sources.items()]))
    save(release/'sources/offline-provenance.json',(repo/'docs/archive/2026-10-20-bot-offline/provenance.json').read_bytes())
    for filename in ['bot_experiment.py','Invoke-BotExperiment.ps1']:
        save(release/'tools'/filename,(repo/'scripts/bot-experiment'/filename).read_bytes())
    for filename in ['README.md','MANUAL-TEST.md']:
        save(release/filename,(repo/'scripts/bot-experiment'/filename).read_bytes())
    save(release/'THIRD-PARTY-NOTICES.txt',b'Experimental candidate, not accepted. Bot-Improver AGPL-3.0; component/dependency licenses and exact corresponding sources are in licenses/ and sources/. MetaMod official runtime comes from the frozen upstream archive; all non-CS2/platform files are excluded. CSS and Human runtime are prerequisites, not distributed. No gameplay/performance acceptance.\n')
    # Freeze current expected preimages and module surface. No private contents are copied.
    baseline={}
    for folder in ['addons','cfg','overrides']:
        start=root/'game/csgo'/folder
        if not start.exists():continue
        for path,value in tx.inventory(start).items():
            if value['kind']=='file' and not path.startswith('counterstrikesharp/logs/'):
                baseline['game/csgo/'+folder+'/'+path]=value['sha256']
    binaries={'game/'+e['path']:e['sha256'] for e in old['gameBinaries']}
    baseline.update(binaries)
    for rel in ['game/csgo/gameinfo.gi','game/csgo/steam.inf']:baseline[rel]=tx.sha(root/rel)
    manifest={'schemaVersion':1,'lane':'bot-experiment','status':'candidate','testFixture':False,'botRef':ref,'submodules':freeze['submodules'],'recursiveSubmodules':freeze['recursiveSubmodules'],'metamod':freeze['metamod'],'cssVersion':'1.0.376','targetBuild':{'PatchVersion':'1.41.9.1','ClientVersion':'2000933','buildId':'25966348'},'gameBinaries':binaries,'baselineFiles':baseline,'moduleInventory':tx.module_inventory(root),'files':files,'components':components,'excludedModules':freeze['excludedModules'],'excludedSubclassBindings':excluded_bindings,'defaultDifficulty':'Medium','rushActive':False,'builderHead':git(repo,'rev-parse','HEAD').decode().strip(),'bundleFiles':{p:v['sha256'] for p,v in tx.inventory(release).items() if v['kind']=='file'}}
    save(release/'experiment-manifest.json',tx.json_bytes(manifest))
    manifest_sha=tx.sha(release/'experiment-manifest.json');save(release/'MANIFEST.sha256',(manifest_sha+'  experiment-manifest.json\n').encode())
    tx.load_candidate(release,manifest_sha)
    # Validate the actual live preflight without applying it or creating a backup.
    tx.make_plan(release,manifest_sha,root,Path(r'E:\CS2MOD\backups\bot-experiment'),isolate_human=True)
    tx.ensure_closed()
    # Publish only a fully validated stage. Both absolute paths are task-owned and
    # checked before the same-volume directory rename; no preexisting output is replaced.
    tx.require(release.is_relative_to(repo/'temp/bot-experiment-build') and destination.parent==tx.no_links(releases_root),'Release path escaped task roots')
    tx.no_links(release);tx.no_links(destination)
    destination.parent.mkdir(parents=True,exist_ok=True)
    tx.require(not destination.exists(),'Release destination appeared during build')
    os.rename(release,destination)
    release=destination
    zip_path=Path(str(release)+'.zip')
    with zipfile.ZipFile(zip_path,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for p,value in tx.inventory(release).items():
            if value['kind']=='file':z.write(release/p,p)
    result={'releaseDir':str(release),'zip':str(zip_path),'zipSha256':tx.sha(zip_path),'manifestSha256':manifest_sha,'payloadFileCount':len(files),'installed':False,'realGameAccepted':False}
    save(Path(str(release)+'.artifact.json'),tx.json_bytes(result))
    print(json.dumps(result,indent=2))
    return result


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--evidence-worktree',required=True);p.add_argument('--metamod-archive',required=True);p.add_argument('--cs2-root',required=True)
    p.add_argument('--releases-root',default=r'E:\CS2MOD\releases\bot-experiment');p.add_argument('--dotnet',required=True)
    a=p.parse_args();build(a.evidence_worktree,a.metamod_archive,a.cs2_root,a.releases_root,a.dotnet)
