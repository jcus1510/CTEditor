"""FORMAS y VARIANTES de las especies para los packs (desde PokeAPI).

- Formas de COMBATE (columnas `formas` y `cambios_forma` de la especie): cambian en mitad del combate y al acabar
  vuelven a la normal. Castform y Cherrim (clima), Darmanitan (PS), Meloetta y Aegislash (movimientos), Giratina,
  Kyogre/Groudon primigenios y Arceus (objeto equipado).
- VARIANTES (filas propias con `forma_de`): especies completas enlazadas a su base. Deoxys, Wormadam, Rotom, Shaymin,
  Basculin, los Tótem, Kyurem, Keldeo, Pumpkaboo/Gourgeist, Hoopa. `objeto_variante` = el objeto que cambia a ella fuera
  del combate (vacío = con un personaje del mapa).
- MEGAEVOLUCIONES (6.ª gen.): formas «mega» / «mega_x» / «mega_y» con la regla `>mega:mega:<megapiedra>` (Rayquaza:
  `;sabe=dragon_ascent`). La megapiedra de cada una se saca del texto de PokeAPI («Have Venusaur hold it...»).
"""
import collections, re
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
    # 7.ª generación
    'wishiwashi': (7, [('wishiwashi-school', 'school', None)], '>school:ps_desde:25;con=schooling|school>:ps_bajo:25;con=schooling', '', True),
    'minior': (7, [('minior-red', 'core', None)], '>core:ps_bajo:50;con=shields_down|core>:ps_desde:50;con=shields_down', '', True),
    'zygarde': (7, [('zygarde-complete', 'complete', None)], '>complete:ps_bajo:50;con=power_construct', '', True),
}

# Silvally (7.ª): un disco en cada tipo, como las tablas de Arceus.
MEMORIES = {t: f'{t}_memory' for t in ('fighting', 'flying', 'poison', 'ground', 'rock', 'bug', 'ghost', 'steel', 'fire', 'water',
                                     'grass', 'electric', 'psychic', 'ice', 'dragon', 'dark', 'fairy')}

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
    # 7.ª generación: formas de Alola (especies completas enlazadas a la de Kanto) y otras variantes.
    'rattata-alola': ('rattata', 7, ''), 'raticate-alola': ('raticate', 7, ''), 'raichu-alola': ('raichu', 7, ''),
    'sandshrew-alola': ('sandshrew', 7, ''), 'sandslash-alola': ('sandslash', 7, ''), 'vulpix-alola': ('vulpix', 7, ''),
    'ninetales-alola': ('ninetales', 7, ''), 'diglett-alola': ('diglett', 7, ''), 'dugtrio-alola': ('dugtrio', 7, ''),
    'meowth-alola': ('meowth', 7, ''), 'persian-alola': ('persian', 7, ''), 'geodude-alola': ('geodude', 7, ''),
    'graveler-alola': ('graveler', 7, ''), 'golem-alola': ('golem', 7, ''), 'grimer-alola': ('grimer', 7, ''),
    'muk-alola': ('muk', 7, ''), 'exeggutor-alola': ('exeggutor', 7, ''), 'marowak-alola': ('marowak', 7, ''),
    'lycanroc-midnight': ('lycanroc', 7, ''), 'lycanroc-dusk': ('lycanroc', 7, ''),
    'oricorio-pom-pom': ('oricorio', 7, 'yellow_nectar'), 'oricorio-pau': ('oricorio', 7, 'pink_nectar'),
    'oricorio-sensu': ('oricorio', 7, 'purple_nectar'),
    'necrozma-dusk': ('necrozma', 7, ''), 'necrozma-dawn': ('necrozma', 7, ''),   # con el N-Solarizador / N-Lunarizador (objetos clave)
    'zygarde-10': ('zygarde', 7, ''),
}

