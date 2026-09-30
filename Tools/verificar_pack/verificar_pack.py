#!/usr/bin/env python3
"""VERIFICAR UN PACK (o tu carpeta de Excel) FUERA DE UNITY.

Uso:
    python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/Gen1-6
    python3 Tools/verificar_pack/verificar_pack.py Assets/GameContent/Packs/Gen1-6 --pokeapi --gen 6

Comprueba, sin abrir Unity:
  1. FORMATO: cada hoja se lee exactamente como la lee el editor (CsvTable): filas, columnas, ids vacíos o repetidos.
  2. REFERENCIAS: todo lo que nombra cada hoja existe, en el pack o en la base que crean las plantillas del código
     (tipos, estados, climas, objetos, Forcejeo...): movimientos de especies y entrenadores, evoluciones, habilidades,
     objetos, naturalezas, grupos huevo, tipos, efectos de movimientos...
  3. Con --pokeapi: los DATOS contra PokeAPI en la generación --gen (estadísticas y tipos de las especies; potencia,
     precisión, PP, prioridad y tipo de los movimientos; la tabla de tipos) y que los movimientos escritos en los
     equipos de entrenadores se puedan aprender en esa generación.

Termina con código 1 si hay ERRORES (útil antes de un commit o en CI). Los AVISOS no hacen fallar.
"""
import argparse, glob, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csvlib import load, split_list  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
EDITOR = os.path.join(ROOT, 'Assets', 'CTEditor', 'Runtime', 'GameDefinition', 'Editor')
KEY = {'tipos.csv': 'types', 'estados.csv': 'status', 'climas.csv': 'weather', 'trampas.csv': 'hazard',
       'efectos_lado.csv': 'side', 'habilidades.csv': 'ability', 'objetos.csv': 'item', 'naturalezas.csv': 'nature',
       'curvas.csv': 'curve', 'movimientos.csv': 'move', 'especies.csv': 'species', 'grupos_huevo.csv': 'egg',
       'niveles_ia.csv': 'ai', 'entrenadores.csv': 'trainer', 'zonas.csv': 'zone', 'equipos.csv': 'team'}
MEMBER = re.compile(r'^(?P<sp>[^@\[\]{}~#"%]+)@(?P<lvl>\d+)(?:%(?P<g>[mhMH]))?(?:\[(?P<moves>[^\]]*)\])?'
                    r'(?:\{(?P<held>[^}]*)\})?(?:~(?P<nat>[^#"]+))?(?:#(?P<iv>\d+))?(?:"(?P<nick>[^"]*)")?$')


class Report:
    def __init__(self):
        self.errors, self.warnings = [], []

    def error(self, msg):
        if msg not in self.errors: self.errors.append(msg)

    def warn(self, msg):
        if msg not in self.warnings: self.warnings.append(msg)


# ---------------- La base que crean las plantillas del código ----------------

def code_base():
    """Ids que crean las plantillas C# (Centro de Contenido → base clásica)."""
    def read(rel):
        p = os.path.join(EDITOR, rel)
        return open(p, encoding='utf-8').read() if os.path.exists(p) else ''
    tuples = lambda s: set(re.findall(r'\(\s*"([a-z0-9_]+)",\s*"[^"]+"', s))
    base = {
        'types': tuples(read('Common/TypeChartTools.cs')) | {'typeless'},
        'status': tuples(read('Common/ClassicStatusPresets.cs')) | set(re.findall(r'\["([a-z0-9_]+)"\]\s*=', read('Common/ClassicStatusPresets.cs'))),
        'weather': tuples(read('Windows/WeatherEditorWindow.cs')),
        'hazard': tuples(read('Windows/HazardEditorWindow.cs')),
        'side': tuples(read('Windows/SideConditionEditorWindow.cs')),
        'nature': tuples(read('Windows/NatureEditorWindow.cs')),
        'egg': tuples(read('Windows/EggGroupEditorWindow.cs')),
        'curve': set(re.findall(r'\(GrowthCurveFormula\.\w+,\s*"([a-z0-9_]+)"', read('Windows/GrowthCurveEditorWindow.cs'))),
        'move': set(re.findall(r'P\("[^"]+",\s*"([a-z0-9_]+)"', read('Common/ClassicMovePresets.cs'))),
        'ability': set(re.findall(r'P\("[^"]+",\s*"([a-z0-9_]+)"', read('Windows/AbilityEditorWindow.cs'))),
        'ai': {f'nivel_{i}' for i in range(1, 8)},
    }
    items = read('Windows/ItemEditorWindow.cs')
    base['item'] = (set(re.findall(r'P\(ItemCategory\.\w+,\s*"(\w+)"', items)) | set(re.findall(r'Berry\("(\w+)"', items))
                    | set(re.findall(r'\(\s*"(\w+)",\s*"[^"]+",\s*\d+', items)))
    return base


