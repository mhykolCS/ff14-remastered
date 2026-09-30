"""Generate a local job-bar plan from the plugin's private English action catalog."""
import argparse
import json
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--catalog',type=Path,required=True,help='Local job-catalog.json exported by the plugin')
parser.add_argument('--output-dir',type=Path,required=True,help='Directory for the plan and key reference')
args=parser.parse_args()
ROOT=args.output_dir
ROOT.mkdir(parents=True,exist_ok=True)
CAT=json.loads(args.catalog.read_text(encoding='utf-8'))
plans={}
def add(job,*rows):
    plans[job]=[r.split('|') for r in rows]
add('PLD',
'Fast Blade|Riot Blade|Rage of Halone|Atonement|Total Eclipse|Prominence|Holy Spirit|Holy Circle|Confiteor|Shield Lob|Sheltron|Intervene',
'Fight or Flight|Requiescat|Spirits Within|Circle of Scorn|Clemency|Divine Veil|Reprisal|Rampart|Sentinel|Arm\'s Length|Hallowed Ground|Sprint',
'Goring Blade|Bulwark|Passage of Arms|Limit Break|Provoke|Shirk|Intervention|Cover|Interject|Low Blow|Iron Will|Shield Bash')
add('MNK',
'Bootshine|True Strike|Snap Punch|Arm of the Destroyer|Dragon Kick|Twin Snakes|Demolish|Perfect Balance|Four-point Fury|Rockbreaker|Steeled Meditation|Thunderclap',
'Riddle of Fire|Brotherhood|Riddle of Wind|Inspirited Meditation|Second Wind|Bloodbath|Feint|True North|Mantra|Arm\'s Length|Riddle of Earth|Sprint',
'Form Shift|Masterful Blitz|Six-sided Star|Limit Break|Duty Action I|Duty Action II|Teleport|Return|Leg Sweep|||Mount Roulette')
add('WAR',
'Heavy Swing|Maim|Storm\'s Path|Storm\'s Eye|Overpower|Mythril Tempest|Inner Beast|Steel Cyclone|Primal Rend|Tomahawk|Raw Intuition|Onslaught',
'Berserk|Infuriate|Upheaval|Orogeny|Equilibrium|Shake It Off|Reprisal|Rampart|Vengeance|Arm\'s Length|Holmgang|Sprint',
'Thrill of Battle|Nascent Flash||Limit Break|Provoke|Shirk|||Interject|Low Blow|Defiance|')
add('DRK',
'Hard Slash|Syphon Strike|Souleater|Bloodspiller|Unleash|Stalwart Soul|Edge of Darkness|Flood of Darkness|Quietus|Unmend|The Blackest Night|Shadowstride',
'Blood Weapon|Living Shadow|Carve and Spit|Abyssal Drain|Dark Mind|Dark Missionary|Reprisal|Rampart|Shadow Wall|Arm\'s Length|Living Dead|Sprint',
'Salted Earth|Shadowbringer|Disesteem|Limit Break|Provoke|Shirk|Oblation||Interject|Low Blow|Grit|')
add('GNB',
'Keen Edge|Brutal Shell|Solid Barrel|Burst Strike|Demon Slice|Demon Slaughter|Gnashing Fang|Continuation|Fated Circle|Lightning Shot|Heart of Stone|Trajectory',
'No Mercy|Bloodfest|Danger Zone|Bow Shock|Aurora|Heart of Light|Reprisal|Rampart|Nebula|Arm\'s Length|Superbolide|Sprint',
'Sonic Break|Double Down|Reign of Beasts|Limit Break|Provoke|Shirk|Camouflage||Interject|Low Blow|Royal Guard|')
add('DRG',
'True Thrust|Vorpal Thrust|Full Thrust|Disembowel|Chaos Thrust|Fang and Claw|Wheeling Thrust|Doom Spike|Sonic Thrust|Coerthan Torment|Jump|Winged Glide',
'Lance Charge|Battle Litany|Life Surge|Geirskogul|Second Wind|Bloodbath|Feint|True North|Dragonfire Dive|Arm\'s Length|Stardiver|Sprint',
'Wyrmwind Thrust|Piercing Talon|Elusive Jump|Limit Break|Mirage Dive|Nastrond|Rise of the Dragon|Starcross|Leg Sweep|||')
add('NIN',
'Spinning Edge|Gust Slash|Aeolian Edge|Armor Crush|Ten|Chi|Jin|Ninjutsu|Death Blossom|Hakke Mujinsatsu|Blood Stalk|Shukuchi'.replace('Blood Stalk','Bhavacakra'),
'Mug|Trick Attack|Kassatsu|Ten Chi Jin|Second Wind|Bloodbath|Feint|True North|Shade Shift|Arm\'s Length|Bunshin|Sprint',
'Assassinate|Hellfrog Medium|Meisui|Limit Break|Forked Raiju|Fleeting Raiju|Phantom Kamaitachi|Throwing Dagger|Leg Sweep|Hide||')
add('SAM',
'Hakaze|Jinpu|Gekko|Yukikaze|Shifu|Kasha|Iaijutsu|Tsubame-gaeshi|Fuga|Mangetsu|Hissatsu: Shinten|Hissatsu: Gyoten',
'Meikyo Shisui|Ikishoten|Hissatsu: Senei|Ogi Namikiri|Second Wind|Bloodbath|Feint|True North|Third Eye|Arm\'s Length|Shoha|Sprint',
'Oka|Hissatsu: Kyuten|Hissatsu: Guren|Limit Break|Enpi|Hissatsu: Yaten|Meditate|Hagakure|Leg Sweep|||')
add('RPR',
'Slice|Waxing Slice|Infernal Slice|Shadow of Death|Gibbet|Gallows|Blood Stalk|Soul Slice|Spinning Scythe|Nightmare Scythe|Enshroud|Hell\'s Ingress',
'Arcane Circle|Gluttony|Plentiful Harvest|Communio|Second Wind|Bloodbath|Feint|True North|Arcane Crest|Arm\'s Length|Guillotine|Sprint',
'Whorl of Death|Soul Scythe|Grim Swathe|Limit Break|Harpe|Hell\'s Egress|Soulsow||Leg Sweep|||')
add('VPR',
'Steel Fangs|Reaving Fangs|Vicewinder|Reawaken|Hunter\'s Coil|Swiftskin\'s Coil|Twinfang|Twinblood|Steel Maw|Reaving Maw|Serpent\'s Tail|Slither',
'Serpent\'s Ire|Uncoiled Fury|Vicepit|Writhing Snap|Second Wind|Bloodbath|Feint|True North|Hunter\'s Den|Arm\'s Length|Swiftskin\'s Den|Sprint',
'|||Limit Break|||||Leg Sweep|||')
add('BRD',
'Heavy Shot|Straight Shot|Venomous Bite|Windbite|Quick Nock|Wide Volley|Iron Jaws|Empyreal Arrow|Apex Arrow|Sidewinder|Bloodletter|Repelling Shot',
'Mage\'s Ballad|Army\'s Paeon|the Wanderer\'s Minuet|Pitch Perfect|Second Wind|Nature\'s Minne|Troubadour|Head Graze|the Warden\'s Paean|Arm\'s Length|Peloton|Sprint',
'Raging Strikes|Battle Voice|Radiant Finale|Limit Break|Barrage|Rain of Death|Foot Graze|Leg Graze|Resonant Arrow|Radiant Encore||')
add('MCH',
'Split Shot|Slug Shot|Clean Shot|Hot Shot|Spread Shot|Auto Crossbow|Drill|Chain Saw|Bioblaster|Heat Blast|Gauss Round|Ricochet',
'Wildfire|Reassemble|Barrel Stabilizer|Hypercharge|Second Wind|Dismantle|Tactician|Head Graze|Rook Autoturret|Arm\'s Length|Peloton|Sprint',
'Flamethrower|Rook Overdrive|Full Metal Field|Limit Break|Foot Graze|Leg Graze||||||')
add('DNC',
'Cascade|Fountain|Reverse Cascade|Fountainfall|Windmill|Bladeshower|Rising Windmill|Bloodshower|Standard Step|Saber Dance|Fan Dance|En Avant',
'Technical Step|Devilment|Flourish|Starfall Dance|Second Wind|Curing Waltz|Shield Samba|Head Graze|Improvisation|Arm\'s Length|Peloton|Sprint',
'Fan Dance II|Fan Dance III|Fan Dance IV|Limit Break|Closed Position|Last Dance|Foot Graze|Leg Graze||||')
add('WHM',
'Stone|Aero|Holy|Afflatus Misery|Cure II|Regen|Afflatus Solace|Tetragrammaton|Medica II|Afflatus Rapture|Assize|Aetherial Shift',
'Presence of Mind|Thin Air|Temperance|Plenary Indulgence|Benediction|Divine Benison|Aquaveil|Swiftcast|Lucid Dreaming|Surecast|Asylum|Sprint',
'Cure|Medica|Cure III|Limit Break|Esuna|Raise|Rescue|Repose|Liturgy of the Bell|||')
add('SCH',
'Ruin|Bio|Art of War|Ruin II|Adloquium|Lustrate|Excogitation|Aetherpact|Succor|Indomitability|Energy Drain|Expedient',
'Chain Stratagem|Aetherflow|Recitation|Dissipation|Whispering Dawn|Fey Blessing|Protraction|Swiftcast|Lucid Dreaming|Surecast|Sacred Soil|Sprint',
'Physick|Deployment Tactics|Emergency Tactics|Limit Break|Esuna|Resurrection|Rescue|Fey Illumination|Summon Eos|Summon Seraph|Consolation|Seraphism')
add('AST',
'Malefic|Combust|Gravity|Earthly Star|Benefic II|Aspected Benefic|Essential Dignity|Celestial Intersection|Aspected Helios|Celestial Opposition|Lightspeed|Collective Unconscious',
'Divination|Astral Draw|Play I|Minor Arcana|Exaltation|Neutral Sect|Synastry|Swiftcast|Lucid Dreaming|Surecast|Horoscope|Sprint',
'Benefic|Helios|Macrocosmos|Limit Break|Esuna|Ascend|Rescue|Play II|Play III|||')
add('SGE',
'Dosis|Eukrasia|Dyskrasia|Phlegma|Diagnosis|Druochole|Taurochole|Haima|Prognosis|Ixochole|Toxikon|Icarus',
'Psyche|Rhizomata|Pneuma|Philosophia|Physis|Holos|Kerachole|Swiftcast|Lucid Dreaming|Surecast|Panhaima|Sprint',
'Soteria|Zoe|Pepsis|Limit Break|Esuna|Egeiro|Rescue|Kardia|Krasis|||')
add('BLM',
'Fire|Fire III|Fire IV|Despair|Blizzard|Blizzard III|Blizzard IV|Thunder|Fire II|Flare|Xenoglossy|Aetherial Manipulation',
'Ley Lines|Triplecast|Manafont|Amplifier|Manaward|Transpose|Addle|Swiftcast|Lucid Dreaming|Surecast|Between the Lines|Sprint',
'Blizzard II|Freeze|Thunder II|Limit Break|Foul|Flare Star|Umbral Soul|Scathe|Sleep|||')
add('SMN',
'Ruin|Gemshine|Astral Flow|Ruin IV|Summon Ruby|Summon Topaz|Summon Emerald|Aethercharge|Outburst|Precious Brilliance|Fester|Painflare',
'Searing Light|Energy Drain|Energy Siphon|Enkindle Bahamut|Radiant Aegis|Physick|Addle|Swiftcast|Lucid Dreaming|Surecast|Lux Solaris|Sprint',
'Summon Carbuncle|||Limit Break|Resurrection|Sleep||||||')
add('RDM',
'Jolt|Verthunder|Veraero|Fleche|Verfire|Verstone|Riposte|Zwerchhau|Scatter|Moulinet|Redoublement|Corps-a-corps',
'Embolden|Manafication|Acceleration|Contre Sixte|Vercure|Magick Barrier|Addle|Swiftcast|Lucid Dreaming|Surecast|Engagement|Sprint',
'Verthunder II|Veraero II|Reprise|Limit Break|Verraise|Displacement|Sleep|||||')
add('PCT',
'Fire in Red|Blizzard in Cyan|Holy in White|Comet in Black|Fire II in Red|Blizzard II in Cyan|Living Muse|Steel Muse|Scenic Muse|Subtractive Palette|Hammer Stamp|Smudge',
'Mog of the Ages|Rainbow Drip|Star Prism|Tempera Grassa|Tempera Coat||Addle|Swiftcast|Lucid Dreaming|Surecast||Sprint',
'Creature Motif|Weapon Motif|Landscape Motif|Limit Break|Sleep|||||||')
# Blue Mage spells stay aligned with the actual active spell book (populated in game).
add('BLU','Water Cannon|||||||||||','||||||Addle|Swiftcast|Lucid Dreaming|Surecast||Sprint','|||Limit Break||||||||')
add('BST',
'Smash Axe|Axeblade Bite|Avalanche Axe|Mistral Axe|Spinning Axe|Gale Axe|First Battlehorn|Second Battlehorn|Third Battlehorn|Shieldsplitter|Beast Mode|Shield Charge',
'Tempered Release|Parting Blow|Rallying Cheer|Rally||||||||Sprint',
'Capture|Gauge|Borrow|Limit Break|Trick|||||||')
add('MIN',
'Scour|Brazen Prospector|Meticulous Prospector|Scrutiny|Sharp Vision|Sharp Vision II|Sharp Vision III|Bountiful Yield|King\'s Yield|King\'s Yield II|Solid Reason|Sneak',
'Collector\'s Focus|Wise to the World|Priming Touch|The Giving Land|Mountaineer\'s Gift I|Mountaineer\'s Gift II|Nald\'thal\'s Tidings|the Twelve\'s Bounty|Luck of the Mountaineer|Clear Vision|Collect|Sprint',
'Prospect|Lay of the Land|Lay of the Land II|Truth of Mountains|Teleport|Return|Mount Roulette|||||')
add('BTN',
'Scour|Brazen Woodsman|Meticulous Woodsman|Scrutiny|Field Mastery|Field Mastery II|Field Mastery III|Bountiful Harvest|Blessed Harvest|Blessed Harvest II|Ageless Words|Sneak',
'Collector\'s Focus|Wise to the World|Priming Touch|The Giving Land|Pioneer\'s Gift I|Pioneer\'s Gift II|Nophica\'s Tidings|the Twelve\'s Bounty|Luck of the Pioneer|Flora Mastery|Collect|Sprint',
'Triangulate|Arbor Call|Arbor Call II|Truth of Forests|Teleport|Return|Mount Roulette|||||')
add('FSH',
'Cast|Hook|Double Hook|Triple Hook|Patience|Patience II|Precision Hookset|Powerful Hookset|Mooch|Mooch II|Thaliak\'s Favor|Sneak',
'Surface Slap|Identical Cast|Prize Catch|Makeshift Bait|Chum|Fish Eyes|Collect|Snagging|Ambitious Lure|Modest Lure|Bait|Sprint',
'Gig|Veteran Trade|Nature\'s Bounty|Electric Current|Shark Eye|Shark Eye II|Vital Sight|Baited Breath|Salvage|Truth of Oceans|Spareful Hand|Big-game Fishing')
for j in ['CRP','BSM','ARM','GSM','LTW','WVR','ALC','CUL']:
 add(j,
 'Basic Synthesis|Careful Synthesis|Groundwork|Intensive Synthesis|Basic Touch|Standard Touch|Advanced Touch|Preparatory Touch|Delicate Synthesis|Byregot\'s Blessing|Master\'s Mend|Tricks of the Trade',
 'Muscle Memory|Reflect|Veneration|Innovation|Waste Not|Waste Not II|Manipulation|Great Strides|Hasty Touch|Rapid Synthesis|Observe|Precise Touch',
 'Prudent Synthesis|Prudent Touch|Trained Finesse|Refined Touch|Immaculate Mend|Trained Perfection|Heart and Soul|Quick Innovation|Careful Observation|Final Appraisal|Trained Eye|Sprint')
