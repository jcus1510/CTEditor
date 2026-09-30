#!/usr/bin/env python3
"""COMPLETAR LOS DATOS MAESTROS (Tools/datos_fuente/) con las especies que falten, sacadas de PokeAPI.

Uso:
    python3 Tools/verificar_pack/completar_fuente.py --gen 7

Añade a `datos_fuente/especies.csv` una fila por cada especie hasta la generación pedida que aún no esté: nombre,
curva, experiencia base, ratio de captura, EVs, grupos huevo, EVOLUCIONES (en el formato del editor: ivysaur@16,
raichu@objeto:thunder_stone, espeon@amistad+hora:dia...) y la Pokédex (número, categoría, altura, peso, color,
% de hembras, legendario y descripción del juego más reciente de la generación). Tipos, estadísticas, habilidades y
aprendizaje los pone después generar_packs.py con los valores de cada generación.

También añade a `datos_fuente/movimientos.csv` los movimientos que falten: objetivo, golpes, crítico, contacto,
etiquetas (sonido, puño, mordisco...) y los efectos que PokeAPI describe (estado, amedrentar, drenar, curar, etapas),
más los ajustes a mano de MOVE_FIX para los especiales. Los movimientos Z no: son de la mecánica de movimientos Z.

Las filas que ya existen NO se tocan (son datos editados a mano). Las habilidades nuevas se añaden a mano en
`datos_fuente/habilidades.csv` (sus efectos no están en PokeAPI de forma utilizable): ver Tools/README.md.
"""
import argparse, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csvlib import load, save  # noqa: E402
from pokeapi import table, pack_id, SPANISH  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'Tools', 'datos_fuente')

GROWTH = {'1': 'slow', '2': 'medium_fast', '3': 'fast', '4': 'medium_slow', '5': 'erratic', '6': 'fluctuating'}
COLOR = {'1': 'negro', '2': 'azul', '3': 'marrón', '4': 'gris', '5': 'verde', '6': 'rosa', '7': 'morado', '8': 'rojo',
         '9': 'blanco', '10': 'amarillo'}
STAT = {'1': 'hp', '2': 'attack', '3': 'defense', '4': 'sp_attack', '5': 'sp_defense', '6': 'speed'}
TIME = {'day': 'dia', 'night': 'noche', 'dusk': 'atardecer', 'morning': 'manana'}
# Grupos de versiones cuya Pokédex se prefiere, por generación (el más reciente primero).
DEX_VGS = {7: ['18', '17'], 8: ['20', '22', '23', '24'], 9: ['25', '27']}


def num(x):
    f = float(x)
    return str(int(f)) if f.is_integer() else ('%.2f' % f).rstrip('0').rstrip('.').replace('.', ',')


