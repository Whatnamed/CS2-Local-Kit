"""Synthetic install/restore and interruption regression; never writes a real game tree."""
import copy
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import bot_experiment as tx

TEMP = tx.REPO / 'temp/bot-experiment-tests'
TEMP.mkdir(parents=True,exist_ok=True)


def write(path, value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(value if isinstance(value,bytes) else value.encode())


class TransactionTests(unittest.TestCase):
    def setUp(self):
        self.home=Path(tempfile.mkdtemp(prefix='case-',dir=TEMP))
        self.root=self.home/'steamapps/common/cs2'
        self.release=self.home/'candidate';self.release.mkdir()
        self.backups=self.home/'backups'
        baseline={
            '.bot-experiment-fake-root':'synthetic-test-only',
            'game/csgo/steam.inf':'PatchVersion=1.41.9.1\nClientVersion=2000933\n',
            'game/csgo/gameinfo.gi':'"GameInfo"\n{\n FileSystem\n {\n  SearchPaths\n  {\n   Game csgo/addons/metamod\n   Game csgo\n  }\n }\n}\n',
            'game/csgo/addons/metamod/bin/win64/metamod.2.cs2.dll':'accepted-1473',
            'game/csgo/addons/metamod/bin/win64/server.dll':'accepted-loader',
            'game/csgo/addons/metamod/counterstrikesharp.vdf':'"Metamod Plugin" { "file" "addons/counterstrikesharp/bin/win64/counterstrikesharp" }',
            'game/csgo/addons/metamod/Other.vdf':'"Metamod Plugin" { "file" "addons/Other/bin/Other" }',
            'game/csgo/addons/UnknownServerPlugin.vdf':'"Plugin" { "file" "addons/Unknown/Unknown" }',
            'game/csgo/addons/metamod/ServerConfig.vdf':'user settings',
            'game/csgo/addons/metamod/metaplugins.ini':'; comments only\n',
            'game/csgo/addons/counterstrikesharp/bin/win64/counterstrikesharp.dll':'CSS-1.0.376',
            'game/csgo/addons/counterstrikesharp/api/CounterStrikeSharp.API.dll':'API-1.0.376',
            'game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll':'accepted-Human',
            'game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/config.json':'private config',
            'game/csgo/addons/counterstrikesharp/plugins/OtherPlugin/OtherPlugin.dll':'unknown plugin sentinel',
            'game/csgo/addons/counterstrikesharp/configs/plugins/InventorySimulator/InventorySimulator.json':'private fixture',
            'game/csgo/cfg/my_bot_normal_config.cfg':'preexisting cfg',
            'game/csgo/cfg/personal.cfg':'personal cfg',
        }
        self.binaries={p:'fake-'+p for p in ['game/csgo/bin/win64/server.dll','game/bin/win64/engine2.dll','game/csgo/bin/win64/client.dll','game/bin/win64/schemasystem.dll','game/bin/win64/cs2.exe']}
        baseline.update(self.binaries)
        for p,v in baseline.items():write(self.root/p,v)
        write(self.root.parent.parent/'appmanifest_730.acf','"AppState" { "buildid" "25966348" "StateFlags" "4" }')
        write(self.home/'presets/personal.json','private preset sentinel')
        (self.root/'game/csgo/addons/counterstrikesharp/plugins/BotAI/recordings').mkdir(parents=True)
        files=[]
        assets=[('framework','addons/metamod/bin/win64/metamod.2.cs2.dll'),('framework','addons/metamod/bin/win64/server.dll'),('bot','addons/BotHider/bin/win64/BotHider.dll'),('bot','addons/metamod/BotHider.vdf'),('bot','addons/counterstrikesharp/plugins/BotAI/BotAI.dll'),('bot','addons/counterstrikesharp/plugins/BotHiderImpl/BotHiderImpl.dll'),('bot','addons/counterstrikesharp/shared/BotHiderApi/BotHiderApi.dll'),('bot','overrides/botprofile.vpk'),('bot','cfg/my_bot_normal_config.cfg')]
        for surface,rel in assets:
            src=surface+'/'+rel;write(self.release/src,'candidate-'+rel)
            files.append({'target':'game/csgo/'+rel,'source':src,'surface':surface,'sha256':tx.sha(self.release/src)})
        self.manifest={'schemaVersion':1,'lane':'bot-experiment','status':'candidate','testFixture':True,'botRef':tx.BOT_REF,'metamod':{'build':1481},'cssVersion':'1.0.376','files':files,'baselineFiles':{p:v['sha256'] for p,v in tx.inventory(self.root).items() if v['kind']=='file' and p!='.bot-experiment-fake-root'},'gameBinaries':{p:tx.sha(self.root/p) for p in self.binaries},'targetBuild':{'PatchVersion':'1.41.9.1','ClientVersion':'2000933','buildId':'25966348'},'moduleInventory':tx.module_inventory(self.root)}
        self.seal()

    def tearDown(self):
        # Keep failures for inspection; successful cases are also small task-local evidence.
        pass

    def seal(self):
        self.manifest['bundleFiles']={p:v['sha256'] for p,v in tx.inventory(self.release).items() if v['kind']=='file' and p not in {'experiment-manifest.json','MANIFEST.sha256'}}
        write(self.release/'experiment-manifest.json',tx.json_bytes(self.manifest))
        self.manifest_sha=tx.sha(self.release/'experiment-manifest.json')

    def snap(self):
        return tx.inventory(self.root),tx.inventory(self.home/'presets')

    def run_install(self,apply=True,**kwargs):
        return tx.install(self.release,self.manifest_sha,self.root,self.backups,apply=apply,fake=True,isolate_human=True,**kwargs)

    def assert_rejected(self,call):
        before=self.snap();backups=tx.inventory(self.backups)
        with self.assertRaises(Exception):call()
        self.assertEqual(before,self.snap())
        self.assertEqual(backups,tx.inventory(self.backups))

    def test_full_roundtrip_and_zero_write_previews(self):
        before=self.snap();home=tx.inventory(self.home)
        plan=self.run_install(apply=False)
        self.assertEqual(home,tx.inventory(self.home))
        self.assertEqual({'created','overwritten','modified','isolated','retained'},{e['action'] for e in plan['entries']})
        result=self.run_install();backup=Path(result['backupDir'])
        self.assertFalse((self.root/'game/csgo/addons/counterstrikesharp/plugins/InventorySimulator').exists())
        self.assertEqual('candidate-addons/metamod/bin/win64/server.dll',(self.root/'game/csgo/addons/metamod/bin/win64/server.dll').read_text())
        home=tx.inventory(self.home);tx.restore(backup,fake=True)
        self.assertEqual(home,tx.inventory(self.home))
        tx.restore(backup,apply=True,fake=True)
        self.assertEqual(before,self.snap())

    def set_profile(self, level):
        db=self.release/f'profiles/{level}/botprofile.db'
        write(db, f'// synthetic {level} fixture\nDefault\n Skill = {dict(Low=10,Medium=50,High=90)[level]}\nEnd\n')
        vpk=self.release/'bot/overrides/botprofile.vpk'
        helper=self.home/'pack-test-profile.ps1'
        write(helper,'param($Common,$Db,$Out)\n. $Common\nNew-BotProfileVpk -DbPath $Db -OutPath $Out\nif (-not (Test-BotProfileVpkEmbedsDb $Out $Db)) { throw "Profile mismatch" }\n')
        subprocess.run(['pwsh','-NoProfile','-File',str(helper),'-Common',str(tx.REPO/'scripts/bot-baseline/BotBaseline.Common.ps1'),'-Db',str(db),'-Out',str(vpk)],check=True,capture_output=True)
        self.manifest['defaultDifficulty']=level
        self.manifest['profile']={'difficulty':level,'upstreamPath':f'overrides/{level}/botprofile.db','dbSource':f'profiles/{level}/botprofile.db','dbSha256':tx.sha(db),'vpkSha256':tx.sha(vpk)}
        next(e for e in self.manifest['files'] if e['target'].endswith('/botprofile.vpk'))['sha256']=tx.sha(vpk)
        self.seal()

    def test_three_profiles_roundtrip_and_explicit_record(self):
        hashes=set()
        for level in ['Low','Medium','High']:
            self.set_profile(level);before=self.snap();hashes.add(self.manifest['profile']['vpkSha256'])
            result=self.run_install();backup=Path(result['backupDir'])
            self.assertEqual(tx.read_json(backup/'install-record.json')['profile'],self.manifest['profile'])
            tx.restore(backup,apply=True,fake=True);self.assertEqual(before,self.snap())
        self.assertEqual(len(hashes),3)

    def test_profile_drift_and_selected_source_refused(self):
        self.set_profile('High')
        self.manifest['profile']['difficulty']='Low';self.seal()
        self.assert_rejected(lambda:self.run_install())
        self.set_profile('High');entry=next(e for e in self.manifest['files'] if e['target'].endswith('/botprofile.vpk'))
        write(self.release/entry['source'],b'invalid VPK')
        entry['sha256']=self.manifest['profile']['vpkSha256']=tx.sha(self.release/entry['source']);self.seal()
        self.assert_rejected(lambda:self.run_install())

    def test_profile_record_and_installed_vpk_drift_refused(self):
        self.set_profile('Low');result=self.run_install();backup=Path(result['backupDir'])
        live=self.root/'game/csgo/overrides/botprofile.vpk';original=live.read_bytes();write(live,b'user changed profile')
        self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))
        write(live,original)
        record=tx.read_json(backup/'install-record.json');record['profile']['difficulty']='High'
        write(backup/'install-record.json',tx.json_bytes(record))
        self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))

    def test_each_profile_failure_interruption_and_retry(self):
        for level in ['Low','Medium','High']:
            self.set_profile(level)
            for surface in ['framework','isolation','bot','gameinfo']:
                before=self.snap()
                with self.assertRaisesRegex(RuntimeError,'rolled back'):self.run_install(fault=surface)
                self.assertEqual(before,self.snap())
                with self.assertRaises(KeyboardInterrupt):self.run_install(fault=surface+'-interrupt')
                active=[p.parent for p in self.backups.glob('*/install-record.json') if tx.read_json(p)['state']=='installing']
                self.assertEqual(len(active),1)
                tx.restore(active[0],apply=True,fake=True);self.assertEqual(before,self.snap())
            result=self.run_install();tx.restore(Path(result['backupDir']),apply=True,fake=True)

    def test_fault_rollback_all_four_surfaces(self):
        for surface in ['framework','isolation','bot','gameinfo']:
            with self.subTest(surface=surface):
                before=self.snap()
                with self.assertRaisesRegex(RuntimeError,'rolled back'):self.run_install(fault=surface)
                self.assertEqual(before,self.snap())
                records=[tx.read_json(p) for p in self.backups.glob('*/install-record.json')]
                self.assertTrue(all(r['state']=='rolled-back' for r in records))

    def test_hard_interruption_resumes_from_exact_record(self):
        for surface in ['framework','isolation','bot','gameinfo']:
            with self.subTest(surface=surface):
                before=self.snap()
                with self.assertRaises(KeyboardInterrupt):self.run_install(fault=surface+'-interrupt')
                active=[p.parent for p in self.backups.glob('*/install-record.json') if tx.read_json(p)['state']=='installing']
                self.assertEqual(len(active),1)
                tx.restore(active[0],apply=True,fake=True)
                self.assertEqual(before,self.snap())

    def test_restore_interruption_resumes(self):
        before=self.snap()
        result=self.run_install();backup=Path(result['backupDir'])
        original=tx.os.replace;calls=[]
        def interrupt_after_replace(source,target):
            original(source,target)
            if str(target).startswith(str(self.root)) and not calls:
                calls.append(target);raise KeyboardInterrupt('restore interrupted after atomic write')
        with patch.object(tx.os,'replace',side_effect=interrupt_after_replace):
            with self.assertRaises(KeyboardInterrupt):tx.restore(backup,apply=True,fake=True)
        self.assertEqual(tx.read_json(backup/'install-record.json')['state'],'restoring')
        tx.restore(backup,apply=True,fake=True)
        self.assertEqual(before,self.snap())

    def test_payload_hash_mismatch(self):
        write(self.release/self.manifest['files'][0]['source'],'corrupt')
        self.assert_rejected(self.run_install)

    def test_missing_payload(self):
        (self.release/self.manifest['files'][0]['source']).unlink()
        self.assert_rejected(self.run_install)

    def test_manifest_digest(self):
        self.manifest_sha='0'*64;self.assert_rejected(self.run_install)

    def test_duplicate_and_case_collision_paths(self):
        for upper in [False,True]:
            e=copy.deepcopy(self.manifest['files'][0])
            if upper:e['target']=e['target'].upper()
            self.manifest['files'].append(e);self.seal()
            self.assert_rejected(self.run_install)
            self.manifest['files'].pop()

    def test_malicious_relative_paths(self):
        original=self.manifest['files'][0]['target']
        for path in ['../outside','C:/outside','game/csgo/../outside','game/csgo/a:stream','game/csgo/CON','game/csgo/file.','/absolute']:
            self.manifest['files'][0]['target']=path;self.seal();self.assert_rejected(self.run_install)
        self.manifest['files'][0]['target']=original

    def test_missing_and_unknown_framework_identity(self):
        p=self.root/'game/csgo/addons/metamod/bin/win64/server.dll'
        p.unlink();self.assert_rejected(self.run_install)
        write(p,'unknown framework');self.assert_rejected(self.run_install)

    def test_extra_startup_entry(self):
        p=self.root/'game/csgo/gameinfo.gi'
        write(p,p.read_text().replace('Game csgo\n','Game csgo/overrides/old\nGame csgo\n'))
        # Bypass baseline mismatch to independently exercise the startup parser.
        self.manifest['baselineFiles']['game/csgo/gameinfo.gi']=tx.sha(p);self.seal()
        self.assert_rejected(self.run_install)

    def test_duplicate_startup_entry(self):
        p=self.root/'game/csgo/gameinfo.gi';write(p,p.read_text().replace('Game csgo/addons/metamod','Game csgo/addons/metamod\nGame csgo/addons/metamod'))
        self.manifest['baselineFiles']['game/csgo/gameinfo.gi']=tx.sha(p);self.seal();self.assert_rejected(self.run_install)

    def test_game_build_and_binary_drift(self):
        p=self.root/'game/bin/win64/engine2.dll';write(p,'updated');self.assert_rejected(self.run_install)

    def test_duplicate_install_and_incomplete_history(self):
        result=self.run_install();self.assert_rejected(self.run_install)
        # Marker missing cannot erase history or authorize a second installation.
        (self.root/tx.MARKER).unlink();self.assert_rejected(self.run_install)

    def test_missing_backup_refuses_before_writes(self):
        result=self.run_install();backup=Path(result['backupDir'])
        (backup/'preimages/game/csgo/addons/metamod/bin/win64/server.dll').unlink()
        self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))

    def test_corrupt_backup_refuses_before_writes(self):
        result=self.run_install();backup=Path(result['backupDir'])
        write(backup/'preimages/game/csgo/addons/metamod/bin/win64/server.dll','corrupted backup')
        self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))

    def test_created_and_overwritten_file_drift(self):
        result=self.run_install();backup=Path(result['backupDir'])
        for rel in ['game/csgo/addons/counterstrikesharp/plugins/BotAI/BotAI.dll','game/csgo/addons/metamod/bin/win64/server.dll']:
            p=self.root/rel;original=p.read_bytes();write(p,'user changed file')
            self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True));write(p,original)

    def test_isolation_conflict_refuses_before_writes(self):
        result=self.run_install();backup=Path(result['backupDir'])
        write(self.root/'game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/new.dll','new user tree')
        self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))

    def test_retained_css_drift(self):
        result=self.run_install();backup=Path(result['backupDir'])
        write(self.root/'game/csgo/addons/counterstrikesharp/api/CounterStrikeSharp.API.dll','new CSS')
        self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))

    def test_unlisted_plugin_refused(self):
        write(self.root/'game/csgo/addons/counterstrikesharp/plugins/NewPlugin/New.dll','unknown')
        self.assert_rejected(self.run_install)

    def test_extra_installed_plugin_file_refuses_restore(self):
        result=self.run_install();backup=Path(result['backupDir'])
        write(self.root/'game/csgo/addons/counterstrikesharp/plugins/BotAI/new.dll','unowned user file')
        self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))

    def test_duplicate_json_keys(self):
        p=self.release/'experiment-manifest.json'
        raw=p.read_text();write(p,raw.rstrip()[:-1]+',"botRef":"duplicate"}')
        self.manifest_sha=tx.sha(p);self.assert_rejected(self.run_install)

    def test_all_five_game_binary_identities_required(self):
        self.manifest['gameBinaries'].pop('game/bin/win64/cs2.exe');self.seal()
        self.assert_rejected(self.run_install)

    def test_reparse_target_and_payload(self):
        outside=self.home/'outside';outside.mkdir()
        for root,rel in [(self.root,'game/csgo/overrides'),(self.release,'framework/addons/metamod/bin/win64')]:
            p=root/rel
            if p.exists():
                saved=p.with_name(p.name+'-saved');p.rename(saved)
            else:saved=None
            if os.name=='nt':subprocess.run(['cmd','/c','mklink','/J',str(p),str(outside)],check=True,capture_output=True)
            else:p.symlink_to(outside,target_is_directory=True)
            with self.assertRaisesRegex(ValueError,'Reparse/link'):self.run_install()
            p.rmdir() if os.name=='nt' else p.unlink()
            if saved:saved.rename(p)

    def test_running_cs2_refused_in_fake_mode_too(self):
        with patch.object(tx,'ensure_closed',side_effect=ValueError('cs2.exe is running')):self.assert_rejected(self.run_install)

    def test_human_requires_explicit_isolation(self):
        self.assert_rejected(lambda:tx.install(self.release,self.manifest_sha,self.root,self.backups,apply=True,fake=True))

    def test_fake_mode_refuses_real_executable(self):
        write(self.root/'game/bin/win64/cs2.exe',b'MZ-real-executable')
        self.assert_rejected(self.run_install)

    def test_dry_run_is_default_at_public_entry(self):
        home=tx.inventory(self.home)
        subprocess.run(['python',str(tx.REPO/'scripts/bot-experiment/bot_experiment.py'),'preview','--release',str(self.release),'--manifest-sha256',self.manifest_sha,'--root',str(self.root),'--backup-root',str(self.backups),'--fake','--isolate-human'],check=True,capture_output=True)
        self.assertEqual(home,tx.inventory(self.home))


