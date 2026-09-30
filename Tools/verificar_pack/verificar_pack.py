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
# especie@nivel%g[movs]{objeto}~naturaleza!habilidad(EVs)#iv(IVs)"mote" (igual que CsvTeamCodecs.cs)
MEMBER = re.compile(r'^(?P<sp>[^@\[\]{}~#"%!()]+)@(?P<lvl>\d+)(?:%(?P<g>[mhMH]))?(?:\[(?P<moves>[^\]]*)\])?'
                    r'(?:\{(?P<held>[^}]*)\})?(?:~(?P<nat>[^#"!(]+))?(?:!(?P<ab>[^#"(]+))?(?:\((?P<ev>[^)]*)\))?'
                    r'(?:#(?P<iv>\d+)?(?:\((?P<ivs>[^)]*)\))?)?(?:"(?P<nick>[^"]*)")?$')


class Report:
    def __init__(self):
        self.errors, self.warnings = [], []

    def error(self, msg):
        if msg not in self.errors: self.errors.append(msg)

    def warn(self, msg):
        if msg not in self.warnings: self.warnings.append(msg)


# ---------------- La base que crean las plantillas del código ----------------

ITEM_TEMPLATES = os.path.join(ROOT, 'Tools', 'datos_fuente', 'objetos.csv')


def item_templates():
    """Los objetos clásicos con TODOS sus efectos (Tools/datos_fuente/objetos.csv): una fila por objeto."""
    return load(ITEM_TEMPLATES)[1] if os.path.exists(ITEM_TEMPLATES) else []


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
    # Objetos: no hay base; los trae el objetos.csv del pack (con todos sus efectos).
    base['item'] = set()
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

EFFECT_TRIGGERS = {'al_usar', 'siempre', 'al_entrar', 'fin_de_turno', 'antes_de_golpe', 'tras_golpe', 'contacto', 'al_hacer_daño',
                   'al_sufrir_estado', 'poca_vida', 'al_caminar'}
STATS = {'hp', 'attack', 'defense', 'sp_attack', 'sp_defense', 'speed', 'accuracy', 'evasion'}
# Acción → qué tipo de id lleva (como EffectText.cs).
EFFECT_REFS = {'curar_estado': 'status_list', 'poner_estado': 'status', 'etapa': 'stat', 'stat': 'stat', 'evs': 'stat',
               'inmune': 'types', 'clima': 'weather', 'enseñar': 'move'}
EFFECT_ACTIONS = set(EFFECT_REFS) | {'curar', 'curar_pct', 'perder', 'curar_del_daño', 'revivir', 'pp', 'pp_todos', 'critico', 'precision',
                                     'evasion', 'potencia', 'daño', 'daño_recibido', 'aguantar', 'primero', 'eleccion', 'sin_movs_estado',
                                     'pantallas', 'retroceso', 'gastar', 'captura', 'amistad', 'nivel', 'huir', 'repelente', 'forma', 'mecanica'}


def check_item_effects(text, w, need, rep):
    """Revisa la columna «efectos» de un objeto: momentos y acciones conocidos e ids que existen (mismo formato que EffectText.cs)."""
    for block in [b.strip() for b in (text or '').split('|') if b.strip()]:
        head, sep, body = block.partition(']:') if '[' in block.split(':')[0] else block.partition(':')
        if not sep:
            rep.error(f'{w}: efecto sin «:» → «{block}»'); continue
        trig = head.split('[')[0].split('@')[0].strip()
        if trig not in EFFECT_TRIGGERS:
            rep.error(f'{w}: momento «{trig}» desconocido')
        for cond in re.findall(r'(?:mov|propio|rival)\.tipo=(\w+)', head): need('types', cond, w)
        for cond in re.findall(r'(?:propio|rival)\.estado=(\w+)', head): need('status', cond, w, soft=True)
        words = body.split(';')[0].split()
        if not words:
            rep.error(f'{w}: efecto sin acción → «{block}»'); continue
        action, args = words[0], [a for a in words[1:] if not re.match(r'^[x×+\-]?[\d,.]+%?$', a)]
        if action not in EFFECT_ACTIONS:
            rep.error(f'{w}: acción «{action}» desconocida'); continue
        kind = EFFECT_REFS.get(action)
        for a in args:
            for ref in a.split(','):
                if kind == 'status_list' or kind == 'status': need('status', ref, w, soft=True)
                elif kind == 'stat':
                    if ref not in STATS: rep.warn(f'{w}: estadística «{ref}» no es de las clásicas')
                elif kind: need(kind, ref, w, soft=kind == 'weather')

