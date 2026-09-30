"""Datos de PokeAPI (github.com/PokeAPI/pokeapi, carpeta data/v2/csv), descargados una vez a Tools/.cache/pokeapi.

Da los valores POR GENERACIÓN: deshace con el historial de PokeAPI los cambios posteriores a la generación pedida
(estadísticas base, tipos, potencia/precisión/PP/prioridad de movimientos, tabla de tipos).
"""
import csv, collections, os, sys, urllib.request

BASE = 'https://raw.githubusercontent.com/PokeAPI/pokeapi/master/data/v2/csv/'
CACHE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '.cache', 'pokeapi')
SPANISH = '7'   # local_language_id del español
PHYSICAL_TYPES = {'normal', 'fighting', 'flying', 'ground', 'rock', 'bug', 'ghost', 'poison', 'steel', 'typeless'}
STAT_COL = {'1': 'ps', '2': 'ataque', '3': 'defensa', '4': 'atq_esp', '5': 'def_esp', '6': 'velocidad'}


def table(name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name + '.csv')
    if not os.path.exists(path):
        print(f'  descargando {name}.csv de PokeAPI…', file=sys.stderr)
        urllib.request.urlretrieve(BASE + name + '.csv', path)
    with open(path, encoding='utf-8') as f:
        return list(csv.DictReader(f))


def pack_id(identifier):
    """Id de PokeAPI (kings-shield) -> id del editor (kings_shield)."""
    return identifier.replace('-', '_')


