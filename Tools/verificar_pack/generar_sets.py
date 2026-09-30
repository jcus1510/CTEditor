#!/usr/bin/env python3
"""SETS DE COMPETICIÓN (Smogon) para cada pack: Assets/GameContent/Packs/GenN/sets.csv.

Uso:
    python3 Tools/verificar_pack/generar_sets.py            # Gen1 … Gen6
    python3 Tools/verificar_pack/generar_sets.py --gen 6

Fuente: los sets de los análisis de Smogon (https://pkmn.github.io/smogon/data/sets/genNformato.json) y sus
estadísticas de uso (…/data/stats/…), formatos de individuales: OU, Ubers, UU, RU, NU, PU y LC (los que existan en
cada generación). Se descargan una vez a Tools/.cache/smogon.

Cada fila es un set: especie, formato, nombre del set, puntuación (0-100: cuánto se usan de verdad sus movimientos,
objeto y habilidad según las estadísticas; la IA elige al azar dando más peso a los más usados), objeto, habilidad y
naturaleza (alternativas separadas por coma), EVs, IVs y movimientos (4 huecos separados por «/»; en cada hueco,
alternativas separadas por coma). Los nombres se traducen a los ids del pack (por su nombre en inglés o su id); lo que
no existe en el pack se quita y, si un set se queda sin movimientos, se descarta (se cuenta en el informe).
"""
import argparse, collections, json, os, re, sys, urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csvlib import load, save  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PACKS = os.path.join(ROOT, 'Assets', 'GameContent', 'Packs')
CACHE = os.path.join(ROOT, 'Tools', '.cache', 'smogon')
BASE = 'https://pkmn.github.io/smogon/data/'
FORMATS = ['ou', 'ubers', 'uu', 'ru', 'nu', 'pu', 'lc']
STAT_KEYS = {'hp': 'PS', 'atk': 'Atq', 'def': 'Def', 'spa': 'AtqE', 'spd': 'DefE', 'spe': 'Vel'}
HEADERS = ['id', 'especie', 'formato', 'nombre', 'puntuacion', 'objeto', 'habilidad', 'naturaleza', 'evs', 'ivs', 'movimientos']


def fetch(kind, name):
    """JSON de Smogon (sets o stats) de un formato; None si no existe."""
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, f'{kind}_{name}.json')
    if not os.path.exists(path):
        try:
            print(f'  descargando {kind}/{name}.json…', file=sys.stderr)
            urllib.request.urlretrieve(f'{BASE}{kind}/{name}.json', path)
        except Exception:   # el formato no existe en esa generación (404)
            with open(path, 'w') as f:
                f.write('null')
    with open(path, encoding='utf-8') as f:
        return json.load(f)


def norm(name):
    """Como ShowdownNames.Normalize: solo letras y números en minúsculas."""
    import unicodedata
    d = unicodedata.normalize('NFD', name or '')
    return ''.join(c.lower() for c in d if c.isalnum() and unicodedata.category(c) != 'Mn')


class Names:
    def __init__(self, rows):
        self.by = {}
        for r in rows:
            for n in (r.get('nombre', ''), r['id'], r.get('nombre_en', '')):
                if n:
                    self.by[norm(n)] = r['id']

    def add_ids(self, ids):
        for i in ids:
            self.by.setdefault(norm(i), i)

    def id(self, name):
        name = re.sub(r'\s*\[.*?\]', '', name or '')
        if name.lower().startswith('hidden power'):
            name = 'Hidden Power'   # «Hidden Power Ice» → hidden_power (el tipo lo decide el juego)
        return self.by.get(norm(name))


def options(v):
    return v if isinstance(v, list) else ([v] if v else [])


def spread(v):
    if isinstance(v, list):
        v = v[0] if v else {}
    return '/'.join(f'{n} {STAT_KEYS[k]}' for k, n in (v or {}).items() if k in STAT_KEYS and n)