# ---------------- 1. Formato ----------------

def check_format(folder, rep):
    sheets = {}
    for path in sorted(glob.glob(os.path.join(folder, '*.csv'))):
        name = os.path.basename(path)
        try:
            headers, rows, lines, ncells = load(path)
        except Exception as e:  # noqa: BLE001
            rep.error(f'{name}: no se puede leer ({e})'); continue
        sheets[name] = (headers, rows, lines)
        if name == 'tabla_tipos.csv':
            continue
        if 'id' not in headers:
            rep.error(f'{name}: falta la columna «id»'); continue
        seen = {}
        for r, ln, n in zip(rows, lines, ncells):
            if n != len(headers):
                rep.warn(f'{name}:{ln}: {n} celdas y la cabecera tiene {len(headers)} (¿un ; o una comilla de más?)')
            if not r['id']:
                rep.error(f'{name}:{ln}: fila sin id')
            elif r['id'] in seen:
                rep.error(f'{name}:{ln}: id «{r["id"]}» repetido (ya en la línea {seen[r["id"]]})')
            else:
                seen[r['id']] = ln
    return sheets


# ---------------- 2. Referencias ----------------

def check_references(sheets, rep):
    base = code_base()
    known = {k: set(v) for k, v in base.items()}
    for name, key in KEY.items():
        if name in sheets:
            known.setdefault(key, set()).update(r['id'] for r in sheets[name][1] if r.get('id'))

    def need(kind, value, where, soft=False):
        if value and value not in known.get(kind, set()):
            (rep.warn if soft else rep.error)(f'{where}: {kind} «{value}» no existe (ni en el pack ni en la base del código)')

    if 'especies.csv' in sheets:
        _, rows, lines = sheets['especies.csv']
        for r, ln in zip(rows, lines):
            w = f'especies.csv:{ln} {r["id"]}'
            for t in split_list(r.get('tipos', '')): need('types', t, w)
            for col in ('habilidad', 'habilidad_2', 'habilidad_oculta'): need('ability', r.get(col, ''), w, soft=True)
            need('curve', r.get('curva', ''), w, soft=True)
            for e in split_list(r.get('aprende', '')): need('move', e.partition(':')[2].strip(), w)
            for col in ('mt', 'tutor', 'huevo'):
                for m in split_list(r.get(col, '')): need('move', m, w, soft=True)
            for g in split_list(r.get('grupos_huevo', '')): need('egg', g, w, soft=True)
            for e in split_list(r.get('evoluciona', '')):
                need('species', e.split('@')[0].strip(), w)
                for it in re.findall(r'(?:objeto|lleva|intercambio):(\w+)', e): need('item', it, w, soft=True)
                for mv in re.findall(r'sabe:(\w+)', e): need('move', mv, w)
            # Formas y variantes
            need('species', r.get('forma_de', ''), w)
            if r.get('forma_de') == r['id']:
                rep.error(f'{w}: es forma de sí misma')
            need('item', r.get('objeto_variante', ''), w, soft=True)
            form_ids = set()
            for f in split_list(r.get('formas', '')):
                p = [x.strip() for x in f.split(';')] + [''] * 6
                if not p[0]:
                    rep.error(f'{w}: forma sin id «{f}»'); continue
                form_ids.add(p[0])
                for t in [x for x in p[2].split('/') if x]: need('types', t, w)
                if p[3] and (len(p[3].split('/')) != 5 or not all(x.strip().isdigit() for x in p[3].split('/'))):
                    rep.error(f'{w}: forma {p[0]}: las estadísticas son 5 números atq/def/atq_esp/def_esp/vel')
                need('ability', p[4], w, soft=True)
            for c in split_list(r.get('cambios_forma', '')):
                parts = [x.strip() for x in c.split(';') if x.strip()]
                main = parts[0]
                if '>' not in main or ':' not in main.split('>', 1)[1]:
                    rep.error(f'{w}: cambio de forma no válido «{c}» (desde>hasta:disparador[:valor])'); continue
                frm, rest = main.split('>', 1)
                to, trig, *val = rest.split(':')
                val = ':'.join(val)
                for fid in (frm, to):
                    if fid and fid != '*' and fid not in form_ids:
                        rep.error(f'{w}: el cambio «{c}» usa la forma «{fid}», que no está en formas')
                if trig in ('objeto', 'mega'): need('item', val, w, soft=True)
                elif trig == 'movimiento': need('move', val, w)
                elif trig == 'clima': need('weather', val, w, soft=True)
                elif trig not in ('ataque', 'ps_bajo', 'ps_desde'):
                    rep.error(f'{w}: disparador desconocido «{trig}» en «{c}»')
                for o in parts[1:]:
                    if o.startswith('con='): need('ability', o[4:], w, soft=True)
    if 'movimientos.csv' in sheets:
        _, rows, lines = sheets['movimientos.csv']
        refs = {'estado': 'status', 'estado_propio': 'status', 'clima': 'weather', 'trampa': 'hazard',
                'lado': 'side', 'lado_rival': 'side', 'campo': 'side'}
        for r, ln in zip(rows, lines):
            w = f'movimientos.csv:{ln} {r["id"]}'
            need('types', r.get('tipo', ''), w)
            for e in split_list(r.get('efectos', '')):
                e = re.sub(r'\[.*?\]', '', e).strip().lstrip('&').split('@')[0]
                p = [x.strip() for x in e.split(':')]
                if p[0] in refs and len(p) > 1 and p[1] and not p[1].isdigit():
                    need(refs[p[0]], p[1], w, soft=True)
    for sheet, col in (('entrenadores.csv', 'equipo'), ('equipos.csv', 'equipo')):
        if sheet not in sheets:
            continue
        _, rows, lines = sheets[sheet]
        for r, ln in zip(rows, lines):
            w = f'{sheet}:{ln} {r["id"]}'
            team = split_list(r.get(col, ''))
            if sheet == 'entrenadores.csv' and not team:
                rep.error(f'{w}: sin equipo')
            for mem in team:
                m = MEMBER.match(mem)
                if not m:
                    rep.error(f'{w}: miembro con formato no válido «{mem}»'); continue
                need('species', m['sp'].strip(), w)
                for mv in (m['moves'] or '').split('/'): need('move', mv.strip(), w)
                need('item', (m['held'] or '').strip(), w, soft=True)
                need('nature', (m['nat'] or '').strip(), w)
            for it in split_list(r.get('mochila', '')): need('item', it.split(':')[0].strip(), w, soft=True)
            ai = r.get('nivel_ia', '')
            if ai and not (ai.isdigit() and 0 <= int(ai) <= 7):
                need('ai', ai, w)
    if 'zonas.csv' in sheets:
        _, rows, lines = sheets['zonas.csv']
        for r, ln in zip(rows, lines):
            for e in split_list(r.get('especies', '')): need('species', e.split('@')[0].strip(), f'zonas.csv:{ln} {r["id"]}')