def evolutions(species_ids):
    """{especie: 'destino@método+condiciones | ...'} de las especies pedidas."""
    spc = {r['id']: r for r in table('pokemon_species')}
    ident = {k: pack_id(v['identifier']) for k, v in spc.items()}
    items = {r['id']: pack_id(r['identifier']) for r in table('items')}
    moves = {r['id']: pack_id(r['identifier']) for r in table('moves')}
    types = {r['id']: r['identifier'] for r in table('types')}
    locations = {r['id']: pack_id(r['identifier']) for r in table('locations')}
    out = {}
    for e in table('pokemon_evolution'):
        tgt = spc[e['evolved_species_id']]
        base = ident.get(tgt['evolves_from_species_id'])
        if not base or base not in species_ids:
            continue
        cond = []
        trig = e['evolution_trigger_id']
        if trig == '1':                                # subir de nivel
            if e['minimum_level']:
                method = e['minimum_level']
            elif e['minimum_happiness']:
                method = 'amistad'
            else:
                method = 'subir'
            if e['minimum_happiness'] and method != 'amistad':
                cond.append('amistad:' + e['minimum_happiness'])
        elif trig == '2':                              # intercambio
            method = 'intercambio' + (':' + items[e['held_item_id']] if e['held_item_id'] else '')
        elif trig == '3':                              # usar un objeto
            method = 'objeto:' + items[e['trigger_item_id']]
        else:
            continue                                    # mudar (Shedinja) y métodos de otras generaciones
        if e['minimum_affection']:
            cond.append('amistad:160')
        if e['time_of_day'] in TIME:
            cond.append('hora:' + TIME[e['time_of_day']])
        if e['known_move_id']:
            cond.append('sabe:' + moves[e['known_move_id']])
        if e['known_move_type_id']:
            cond.append('sabe_tipo:' + types[e['known_move_type_id']])
        if e['held_item_id'] and trig != '2':
            cond.append('lleva:' + items[e['held_item_id']])
        if e['location_id']:
            cond.append('lugar:' + locations.get(e['location_id'], e['location_id']))
        if e['gender_id']:
            cond.append('genero:' + ('hembra' if e['gender_id'] == '1' else 'macho'))
        if e['relative_physical_stats']:
            cond.append('stats:' + {'1': 'atq>def', '-1': 'atq<def', '0': 'atq=def'}[e['relative_physical_stats']])
        if e['party_species_id']:
            cond.append('equipo_especie:' + ident[e['party_species_id']])
        if e['party_type_id']:
            cond.append('equipo_tipo:' + types[e['party_type_id']])
        if e['needs_overworld_rain'] == '1':
            cond.append('clima:rain')
        text = pack_id(tgt['identifier']) + '@' + method + ''.join('+' + c for c in cond)
        out.setdefault(base, [])
        if text not in out[base]:
            out[base].append(text)
    return {k: ' | '.join(v) for k, v in out.items()}


# ---------------- Movimientos ----------------

TARGET = {'selected-pokemon': 'rival', 'selected-pokemon-me-first': 'rival', 'random-opponent': 'rival',
          'all-opponents': 'rivales', 'all-other-pokemon': 'rivales', 'opponents-field': 'rivales',
          'user': 'propio', 'users-field': 'propio', 'user-or-ally': 'propio', 'ally': 'propio', 'user-and-allies': 'propio',
          'all-allies': 'propio', 'specific-move': 'propio', 'entire-field': 'todos', 'all-pokemon': 'todos',
          'fainting-pokemon': 'propio'}
FLAG_TAG = {'sound': 'sonido', 'punch': 'puño', 'bite': 'mordisco', 'pulse': 'pulso', 'powder': 'polvo', 'ballistic': 'bomba',
            'dance': 'danza'}
AILMENT = {'paralysis': 'paralysis', 'sleep': 'sleep', 'freeze': 'freeze', 'burn': 'burn', 'poison': 'poison',
           'confusion': 'confusion', 'infatuation': 'infatuation', 'trap': 'trapped', 'yawn': 'drowsy', 'leech-seed': 'leech_seed'}
MOVE_STAT = {'1': 'hp', '2': 'attack', '3': 'defense', '4': 'sp_attack', '5': 'sp_defense', '6': 'speed', '7': 'accuracy', '8': 'evasion'}
CAT = {'1': 'estado', '2': 'fisico', '3': 'especial'}
# Movimientos Z (van en la mecánica de movimientos Z) y los de Let's Go (fuera de los juegos principales).
Z_OR_LETSGO = set(range(622, 659)) | set(range(695, 704)) | {719} | set(range(723, 729)) | set(range(729, 743))

