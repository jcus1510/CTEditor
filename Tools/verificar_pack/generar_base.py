#!/usr/bin/env python3
"""GENERAR LAS HOJAS BASE DE UN PACK DESDE POKEAPI.

Uso:
    python3 Tools/verificar_pack/generar_base.py Assets/GameContent/Packs/Gen1-6 --gen 6
    python3 Tools/verificar_pack/generar_base.py Assets/GameContent/Packs/Gen1 --gen 1

Escribe (o reescribe) en la carpeta del pack, con el formato del editor (';', UTF-8 con BOM):
  • tipos.csv         los tipos que existen en esa generación (sin Hada antes de la 6.ª, sin Siniestro/Acero en la 1.ª)
                      + «typeless» (el de Forcejeo). Nombres en español de PokeAPI; colores: los del editor.
  • tabla_tipos.csv   la tabla de tipos DE ESA GENERACIÓN (con los cambios históricos de PokeAPI).
  • naturalezas.csv   las 25 naturalezas (desde la 3.ª gen.; en la 1.ª y 2.ª no hay naturalezas: no se escribe).
  • grupos_huevo.csv  desde la 2.ª gen. (en la 1.ª no hay crianza: no se escribe).
Así cada pack trae su propia base y el Centro de Contenido no la saca de las plantillas del código («el pack manda»).
"""
import argparse, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csvlib import save  # noqa: E402
from pokeapi import PokeApi, SPANISH, table, pack_id  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
EDITOR = os.path.join(ROOT, 'Assets', 'CTEditor', 'Runtime', 'GameDefinition', 'Editor')
CLASSIC_ORDER = ['normal', 'fire', 'water', 'electric', 'grass', 'ice', 'fighting', 'poison', 'ground', 'flying',
                 'psychic', 'bug', 'rock', 'ghost', 'dragon', 'dark', 'steel', 'fairy']


def editor_colors():
    """Colores de los tipos y datos de los grupos huevo que usan las plantillas del editor."""
    src = open(os.path.join(EDITOR, 'Common', 'TypeChartTools.cs'), encoding='utf-8').read()
    colors = dict(re.findall(r'\(\s*"([a-z]+)",\s*"[^"]+",\s*"([0-9A-Fa-f]{6})"\)', src))
    colors.setdefault('typeless', '8C9999')
    return colors


def num(x):
    return str(int(x)) if float(x).is_integer() else str(x).replace('.', ',')


def main():
    ap = argparse.ArgumentParser(description='Genera tipos, tabla de tipos, naturalezas y grupos huevo desde PokeAPI.')
    ap.add_argument('carpeta')
    ap.add_argument('--gen', type=int, required=True)
    args = ap.parse_args()
    os.makedirs(args.carpeta, exist_ok=True)
    api = PokeApi(args.gen)
    colors = editor_colors()

    # Tipos
    alive = api.types_in_gen()
    tid = {r['identifier']: r['id'] for r in table('types')}
    names = {r['type_id']: r['name'] for r in table('type_names') if r['local_language_id'] == SPANISH}
    rows = [{'id': t, 'nombre': names.get(tid[t], t), 'color': colors.get(t, 'A8A77A')} for t in alive]
    rows.append({'id': 'typeless', 'nombre': 'Sin tipo', 'color': colors['typeless']})
    save(os.path.join(args.carpeta, 'tipos.csv'), ['id', 'nombre', 'color'], sorted(rows, key=lambda r: r['id']))

    # Tabla de tipos
    chart = api.type_chart()
    order = [t for t in CLASSIC_ORDER if t in alive] + ['typeless']
    corner = 'ataca\\defiende'
    trows = [{corner: a, **{d: ('' if (a, d) not in chart else num(chart[(a, d)])) for d in order}} for a in order]
    save(os.path.join(args.carpeta, 'tabla_tipos.csv'), [corner] + order, trows)
    written = ['tipos.csv', 'tabla_tipos.csv']

    # Naturalezas (3.ª gen. en adelante)
    if args.gen >= 3:
        stat = {s['id']: s['identifier'].replace('special-', 'sp_') for s in table('stats')}
        nn = {r['nature_id']: r['name'] for r in table('nature_names') if r['local_language_id'] == SPANISH}
        nat = [{'id': n['identifier'], 'nombre': nn[n['id']], 'sube': stat[n['increased_stat_id']],
                'baja': stat[n['decreased_stat_id']], 'porcentaje': '10'} for n in table('natures')]
        save(os.path.join(args.carpeta, 'naturalezas.csv'), ['id', 'nombre', 'sube', 'baja', 'porcentaje'],
             sorted(nat, key=lambda r: r['id']))
        written.append('naturalezas.csv')

    # Grupos huevo (2.ª gen. en adelante): nombre de PokeAPI; color y descripción, los de la plantilla del editor.
    if args.gen >= 2:
        src = open(os.path.join(EDITOR, 'Windows', 'EggGroupEditorWindow.cs'), encoding='utf-8').read()
        tpl = {m[0]: m for m in re.findall(r'\(\s*"([a-z0-9_]+)",\s*"([^"]+)",\s*"([0-9A-Fa-f]{6})"', src)}
        gp = {r['egg_group_id']: r['name'] for r in table('egg_group_prose') if r['local_language_id'] == SPANISH}
        eg = []
        for g in table('egg_groups'):
            i = pack_id(g['identifier'])
            name = gp.get(g['id'], tpl.get(i, (i, i))[1])
            no = i == 'no_eggs'
            eg.append({'id': i, 'nombre': name, 'color': tpl[i][2] if i in tpl else 'A3A3A3', 'no_cria': 'si' if no else 'no',
                       'descripcion': 'No puede criar (legendarios, bebés y especies especiales).' if no
                       else f'Las especies del grupo {name} pueden criar entre sí.'})
        save(os.path.join(args.carpeta, 'grupos_huevo.csv'), ['id', 'nombre', 'color', 'no_cria', 'descripcion'],
             sorted(eg, key=lambda r: r['id']))
        written.append('grupos_huevo.csv')
    print('Escritas en ' + args.carpeta + ': ' + ', '.join(written))


if __name__ == '__main__':
    main()