def check_references(sheets, rep):
    base = code_base()
    known = {k: set(v) for k, v in base.items()}
    for name, key in KEY.items():
        if name in sheets:
            known.setdefault(key, set()).update(r['id'] for r in sheets[name][1] if r.get('id'))

    def need(kind, value, where, soft=False):
        if value and value not in known.get(kind, set()):
            (rep.warn if soft else rep.error)(f'{where}: {kind} «{value}» no existe (ni en el pack ni en la base del código)')

    # EFECTOS de los objetos: los ids que nombran (tipos, estados, estadísticas, climas, movimientos) deben existir.
    if 'objetos.csv' in sheets:
        _, rows, lines = sheets['objetos.csv']
        for r, ln in zip(rows, lines):
            w = f'objetos.csv:{ln} {r["id"]}'
            check_item_effects(r.get('efectos', ''), w, need, rep)

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
                if trig == 'objeto': need('item', val, w, soft=True)
                elif trig == 'mega':
                    need('item', val, w, soft=True)
                    if not val and not any(o.startswith('sabe=') for o in parts[1:]):
                        rep.error(f'{w}: la megaevolución «{c}» necesita megapiedra o ;sabe=movimiento')
                elif trig == 'movimiento': need('move', val, w)
                elif trig == 'clima': need('weather', val, w, soft=True)
                elif trig not in ('ataque', 'ps_bajo', 'ps_desde'):
                    rep.error(f'{w}: disparador desconocido «{trig}» en «{c}»')
                for o in parts[1:]:
                    if o.startswith('con='): need('ability', o[4:], w, soft=True)
                    elif o.startswith('sabe='): need('move', o[5:], w)
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
    species_abilities = {}
    if 'especies.csv' in sheets:
        for r in sheets['especies.csv'][1]:
            species_abilities[r['id']] = {r.get(c, '') for c in ('habilidad', 'habilidad_2', 'habilidad_oculta') if r.get(c, '')}
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
                sp = m['sp'].strip()
                ab = (m['ab'] or '').strip()
                if ab:
                    need('ability', ab, w, soft=True)
                    own = species_abilities.get(sp)
                    if own is not None and ab not in own:
                        rep.warn(f'{w}: {sp} no puede tener la habilidad «{ab}» (tiene: {", ".join(sorted(own)) or "ninguna"})')
                for label, text, top, total_top in (('EVs', m['ev'], 252, 510), ('IVs', m['ivs'], 31, None)):
                    vals, err = parse_spread(text or '')
                    if err:
                        rep.error(f'{w}: {label} de {sp} no válidos: {err}'); continue
                    if any(v > top for v in vals.values()):
                        rep.warn(f'{w}: {label} de {sp} por encima de {top} en alguna estadística (clásico)')
                    if total_top and sum(vals.values()) > total_top:
                        rep.warn(f'{w}: {label} de {sp} suman {sum(vals.values())} (clásico: máximo {total_top})')
            for it in split_list(r.get('mochila', '')): need('item', it.split(':')[0].strip(), w, soft=True)
            ai = r.get('nivel_ia', '')
            if ai and not (ai.isdigit() and 0 <= int(ai) <= 7):
                need('ai', ai, w)
    if 'sets.csv' in sheets:
        _, rows, lines = sheets['sets.csv']
        for r, ln in zip(rows, lines):
            w = f'sets.csv:{ln} {r["id"]}'
            need('species', r.get('especie', ''), w)
            for slot in (r.get('movimientos', '') or '').split('/'):
                for mv in slot.split(','): need('move', mv.strip(), w)
            for it in (r.get('objeto', '') or '').split(','): need('item', it.strip(), w, soft=True)
            for ab in (r.get('habilidad', '') or '').split(','): need('ability', ab.strip(), w, soft=True)
            for nat in (r.get('naturaleza', '') or '').split(','): need('nature', nat.strip(), w)
            for label, col in (('EVs', 'evs'), ('IVs', 'ivs')):
                _, err = parse_spread(r.get(col, '') or '')
                if err:
                    rep.error(f'{w}: {label} no válidos: {err}')
    if 'zonas.csv' in sheets:
        _, rows, lines = sheets['zonas.csv']
        for r, ln in zip(rows, lines):
            for e in split_list(r.get('especies', '')): need('species', e.split('@')[0].strip(), f'zonas.csv:{ln} {r["id"]}')


STAT_ALIASES = {'ps': 'hp', 'hp': 'hp', 'atq': 'attack', 'atk': 'attack', 'def': 'defense', 'atqe': 'sp_attack', 'spa': 'sp_attack',
                'defe': 'sp_defense', 'spd': 'sp_defense', 'vel': 'speed', 'spe': 'speed'}


def parse_spread(text):
    """«252 Atq/4 PS/252 Vel» (como StatSpread.cs) -> ({stat: n}, error)."""
    vals = {}
    for part in re.split(r'[/,]', text):
        part = part.strip()
        if not part:
            continue
        tok = re.split(r'[\s:=]+', part)
        if len(tok) != 2:
            return {}, f'«{part}» no es «número estadística»'
        num, name = (tok[0], tok[1]) if tok[0].isdigit() else (tok[1], tok[0])
        if not num.isdigit():
            return {}, f'«{part}» no tiene un número'
        key = STAT_ALIASES.get(re.sub(r'[^a-z0-9]', '', name.lower()), name.lower())
        if key in vals:
            return {}, f'estadística repetida en «{part}»'
        vals[key] = int(num)
    return vals, None


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
