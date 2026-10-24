"""Read the installed two-parent chain and build a new immutable 49-file bundle.

Does not Install/Restore or change any live file, transaction, preset or lock.
"""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

sys.dont_write_bytecode=True
import nades_14192 as n
import compat_14192 as c
tx=n.tx


def package(compat,compat_sha,compat_id,build_dir,output):
    compat,cm=c.load(compat,compat_sha)
    root=tx.validate_root(cm['cs2Root'],False);tx.ensure_closed()
    steam=n.steam_identity(root)
    c.images(cm,root,installed=True)
    parent=tx.no_links(cm['parent']['backupDir'])
    tx.require(tx.inventory(parent)==cm['parent']['inventory'],'Original Bot parent changed')
    cb=tx.no_links(Path('E:/CS2MOD/backups/compat-14192')/compat_id)
    r=tx.read_json(cb/'recovery-record.json')
    tx.require(r=={'installId':compat_id,'manifestSha256':compat_sha,'cs2Root':str(root),'state':'installed'},'Compatibility layer identity/state mismatch')
    tx.require(tx.read_json(root/n.COMPAT_MARKER)=={'installId':compat_id,'manifestSha256':compat_sha},'Compatibility marker mismatch')
    tx.require(tx.sha(cb/'recovery-manifest.json')==compat_sha,'Compatibility backup manifest changed')
    for p in c.TARGETS:
        tx.require(tx.sha(cb/'preimages'/p)==cm['baselineFiles'][p],'Compatibility preimage changed')
    b=Path(build_dir);br=tx.read_json(b/'build-result.json')
    tx.require(br['upstreamRef']=='009ce03005af68ba68dae62967bddc8e92ad172c','Nade source ref drift')
    dll=b/'source/bin/Release/net10.0/NadeSystem.dll'
    tx.require(tx.sha(dll)==br['dllSha256'] and tx.sha(b/'NadeSystem-patched-source.zip')==br['patchedSourceSha256'],'Built artifact drift')
    tx.require(tx.sha(b/'source/bin/Release/net10.0/NadeSystem.deps.json')==tx.sha(root/(n.PREFIX+'NadeSystem.deps.json')),'Dependency closure changed; stop')
    tx.require(tx.inventory(root/(n.PREFIX+'grenades'))=={},'New data folder must exist and be empty')
    output=tx.no_links(output)
    tx.require(not output.exists() and not Path(str(output)+'.zip').exists(),'Immutable destination already exists')
    output.mkdir(parents=True)
    changes={}
    def payload(source,rel):
        dst=tx.under(output,'payload/'+rel);dst.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(source,dst);changes[rel]={'source':'payload/'+rel,'sha256':tx.sha(dst)}
    payload(dll,n.DLL)
    for rel in sorted(cm['baselineFiles']):
        if rel.startswith(n.OLD_DATA) and rel.endswith('.json'):
            # Reuse the match candidate's destination mapping. Keep old live data
            # in place because the immutable Bot parent owns those original paths.
            payload(root/rel,n.PREFIX+'grenades/'+Path(rel).name)
    source=output/'sources';source.mkdir()
    shutil.copyfile(compat/'baseline-bot/sources/CS2-Bot-NadeSystem-009ce03005af.zip',source/'NadeSystem-original-009ce030.zip')
    for name in ['NadeSystem-patched-source.zip','nade-14192.patch','build-result.json','native-signatures.json']:
        shutil.copyfile(b/name,source/name)
    shutil.copyfile(Path(__file__).with_name('Build-Nades14192.py'),source/'Build-Nades14192.py')
    shutil.copyfile(b/'source/LICENSE',source/'LICENSE-NadeSystem')
    tools=output/'tools';tools.mkdir()
    for name in ['nades_14192.py','Invoke-Nades14192.ps1']:shutil.copyfile(Path(__file__).parent/name,tools/name)
    shutil.copyfile(tx.REPO/'scripts/bot-experiment/bot_experiment.py',tools/'bot_experiment.py')
    shutil.copyfile(tx.REPO/'experiments/nades-14192/README.md',output/'README.md')
    shutil.copyfile(tx.REPO/'temp/evidence/database.json',output/'mirage-points.json')
    baseline={p:cm['changes'][p]['sha256'] if p in c.TARGETS else h for p,h in cm['baselineFiles'].items()}
    m={'schemaVersion':1,'lane':'nades-14192','status':'candidate','realGameAccepted':False,
       'targetBuild':n.BUILD,'botRef':tx.BOT_REF,'difficulty':'Medium','humanPolicy':'preserve',
       'cs2Root':str(root),'builderHead':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
       'sourceRef':br['upstreamRef'],'parents':{
           'bot':{'installId':cm['parent']['installId'],'backupDir':str(parent),'recordName':'install-record.json','marker':tx.MARKER,'inventory':tx.inventory(parent)},
           'compat':{'installId':compat_id,'backupDir':str(cb),'recordName':'recovery-record.json','marker':n.COMPAT_MARKER,'inventory':tx.inventory(cb)}},
       'markers':{p:tx.identity(root/p) for p in [tx.MARKER,n.COMPAT_MARKER]},
       'steamIdentity':steam,'observedSteamManifestSha256':tx.steam_idle(root),
       'baselineFiles':baseline,'gameBinaries':cm['gameBinaries'],'moduleInventory':tx.module_inventory(root),
       'changes':changes,'bundleFiles':{p:v['sha256'] for p,v in tx.inventory(output).items() if v['kind']=='file'}}
    (output/'nade-manifest.json').write_bytes(tx.json_bytes(m));digest=tx.sha(output/'nade-manifest.json')
    (output/'MANIFEST.sha256').write_text(digest+'  nade-manifest.json\n',encoding='utf-8')
    preview=n.install(output,digest,root,Path('E:/CS2MOD/backups/nades-14192'))
    archive=Path(str(output)+'.zip')
    with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED) as z:
        for p,v in tx.inventory(output).items():
            if v['kind']=='file':z.write(output/p,p)
    result={'releaseDir':str(output),'manifestSha256':digest,'zip':str(archive),'zipSha256':tx.sha(archive),
        'dllSha256':br['dllSha256'],'payloadFiles':len(changes),'preview':preview,'installed':False,'realGameAccepted':False}
    Path(str(output)+'.artifact.json').write_bytes(tx.json_bytes(result));print(tx.json_bytes(result).decode())


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for name in ['compat','compat-sha','compat-id','build-dir','output']:p.add_argument('--'+name,required=True)
    a=p.parse_args();package(a.compat,a.compat_sha,a.compat_id,a.build_dir,a.output)