# Base classes keep the same muscle memory, filtering out actions only usable as a job.
for base,job in [('GLA','PLD'),('PGL','MNK'),('MRD','WAR'),('LNC','DRG'),('ARC','BRD'),('CNJ','WHM'),('THM','BLM'),('ACN','SMN'),('ROG','NIN')]:
 plans[base]=[r.copy() for r in plans[job]]
keys=['1','2','3','4','Q','E','R','F','Z','X','5','Mouse5']
# Existing native GeneralAction IDs, as used in the prior verified layout.
general={'Sprint':4,'Limit Break':3,'Teleport':7,'Return':8,'Duty Action I':26,'Duty Action II':27,'Mount Roulette':9}
out=[];problems=[]
for job,rows in plans.items():
 j=next(j for j in CAT['Jobs'] if j['Abbreviation']==job)
 flat=[]
 for b,row in enumerate(rows):
  if len(row)!=12:problems.append(f'{job} row {b+1} has {len(row)} slots');continue
  for i,name in enumerate(row):
   d={'Bar':b,'Slot':i,'Key':('Shift+' if b==1 else 'Ctrl+' if b==2 else '')+keys[i],'Name':name,'Type':'Empty','Id':0}
   if name in general:d.update(Type='GeneralAction',Id=general[name])
   elif name:
    found=[a for a in CAT['Actions'] if a['Name'].casefold()==name.casefold() and (job in a['Jobs'] or a['ClassJob']==j['Id']) and a['IsPlayerAction'] and a['ClassJobLevel']>0]
    crafts=[a for a in CAT['CraftActions'] if a['Name'].casefold()==name.casefold() and a['ClassJob']==j['Id']]
    if name=='Emergency Tactics':found=[a for a in found if a['Id']==3586]
    if crafts:found=crafts
    if len(found)!=1:
     if j['Id'] in [1,2,3,4,5,6,7,26,29] and not found:d['Name']=''
     else:problems.append(f'{job}: {name} -> {[a["Id"] for a in found]}')
    else:d.update(Type='CraftAction' if crafts else 'Action',Id=found[0]['Id'])
   flat.append(d)
 if len(flat)==36:out.append({'Job':j['Id'],'Abbreviation':job,'Slots':flat})
if problems:
 print('\n'.join(problems));raise SystemExit(1)
assert len(out)==43 and len({j['Job'] for j in out})==43
for j in out:
 ids=[(s['Type'],s['Id']) for s in j['Slots'] if s['Id']]
 assert len(set(ids))==len(ids),f'duplicate {j["Abbreviation"]}'
(ROOT/'job-bars.json').write_text(json.dumps(out,indent=2))
with (ROOT/'ALL-JOB-KEYBINDS.txt').open('w') as f:
 f.write('FFXIV KEYBOARD + G502 JOB LAYOUTS\n\nKeys: '+', '.join(keys)+'\nPlain = bar 1. Shift = bar 2. Ctrl = bar 3. Bar 4 remains shared travel/RP.\nBase skills upgrade with the game. Future skills are placed before unlock.\nBlue Mage uses its active spell book. PvP bars are unchanged.\n\n')
 for j in sorted(out,key=lambda j:j['Job']):
  f.write(j['Abbreviation']+'\n')
  for s in j['Slots']:f.write(f'  {s["Key"]:14} {s["Name"] or "(empty)"}\n')
  f.write('\n')
print(f'Validated {len(out)} job/class layouts, {sum(len(j["Slots"]) for j in out)} slots.')