class CoexistenceTests(unittest.TestCase):
    # Reuse the same synthetic fixture and transaction, without inheriting the isolation-only tests.
    seal = TransactionTests.seal
    snap = TransactionTests.snap
    assert_rejected = TransactionTests.assert_rejected

    def setUp(self):
        guard=patch.object(tx,'ensure_closed')
        guard.start();self.addCleanup(guard.stop)
        TransactionTests.setUp(self)
        write(self.root/tx.HUMAN_CONFIG/'inventories.json','private CT/T fixture sentinel')
        write(self.root/tx.HUMAN_GAMEDATA,'accepted Human gamedata')
        self.manifest['baselineFiles']={p:v['sha256'] for p,v in tx.inventory(self.root).items() if v['kind']=='file' and p!='.bot-experiment-fake-root'}
        self.seal()
        old_plan=tx.make_plan(self.release,self.manifest_sha,self.root,self.backups,fake=True,isolate_human=True)[3]
        self.manifest['humanPolicy']='preserve'
        self.manifest['coexistence']={'status':'ready-for-manual-test','reviewedBuild':self.manifest['targetBuild'],
            'evidenceSha256':tx.digest(b'synthetic compatibility evidence, not a real candidate'),
            'humanFiles':{p:h for p,h in self.manifest['baselineFiles'].items() if tx.human_path(p)},
            'isolatedModules':{e['target']:e['before'] for e in old_plan if e['action']=='isolated' and e['target']!=tx.HUMAN_PLUGIN}}
        self.seal()

    def run_install(self,apply=True,**kwargs):
        return tx.install(self.release,self.manifest_sha,self.root,self.backups,apply=apply,fake=True,preserve_human=True,**kwargs)

    def test_normal_joint_normal_repeat_and_human_unchanged(self):
        before=self.snap();human=tx.inventory(self.root/tx.HUMAN_PLUGIN)
        for _ in range(2):
            all_before=tx.inventory(self.home)
            preview=self.run_install(False)
            self.assertEqual(all_before,tx.inventory(self.home))
            self.assertFalse(any(e['action']=='isolated' and e['target']==tx.HUMAN_PLUGIN for e in preview['entries']))
            installed=self.run_install();backup=Path(installed['backupDir'])
            self.assertEqual(tx.inventory(self.root/tx.HUMAN_PLUGIN),human)
            self.assertEqual(tx.read_json(backup/'install-record.json')['coexistence'],self.manifest['coexistence'])
            tx.restore(backup,apply=True,fake=True)
            self.assertEqual(self.snap(),before)

    def test_joint_failure_and_hard_interruption_all_four_stages(self):
        for surface in ['framework','isolation','bot','gameinfo']:
            before=self.snap()
            with self.assertRaisesRegex(RuntimeError,'rolled back'):self.run_install(fault=surface)
            self.assertEqual(self.snap(),before)
            with self.assertRaises(KeyboardInterrupt):self.run_install(fault=surface+'-interrupt')
            marker=tx.read_json(self.root/tx.MARKER)
            tx.restore(self.backups/marker['installId'],apply=True,fake=True)
            self.assertEqual(self.snap(),before)

    def test_joint_restore_interruption_resumes_exactly(self):
        before=self.snap();installed=self.run_install();backup=Path(installed['backupDir'])
        real=tx.shutil.copyfile
        def interrupt(source,target,*args,**kwargs):
            real(source,target,*args,**kwargs)
            raise KeyboardInterrupt('synthetic restore interruption')
        with patch.object(tx.shutil,'copyfile',side_effect=interrupt):
            with self.assertRaises(KeyboardInterrupt):tx.restore(backup,apply=True,fake=True)
        self.assertEqual(tx.read_json(backup/'install-record.json')['state'],'restoring')
        tx.restore(backup,apply=True,fake=True);self.assertEqual(self.snap(),before)

    def test_joint_human_file_drift_and_bad_backup_refuse_without_writes(self):
        installed=self.run_install();backup=Path(installed['backupDir'])
        for path in [self.root/tx.HUMAN_CONFIG/'inventories.json',backup/'preimages/game/csgo/addons/metamod/bin/win64/server.dll']:
            raw=path.read_bytes();write(path,b'changed')
            self.assert_rejected(lambda:tx.restore(backup,apply=True,fake=True))
            write(path,raw)

    def test_joint_unknown_module_and_game_update_refuse(self):
        path=self.root/'game/csgo/addons/counterstrikesharp/plugins/Unreviewed/Unknown.dll'
        write(path,'unknown')
        self.assert_rejected(self.run_install)
        path.unlink();path.parent.rmdir()
        write(self.root/'game/csgo/bin/win64/server.dll','new game build')
        self.assert_rejected(self.run_install)

    def test_joint_evidence_and_explicit_policy_required(self):
        self.assert_rejected(lambda:tx.install(self.release,self.manifest_sha,self.root,self.backups,apply=True,fake=True))
        self.manifest['coexistence']['status']='blocked';self.seal();self.assert_rejected(self.run_install)
        self.manifest['coexistence']['status']='ready-for-manual-test'
        self.manifest['coexistence']['humanFiles'].pop(tx.HUMAN_GAMEDATA);self.seal();self.assert_rejected(self.run_install)

    def test_joint_steam_update_before_and_during_write_keeps_recovery(self):
        steam=self.root.parent.parent/'appmanifest_730.acf';raw=steam.read_bytes()
        write(steam,raw.replace(b'"4"',b'"1026"'));self.assert_rejected(self.run_install)
        write(steam,raw);before=self.snap();real=tx.os.replace
        def update(source,target):
            real(source,target)
            if str(target).replace('\\','/').endswith('game/csgo/addons/metamod/bin/win64/metamod.2.cs2.dll'):
                write(steam,raw.replace(b'"4"',b'"1026"'))
        with patch.object(tx.os,'replace',side_effect=update):
            with self.assertRaisesRegex(RuntimeError,'recovery blocked'):self.run_install()
        marker=tx.read_json(self.root/tx.MARKER)
        backup=self.backups/marker['installId']
        self.assertTrue((backup/'install-record.json').is_file())
        self.assertTrue((backup/'preimages').is_dir())
        write(steam,raw);tx.restore(backup,apply=True,fake=True);self.assertEqual(before,self.snap())

    def test_public_joint_wrapper_roundtrip_without_real_target(self):
        before=self.snap()
        def invoke(action,apply=False,install_id=None):
            args=['pwsh','-NoProfile','-File',str(tx.REPO/'scripts/bot-experiment/Invoke-BotExperiment.ps1'),'-Action',action,'-BackupRoot',str(self.backups),'-AllowFakeTree']
            if action in {'Preview','Install'}:args+=['-ReleaseDir',str(self.release),'-ManifestSha256',self.manifest_sha,'-Cs2Root',str(self.root),'-PreserveHumanCosmetics']
            else:args+=['-InstallId',install_id]
            if apply:args+=['-Apply']
            result=subprocess.run(args,capture_output=True,text=True,encoding='utf-8',errors='replace')
            self.assertEqual(result.returncode,0,result.stdout+result.stderr)
            return json.loads(result.stdout)
        snapshot=tx.inventory(self.home);invoke('Preview');self.assertEqual(snapshot,tx.inventory(self.home))
        installed=invoke('Install',True);self.assertTrue((self.root/tx.HUMAN_PLUGIN).exists())
        snapshot=tx.inventory(self.home);invoke('Diagnostics',install_id=installed['installId']);self.assertEqual(snapshot,tx.inventory(self.home))
        invoke('Restore',True,installed['installId']);self.assertEqual(self.snap(),before)


if __name__=='__main__':
    unittest.main(verbosity=2)