# Ajustes a mano de los movimientos que PokeAPI no describe bien (columna → valor). Lo que no se pueda representar se
# aproxima y queda anotado en la columna del informe (Tools/README.md).
MOVE_FIX = {
    'shore_up': {'efectos': 'curar:50 [si !clima=sandstorm] | curar:66,67 [si clima=sandstorm]'},
    'first_impression': {'requisitos': 'propio.turnos_campo<1'},
    'baneful_bunker': {'efectos': 'estado_propio:baneful_bunker', 'etiquetas': 'no_metronomo'},
    'spirit_shackle': {'efectos': 'estado:cant_escape'},
    'anchor_shot': {'efectos': 'estado:cant_escape'},
    'darkest_lariat': {'etiquetas': 'ignora_etapas'},
    'sparkling_aria': {'efectos': 'curar_estado_rival:burn'},
    'floral_healing': {'efectos': 'curar_rival:50 [si !campo=grassy_terrain] | curar_rival:66,67 [si campo=grassy_terrain]'},
    'strength_sap': {'efectos': 'stat:attack:-1 | curar:33'},
    'solar_blade': {'dos_turnos': 'carga', 'potencia_mod': 'x0,5 [si clima=rain] | x0,5 [si clima=hail] | x0,5 [si clima=sandstorm]'},
    'laser_focus': {'efectos': 'foco:3'},
    'psychic_terrain': {'efectos': 'lado:psychic_terrain'},
    'power_trip': {'formula_potencia': '20+20*subidas'},
    'burn_up': {'requisitos': 'propio.tipo=fire'},
    'purify': {'requisitos': 'rival.estado', 'efectos': 'curar:50 | curar_estado_rival'},
    'aurora_veil': {'requisitos': 'clima=hail', 'efectos': 'lado:aurora_veil'},
    'psychic_fangs': {'efectos': 'quitar_lado:reflect:light_screen:aurora_veil'},
    'prismatic_laser': {'dos_turnos': 'recarga'},
    'natures_madness': {'daño_especial': 'mitad', 'potencia': '0'},
    'mind_blown': {'efectos': 'retroceso_ps:50'},
    'beak_blast': {'etiquetas': 'bomba'},
    'spectral_thief': {'etiquetas': 'ignora_etapas'},
}


def moves_to_add(gen, have):
    names = {r['move_id']: r['name'] for r in table('move_names') if r['local_language_id'] == SPANISH}
    types = {r['id']: r['identifier'] for r in table('types')}
    targets = {r['id']: r['identifier'] for r in table('move_targets')}
    flags = {r['id']: r['identifier'] for r in table('move_flags')}
    ailments = {r['id']: r['identifier'] for r in table('move_meta_ailments')}
    meta = {r['move_id']: r for r in table('move_meta')}
    stat_changes = {}
    for r in table('move_meta_stat_changes'):
        stat_changes.setdefault(r['move_id'], []).append((MOVE_STAT[r['stat_id']], int(r['change'])))
    move_flags = {}
    for r in table('move_flag_map'):
        move_flags.setdefault(r['move_id'], set()).add(flags[r['move_flag_id']])
    out = []
    for m in table('moves'):
        mid = int(m['id'])
        ident = pack_id(m['identifier'])
        if mid >= 10000 or mid in Z_OR_LETSGO or int(m['generation_id']) > gen or ident in have or ident == 'struggle':
            continue
        f = move_flags.get(m['id'], set())
        mt = meta.get(m['id'], {})
        cat = CAT[m['damage_class_id']]
        target = TARGET.get(targets.get(m['target_id'], ''), 'rival')
        effects = []
        ail = AILMENT.get(ailments.get(mt.get('meta_ailment_id', ''), ''), '')
        if ail:
            chance = int(mt.get('ailment_chance') or 0)
            self_target = target == 'propio'
            effects.append(('estado_propio' if self_target else 'estado') + ':' + ail + (f'@{chance}' if 0 < chance < 100 else ''))
        if int(mt.get('flinch_chance') or 0) > 0:
            c = int(mt['flinch_chance'])
            effects.append('amedrentar' + (f'@{c}' if c < 100 else ''))
        drain = int(mt.get('drain') or 0)
        if drain > 0: effects.append(f'drenar:{drain}')
        if drain < 0: effects.append(f'retroceso:{-drain}')
        if int(mt.get('healing') or 0) > 0: effects.append(f"curar:{mt['healing']}")
        sc = int(mt.get('stat_chance') or 0)
        meta_cat = mt.get('meta_category_id', '')
        own = meta_cat in ('2', '7') or (cat == 'estado' and target == 'propio')   # net-good-stats / damage+raise
        for stat, change in stat_changes.get(m['id'], []):
            effects.append(('stat_propio' if own else 'stat') + f':{stat}:{change:+d}' + (f'@{sc}' if 0 < sc < 100 else ''))
        lo, hi = mt.get('min_hits') or '', mt.get('max_hits') or ''
        row = {
            'id': ident, 'nombre': names.get(m['id'], ident), 'tipo': types[m['type_id']], 'categoria': cat,
            'potencia': m['power'] or '0', 'precision': m['accuracy'] or 'nunca', 'pp': m['pp'] or '1',
            'prioridad': m['priority'] or '0', 'objetivo': target,
            'golpes': f'{lo}-{hi}' if lo and hi and lo != hi else (lo or '1'),
            'critico': mt.get('crit_rate') or '0',
            'dos_turnos': 'recarga' if 'recharge' in f else 'carga' if 'charge' in f else 'no',
            'contacto': 'si' if 'contact' in f else 'no', 'daño_especial': '', 'respeta_inmunidad': 'no',
            'potencia_mod': '', 'formula_potencia': '', 'requisitos': '', 'stat_ataque': '', 'stat_defensa': '',
            'ataca_con_rival': 'no', 'etiquetas': '|'.join(FLAG_TAG[x] for x in sorted(f) if x in FLAG_TAG),
            'efectos': ' | '.join(effects), 'tipo_clima': '', 'animacion': '',
        }
        for k, v in MOVE_FIX.get(ident, {}).items():
            if k == 'etiquetas' and row['etiquetas']:
                v = row['etiquetas'] + '|' + v
            row[k] = v
        out.append(row)
    return out


