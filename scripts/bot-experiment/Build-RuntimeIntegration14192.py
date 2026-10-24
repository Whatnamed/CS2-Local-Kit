"""Prepare complete pinned candidates over the verified playable three-layer baseline.

No game writes, no parent record changes, no runtime upgrade. Medium is built first;
Low/High use the same binaries/data and only their exact upstream profile differs.
"""
import argparse,copy,shutil,subprocess,sys,zipfile
from pathlib import Path
sys.dont_write_bytecode=True
import bot_experiment as tx
sys.path.insert(0,str(tx.REPO/'scripts/compatibility'))
import nades_14192 as n

def build(args):
    root=tx.no_links(args.root);data=tx.no_links(args.data_root)
    nr=tx.no_links(args.nade_release);cr=tx.no_links(args.compat_release)
    nd=tx.sha(nr/'nade-manifest.json');_,nm=n.load(nr,nd)
    n.context(nm,root,data/'backups/nades-14192',False);n.images(nm,root,'after')
    nid=tx.read_json(root/n.MARKER)['installId']
    n.restore(nr,nd,root,data/'backups/nades-14192',nid)
    tx.require(tx.sha(root/n.DLL)=='75e2573c6db9e60488a75b466468eb791fb108c1ccfd17cac3f70f3b51da8687','Only the real-game-verified Nade DLL is allowed')
    parents=copy.deepcopy(nm['parents'])
    for name,p in parents.items():
        p['manifestName']='candidate-manifest.json' if name=='bot' else 'recovery-manifest.json'
        p['markerIdentity']=tx.identity(root/p['marker'])
    nb=data/'backups/nades-14192'/nid
    parents['nades']={'backupDir':str(nb),'installId':nid,'recordName':'nade-record.json','manifestName':'nade-manifest.json','marker':n.MARKER,'markerIdentity':tx.identity(root/n.MARKER),'inventory':tx.inventory(nb)}
    paths=set(nm['baselineFiles'])|set(nm['changes'])|set(nm['markers'])|{n.MARKER}
    baseline={p:tx.sha(root/p) for p in sorted(paths)}
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=tx.REPO,text=True).strip()
    tx.require(subprocess.run(['git','diff','--quiet'],cwd=tx.REPO).returncode==0 and subprocess.run(['git','diff','--cached','--quiet'],cwd=tx.REPO).returncode==0,'Commit before immutable packaging')
    catalog=tx.read_json(tx.REPO/'src/CS2LocalKit.App/BotCandidates.json')
    results=[]
    for level in args.levels:
        index=['Low','Medium','High'].index(level)
        c=next(c for c in reversed(catalog) if c['Difficulty']==index and c.get('MatchSetup') and not c.get('RuntimeIntegration'))
        source,old=tx.load_candidate(data/'releases/bot-experiment'/c['Directory'],c['ManifestSha256'])
        out=data/'releases/bot-experiment'/('bot-integrated-14192-'+level.lower()+'-'+head[:7])
        tx.require(not out.exists(),'Immutable output exists: '+str(out));shutil.copytree(source,out)
        m=copy.deepcopy(old);m['builderHead']=head;m['targetBuild']=n.BUILD
        m['baselineFiles']=baseline;m['gameBinaries']={p:baseline[p] for p in tx.GAME_BINARIES};m['moduleInventory']=tx.module_inventory(root)
        m['coexistence'].update(reviewedBuild=n.BUILD,humanFiles={p:h for p,h in baseline.items() if tx.human_path(p)},isolatedModules={})
        m['runtimeIntegration']={'schemaVersion':1,'cs2Root':str(root),'parents':parents,'restoreTarget':'verified-playable-medium-human-nades','manualAcceptance':'pending','nadeDllSha256':baseline[n.DLL]}
        review={'status':'ready-for-manual-test','targetBuild':n.BUILD,'gameBinaries':m['gameBinaries'],
                'sourceManifests':{'matchSetup':c['ManifestSha256'],'nades':nd,'compatibility':tx.sha(cr/'recovery-manifest.json')},
                'baseline':'User reports Medium/Human and smoke/flash/HE/molotov accepted; current files and three parent records/backups independently verified',
                'integratedCandidateGameplay':'pending','sourceHead':head}
        (out/'integration-review.json').write_bytes(tx.json_bytes(review));m['coexistence']['evidenceSha256']=tx.sha(out/'integration-review.json')
        m['coexistence']['evidence']='integration-review.json'
        adapter=Path(args.adapter);aim=Path(args.aim)
        for e in m['files']:
            p=e['target'];override=None
            if p.endswith('/BotAimImprover/BotAimImprover.dll'):override=aim
            elif '/LocalKitMatchSetup/' in p:override=adapter/Path(p).name
            elif p!='game/csgo/overrides/botprofile.vpk' and not p.startswith('game/csgo/cfg/') and p in baseline:override=root/p
            if override is not None:
                shutil.copyfile(override,out/e['source']);e['sha256']=tx.sha(override)
        for rel in ['game/csgo/addons/counterstrikesharp/bin/win64/counterstrikesharp.dll','game/csgo/addons/counterstrikesharp/gamedata/gamedata.json']:
            dest='compatibility/'+rel
            (out/dest).parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(root/rel,out/dest)
            m['files'].append({'target':rel,'source':dest,'sha256':baseline[rel],'surface':'compatibility'})
        for p in [cr/'css-corresponding-source.zip',cr/'bot-adapters-corresponding-source.zip',nr/'sources/NadeSystem-patched-source.zip',aim.parents[4]/'BotAimImprover-integrated-source.zip']:
            tx.require(p.is_file(),'Corresponding source missing: '+str(p));shutil.copyfile(p,out/'sources'/p.name)
        for component in m['components']:
            if component['name'] in {'NadeSystem','BotAimImprover'}:
                entry=next(e for e in m['files'] if e['target'].endswith('/'+component['name']+'/'+component['name']+'.dll'))
                component['managedSha256']=entry['sha256']
        m['localKitPatch']={'version':2,'dataLayout':'plugin-grenades-v1','aim':'d796572 + PBot 0x1500 + uniform Head policy + Hook/write evidence',
            'nades':'exact verified DLL and all 48 data files; SmokeCreate repair and v2 creation evidence preserved','liveInstall':False}
        archive=out/('sources/LocalKitMatchSetup-'+head[:12]+'.zip')
        subprocess.run(['git','archive','--format=zip','-o',str(archive),head,'src/CS2LocalKit.MatchSetup','src/CS2LocalKit.Core','data/match-setup'],cwd=tx.REPO,check=True)
        m['matchSetup'].update(sourceHead=head,sourceArchive=str(archive.relative_to(out)).replace('\\','/'),sourceArchiveSha256=tx.sha(archive),manualAcceptance='pending')
        for name in ['bot_experiment.py','runtime_integration.py']:
            shutil.copyfile(tx.REPO/'scripts/bot-experiment'/name,out/'tools'/name)
        wrapper=(tx.REPO/'scripts/bot-experiment/Invoke-BotExperiment.ps1').read_text(encoding='utf-8-sig').replace("'bot_experiment.py'","'runtime_integration.py'")
        (out/'tools/Invoke-BotExperiment.ps1').write_text(wrapper,encoding='utf-8')
        (out/'README.md').write_text('''# CS2 1.41.9.2 integrated candidate

Use the unified Controller: choose Enhanced, map, difficulty, aim/nades and lineups; Prepare Match previews the exact changes before confirmation. It does not start CS2 or edit Steam launch options.

This package requires the exact installed Bot + compatibility + NadeSystem substrate recorded in experiment-manifest.json. All three parent IDs, manifests and backup inventories are immutable. Full Bot payload and corresponding patched sources are included; identical CSS/MetaMod/Nade files are retained, not replaced by older components.

Install/Restore require a closed game, Steam idle, exact build and five game binaries, complete file identities, package digest and parent backup checks. A new integration install ID owns its delta. Difficulty switching restores that ID to the playable Medium substrate, then performs a fresh full preflight before installing the selected profile. Personal Human presets remain separate; their verified fixture sidecar transfers across switches.

To withdraw the integration, use its exact new ID in System Settings > Restore. This returns the current verified playable Medium + Human + NadeSystem baseline, not pure Valve mode. Never restore a parent underneath an active integration. A failure preserves its journal and exact backups; no blind automatic retry.

All synthetic checks are preparation evidence. Real Low/Medium/High switching, Hook/Head-Body comparison, Off-Max grenades, names/TAB/rounds/side change, CT/T cosmetics and performance remain manual acceptance gates.
''',encoding='utf-8')
        m['bundleFiles']={p:v['sha256'] for p,v in tx.inventory(out).items() if v['kind']=='file' and p not in {'experiment-manifest.json','MANIFEST.sha256'}}
        (out/'experiment-manifest.json').write_bytes(tx.json_bytes(m));digest=tx.sha(out/'experiment-manifest.json')
        (out/'MANIFEST.sha256').write_text(digest+'  experiment-manifest.json\n')
        tx.load_candidate(out,digest)
        previous=tx.MARKER;tx.MARKER=tx.INTEGRATION_MARKER
        try:preview=tx.install(out,digest,root,data/'backups/bot-experiment',preserve_human=True)
        finally:tx.MARKER=previous
        result={'Difficulty':index,'Directory':out.name,'ManifestSha256':digest,'PreservesHuman':True,'MatchSetup':True,'RuntimeIntegration':True}
        results.append(result);print(tx.json_bytes(result).decode(),flush=True)
    target=tx.REPO/'temp/integration'/('candidates-'+ '-'.join(args.levels)+'.json')
    target.write_bytes(tx.json_bytes(results))

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root',default='E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive')
    p.add_argument('--data-root',default='E:/CS2MOD');p.add_argument('--levels',nargs='+',choices=['Low','Medium','High'],default=['Medium'])
    for name in ['nade-release','compat-release','adapter','aim']:p.add_argument('--'+name,required=True)
    build(p.parse_args())
