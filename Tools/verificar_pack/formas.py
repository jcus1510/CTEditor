"""FORMAS y VARIANTES de las especies para los packs (desde PokeAPI).

- Formas de COMBATE (columnas `formas` y `cambios_forma` de la especie): cambian en mitad del combate y al acabar
  vuelven a la normal. Castform y Cherrim (clima), Darmanitan (PS), Meloetta y Aegislash (movimientos), Giratina,
  Kyogre/Groudon primigenios y Arceus (objeto equipado).
- VARIANTES (filas propias con `forma_de`): especies completas enlazadas a su base. Deoxys, Wormadam, Rotom, Shaymin,
  Basculin, los Tótem, Kyurem, Keldeo, Pumpkaboo/Gourgeist, Hoopa. `objeto_variante` = el objeto que cambia a ella fuera
  del combate (vacío = con un personaje del mapa).
Las megaevoluciones van aparte (paso de la megaevolución).
"""
import collections
from pokeapi import table, pack_id, SPANISH

WEATHERS = ['sun', 'rain', 'sandstorm', 'hail']

# base: (gen mínima, [(forma de PokeAPI, id de la forma, clima o None)], reglas o 'clima', habilidad, vuelve al retirarse)
BATTLE = {
    'castform': (3, [('castform-sunny', 'sunny', 'sun'), ('castform-rainy', 'rainy', 'rain'), ('castform-snowy', 'snowy', 'hail')],
                 'clima', 'forecast', True),
    'cherrim': (4, [('cherrim-sunshine', 'sunshine', 'sun')], 'clima', 'flower_gift', True),
    'darmanitan': (5, [('darmanitan-zen', 'zen', None)], '>zen:ps_bajo:50;con=zen_mode|zen>:ps_desde:50;con=zen_mode', '', True),
    'meloetta': (5, [('meloetta-pirouette', 'pirouette', None)],
                 '>pirouette:movimiento:relic_song;despues|pirouette>:movimiento:relic_song;despues', '', True),
    'aegislash': (6, [('aegislash-blade', 'blade', None)],
                  '>blade:ataque;con=stance_change|blade>:movimiento:kings_shield;con=stance_change', '', True),
    'giratina': (4, [('giratina-origin', 'origin', None)], '>origin:objeto:griseous_orb', '', False),
    'kyogre': (6, [('kyogre-primal', 'primal', None)], '>primal:objeto:blue_orb', '', False),
    'groudon': (6, [('groudon-primal', 'primal', None)], '>primal:objeto:red_orb', '', False),
}

PLATES = {'fighting': 'fist_plate', 'flying': 'sky_plate', 'poison': 'toxic_plate', 'ground': 'earth_plate', 'rock': 'stone_plate',
          'bug': 'insect_plate', 'ghost': 'spooky_plate', 'steel': 'iron_plate', 'fire': 'flame_plate', 'water': 'splash_plate',
          'grass': 'meadow_plate', 'electric': 'zap_plate', 'psychic': 'mind_plate', 'ice': 'icicle_plate', 'dragon': 'draco_plate',
          'dark': 'dread_plate', 'fairy': 'pixie_plate'}

# variante de PokeAPI: (base, gen mínima, objeto que cambia a ella fuera del combate)
VARIANTS = {
    'deoxys-attack': ('deoxys', 3, ''), 'deoxys-defense': ('deoxys', 3, ''), 'deoxys-speed': ('deoxys', 3, ''),
    'wormadam-sandy': ('wormadam', 4, ''), 'wormadam-trash': ('wormadam', 4, ''),
    'rotom-heat': ('rotom', 4, ''), 'rotom-wash': ('rotom', 4, ''), 'rotom-frost': ('rotom', 4, ''),
    'rotom-fan': ('rotom', 4, ''), 'rotom-mow': ('rotom', 4, ''),
    'shaymin-sky': ('shaymin', 4, 'gracidea'),
    'basculin-blue-striped': ('basculin', 5, ''),
    'tornadus-therian': ('tornadus', 5, 'reveal_glass'), 'thundurus-therian': ('thundurus', 5, 'reveal_glass'),
    'landorus-therian': ('landorus', 5, 'reveal_glass'),
    'kyurem-black': ('kyurem', 5, 'dna_splicers'), 'kyurem-white': ('kyurem', 5, 'dna_splicers'),
    'keldeo-resolute': ('keldeo', 5, ''),
    'pumpkaboo-small': ('pumpkaboo', 6, ''), 'pumpkaboo-large': ('pumpkaboo', 6, ''), 'pumpkaboo-super': ('pumpkaboo', 6, ''),
    'gourgeist-small': ('gourgeist', 6, ''), 'gourgeist-large': ('gourgeist', 6, ''), 'gourgeist-super': ('gourgeist', 6, ''),
    'hoopa-unbound': ('hoopa', 6, 'prison_bottle'),
}

STATS = ('ataque', 'defensa', 'atq_esp', 'def_esp', 'velocidad')