# ---------------- Movimientos Z (7.ª gen.) ----------------
# Los genéricos (uno por tipo: PokeAPI los tiene en versión física y especial) tienen potencia «1» = la de la tabla de la
# ficha de mecánica según el movimiento base; los exclusivos, la suya y sus efectos.
Z_FIX = {
    'catastropika': {'contacto': 'si'},
    '10_000_000_volt_thunderbolt': {'critico': '2'},
    'stoked_sparksurfer': {'efectos': 'estado:paralysis'},
    'extreme_evoboost': {'objetivo': 'propio', 'efectos': 'stat_propio:attack:+2 | stat_propio:defense:+2 | stat_propio:sp_attack:+2 | stat_propio:sp_defense:+2 | stat_propio:speed:+2'},
    'pulverizing_pancake': {'contacto': 'si'},
    'genesis_supernova': {'efectos': 'lado:psychic_terrain'},
    'malicious_moonsault': {'contacto': 'si'},
    'guardian_of_alola': {'daño_especial': 'mitad', 'potencia': '0'},
    'soul_stealing_7_star_strike': {'contacto': 'si'},
    'clangorous_soulblaze': {'objetivo': 'rivales', 'etiquetas': 'sonido', 'efectos': 'stat_propio:attack:+1 | stat_propio:defense:+1 | stat_propio:sp_attack:+1 | stat_propio:sp_defense:+1 | stat_propio:speed:+1'},
    'lets_snuggle_forever': {'contacto': 'si'},
    'searing_sunraze_smash': {'contacto': 'si'},
}


