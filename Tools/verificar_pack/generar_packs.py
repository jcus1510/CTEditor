#!/usr/bin/env python3
"""GENERAR LOS PACKS FIELES A CADA GENERACIÓN (Gen1 … Gen6) desde los datos maestros y PokeAPI.

Uso:
    python3 Tools/verificar_pack/generar_packs.py            # los 6 packs
    python3 Tools/verificar_pack/generar_packs.py --gen 3    # solo uno

Cada pack GenN = el «juego» de esa generación: todas las especies hasta ella (151, 251, 386, 493, 649, 721) con
los valores QUE TENÍAN EN ESA GENERACIÓN, sacados de PokeAPI (deshaciendo los cambios posteriores):

  especies     tipos, estadísticas base (1.ª gen.: una sola «Especial» → Atq. Esp. = Def. Esp.), habilidades
               (desde la 3.ª; oculta desde la 5.ª), aprendizaje por nivel del juego de referencia de la
               generación y MT/tutor/huevo de todos sus juegos, grupos huevo (desde la 2.ª), sin género en la 1.ª,
               evoluciones solo hacia especies que existen.
  movimientos  solo los que existen en la generación, con su tipo, potencia, precisión, PP y prioridad de
               entonces; categoría física/especial SEGÚN EL TIPO hasta la 3.ª generación.
  habilidades  las que usan sus especies (desde la 3.ª).
  entrenadores los de los juegos hasta esa generación, con sus movimientos válidos en ella (sin objetos
               equipados en la 1.ª ni naturalezas antes de la 3.ª), más el laboratorio de IA y los especialistas.
  tipos, tabla_tipos, naturalezas, grupos_huevo  (generar_base.py)

Lo que no se puede sacar de PokeAPI (efectos de movimientos, configuración de habilidades, Pokédex, entrenadores)
viene de Tools/datos_fuente/. Para cambiarlo, edita allí y vuelve a generar.
"""
import argparse, collections, hashlib, os, re, shutil, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csvlib import load, save, split_list  # noqa: E402
from pokeapi import PokeApi, table, pack_id, SPANISH  # noqa: E402
import generar_base  # noqa: E402
from formas import Forms, VARIANTS, BASE_TO_VARIANT_EVOS  # noqa: E402

VARIANT_ITEMS = {item for _, _, item in VARIANTS.values() if item}   # se usan fuera del combate para cambiar de variante

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'Tools', 'datos_fuente')
PACKS = os.path.join(ROOT, 'Assets', 'GameContent', 'Packs')

# gen: (última especie, grupo de versiones de referencia para el aprendizaje por nivel, juego de referencia)
GENS = {1: (151, '1', 'Rojo/Azul'), 2: (251, '4', 'Cristal'), 3: (386, '6', 'Esmeralda'),
        4: (493, '9', 'Platino'), 5: (649, '14', 'Negro 2/Blanco 2'), 6: (721, '16', 'Rubí Omega/Zafiro Alfa'),
        7: (807, '18', 'Ultrasol/Ultraluna')}
