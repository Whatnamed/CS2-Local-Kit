"""Reproducible data-only export from exact Git objects; no source tree is vendored."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

BOT = 'e18c029a76b7515ea5cb15cb08c41f03de4c0a57'
HIDER = '9a77fbaf1ee106ec45a451f482e64f26127f2f21'
ROOT = Path(__file__).resolve().parents[2]


def blob(repo, ref, path):
    return subprocess.check_output(['git', '-C', str(repo), 'show', ref + ':' + path])


def main(upstream):
    commands = blob(upstream, BOT, 'Commands.txt')
    raw = blob(upstream/'addons/BotHider', HIDER, 'configs/addons/BotHider/bot_info.json')
    data = json.loads(raw)
    players = []
    for group, enabled in [('players', True), ('disabled_players', False)]:
        for key, p in data.get(group, {}).items():
            if not enabled:
                players.append({'personaId':'disabled-'+hashlib.sha256(key.encode()).hexdigest()[:16], 'name':key, 'steamId64':0, 'enabled':False})
                continue
            if not key.isdigit() or not 0 < int(key) <= 0xffffffff: continue
            # Keep both the disabled status and identity; do not infer persona from
            # an ambiguous nickname, or edit the upstream database for user data.
            if any(x['personaId'] == key for x in players):
                if not enabled: continue
                raise ValueError('Duplicate enabled persona ID')
            players.append({'personaId':key, 'name':p['player_name'], 'steamId64':76561197960265728+int(key), 'enabled':enabled})
    by_name = {}
    for p in players:
        if p['enabled']: by_name.setdefault(p['name'].casefold(), []).append(p['personaId'])
    def slots(names):
        return [{'name':n, 'personaId':by_name.get(n.casefold(), [None])[0] if len(by_name.get(n.casefold(), [])) == 1 else None} for n in names]
    recommended = {1,2,3,4,5,6,7,8,9,10,12,13,15,18,19,20,21,22,23,25,27,28,30,33,38}
    teams=[]; rosters=[]
    date=subprocess.check_output(['git','-C',str(upstream),'show','-s','--format=%cs',BOT],text=True).strip()
    for block in re.split(r'(?m)^(?=\d+\. )', commands.decode()):
        match=re.match(r'(\d+)\. ([^\r\n]+)',block)
        if not match:continue
        number,name=int(match[1]),match[2].strip()
        ct=next(line for line in block.splitlines() if line.startswith('bot_add_ct'))
        names=re.findall(r'bot_add_ct "([^"]+)"',ct)
        if len(names)!=5:raise ValueError('Expected five seats: '+name)
        logo=re.search(r'mp_teamlogo_1 ([^;\s]+)',ct)[1]
        tid='team-'+str(number)
        teams.append({'id':tid,'name':name,'logo':logo,'recommended':number in recommended})
        rosters.append({'id':tid+'-upstream-e18c029','teamId':tid,'name':date+' · upstream e18c029','representativeDate':date,'event':'固定上游命令快照（不表示实时阵容）','source':'https://github.com/ed0ard/CS2-Bot-Improver/blob/'+BOT+'/Commands.txt','slots':slots(names)})
    if len(teams)!=40:raise ValueError('Expected 40 teams')
    for h in json.loads((ROOT/'data/match-setup/history-input.json').read_text(encoding='utf-8')):
        number=h.pop('teamNumber'); names=h.pop('players');tid='team-'+str(number)
        rosters.append({**h,'id':tid+'-'+h['representativeDate'],'teamId':tid,'slots':slots(names)})
    out=ROOT/'data/match-setup';out.mkdir(parents=True,exist_ok=True)
    def save(name,value): (out/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    save('players.json',sorted(players,key=lambda x:(not x['enabled'],x['personaId'])))
    save('rosters.json',{'schemaVersion':1,'botRef':BOT,'botHiderRef':HIDER,'teams':teams,'rosters':rosters})
    save('provenance.json',{'schemaVersion':1,'sources':[{'url':'https://github.com/ed0ard/CS2-Bot-Improver','ref':BOT,'path':'Commands.txt','sha256':hashlib.sha256(commands).hexdigest()},{'url':'https://github.com/XBribo/CS2-Bot-Hider','ref':HIDER,'path':'configs/addons/BotHider/bot_info.json','sha256':hashlib.sha256(raw).hexdigest()}], 'license':'AGPL-3.0; data-only derived index, original notices retained in NOTICE.txt','teams':len(teams),'recommended':sum(t['recommended'] for t in teams),'snapshots':len(rosters),'enabledPlayers':sum(p['enabled'] for p in players),'disabledPlayers':sum(not p['enabled'] for p in players),'distinctEnabledNicknames':len(by_name),'upstreamUnmatchedNames':sorted({s['name'] for r in rosters[:40] for s in r['slots'] if not by_name.get(s['name'].casefold())})})
    print((out/'provenance.json').read_text(encoding='utf-8'))


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--upstream',required=True);a=p.parse_args();main(Path(a.upstream))