def z_moves_to_add(have):
    names = {r['move_id']: r['name'] for r in table('move_names') if r['local_language_id'] == SPANISH}
    types = {r['id']: r['identifier'] for r in table('types')}
    out, seen = [], set()
    for m in table('moves'):
        mid = int(m['id'])
        if mid not in Z_OR_LETSGO or mid >= 729:
            continue
        ident = pack_id(m['identifier'].replace('--physical', '').replace('--special', ''))
        if ident in have or ident in seen:
            continue
        seen.add(ident)
        generic = '--' in m['identifier']
        row = {
            'id': ident, 'nombre': names.get(m['id'], ident).split(' (')[0], 'tipo': types[m['type_id']],
            'categoria': CAT[m['damage_class_id']], 'potencia': '1' if generic else (m['power'] or '0'), 'precision': 'nunca',
            'pp': '1', 'prioridad': '0', 'objetivo': 'rival', 'golpes': '1', 'critico': '0', 'dos_turnos': 'no', 'contacto': 'no',
            'daño_especial': '', 'respeta_inmunidad': 'no', 'potencia_mod': '', 'formula_potencia': '', 'requisitos': '',
            'stat_ataque': '', 'stat_defensa': '', 'ataca_con_rival': 'no', 'etiquetas': 'z|no_metronomo', 'efectos': '',
            'tipo_clima': '', 'animacion': '',
        }
        for k, v in Z_FIX.get(ident, {}).items():
            row[k] = row['etiquetas'] + '|' + v if k == 'etiquetas' else v
        out.append(row)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--gen', type=int, required=True)
    args = ap.parse_args()

    mpath = os.path.join(SRC, 'movimientos.csv')
    mheads, mrows = load(mpath)[:2]
    new_moves = moves_to_add(args.gen, {r['id'] for r in mrows})
    if args.gen >= 7:
        new_moves += z_moves_to_add({r['id'] for r in mrows} | {r['id'] for r in new_moves})
    if new_moves:
        for r in new_moves:
            mrows.append({h: r.get(h, '') for h in mheads})
        save(mpath, mheads, mrows)
        print(f'{len(new_moves)} movimientos añadidos: {", ".join(r["id"] for r in new_moves)}')

    path = os.path.join(SRC, 'especies.csv')
    heads, rows = load(path)[:2]
    have = {r['id'] for r in rows}

    spc = {r['id']: r for r in table('pokemon_species')}
    new = [r for r in spc.values() if int(r['generation_id']) <= args.gen and pack_id(r['identifier']) not in have]
    new.sort(key=lambda r: int(r['id']))
    if not new:
        print('No falta ninguna especie.'); return
    new_ids = {pack_id(r['identifier']) for r in new}

    names = {r['pokemon_species_id']: r for r in table('pokemon_species_names') if r['local_language_id'] == SPANISH}
    pk = {r['species_id']: r for r in table('pokemon') if r['is_default'] == '1'}
    effort = {}
    for r in table('pokemon_stats'):
        if r['effort'] not in ('', '0'):
            effort.setdefault(r['pokemon_id'], []).append(f"{STAT[r['stat_id']]}:{r['effort']}")
    eggs = {r['id']: pack_id(r['identifier']) for r in table('egg_groups')}
    egg_of = {}
    for r in table('pokemon_egg_groups'):
        egg_of.setdefault(r['species_id'], []).append(eggs[r['egg_group_id']])
    version_vg = {r['id']: r['version_group_id'] for r in table('versions')}
    order = DEX_VGS.get(args.gen, [])
    flavor = {}
    for r in table('pokemon_species_flavor_text'):
        if r['language_id'] != SPANISH:
            continue
        vg = version_vg.get(r['version_id'], '')
        rank = order.index(vg) if vg in order else len(order) + (100 - int(vg or 0))
        text = ' '.join(r['flavor_text'].replace('\x0c', ' ').split())
        if r['species_id'] not in flavor or rank < flavor[r['species_id']][0]:
            flavor[r['species_id']] = (rank, text)
    evo = evolutions(new_ids)

    added = []
    for s in new:
        sid, ident = s['id'], pack_id(s['identifier'])
        p = pk[sid]
        n = names.get(sid, {})
        gr = int(s['gender_rate'])
        row = {h: '' for h in heads}
        row.update({
            'id': ident, 'nombre': n.get('name', ident),
            'curva': GROWTH[s['growth_rate_id']], 'exp_base': p['base_experience'] or '0', 'ratio_captura': s['capture_rate'],
            'evs': '|'.join(effort.get(p['id'], [])), 'grupos_huevo': '|'.join(egg_of.get(sid, [])),
            'evoluciona': evo.get(ident, ''), 'numero': sid,
            'categoria': n.get('genus', '').replace('Pokémon ', '').strip(),
            'altura': num(int(p['height']) / 10), 'peso': num(int(p['weight']) / 10), 'color': COLOR[s['color_id']],
            'hembras': 'sin_genero' if gr < 0 else num(gr * 12.5),
            'legendario': 'si' if s['is_legendary'] == '1' or s['is_mythical'] == '1' else 'no',
            'descripcion': flavor.get(sid, (0, ''))[1],
        })
        rows.append(row)
        added.append(ident)
    save(path, heads, rows)
    print(f'{len(added)} especies añadidas a {os.path.relpath(path, ROOT)}: {", ".join(added)}')


if __name__ == '__main__':
    main()