MAIN_VGS = {'1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '14', '15', '16', '17', '18'}   # sin Colosseum/XD ni Let's Go
CAT_ES = {'physical': 'fisico', 'special': 'especial', 'status': 'estado'}
LAB = {1: 'dragonite@50 | alakazam@50 | gyarados@50 | gengar@50 | snorlax@50 | jolteon@50',
       2: 'tyranitar@50 | alakazam@50 | gyarados@50 | gengar@50 | snorlax@50 | skarmory@50',
       3: 'salamence@50 | metagross@50 | gyarados@50 | gengar@50 | swampert@50 | blaziken@50',
       4: 'garchomp@50 | metagross@50 | gyarados@50 | gengar@50 | scizor@50 | togekiss@50',
       5: 'garchomp@50 | metagross@50 | gyarados@50 | gengar@50 | scizor@50 | togekiss@50',
       6: 'garchomp@50 | metagross@50 | gyarados@50 | gengar@50 | scizor@50 | togekiss@50',
       7: 'garchomp@50 | metagross@50 | gyarados@50 | gengar@50 | toxapex@50 | kommo_o@50'}
# Entrenadores que solo existen desde una generación (los de Alola usan especies antiguas y, sin esto, entrarían en
# los packs anteriores).
TRAINER_FROM_GEN = {t: 7 for t in ('kahuna_hala', 'kahuna_olivia', 'kahuna_nanu', 'kahuna_hapu', 'alto_mando_hala',
                                   'alto_mando_olivia', 'alto_mando_acerola', 'alto_mando_kahili', 'alto_mando_molayne',
                                   'campeon_kukui', 'rival_hau_liga', 'gladio', 'jefe_guzman', 'presidenta_samina')}
# Entrenadores que megaevolucionan en los juegos (desde la 6.ª gen.): (especie, megapiedra).
MEGA_TRAINERS = {'campeona_dianta': ('gardevoir', 'gardevoirite')}
MEMBER = re.compile(r'^(?P<sp>[^@\[\]{}~#"%!()]+)@(?P<lvl>\d+)(?P<g>%[mhMH])?(?:\[(?P<moves>[^\]]*)\])?'
                    r'(?:\{(?P<held>[^}]*)\})?(?:~(?P<nat>[^#"!(]+))?(?:!(?P<ab>[^#"(]+))?(?P<ev>\([^)]*\))?'
                    r'(?P<iv>#\d*(?:\([^)]*\))?)?(?P<nick>"[^"]*")?$')


def num(x):
    return str(int(x)) if float(x).is_integer() else str(x).replace('.', ',')


class Gen:
    """Todo lo de PokeAPI que hace falta para una generación."""

    def __init__(self, n):
        self.n = n
        self.max_dex, self.ref_vg, self.game = GENS[n]
        self.api = PokeApi(n)
        pk = {r['id']: r for r in table('pokemon') if r['is_default'] == '1'}
        spc = {r['id']: r for r in table('pokemon_species')}
        self.pid = {pack_id(spc[r['species_id']]['identifier']): pid for pid, r in pk.items()}
        self.dex = {pack_id(spc[r['species_id']]['identifier']): int(r['species_id']) for r in pk.values()}
        self.species_vals = self.api.species()
        self.moves_vals = self.api.moves()
        # Estadística Especial de la 1.ª generación (stat 9).
        self.special = {r['pokemon_id']: r['base_stat'] for r in table('pokemon_stats_past')
                        if r['stat_id'] == '9' and int(r['generation_id']) >= n} if n == 1 else {}
        self._abilities()
        self._learnsets()

    def _abilities(self):
        ab = {r['id']: r for r in table('abilities')}
        self.ability_gen = {pack_id(r['identifier']): int(r['generation_id']) for r in ab.values()}
        self.ability_name = {pack_id(ab[r['ability_id']]['identifier']): r['name'] for r in table('ability_names')
                             if r['local_language_id'] == SPANISH and r['ability_id'] in ab}
        cur = collections.defaultdict(dict)
        for r in table('pokemon_abilities'):
            cur[r['pokemon_id']][r['slot']] = pack_id(ab[r['ability_id']]['identifier'])
        past = collections.defaultdict(list)
        for r in table('pokemon_abilities_past'):
            past[(r['pokemon_id'], r['slot'])].append((int(r['generation_id']), r['ability_id']))
        for (pid, slot), v in past.items():
            later = sorted(x for x in v if x[0] >= self.n)
            if later:
                a = later[0][1]
                if a:
                    cur[pid][slot] = pack_id(ab[a]['identifier'])
                else:
                    cur[pid].pop(slot, None)
        self.abilities = cur

    def _learnsets(self):
        gen_vgs = {vg for vg, g in self.api.vg_gen.items() if g == self.n and vg in MAIN_VGS}
        move_id = {r['id']: pack_id(r['identifier']) for r in table('moves')}
        lvl = collections.defaultdict(lambda: collections.defaultdict(list))   # pid -> vg -> [(level, move)]
        other = collections.defaultdict(lambda: collections.defaultdict(set))  # pid -> método -> moves
        for r in table('pokemon_moves'):
            vg = r['version_group_id']
            if vg not in gen_vgs:
                continue
            mv = move_id[r['move_id']]
            if r['pokemon_move_method_id'] == '1':
                lvl[r['pokemon_id']][vg].append((max(1, int(r['level'] or 1)), mv))
            elif r['pokemon_move_method_id'] in ('2', '3', '4'):
                other[r['pokemon_id']][r['pokemon_move_method_id']].add(mv)
        self.level_moves, self.other_moves = lvl, other
        self.gen_vgs = gen_vgs

    def learnset(self, sp):
        return self.learnset_of_pid(self.pid.get(sp))

    def learnset_of_pid(self, pid):
        by_vg = self.level_moves.get(pid, {})
        # El juego de referencia; si la especie no está en él, el primero de la generación que la tenga.
        order = [self.ref_vg] + sorted(v for v in self.gen_vgs if v != self.ref_vg)
        for vg in order:
            if by_vg.get(vg):
                seen, out = set(), []
                for lv, mv in sorted(by_vg[vg]):
                    if (lv, mv) not in seen:
                        seen.add((lv, mv)); out.append((lv, mv))
                return out
        return []


def build(n, verbose=True):
    g = Gen(n)
    out = os.path.join(PACKS, f'Gen{n}')
    os.makedirs(out, exist_ok=True)
    notes = collections.defaultdict(list)

    # ---------------- Movimientos ----------------
    mh, mrows, _, _ = load(os.path.join(SRC, 'movimientos.csv'))
    moves = []
    for r in mrows:
        a = g.moves_vals.get(r['id']) or g.moves_vals.get(r['id'] + '__physical')   # movimientos Z genéricos
        if not a:
            continue   # no existe en esta generación
        r = dict(r)
        r['tipo'] = a['tipo']
        if r['potencia'] != '1' and a['potencia']:
            r['potencia'] = a['potencia']
        r['precision'] = a['precision'] or 'nunca'
        r['pp'] = a['pp']
        r['prioridad'] = a['prioridad']
        r['categoria'] = CAT_ES.get(a['categoria'], r['categoria'])   # hasta la 3.ª gen., ya viene según el tipo
        moves.append(r)
    move_ids = {m['id'] for m in moves}

    # ---------------- Especies ----------------
    sh, srows, _, _ = load(os.path.join(SRC, 'especies.csv'))
    species = [dict(r) for r in srows if g.dex.get(r['id'], 99999) <= g.max_dex]
    ids = {s['id'] for s in species}
    # Las variantes de esta generación también son destinos de evolución válidos (Rockruff → Lycanroc Nocturno...).
    variant_ids = {pack_id(k) for k, (b, gmin, _) in VARIANTS.items() if n >= gmin and b in ids}
    used_abilities = set()
    for s in species:
        sp, pid = s['id'], g.pid[s['id']]
        v = g.species_vals[sp]
        for col in ('ps', 'ataque', 'defensa', 'atq_esp', 'def_esp', 'velocidad', 'tipos'):
            s[col] = v[col]
        if n == 1 and pid in g.special:
            s['atq_esp'] = s['def_esp'] = g.special[pid]
        ab = g.abilities.get(pid, {})
        if n >= 3:
            s['habilidad'] = ab.get('1', '')
            s['habilidad_2'] = ab.get('2', '')
            s['habilidad_oculta'] = ab.get('3', '') if n >= 5 else ''
            for col in ('habilidad', 'habilidad_2', 'habilidad_oculta'):
                if s[col] and g.ability_gen.get(s[col], 99) > n:
                    s[col] = ''
                if s[col]:
                    used_abilities.add(s[col])
        else:
            s['habilidad'] = s['habilidad_2'] = s['habilidad_oculta'] = ''
        s['aprende'] = '|'.join(f'{lv}:{mv}' for lv, mv in g.learnset(sp) if mv in move_ids)
        om = g.other_moves.get(pid, {})
        for col, meth in (('mt', '4'), ('tutor', '3'), ('huevo', '2')):
            s[col] = '|'.join(sorted(m for m in om.get(meth, ()) if m in move_ids))
        if not s['aprende']:
            notes['sin aprendizaje por nivel en PokeAPI'].append(sp)
        if n == 1:
            s['grupos_huevo'] = ''
            s['hembras'] = 'sin_genero'      # en la 1.ª generación no hay géneros
        evos = []
        extra_evo = BASE_TO_VARIANT_EVOS.get(sp) if n >= 7 else None
        for e in split_list(s['evoluciona']) + ([extra_evo] if extra_evo else []):
            tgt = e.split('@')[0].strip()
            if tgt not in ids and tgt not in variant_ids:
                continue
            bad = [m for m in re.findall(r'sabe:(\w+)', e) if m not in move_ids]
            if bad:
                notes['evolución que pide un movimiento que no existe'].append(f'{sp}→{tgt}')
                continue
            if n == 1 and ('amistad' in e or 'hora:' in e or re.search(r'intercambio:\w+', e) or 'lleva:' in e):
                notes['evolución con un método posterior a la 1.ª gen. (se deja)'].append(f'{sp}→{tgt}')
            evos.append(e)
        s['evoluciona'] = ' | '.join(evos) if evos else ''
    # ---------------- Formas de combate y variantes ----------------
    for col in ('forma_de', 'objeto_variante', 'formas', 'cambios_forma'):
        if col not in sh:
            sh.append(col)
    fm = Forms(g)
    with_forms = []
    for s in species:
        for col in ('forma_de', 'objeto_variante', 'formas', 'cambios_forma'):
            s.setdefault(col, '')
        if n >= 3 and fm.battle_forms(s, n, used_abilities):
            with_forms.append(s['id'])
    variants = fm.variants({s['id']: s for s in species}, n, move_ids, used_abilities) if n >= 3 else []
    species.extend(variants)
    if with_forms:
        notes['formas de combate (se cambian en mitad del combate)'] = with_forms
    if variants:
        notes['variantes (especies con «forma_de»)'] = [f"{v['id']} → {v['forma_de']}" + (f" ({v['objeto_variante']})" if v['objeto_variante'] else '') for v in variants]

    # ---------------- Habilidades ----------------
    ah, arows, _, _ = load(os.path.join(SRC, 'habilidades.csv'))
    abil = [r for r in arows if r['id'] in used_abilities]
    have = {r['id'] for r in abil}
    for a in sorted(used_abilities - have):   # sin configurar en los datos maestros: solo con nombre
        abil.append({'id': a, 'nombre': g.ability_name.get(a, a)})
        notes['habilidades solo con nombre (sin efecto configurado)'].append(a)

    # ---------------- Entrenadores ----------------
    th, trows, _, _ = load(os.path.join(SRC, 'entrenadores.csv'))
    master = {r['id']: r for r in trows}
    level_of = {r['id']: r.get('nivel_ia', '') for r in trows}
    legacy = {'novato': '1', 'listo': '2', 'experto': '4'}
    source = {1: 'entrenadores_gen1.csv', 2: 'entrenadores_gen2.csv', 3: 'entrenadores_gen4.csv', 4: 'entrenadores_gen4.csv'}
    if n in source:
        _, base_rows, _, _ = load(os.path.join(SRC, source[n]))
        candidates = [dict(r) for r in base_rows]
        extra = [dict(r) for r in trows if r['id'].startswith('especialista_')]
    else:
        candidates, extra = [dict(r) for r in trows if not r['id'].startswith('prueba_nivel_')], []
    trainers = []
    for r in candidates + extra:
        if TRAINER_FROM_GEN.get(r['id'], 0) > n:
            continue
        if not r.get('nivel_ia'):
            r['nivel_ia'] = level_of.get(r['id']) or legacy.get(r.get('ia', ''), '')
        team, dropped = [], []
        for mem in split_list(r.get('equipo', '')):
            m = MEMBER.match(mem)
            if not m or m['sp'].strip() not in ids:
                dropped.append(mem.split('@')[0]); continue
            mv = [x.strip() for x in (m['moves'] or '').split('/') if x.strip()]
            ok = [x for x in mv if x in move_ids]
            txt = f"{m['sp'].strip()}@{m['lvl']}" + (m['g'] or '' if n >= 2 else '')
            if ok:
                txt += '[' + '/'.join(ok) + ']'
            if m['held'] and n >= 2:
                txt += '{' + m['held'] + '}'
            if m['nat'] and n >= 3:
                txt += '~' + m['nat']
            if m['ab'] and n >= 3:
                txt += '!' + m['ab']
            if m['ev'] and n >= 3:
                txt += m['ev']
            txt += (m['iv'] or '') + (m['nick'] or '')
            team.append(txt)
        if n >= 6 and r['id'] in MEGA_TRAINERS:   # los que megaevolucionan en los juegos: su megapiedra
            sp_mega, stone = MEGA_TRAINERS[r['id']]
            for i, t in enumerate(team):
                if t.startswith(sp_mega + '@'):
                    team[i] = re.sub(r'\{[^}]*\}', '', t)
                    head = re.match(r'^[^\[{~#"]+(?:\[[^\]]*\])?', team[i]).group(0)
                    team[i] = head + '{' + stone + '}' + team[i][len(head):]
                    break
        if len(team) < max(1, len(split_list(r.get('equipo', ''))) // 2) or not team:
            continue   # le faltan la mayoría de sus especies en esta generación
        if dropped:
            notes['entrenadores con miembros de generaciones posteriores (quitados)'].append(r['id'])
        r['equipo'] = ' | '.join(team)
        trainers.append(r)
    names = ['Novato', 'Aficionado', 'Veterano', 'Élite', 'Campeón', 'Maestro', 'Injusto']
    for lv in range(1, 8):
        trainers.append({'id': f'prueba_nivel_{lv}', 'nombre': f'{names[lv - 1]} (nivel {lv})', 'clase': 'Laboratorio de IA',
                         'nivel_ia': str(lv), 'ia': ['novato', 'novato', 'listo', 'experto', 'experto', 'experto', 'experto'][lv - 1],
                         'usa_objetos': 'si', 'curar_bajo': '25', 'puede_cambiar': 'si', 'movimientos_auto': 'ia', 'dinero_base': '0',
                         'equipo': LAB[n], 'frase_inicio': f'Soy la IA de nivel {lv}: mismo equipo que todos, distinta cabeza.',
                         'frase_derrota': 'Anotado en el laboratorio.', 'frase_victoria': 'La cabeza también cuenta.'})

    # ---------------- Nombres en inglés (para importar/exportar en formato Showdown) ----------------
    english_names(species, moves, abil, sh, mh, ah)

    # ---------------- Escribir ----------------
    save(os.path.join(out, 'especies.csv'), sh, species)
    save(os.path.join(out, 'movimientos.csv'), mh, moves)
    if n >= 3:
        save(os.path.join(out, 'habilidades.csv'), ah, abil)
    elif os.path.exists(os.path.join(out, 'habilidades.csv')):
        os.remove(os.path.join(out, 'habilidades.csv'))
    save(os.path.join(out, 'entrenadores.csv'), th, trainers)
    items = items_for(n)
    save(os.path.join(out, 'objetos.csv'), ITEM_HEADERS, items)
    notes_items = collections.Counter(r['categoria'] for r in items if not r['efectos'])
    notes['objetos solo con sus datos (sin efectos todavía), por categoría'] = [f'{k}: {v}' for k, v in sorted(notes_items.items())]
    sys.argv = ['generar_base.py', out, '--gen', str(n)]
    generar_base.main()
    write_report(n, g, out, species, moves, abil, trainers, notes)
    ensure_metas(out)
    if verbose:
        print(f'Gen{n}: {len(species)} especies, {len(moves)} movimientos, {len(abil)} habilidades, {len(trainers)} entrenadores')
    return notes


# ---------------- Objetos: todos los de la generación, con TODOS sus efectos ----------------

ITEM_HEADERS = ['id', 'nombre', 'nombre_en', 'descripcion', 'categoria', 'precio', 'en_combate', 'fuera_combate', 'se_gasta',
                'es_baya', 'efectos']

ITEM_CATEGORY = {'evolution': 'Evolution', 'vitamins': 'Vitamin'}

# Las categorías de la mochila, como en los juegos (pocas): las finas de antes cuentan como la suya (ItemCategories).
BAG_CATEGORY = {'Revive': 'Medicine', 'StatusCure': 'Medicine', 'PpRestore': 'Medicine', 'Vitamin': 'Medicine',
                'Held': 'Other', 'Evolution': 'Other'}


def bag_category(row):
    if row.get('es_baya') == 'si':
        return 'Berry'
    if re.match(r'^(tm|hm|tr)\d', row['id']):
        return 'Machine'
    return BAG_CATEGORY.get(row['categoria'], row['categoria'] or 'Other')


ENGLISH = '9'   # local_language_id del inglés (los nombres de Showdown)


def english_names(species, moves, abil, sh, mh, ah):
    """Columna nombre_en (el nombre de Showdown) en especies, movimientos y habilidades. Variantes al estilo Showdown:
    «Rotom-Wash», «Deoxys-Attack» (el nombre de la especie base + la parte de su forma)."""
    sp_en = {pack_id(s['identifier']): None for s in table('pokemon_species')}
    ids = {r['id']: pack_id(r['identifier']) for r in table('pokemon_species')}
    for r in table('pokemon_species_names'):
        if r['local_language_id'] == ENGLISH and r['pokemon_species_id'] in ids:
            # Showdown: Nidoran♀ = «Nidoran-F», Nidoran♂ = «Nidoran-M».
            sp_en[ids[r['pokemon_species_id']]] = r['name'].replace('♀', '-F').replace('♂', '-M')
    for s in species:
        base = s.get('forma_de') or ''
        if base:
            suffix = s['id'][len(base):].strip('_')
            s['nombre_en'] = (sp_en.get(base) or base) + ''.join('-' + p.capitalize() for p in suffix.split('_') if p)
        else:
            s['nombre_en'] = sp_en.get(s['id']) or ''
    mv = {r['id']: pack_id(r['identifier']) for r in table('moves')}
    mv_en = {mv[r['move_id']]: r['name'] for r in table('move_names') if r['local_language_id'] == ENGLISH and r['move_id'] in mv}
    for m in moves:
        m['nombre_en'] = mv_en.get(m['id'], '') or mv_en.get(m['id'] + '__physical', '').replace(' (Physical)', '')
    ab = {r['id']: pack_id(r['identifier']) for r in table('abilities')}
    ab_en = {ab[r['ability_id']]: r['name'] for r in table('ability_names') if r['local_language_id'] == ENGLISH and r['ability_id'] in ab}
    for a in abil:
        a['nombre_en'] = ab_en.get(a['id'], '')
    for headers in (sh, mh, ah):
        if 'nombre_en' not in headers:
            headers.insert(headers.index('nombre') + 1 if 'nombre' in headers else 1, 'nombre_en')


def items_for(n):
    """Filas de objetos.csv: TODOS los objetos que existen en la generación n (salvo las MT), con sus efectos. Los que tienen
    plantilla (Tools/datos_fuente/objetos.csv) llevan su categoría, dónde se usan, si son baya y sus EFECTOS; el
    nombre, la descripción y el precio son los oficiales de PokeAPI. Los demás: categoría según el bolsillo y, las bolas,
    su efecto de captura."""
    import verificar_pack
    templates = {r['id']: r for r in verificar_pack.item_templates()}
    first_gen = {}
    for r in table('item_game_indices'):
        g = int(r['generation_id'])
        first_gen[r['item_id']] = min(first_gen.get(r['item_id'], 99), g)
    cats = {r['id']: (r['pocket_id'], r['identifier']) for r in table('item_categories')}
    names = {r['item_id']: r['name'] for r in table('item_names') if r['local_language_id'] == SPANISH}
    english = {r['item_id']: r['name'] for r in table('item_names') if r['local_language_id'] == ENGLISH}
    flavor = {}
    for r in table('item_flavor_text'):
        # Los textos en español de PokeAPI para X/Y y ROZA (grupos 15-16) están DESCOLOCADOS (la Venusaurita habla de
        # Charizard, el Mega-Aro de una gema): se usa el primer texto en español correcto, de Sol/Luna en adelante.
        if r['language_id'] != SPANISH or int(r['version_group_id']) < 17:
            continue
        prev = flavor.get(r['item_id'])
        if prev is None or int(r['version_group_id']) < prev[0]:
            flavor[r['item_id']] = (int(r['version_group_id']), ' '.join(r['flavor_text'].split()))
    rows = []
    for it in table('items'):
        if it['identifier'].endswith('--bag'):
            continue   # Cristales Z: PokeAPI tiene la versión «de la mochila» y la «para llevar»; el editor usa una sola
        iid = pack_id(it['identifier'].replace('--held', ''))
        pocket, cat = cats.get(it['category_id'], ('', ''))
        tpl = templates.get(iid)
        # Sin índice de juego en PokeAPI (los cristales Z de Ultrasol/Ultraluna): cuenta desde la 7.ª si tiene datos en la fuente.
        gen_of = first_gen.get(it['id'], 7 if tpl and '--held' in it['identifier'] else 99)
        if gen_of > n or pocket == '4' or (cat in ('unused', 'all-machines') and not tpl):
            continue
        name = names.get(it['id']) or (tpl or {}).get('nombre') or iid
        desc = flavor.get(it['id'], (0, ''))[1] or (tpl or {}).get('descripcion', '')
        if tpl:
            row = {k: tpl.get(k, '') for k in ITEM_HEADERS}
            row.update(id=iid, nombre=name, nombre_en=english.get(it['id'], '') or tpl.get('nombre_en', ''), descripcion=desc,
                       precio=it['cost'] or tpl.get('precio', '0'))
        else:
            category = 'Ball' if pocket == '3' else 'Key' if pocket == '8' else ITEM_CATEGORY.get(cat, 'Other')
            row = {'id': iid, 'nombre': name, 'nombre_en': english.get(it['id'], ''), 'descripcion': desc,
                   'categoria': category, 'precio': it['cost'] or '0',
                   'en_combate': 'si' if category == 'Ball' else 'no',
                   'fuera_combate': 'si' if category in ('Evolution', 'Vitamin') or iid in VARIANT_ITEMS else 'no',
                   'se_gasta': 'si' if category in ('Ball', 'Evolution', 'Vitamin') else 'no',
                   'es_baya': 'si' if cat.endswith('berries') or iid.endswith('_berry') else 'no',
                   # Bolas especiales: de momento como una Poké Ball (sus condiciones se añaden en su efecto).
                   'efectos': 'al_usar: captura x1' if category == 'Ball' else ''}
        row['categoria'] = bag_category(row)
        rows.append(row)
    return rows


def write_report(n, g, out, species, moves, abil, trainers, notes):
    by = collections.Counter(t.get('nivel_ia', '') for t in trainers)
    lines = [f'PACK {n}.ª GENERACIÓN — fiel a su generación (generado por Tools/verificar_pack/generar_packs.py)',
             '=' * 88, '',
             f'• {len(species)} especies (hasta la n.º {g.max_dex}) con los datos QUE TENÍAN EN LA {n}.ª GENERACIÓN (PokeAPI):',
             '  tipos, estadísticas base' + (' (una sola ESPECIAL: Atq. Esp. = Def. Esp.)' if n == 1 else '') + ',',
             f'  aprendizaje por nivel de {g.game} y MT/tutor/huevo de los juegos de la generación.',
             f'• {len(moves)} movimientos: solo los que existen en la {n}.ª gen., con su tipo, potencia, precisión, PP y prioridad',
             '  de entonces.' + (' Categoría FÍSICA/ESPECIAL SEGÚN EL TIPO (así era hasta la 3.ª gen.).' if n <= 3 else ''),
             (f'• {len(abil)} habilidades' + (' (sin ocultas: llegaron en la 5.ª gen.)' if n < 5 else ' (con ocultas)') + '.')
             if n >= 3 else '• SIN habilidades (llegaron en la 3.ª generación).',
             '• Tipos y TABLA DE TIPOS de la generación' + (' (sin Siniestro ni Acero; Fantasma no afecta a Psíquico).' if n == 1
                                                          else ' (sin Hada).' if n < 6 else ' (con Hada).'),
             '• ' + ('Naturalezas y grupos huevo.' if n >= 3 else 'Grupos huevo (sin naturalezas: llegaron en la 3.ª gen.).' if n == 2
                     else 'Sin naturalezas, sin crianza, sin géneros y sin objetos equipados (así era la 1.ª gen.).'),
             f'• {len(trainers)} entrenadores con su nivel de IA: ' + ', '.join(f'nivel {k}: {v}' for k, v in sorted(by.items()) if k) + '.',
             '  Incluye el Laboratorio de IA (prueba_nivel_1 … 7: el mismo equipo en cada nivel) para el Torneo de IAs.', '',
             'APROXIMACIONES (lo que el MOTOR aún hace como en la 6.ª gen.; se ajustará con las mecánicas por generación):',
             '  • Los EFECTOS de los movimientos y de las habilidades son los de la 6.ª gen.'
             + (' (los que llegaron en la 7.ª, los suyos; algunas habilidades de la 7.ª solo tienen nombre).' if n >= 7 else ''),
             '  • Críticos, fórmula de daño, EVs (en vez de «experiencia de estadística») y demás reglas: las del Ruleset.',
             ]
    if n <= 2:
        lines.append('  • El motor aún da naturaleza aleatoria si existen fichas de naturaleza en el proyecto.')
    for title, items in notes.items():
        lines += ['', f'{title.upper()} ({len(items)}):', '   ' + ', '.join(sorted(set(items)))]
    lines += ['', 'Datos: PokeAPI (github.com/PokeAPI/pokeapi). Pokémon y sus nombres son marcas de Nintendo / Game Freak /',
              'The Pokémon Company: úsalo para aprender y para proyectos personales, no para publicar un juego.']
    with open(os.path.join(out, 'INFORME.txt'), 'w', encoding='utf-8-sig') as f:
        f.write('\n'.join(lines) + '\n')


def ensure_metas(folder):
    """Unity necesita un .meta por archivo; se crea con un GUID ESTABLE (derivado de la ruta) si falta."""
    rel_root = os.path.relpath(folder, ROOT)
    paths = [folder] + [os.path.join(folder, f) for f in os.listdir(folder) if not f.endswith('.meta')]
    for p in paths:
        meta = p + '.meta'
        if os.path.exists(meta):
            continue
        guid = hashlib.md5(os.path.relpath(p, ROOT).replace('\\', '/').encode()).hexdigest()
        body = (f'fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n'
                '  userData: \n  assetBundleName: \n  assetBundleVariant: \n') if os.path.isdir(p) else \
               (f'fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n'
                '  assetBundleName: \n  assetBundleVariant: \n')
        with open(meta, 'w', encoding='utf-8', newline='\n') as f:
            f.write(body)
    for f in os.listdir(folder):   # .meta huérfanos (archivos que ya no se generan)
        if f.endswith('.meta') and not os.path.exists(os.path.join(folder, f[:-5])):
            os.remove(os.path.join(folder, f))
    _ = rel_root


def main():
    ap = argparse.ArgumentParser(description='Genera los packs Gen1…Gen6 fieles a su generación.')
    ap.add_argument('--gen', type=int, choices=sorted(GENS), help='solo esta generación')
    args = ap.parse_args()
    for n in ([args.gen] if args.gen else sorted(GENS)):
        build(n)


if __name__ == '__main__':
    main()