# Evoluciones de las variantes (las de Alola evolucionan a su forma de Alola). En Alola, las especies de Kanto que tienen
# forma regional evolucionan a ella: se anota con «lugar:alola».
VARIANT_EVOS = {
    'rattata_alola': 'raticate_alola@20+hora:noche', 'sandshrew_alola': 'sandslash_alola@objeto:ice_stone',
    'vulpix_alola': 'ninetales_alola@objeto:ice_stone', 'diglett_alola': 'dugtrio_alola@26', 'meowth_alola': 'persian_alola@amistad',
    'geodude_alola': 'graveler_alola@25', 'graveler_alola': 'golem_alola@intercambio', 'grimer_alola': 'muk_alola@38',
}
BASE_TO_VARIANT_EVOS = {
    'pichu': None, 'pikachu': 'raichu_alola@objeto:thunder_stone+lugar:alola',
    'exeggcute': 'exeggutor_alola@objeto:leaf_stone+lugar:alola', 'cubone': 'marowak_alola@28+hora:noche+lugar:alola',
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
        self._megas = None

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
        if sp == 'silvally' and n >= 7:
            for t, memory in MEMORIES.items():
                entries.append(self._form_entry(s, f'silvally-{t}', t, False, used_abilities, n))
                rules.append(f'>{t}:objeto:{memory};con=rks_system')
        elif sp == 'arceus' and n >= 4:
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
        for ident, fid, stone, move in (self.megas().get(sp, []) if n >= 6 else []):
            entries.append(self._form_entry(s, ident, fid, False, used_abilities, n))
            rules.append(f'>{fid}:mega:{stone}' if stone else f'>{fid}:mega;sabe={move}')
        if not entries:
            return False
        s['formas'] = '|'.join(entries)
        s['cambios_forma'] = '|'.join(rules)
        return True

    def megas(self):
        """{especie: [(forma de PokeAPI, id de la forma, megapiedra, movimiento)]} de las megas de la 6.ª gen."""
        if self._megas is not None:
            return self._megas
        vg_gen = {r['id']: int(r['generation_id']) for r in table('version_groups')}
        cats = {r['id']: r['identifier'] for r in table('item_categories')}
        stones = {r['id']: r['identifier'] for r in table('items') if cats.get(r['category_id']) == 'mega-stones'}
        text = {}
        for r in table('item_flavor_text'):
            if r['language_id'] == '9' and r['item_id'] in stones:
                text[stones[r['item_id']]] = r['flavor_text'].replace('\n', ' ')
        en = {r['pokemon_species_id']: r['name'] for r in table('pokemon_species_names') if r['local_language_id'] == '9'}
        spc = {r['id']: r['identifier'] for r in table('pokemon_species')}
        out = collections.defaultdict(list)
        for f in table('pokemon_forms'):
            if f['is_mega'] != '1' or vg_gen.get(f['introduced_in_version_group_id'], 99) > 6:
                continue
            sid = self.pokemon[f['pokemon_id']]['species_id']
            suffix = f['identifier'].split('-mega', 1)[1]          # '', '-x', '-y'
            fid = 'mega' + suffix.replace('-', '_')
            found = [st for st, t in text.items() if re.search(r'\b' + re.escape(en[sid]) + r'\b', t)
                     and (st.endswith(suffix) if suffix else not re.search(r'-[xy]$', st))
                     and self._item_gen(st) <= 6]
            stone = pack_id(found[0]) if len(found) == 1 else ''
            move = 'dragon_ascent' if spc[sid] == 'rayquaza' else ''
            if stone or move:
                out[pack_id(spc[sid])].append((f['identifier'], fid, stone, move))
        self._megas = out
        return out

    def _item_gen(self, identifier):
        """Primera generación en que existe el objeto (índices de juego de PokeAPI): para no coger las megapiedras de Z-A."""
        if not hasattr(self, '_item_first'):
            ids = {r['id']: r['identifier'] for r in table('items')}
            first = {}
            for r in table('item_game_indices'):
                ident = ids.get(r['item_id'])
                if ident:
                    first[ident] = min(first.get(ident, 99), int(r['generation_id']))
            self._item_first = first
        return self._item_first.get(identifier, 99)

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
            r['evoluciona'] = VARIANT_EVOS.get(r['id'], '') if n >= 7 else ''
            r['forma_de'] = base
            r['objeto_variante'] = item
            r['formas'] = r['cambios_forma'] = ''
            rows.append(r)
        return rows


def _dec(x):
    return str(int(x)) if float(x).is_integer() else str(x).replace('.', ',')
