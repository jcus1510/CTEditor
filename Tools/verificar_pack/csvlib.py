"""Lectura y escritura de los CSV del editor, IDÉNTICA a CsvTable.cs (para verificar fuera de Unity).

- Separador ';' (o ',' / tabulador, detectado en la cabecera; respeta la línea "sep=").
- Comillas: una celda "a;b" o con saltos de línea se lee entera; "" = una comilla.
- UTF-8 con BOM. Las filas vacías se saltan. Cada fila recuerda su número de línea (como en Excel).
"""
import os


def parse(text):
    """Devuelve (cabeceras, filas) donde cada fila es (dict celda por cabecera, número de línea)."""
    if text and text[0] == '﻿':
        text = text[1:]
    sep, start = ';', 0
    if text[:4].lower() == 'sep=' and len(text) > 4:
        sep, start = text[4], text.find('\n') + 1
    else:
        nl = text.find('\n')
        first = text if nl < 0 else text[:nl]
        sep = ';' if ';' in first else (',' if ',' in first else ('\t' if '\t' in first else ';'))
    records, cells, cell, quoted = [], [], [], False
    line = 2 if start > 0 else 1
    record_line = line
    i = start
    while i < len(text):
        ch = text[i]
        if quoted:
            if ch == '"':
                if i + 1 < len(text) and text[i + 1] == '"':
                    cell.append('"'); i += 1
                else:
                    quoted = False
            else:
                if ch == '\n':
                    line += 1
                cell.append(ch)
        elif ch == '"':
            quoted = True
        elif ch == sep:
            cells.append(''.join(cell)); cell = []
        elif ch == '\r':
            pass
        elif ch == '\n':
            cells.append(''.join(cell)); cell = []
            records.append((cells, record_line)); cells = []
            line += 1; record_line = line
        else:
            cell.append(ch)
        i += 1
    if cell or cells:
        cells.append(''.join(cell)); records.append((cells, record_line))
    if not records:
        return [], []
    headers = [h.strip() for h in records[0][0]]
    rows = []
    for cells, ln in records[1:]:
        if all(not c.strip() for c in cells):
            continue
        rows.append(({h: (cells[k].strip() if k < len(cells) else '') for k, h in enumerate(headers)}, ln, len(cells)))
    return headers, rows


def load(path):
    """(cabeceras, [dict], [línea], [nº de celdas]) de un archivo."""
    with open(path, encoding='utf-8') as f:
        headers, rows = parse(f.read())
    return headers, [r for r, _, _ in rows], [ln for _, ln, _ in rows], [n for _, _, n in rows]


def split_list(cell):
    """'a | b | c' -> ['a', 'b', 'c'] (como CsvCodecs.SplitList)."""
    return [x.strip() for x in (cell or '').split('|') if x.strip()]


def quote(v, sep=';'):
    v = v or ''
    needs = sep in v or '"' in v or '\n' in v or '\r' in v or v.startswith(' ') or v.endswith(' ')
    return '"' + v.replace('"', '""') + '"' if needs else v


def save(path, headers, rows, newline=None):
    """Escribe como CsvTable.Save: ';', UTF-8 con BOM. Conserva el fin de línea del archivo si ya existía."""
    if newline is None:
        newline = '\n'
        if os.path.exists(path):
            with open(path, 'rb') as f:
                if b'\r\n' in f.read(4096):
                    newline = '\r\n'
    lines = [';'.join(quote(h) for h in headers)] + [';'.join(quote(r.get(h, '')) for h in headers) for r in rows]
    with open(path, 'wb') as f:
        f.write(('﻿' + newline.join(lines) + newline).encode('utf-8'))
