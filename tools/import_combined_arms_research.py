"""Audit/import Combined Arms research only. Run with --apply to replace base data.

Gameplay unlock assets are deliberately inventoried, not imported as an entire mod.
All overwritten files and removed technologies are archived before writing.
"""
import argparse
import collections
import datetime
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'game/Content'
MOD = ROOT / 'game/Mods/Combined Arms'

def technologies(folder):
    result = {}
    for path in sorted(folder.rglob('*.xml')):
        if path.stem in result:
            raise ValueError('Duplicate technology UID: '+path.stem)
        result[path.stem] = (path, ET.parse(path).getroot())
    return result

def text_blocks(path):
    text = path.read_text(encoding='utf-8-sig')
    return text, [(m.group(0), int(re.search(r'^\s+Id:\s*(\d+)', m.group(0), re.M).group(1)))
                  for m in re.finditer(r'^\w[^\n]*:\s*\n(?:[ \t].*(?:\n|$)|\s*\n)*', text, re.M)
                  if re.search(r'^\s+Id:\s*(\d+)', m.group(0), re.M)]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    assert MOD.is_dir(), 'Combined Arms is not installed'
    old, new = technologies(BASE/'Technology'), technologies(MOD/'Technology')
    graph = {uid: [e.text for e in tree.findall('./LeadsTo/LeadsToTech/UID')] for uid, (_, tree) in new.items()}
    missing_edges = [(uid, child) for uid, children in graph.items() for child in children if child not in new]
    # Reject cycles before replacing anything: the runtime recursively walks dependencies.
    visited, active = set(), set()
    def visit(uid):
        if uid in active: raise ValueError('Cyclic technology dependency: '+uid)
        if uid in visited or uid not in graph: return
        active.add(uid)
        for child in graph[uid]: visit(child)
        active.remove(uid)
        visited.add(uid)
    for uid in graph: visit(uid)
    ids = {int(e.text) for _, tree in new.values() for e in tree.iter()
           if e.tag in ('NameIndex', 'DescriptionIndex', 'BonusIndex', 'BonusNameIndex') and e.text and e.text.isdigit() and int(e.text)>0}
    base_text, base_blocks = text_blocks(BASE/'GameText.yaml')
    _, mod_blocks = text_blocks(MOD/'GameText.yaml')
    base_by_id = {id_: block for block,id_ in base_blocks}
    mod_by_id = {id_: block for block,id_ in mod_blocks}
    missing_text = sorted(ids - base_by_id.keys() - mod_by_id.keys())
    changed_text = 0
    for id_ in sorted(ids & mod_by_id.keys()):
        block = mod_by_id[id_]
        if id_ in base_by_id:
            # Preserve the base symbolic token name used by compiled GameText enums.
            block = base_by_id[id_].split('\n', 1)[0]+'\n'+block.split('\n', 1)[1]
            if block != base_by_id[id_]: changed_text += 1
            base_text = base_text.replace(base_by_id[id_], block, 1)
        else:
            block = f'CombinedArmsResearch{id_}:\n'+block.split('\n', 1)[1]
            base_text += '\n'+block
            changed_text += 1
    icon_files, missing_icons = {}, []
    for uid, (_,tree) in new.items():
        icon = tree.findtext('IconPath') or uid
        # Root icons use ResearchMenu, technology icons use TechIcons.
        folder = 'ResearchMenu' if int(tree.findtext('RootNode') or 0) else 'TechIcons'
        rel = Path('Textures') / folder / (icon+'.png')
        if (MOD/rel).is_file(): icon_files[rel] = MOD/rel
        elif not (BASE/rel).is_file(): missing_icons.append(str(rel))
    dependencies = {}
    specs = [('ModulesUnlocked/UnlockedMod/ModuleUID','ShipModules','UID'),
             ('BuildingsUnlocked/UnlockedBuilding/Name','Buildings','Name'),
             ('TroopsUnlocked/UnlockedTroop/Name','Troops','Name')]
    for xpath, folder, field in specs:
        available = set()
        for path in (BASE/folder).rglob('*.xml'):
            tree=ET.parse(path).getroot()
            available.add(path.stem if folder=='ShipModules' else (tree.findtext(field) or path.stem))
        refs=collections.defaultdict(list)
        for uid,(_,tree) in new.items():
            for e in tree.findall(xpath):
                if e.text not in available: refs[e.text].append(uid)
        dependencies[folder]=dict(sorted(refs.items()))
    hulls=set()
    for path in (BASE/'Hulls').rglob('*.hull'):
        hulls.add(path.relative_to(BASE/'Hulls').with_suffix('').as_posix())
    refs=collections.defaultdict(list)
    for uid,(_,tree) in new.items():
        for e in tree.findall('HullsUnlocked/UnlockedHull/Name'):
            if e.text.replace('\\','/') not in hulls: refs[e.text].append(uid)
    dependencies['Hulls']=dict(sorted(refs.items()))
    roots=[{'uid':uid,'order':int(tree.findtext('RootNode')),'seeds':graph[uid]}
           for uid,(_,tree) in new.items() if int(tree.findtext('RootNode') or 0)>0]
    report={'technology_count':len(new),'old_technology_count':len(old),'roots':sorted(roots,key=lambda r:r['order']),
            'removed_base_uids':sorted(old.keys()-new.keys()),'missing_links':missing_edges,
            'missing_text_ids':missing_text,'text_entries_imported':changed_text,
            'icons_imported':len(icon_files),'missing_icon_files':missing_icons,
            'missing_unlock_assets':dependencies}
    if args.apply:
        if missing_edges or missing_text or missing_icons:
            raise ValueError('Research references are incomplete; refusing replacement')
        target=(BASE/'Technology').resolve()
        assert target == ROOT/'game/Content/Technology' and target.is_relative_to(ROOT)
        backup=ROOT/'output/backups'/('research-before-combined-arms-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.zip')
        backup.parent.mkdir(parents=True,exist_ok=True)
        touched=[path for path,_ in old.values()]+[BASE/'GameText.yaml']+[BASE/rel for rel in icon_files if (BASE/rel).exists()]
        with zipfile.ZipFile(backup,'w',zipfile.ZIP_DEFLATED) as archive:
            for path in touched: archive.write(path,path.relative_to(ROOT))
        # Only technology XML files under the checked content target are removed.
        for path,_ in old.values():
            assert path.resolve().is_relative_to(target)
            path.unlink()
        for path,_ in new.values():
            dest=target/path.relative_to(MOD/'Technology')
            dest.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(path,dest)
        (BASE/'GameText.yaml').write_text(base_text,encoding='utf-8')
        for rel,path in icon_files.items():
            (BASE/rel).parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(path,BASE/rel)
        report['backup']=backup.relative_to(ROOT).as_posix()
    dest=ROOT/'docs/combined-arms-research-import.json'
    dest.write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ('technology_count','old_technology_count','icons_imported','text_entries_imported','missing_links','missing_text_ids')}))
    print('Missing unlock assets:', {k:len(v) for k,v in dependencies.items()})
    print('Report:',dest)

if __name__=='__main__': main()