def build(n, verbose=True):
    folder = os.path.join(PACKS, f'Gen{n}')
    load_rows = lambda f: load(os.path.join(folder, f))[1] if os.path.exists(os.path.join(folder, f)) else []
    species = {r['id']: r for r in load_rows('especies.csv')}
    sp_names = Names(species.values())
    mv_names = Names(load_rows('movimientos.csv'))
    ab_names = Names(load_rows('habilidades.csv'))
    it_names = Names(load_rows('objetos.csv'))
    na_names = Names(load_rows('naturalezas.csv'))
    import verificar_pack
    base = verificar_pack.code_base()
    it_names.add_ids(base['item'])          # los objetos con efecto los crean siempre las plantillas del código
    if n >= 3:
        na_names.add_ids(base.get('nature', []))
    rows, dropped, counter = [], collections.Counter(), collections.Counter()
    for fmt in FORMATS:
        sets = fetch('sets', f'gen{n}{fmt}')
        if not sets:
            continue
        stats = (fetch('stats', f'gen{n}{fmt}') or {}).get('pokemon', {})
        for sp_name, by_name in sets.items():
            sp = sp_names.id(re.sub(r'-(Mega(-[XY])?|Primal)$', '', sp_name))
            if not sp:
                dropped['especie que no está en el pack'] += len(by_name)
                continue
            st = stats.get(sp_name) or next((v for k, v in stats.items() if k.startswith(sp_name + '-')), {})
            own_abilities = {species[sp].get(c, '') for c in ('habilidad', 'habilidad_2', 'habilidad_oculta')} - {''}
            for i, (set_name, s) in enumerate(by_name.items()):
                slots, move_use = [], []
                for slot in options(s.get('moves')):
                    ids = []
                    for m in options(slot):
                        mid = mv_names.id(m)
                        if mid and mid not in ids:
                            ids.append(mid)
                        elif not mid:
                            dropped['movimiento que no está en el pack'] += 1
                    if ids:
                        slots.append(ids)
                        move_use.append(max((st.get('moves', {}).get(m, 0) for m in options(slot)), default=0))
                if not slots:
                    dropped['set sin movimientos válidos'] += 1
                    continue
                items = [x for x in (it_names.id(v) for v in options(s.get('item'))) if x] if n >= 2 else []
                abilities = [x for x in (ab_names.id(v) for v in options(s.get('ability'))) if x and x in own_abilities] if n >= 3 else []
                natures = [x for x in (na_names.id(v) for v in options(s.get('nature'))) if x] if n >= 3 else []
                item_use = max((st.get('items', {}).get(v, 0) for v in options(s.get('item'))), default=0)
                ab_use = max((st.get('abilities', {}).get(v, 0) for v in options(s.get('ability'))), default=0)
                score = (sum(move_use) / max(1, len(move_use))) * 0.6 + item_use * 0.25 + ab_use * 0.15
                # Numerado por especie y formato («Camerupt» y «Camerupt-Mega» son la misma especie aquí).
                counter[(sp, fmt)] += 1
                rows.append({'id': f'{sp}_{fmt}_{counter[(sp, fmt)]}', 'especie': sp, 'formato': fmt, 'nombre': set_name,
                             'puntuacion': str(round(score * 100)), 'objeto': ','.join(items), 'habilidad': ','.join(abilities),
                             'naturaleza': ','.join(natures), 'evs': spread(s.get('evs')) if n >= 3 else '',
                             'ivs': spread(s.get('ivs')), 'movimientos': '/'.join(','.join(sl) for sl in slots)})
    save(os.path.join(folder, 'sets.csv'), HEADERS, rows)
    meta = os.path.join(folder, 'sets.csv.meta')
    if not os.path.exists(meta):
        import generar_packs
        generar_packs.ensure_metas(folder)
    if verbose:
        by_fmt = collections.Counter(r['formato'] for r in rows)
        print(f'Gen{n}: {len(rows)} sets ({", ".join(f"{k} {v}" for k, v in by_fmt.items())})'
              + (f'; quitados: {dict(dropped)}' if dropped else ''))
    return rows, dropped


def main():
    ap = argparse.ArgumentParser(description='Genera sets.csv (sets de Smogon) para los packs.')
    ap.add_argument('--gen', type=int, choices=range(1, 7))
    args = ap.parse_args()
    for n in ([args.gen] if args.gen else range(1, 7)):
        build(n)


if __name__ == '__main__':
    main()