class PokeApi:
    def __init__(self, gen):
        self.gen = gen
        self.vg_gen = {r['id']: int(r['generation_id']) for r in table('version_groups')}
        self.vg_order = {r['id']: int(r['order']) for r in table('version_groups')}
        # «???» (unknown: Maldición en la 2.ª-4.ª gen.) = «typeless» del editor: neutro contra todo.
        self.type_name = {r['id']: ('typeless' if r['identifier'] == 'unknown' else r['identifier']) for r in table('types')}
        self.type_gen = {r['identifier']: int(r['generation_id']) for r in table('types')}

    # ---------------- Tipos ----------------
    def types_in_gen(self):
        """Tipos de juego que existen en la generación (sin 'unknown', 'shadow', 'stellar')."""
        return [r['identifier'] for r in table('types')
                if int(r['id']) < 10000 and self.type_gen[r['identifier']] <= self.gen and r['identifier'] != 'stellar']

    def type_chart(self):
        """{(ataca, defiende): multiplicador} de la generación (solo lo que no es ×1)."""
        chart = {}
        for r in table('type_efficacy'):
            a, d = self.type_name.get(r['damage_type_id']), self.type_name.get(r['target_type_id'])
            if a and d:
                chart[(a, d)] = int(r['damage_factor']) / 100
        past = collections.defaultdict(list)
        for r in table('type_efficacy_past'):
            a, d = self.type_name[r['damage_type_id']], self.type_name[r['target_type_id']]
            past[(a, d)].append((int(r['generation_id']), int(r['damage_factor']) / 100))
        for k, v in past.items():
            later = sorted(x for x in v if x[0] >= self.gen)
            if later:
                chart[k] = later[0][1]
        alive = set(self.types_in_gen())
        return {k: v for k, v in chart.items() if v != 1 and k[0] in alive and k[1] in alive}

    # ---------------- Especies ----------------
    def species(self):
        """{id del editor: {'tipos': 'a|b', 'ps': ..., ...}} con los valores de la generación."""
        pk = {r['id']: r for r in table('pokemon') if r['is_default'] == '1'}
        sp_of = {r['id']: r['identifier'] for r in table('pokemon_species')}
        out = {}
        for pid, r in pk.items():
            out[pid] = {'id': pack_id(sp_of[r['species_id']])}
        for r in table('pokemon_stats'):
            if r['pokemon_id'] in out and r['stat_id'] in STAT_COL:
                out[r['pokemon_id']][STAT_COL[r['stat_id']]] = r['base_stat']
        past = collections.defaultdict(list)
        for r in table('pokemon_stats_past'):
            if r['stat_id'] in STAT_COL:
                past[(r['pokemon_id'], STAT_COL[r['stat_id']])].append((int(r['generation_id']), r['base_stat']))
        for (pid, st), v in past.items():
            later = sorted(x for x in v if x[0] >= self.gen)
            if later and pid in out:
                out[pid][st] = later[0][1]
        types = collections.defaultdict(list)
        for r in table('pokemon_types'):
            types[r['pokemon_id']].append((int(r['slot']), self.type_name[r['type_id']]))
        tpast = collections.defaultdict(lambda: collections.defaultdict(list))
        for r in table('pokemon_types_past'):
            tpast[r['pokemon_id']][int(r['generation_id'])].append((int(r['slot']), self.type_name[r['type_id']]))
        if self.gen == 1:   # 1.ª gen.: una sola estadística ESPECIAL (en el editor, Atq. Esp. = Def. Esp.)
            for r in table('pokemon_stats_past'):
                if r['stat_id'] == '9' and int(r['generation_id']) >= 1 and r['pokemon_id'] in out:
                    out[r['pokemon_id']]['atq_esp'] = out[r['pokemon_id']]['def_esp'] = r['base_stat']
        for pid in out:
            gens = sorted(g for g in tpast[pid] if g >= self.gen)
            chosen = tpast[pid][gens[0]] if gens else types[pid]
            out[pid]['tipos'] = '|'.join(t for _, t in sorted(chosen))
        return {v['id']: v for v in out.values()}

    # ---------------- Movimientos ----------------
    def moves(self):
        """{id del editor: {'tipo','potencia','precision','pp','prioridad','categoria','gen'}} de la generación."""
        dc = {r['id']: r['identifier'] for r in table('move_damage_classes')}
        mv = {}
        for r in table('moves'):
            mv[r['id']] = {'id': pack_id(r['identifier']), 'tipo': self.type_name.get(r['type_id']), 'potencia': r['power'],
                           'pp': r['pp'], 'precision': r['accuracy'], 'prioridad': r['priority'],
                           'categoria': dc.get(r['damage_class_id']), 'gen': int(r['generation_id'])}
        log = sorted(table('move_changelog'), key=lambda r: self.vg_order[r['changed_in_version_group_id']], reverse=True)
        cols = {'type_id': 'tipo', 'power': 'potencia', 'pp': 'pp', 'accuracy': 'precision', 'priority': 'prioridad'}
        for c in log:   # de lo más nuevo a lo más viejo: deshace los cambios posteriores a la generación
            if self.vg_gen[c['changed_in_version_group_id']] > self.gen and c['move_id'] in mv:
                for col, key in cols.items():
                    if c.get(col):
                        mv[c['move_id']][key] = self.type_name[c[col]] if col == 'type_id' else c[col]
        if self.gen <= 3:   # hasta la 3.ª gen., físico o especial lo decide el TIPO del movimiento
            for v in mv.values():
                if v['categoria'] != 'status':
                    v['categoria'] = 'physical' if v['tipo'] in PHYSICAL_TYPES else 'special'
        return {v['id']: v for v in mv.values() if v['gen'] <= self.gen}

    # ---------------- Qué puede aprender (para equipos de entrenadores) ----------------
    def learnsets(self):
        """
        {especie: [(movimiento, método, nivel)]} en CUALQUIER juego hasta la generación (un líder de Rojo/Azul con sus
        movimientos de entonces es legal), sumando la línea evolutiva previa.
        """
        groups = {vg for vg, g in self.vg_gen.items() if g <= self.gen}
        pk = {r['id']: r for r in table('pokemon') if r['is_default'] == '1'}
        spc = {r['id']: r for r in table('pokemon_species')}
        pid_of = {r['species_id']: pid for pid, r in pk.items()}
        move_id = {r['id']: pack_id(r['identifier']) for r in table('moves')}
        own = collections.defaultdict(list)
        for r in table('pokemon_moves'):
            if r['version_group_id'] in groups and r['pokemon_id'] in pk:
                own[r['pokemon_id']].append((move_id[r['move_id']], r['pokemon_move_method_id'], int(r['level'] or 0)))
        out = {}
        for pid, r in pk.items():
            chain, cur = [], r['species_id']
            while cur:
                if cur in pid_of:
                    chain.append(pid_of[cur])
                cur = spc[cur]['evolves_from_species_id']
            out[pack_id(spc[r['species_id']]['identifier'])] = [x for p in chain for x in own[p]]
        return out

    def can_learn(self, learnsets, species, move, level):
        for mv, method, lv in learnsets.get(species, []):
            if mv == move and (method != '1' or lv <= level):
                return True
        return False