# ---------------- 3. Datos contra PokeAPI ----------------

def check_pokeapi(sheets, gen, rep):
    from pokeapi import PokeApi
    api = PokeApi(gen)
    if 'especies.csv' in sheets:
        ref = api.species()
        _, rows, lines = sheets['especies.csv']
        from pokeapi import table, pack_id
        form_pid = {pack_id(p['identifier']): p['id'] for p in table('pokemon') if p['is_default'] == '0'}
        for r, ln in zip(rows, lines):
            a = ref.get(r['id'])
            if not a and r.get('forma_de') and r['id'] in form_pid:   # variante: sus datos de PokeAPI son los de su forma
                a = api.pokemon_vals({form_pid[r['id']]: r['id']})[form_pid[r['id']]]
            if not a:
                rep.warn(f'especies.csv:{ln} {r["id"]}: no está en PokeAPI (¿especie inventada?)'); continue
            for col in ('ps', 'ataque', 'defensa', 'atq_esp', 'def_esp', 'velocidad', 'tipos'):
                if col in r and r[col] != a.get(col):
                    rep.error(f'especies.csv:{ln} {r["id"]}.{col}: pack={r[col]} PokeAPI (gen {gen})={a.get(col)}')
    if 'movimientos.csv' in sheets:
        ref = api.moves()
        cat = {'fisico': 'physical', 'especial': 'special', 'estado': 'status'}
        _, rows, lines = sheets['movimientos.csv']
        for r, ln in zip(rows, lines):
            a = ref.get(r['id'])
            if not a:
                rep.warn(f'movimientos.csv:{ln} {r["id"]}: no está en PokeAPI para la gen {gen}'); continue
            checks = [('tipo', r.get('tipo'), a['tipo']), ('categoria', cat.get(r.get('categoria')), a['categoria']),
                      ('precision', r.get('precision'), a['precision'] or 'nunca'), ('pp', r.get('pp'), a['pp']),
                      ('prioridad', r.get('prioridad'), a['prioridad'])]
            # Potencia variable: el pack pone 1 y una fórmula (PokeAPI no tiene potencia fija).
            if r.get('potencia') != '1' and a['potencia']:
                checks.append(('potencia', r.get('potencia'), a['potencia']))
            for col, ours, theirs in checks:
                if ours is not None and str(ours) != str(theirs):
                    rep.error(f'movimientos.csv:{ln} {r["id"]}.{col}: pack={ours} PokeAPI (gen {gen})={theirs}')
    if 'tabla_tipos.csv' in sheets:
        headers, rows, lines = sheets['tabla_tipos.csv']
        ref = api.type_chart()
        corner = headers[0]
        ours = {}
        for r in rows:
            for d in headers[1:]:
                v = r.get(d, '').replace(',', '.')
                if v:
                    ours[(r[corner], d)] = float(v)
        alive = set(api.types_in_gen())
        for k in sorted(set(ours) | set(ref)):
            if k[0] in alive and k[1] in alive and ours.get(k, 1) != ref.get(k, 1):
                rep.error(f'tabla_tipos.csv: {k[0]} → {k[1]}: pack=×{ours.get(k, 1):g} PokeAPI (gen {gen})=×{ref.get(k, 1):g}')
    if 'entrenadores.csv' in sheets:
        learn = api.learnsets()
        _, rows, lines = sheets['entrenadores.csv']
        for r, ln in zip(rows, lines):
            for mem in split_list(r.get('equipo', '')):
                m = MEMBER.match(mem)
                if not m or not m['moves']:
                    continue
                sp, lvl = m['sp'].strip(), int(m['lvl'])
                if sp not in learn:
                    continue
                for mv in m['moves'].split('/'):
                    mv = mv.strip()
                    if mv and not api.can_learn(learn, sp, mv, lvl):
                        rep.warn(f'entrenadores.csv:{ln} {r["id"]}: {sp}@{lvl} no puede aprender «{mv}» en ningún juego hasta la gen {gen}')