class Forms:
    def __init__(self, g):
        self.g = g
        forms = table('pokemon_forms')
        self.form = {r['identifier']: r for r in forms}
        self.form_name = {r['pokemon_form_id']: r['form_name'] for r in table('pokemon_form_names')
                          if r['local_language_id'] == SPANISH}
        self.form_types = collections.defaultdict(list)
        for r in table('pokemon_form_types'):
            self.form_types[r['pokemon_form_id']].append((int(r['slot']), g.api.type_name[r['type_id']]))
        self.pokemon = {r['id']: r for r in table('pokemon')}

    def name_of(self, identifier):
        f = self.form.get(identifier)
        return self.form_name.get(f['id'], '') if f else ''

    def vals(self, identifier):
        f = self.form[identifier]
        pid = f['pokemon_id']
        v = self.g.api.pokemon_vals({pid: identifier})[pid]
        if self.form_types.get(f['id']):   # Arceus: la forma cambia el tipo sin ser otro Pokémon
            v['tipos'] = '|'.join(t for _, t in sorted(self.form_types[f['id']]))
        return pid, v

    # ---------------- Formas de combate ----------------
    def battle_forms(self, s, n, used_abilities):
        """Rellena `formas` y `cambios_forma` de la especie s (dict de la fila). Devuelve True si tiene formas."""
        sp = s['id']
        entries, rules = [], []
        if sp == 'arceus' and n >= 4:
            for t, plate in PLATES.items():
                if t == 'fairy' and n < 6:
                    continue
                entries.append(self._form_entry(s, f'arceus-{t}', t, False, used_abilities, n))
                rules.append(f'>{t}:objeto:{plate};con=multitype')
        elif sp in BATTLE and n >= BATTLE[sp][0]:
            _, forms, how, ability, reverts = BATTLE[sp]
            for ident, fid, weather in forms:
                entries.append(self._form_entry(s, ident, fid, reverts, used_abilities, n))
            if how == 'clima':
                con = f';con={ability}' if ability else ''
                with_form = {w: fid for _, fid, w in forms}
                for w in WEATHERS:
                    if w in with_form:
                        rules.append(f'*>{with_form[w]}:clima:{w}{con}')
                    else:
                        rules.append(f'*>:clima:{w}{con}')
                rules.append(f'*>:clima{con}')
            else:
                rules.extend(how.split('|'))
        else:
            return False
        s['formas'] = '|'.join(entries)
        s['cambios_forma'] = '|'.join(rules)
        return True

    def _form_entry(self, s, ident, fid, reverts, used_abilities, n):
        pid, v = self.vals(ident)
        types = v['tipos'].replace('|', '/') if v['tipos'] != s['tipos'] else ''
        stats = '/'.join(v[c] for c in STATS) if any(v[c] != s[c] for c in STATS) else ''
        ab = self.g.abilities.get(pid, {}).get('1', '') if pid != self.g.pid.get(s['id']) else ''
        if ab and (ab == s.get('habilidad') or self.g.ability_gen.get(ab, 99) > n):
            ab = ''
        if ab:
            used_abilities.add(ab)
        parts = [fid, self.name_of(ident) or fid, types, stats, ab, 'vuelve' if reverts else '']
        while len(parts) > 2 and not parts[-1]:
            parts.pop()
        return ';'.join(parts)

    # ---------------- Variantes ----------------
    def variants(self, species_by_id, n, move_ids, used_abilities):
        """Filas nuevas de especie para las variantes de la generación n (copian su base y cambian lo suyo)."""
        rows = []
        for ident, (base, gmin, item) in VARIANTS.items():
            if n < gmin or base not in species_by_id or ident not in self.form:
                continue
            b = species_by_id[base]
            pid, v = self.vals(ident)
            r = dict(b)
            r['id'] = pack_id(ident)
            fname = self.name_of(ident)
            base_name = b['nombre']
            r['nombre'] = fname if fname.startswith(base_name) else (f'{base_name} ({fname})' if fname else r['id'])
            for c in ('ps',) + STATS + ('tipos',):
                r[c] = v[c]
            ab = self.g.abilities.get(pid, {})
            if n >= 3:
                r['habilidad'] = ab.get('1', '')
                r['habilidad_2'] = ab.get('2', '')
                r['habilidad_oculta'] = ab.get('3', '') if n >= 5 else ''
                for col in ('habilidad', 'habilidad_2', 'habilidad_oculta'):
                    if r[col] and self.g.ability_gen.get(r[col], 99) > n:
                        r[col] = ''
                    if r[col]:
                        used_abilities.add(r[col])
            lv = self.g.learnset_of_pid(pid)
            if lv:
                r['aprende'] = '|'.join(f'{a}:{m}' for a, m in lv if m in move_ids)
            om = self.g.other_moves.get(pid, {})
            if om:
                for col, meth in (('mt', '4'), ('tutor', '3'), ('huevo', '2')):
                    r[col] = '|'.join(sorted(m for m in om.get(meth, ()) if m in move_ids))
            p = self.pokemon.get(pid)
            if p:
                if p['height']:
                    r['altura'] = _dec(int(p['height']) / 10)
                if p['weight']:
                    r['peso'] = _dec(int(p['weight']) / 10)
            r['evoluciona'] = ''
            r['forma_de'] = base
            r['objeto_variante'] = item
            r['formas'] = r['cambios_forma'] = ''
            rows.append(r)
        return rows


def _dec(x):
    return str(int(x)) if float(x).is_integer() else str(x).replace('.', ',')
