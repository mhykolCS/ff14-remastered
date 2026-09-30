"""Package only redistributable plugin files; never include runtime configuration."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--build-dir',type=Path,default=Path('bin/Release/net10.0'))
parser.add_argument('--output-dir',type=Path,default=Path('dist'))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
manifest = json.loads((root/'CinematicMode.json').read_text())
version = manifest['AssemblyVersion'].removesuffix('.0')
files = {
    'CinematicMode.dll': args.build_dir/'CinematicMode.dll',
    'CinematicMode.json': root/'CinematicMode.json',
    'KamiToolKit.dll': args.build_dir/'KamiToolKit.dll',
    'assets/combat-emotes.png': root/'assets/combat-emotes.png',
    'assets/Twemoji-LICENSE.txt': root/'assets/Twemoji-LICENSE.txt',
    'LICENSE': root/'LICENSE',
    'THIRD_PARTY_NOTICES.md': root/'THIRD_PARTY_NOTICES.md',
    'KamiToolKit-LICENSE.txt': root/'lib/KamiToolKit-LICENSE.txt',
}
for path in files.values():
    if not path.is_file():
        raise SystemExit(f'Missing release input: {path}')
args.output_dir.mkdir(parents=True,exist_ok=True)
archive=args.output_dir/f'FF14Remastered-{version}.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as output:
    for name,path in files.items():
        output.write(path,name)
digest=hashlib.sha256(archive.read_bytes()).hexdigest()
(archive.parent/'SHA256SUMS').write_text(f'{digest}  {archive.name}\n')
print(f'{archive}: {len(files)} files, SHA256 {digest}')