def main():
    ap = argparse.ArgumentParser(description='Verifica un pack de CSV del editor fuera de Unity.')
    ap.add_argument('carpeta', help='carpeta con los .csv (un pack o tu carpeta Excel/)')
    ap.add_argument('--pokeapi', action='store_true', help='comprobar también los datos contra PokeAPI')
    ap.add_argument('--gen', type=int, default=6, help='generación de referencia para --pokeapi (por defecto 6)')
    ap.add_argument('--max', type=int, default=60, help='máximo de líneas por sección en el informe')
    args = ap.parse_args()
    if not os.path.isdir(args.carpeta):
        sys.exit(f'No existe la carpeta {args.carpeta}')
    rep = Report()
    sheets = check_format(args.carpeta, rep)
    print(f'Hojas: {", ".join(f"{n} ({len(s[1])})" for n, s in sheets.items())}')
    check_references(sheets, rep)
    if args.pokeapi:
        check_pokeapi(sheets, args.gen, rep)
    for title, items in (('ERRORES', rep.errors), ('AVISOS', rep.warnings)):
        print(f'\n{title}: {len(items)}')
        for x in items[:args.max]:
            print('  • ' + x)
        if len(items) > args.max:
            print(f'  … y {len(items) - args.max} más (usa --max)')
    print('\n✔ Sin errores.' if not rep.errors else '\n✖ Hay errores.')
    sys.exit(1 if rep.errors else 0)


if __name__ == '__main__':
    main()
