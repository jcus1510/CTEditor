using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Species;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// EDITOR VISUAL DE CADENAS EVOLUTIVAS (menú CTEditor → Criaturas → Cadenas evolutivas).
    ///
    /// Izquierda: las cadenas del juego (cada una empieza en una especie que nadie produce al evolucionar).
    /// Derecha: el árbol dibujado — tarjetas con los colores de sus tipos, flechas con el NIVEL editable
    /// directamente, botones para añadir una evolución (también ramas, como Eevee) o quitarla, y «✎» para
    /// abrir el panel de la evolución: método, nivel, objeto, amistad y CONDICIONES EXTRA combinables
    /// (de día, llevando un objeto, sabiendo un movimiento, Ataque &gt; Defensa...) con plantillas clásicas.
    ///
    /// Los datos siguen viviendo donde siempre (la lista 'evolutions' de cada especie): este editor es otra
    /// forma de verlos y cambiarlos, con avisos si algo no tiene sentido (ciclos, niveles que retroceden).
    /// </summary>
    public sealed class EvolutionChainWindow : EditorWindow
    {
        private const float NodeW = 170f, NodeH = 66f, ColW = 250f, RowH = 92f, Pad = 20f;

        private List<SpeciesData> _species = new List<SpeciesData>();
        private SpeciesData _root;
        private string _search = "";
        private bool _showSingles;
        private Vector2 _listScroll, _canvasScroll;

        // Panel "añadir evolución".
        private SpeciesData _addFrom;
        private int _addTarget;
        private int _addLevel = 16;

        // Panel "editar evolución" (la flecha elegida con ✎).
        private SpeciesData _editFrom;
        private int _editIndex = -1;

        [MenuItem(EditorMenus.Creatures + "Cadenas evolutivas", false, EditorMenus.CreaturesOrder + 2)]
        public static void Open() => OpenFor(null);

        /// <summary>Abre el editor mostrando la cadena a la que pertenece una especie.</summary>
        public static void OpenFor(SpeciesData species)
        {
            var w = GetWindow<EvolutionChainWindow>();
            w.titleContent = new GUIContent("Cadenas evolutivas");
            w.minSize = new Vector2(1100, 560);
            w.Reload();
            if (species != null) w._root = w.RootOf(species);
            w.Show();
        }

        private void OnEnable() => Reload();

        private void Reload()
        {
            _species = ContentAssets.LoadAll<SpeciesData>();
            _species.Sort((a, b) => string.Compare(ContentAssets.Label(a), ContentAssets.Label(b), StringComparison.OrdinalIgnoreCase));
        }

        // ---------------- Grafo ----------------

        private IEnumerable<(SpeciesData target, int level, int index)> Children(SpeciesData s)
        {
            if (s == null || s.Evolutions == null) yield break;
            for (int i = 0; i < s.Evolutions.Length; i++)
                if (s.Evolutions[i].target != null)
                    yield return (s.Evolutions[i].target, s.Evolutions[i].requiredLevel, i);
        }

        private SpeciesData ParentOf(SpeciesData s)
            => _species.FirstOrDefault(p => Children(p).Any(c => c.target == s));

        private SpeciesData RootOf(SpeciesData s)
        {
            var seen = new HashSet<SpeciesData>();
            while (s != null && seen.Add(s))
            {
                var parent = ParentOf(s);
                if (parent == null) return s;
                s = parent;
            }
            return s; // ciclo: devolvemos donde se detectó
        }

        private List<SpeciesData> Roots()
        {
            var targets = new HashSet<SpeciesData>();
            foreach (var s in _species) foreach (var c in Children(s)) targets.Add(c.target);
            return _species.Where(s => !targets.Contains(s) && (_showSingles || Children(s).Any())).ToList();
        }

        // ---------------- UI ----------------

        // Zoom de texto (Ctrl + rueda, o los botones A− / A+ de los editores).
        private void OnGUI()
        {
            EditorZoom.Begin(this);
            try { DrawWindow(); }
            finally { EditorZoom.End(); }
        }

        private void DrawWindow()
        {
            EditorTheme.TitleBand("Cadenas evolutivas",
                "Elige una cadena a la izquierda. En el árbol: edita cómo evoluciona sobre cada flecha, «+ Evolución» añade una etapa (o una rama) y «✕» la quita.",
                EditorTheme.Species, "🌱");
            EditorTheme.Guide("EvolutionChain", new[]
            {
                "Elige una cadena en la lista (o «Mostrar especies sin evolución» para empezar una nueva).",
                "Sobre cada flecha ves cómo evoluciona; pulsa «✎» para editarla: método, nivel, objeto, amistad y CONDICIONES EXTRA.",
                "Las condiciones se combinan (todas a la vez): amistad + de día, nivel + Ataque > Defensa, objeto equipado + de noche... Usa las plantillas.",
                "«+ Evolución» añade la siguiente etapa; si ya tiene una, crea una RAMA (como Eevee).",
            }, EditorTheme.Species);
            EditorGUILayout.BeginHorizontal();
            DrawChainList();
            DrawCanvas();
            DrawSidePanel();
            EditorGUILayout.EndHorizontal();
        }

        // Panel de la DERECHA, alto y con su propio desplazamiento: ahí se edita una evolución (método y
        // condiciones) o se añade otra, con todo el sitio que haga falta.
        private Vector2 _sideScroll;
        private void DrawSidePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(380));
            _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll);
            if (_addFrom == null && _editFrom == null)
            {
                EditorTheme.Section("Editar", EditorTheme.Species);
                EditorTheme.Paragraph("Pulsa «✎» sobre una flecha para editar cómo evoluciona (método, nivel, objeto y CONDICIONES), " +
                                      "o «+ Evolución» en una tarjeta para añadir una etapa.\n\nArrastra las tarjetas por su nombre para colocarlas a tu gusto.");
            }
            DrawAddPanel();
            DrawEvolutionDetail();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ---------------- Tarjetas que se pueden mover ----------------
        // Cada especie guarda cuánto la moviste respecto a su sitio automático (por proyecto, en EditorPrefs).
        private SpeciesData _dragging;
        private readonly Dictionary<string, Vector2> _offsets = new Dictionary<string, Vector2>();

        private string OffsetKey(SpeciesData s) => "CTEditor.EvoPos." + StableHash(Application.dataPath) + "." + s.Id;

        // Hash que no cambia entre sesiones (string.GetHashCode puede cambiar en cada ejecución).
        private static uint StableHash(string text)
        {
            uint h = 2166136261;
            foreach (char c in text ?? "") { h ^= c; h *= 16777619; }
            return h;
        }

        private Vector2 OffsetOf(SpeciesData s)
        {
            if (s == null || string.IsNullOrEmpty(s.Id)) return Vector2.zero;
            if (_offsets.TryGetValue(s.Id, out var v)) return v;
            var parts = EditorPrefs.GetString(OffsetKey(s), "").Split(';');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var num = System.Globalization.NumberStyles.Float;
            v = parts.Length == 2 && float.TryParse(parts[0], num, inv, out var x) && float.TryParse(parts[1], num, inv, out var y) ? new Vector2(x, y) : Vector2.zero;
            _offsets[s.Id] = v;
            return v;
        }

        private void SaveOffset(SpeciesData s)
        {
            if (_offsets.TryGetValue(s.Id, out var v)) EditorPrefs.SetString(OffsetKey(s),
                v.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + v.y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private void ResetOffsets(IEnumerable<SpeciesData> species)
        {
            foreach (var s in species) { _offsets.Remove(s.Id); EditorPrefs.DeleteKey(OffsetKey(s)); }
        }

        private void DrawChainList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(230));
            _search = EditorGUILayout.TextField("Buscar", _search);
            _showSingles = EditorGUILayout.ToggleLeft("Mostrar especies sin evolución", _showSingles);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            foreach (var root in Roots())
            {
                string chain = ChainLabel(root);
                if (!string.IsNullOrEmpty(_search) && chain.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var row = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                if (root == _root) EditorGUI.DrawRect(row, EditorTheme.WithAlpha(EditorTheme.Species, 0.35f));
                if (root.Types != null && root.Types.Length > 0 && root.Types[0] != null)
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 3, 4, row.height - 6), root.Types[0].Color);
                if (GUI.Button(new Rect(row.x + 8, row.y, row.width - 8, row.height), new GUIContent(chain, chain),
                        root == _root ? EditorStyles.boldLabel : EditorStyles.label)) { _root = root; _addFrom = null; }
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Refrescar")) Reload();
            EditorGUILayout.EndVertical();
        }

        // "Bulbasaur → Ivysaur → Venusaur" (con ramas: "Eevee → Vaporeon / Jolteon / Flareon").
        private string ChainLabel(SpeciesData root)
        {
            var parts = new List<string> { root.DisplayName };
            var level = Children(root).Select(c => c.target).ToList();
            var seen = new HashSet<SpeciesData> { root };
            while (level.Count > 0)
            {
                parts.Add(string.Join(" / ", level.Select(s => s.DisplayName)));
                level = level.Where(seen.Add).SelectMany(s => Children(s).Select(c => c.target)).ToList();
            }
            return string.Join(" → ", parts);
        }

        private void DrawCanvas()
        {
            EditorGUILayout.BeginVertical();
            if (_root == null)
            {
                EditorGUILayout.HelpBox("Elige una cadena, o empieza una nueva:", MessageType.Info);
                var names = _species.ConvertAll(s => ContentAssets.Label(s)).ToArray();
                if (names.Length > 0)
                {
                    _addTarget = Mathf.Clamp(EditorGUILayout.Popup("Primera etapa", _addTarget, names), 0, names.Length - 1);
                    if (GUILayout.Button("Abrir su cadena")) _root = RootOf(_species[_addTarget]);
                }
                EditorGUILayout.EndVertical();
                return;
            }

            // Disposición: columna = etapa; fila = orden de las hojas (así las ramas no se pisan).
            var pos = new Dictionary<SpeciesData, Rect>();
            var warnings = new List<string>();
            int nextRow = 0;
            int maxDepth = 0;
            Layout(_root, 0, new HashSet<SpeciesData>(), pos, warnings, ref nextRow, ref maxDepth);

            // Cada tarjeta: su sitio automático + lo que la hayas movido.
            foreach (var key in pos.Keys.ToList())
            {
                var o = OffsetOf(key);
                pos[key] = new Rect(pos[key].x + o.x, pos[key].y + o.y, pos[key].width, pos[key].height);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ordenar tarjetas", GUILayout.Width(130))) { ResetOffsets(pos.Keys); Repaint(); }
            GUILayout.Label("Arrastra una tarjeta por su nombre para moverla.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            var canvas = GUILayoutUtility.GetRect(200f, 4000f, 260f, 4000f);
            float maxX = pos.Values.Select(v => v.xMax).DefaultIfEmpty(0).Max(), maxY = pos.Values.Select(v => v.yMax).DefaultIfEmpty(0).Max();
            var content = new Rect(0, 0, Math.Max(Pad * 2 + (maxDepth + 1) * ColW, maxX + Pad), Math.Max(Pad * 2 + Math.Max(1, nextRow) * RowH, maxY + Pad));
            _canvasScroll = GUI.BeginScrollView(canvas, _canvasScroll, content);
            HandleDrag(pos);

            // Flechas (con nivel editable y botón de quitar).
            // Copias (ToList): quitar una evolución modifica la lista mientras se dibuja.
            foreach (var kv in pos.ToList())
                foreach (var c in Children(kv.Key).ToList())
                {
                    if (!pos.TryGetValue(c.target, out var to)) continue;
                    DrawEdge(kv.Key, kv.Value, to, c.level, c.index, warnings);
                }

            // Tarjetas.
            foreach (var kv in pos) DrawNode(kv.Key, kv.Value);

            GUI.EndScrollView();

            foreach (var w in warnings.Distinct().Take(4)) EditorGUILayout.HelpBox(w, MessageType.Warning);
            EditorGUILayout.EndVertical();
        }

        // Arrastrar por la franja del nombre (los botones de abajo siguen funcionando).
        private void HandleDrag(Dictionary<SpeciesData, Rect> pos)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0) break;
                    foreach (var kv in pos)
                        if (new Rect(kv.Value.x, kv.Value.y, kv.Value.width, 40).Contains(e.mousePosition))
                        {
                            _dragging = kv.Key;
                            GUIUtility.hotControl = id;   // así el soltar llega aunque sueltes fuera de la ventana
                            e.Use();
                            return;
                        }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || _dragging == null) break;
                    var r = pos[_dragging];
                    var o = OffsetOf(_dragging) + e.delta;
                    // Que la tarjeta no se salga por arriba/izquierda (ahí no se puede desplazar para recuperarla).
                    float autoX = r.x - OffsetOf(_dragging).x, autoY = r.y - OffsetOf(_dragging).y;
                    o.x = Math.Max(o.x, 4 - autoX);
                    o.y = Math.Max(o.y, 4 - autoY);
                    _offsets[_dragging.Id] = o;
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    if (_dragging != null) SaveOffset(_dragging);
                    _dragging = null;
                    e.Use();
                    break;
            }
        }

        private void Layout(SpeciesData s, int depth, HashSet<SpeciesData> path, Dictionary<SpeciesData, Rect> pos,
            List<string> warnings, ref int nextRow, ref int maxDepth)
        {
            if (!path.Add(s))
            {
                warnings.Add($"Ciclo evolutivo: '{s.DisplayName}' vuelve a aparecer en su propia cadena. Quita alguna evolución.");
                return;
            }
            if (pos.ContainsKey(s)) { path.Remove(s); return; } // alcanzado por dos caminos: se dibuja una vez

            maxDepth = Math.Max(maxDepth, depth);
            var kids = Children(s).ToList();
            int firstRow = nextRow;
            foreach (var c in kids) Layout(c.target, depth + 1, path, pos, warnings, ref nextRow, ref maxDepth);
            if (kids.Count == 0 || nextRow == firstRow) nextRow++;

            // Centrado verticalmente respecto a sus hijos.
            float rowCenter = (firstRow + Math.Max(firstRow, nextRow - 1)) / 2f;
            pos[s] = new Rect(Pad + depth * ColW, Pad + rowCenter * RowH, NodeW, NodeH);
            path.Remove(s);
        }

        private void DrawNode(SpeciesData s, Rect r)
        {
            GUI.Box(r, GUIContent.none);

            // Franja con los colores de sus tipos.
            var types = s.Types?.Where(t => t != null).ToList() ?? new List<ElementTypeData>();
            for (int i = 0; i < types.Count; i++)
                EditorGUI.DrawRect(new Rect(r.x + i * r.width / types.Count, r.y, r.width / types.Count, 5), types[i].Color);

            int bst = s.Hp + s.Attack + s.Defense + s.SpAttack + s.SpDefense + s.Speed;
            GUI.Label(new Rect(r.x + 6, r.y + 7, r.width - 12, 18), s.DisplayName, EditorStyles.boldLabel);
            GUI.Label(new Rect(r.x + 6, r.y + 24, r.width - 12, 16),
                (types.Count > 0 ? string.Join("/", types.Select(t => t.DisplayName)) : "sin tipo") + $" · BST {bst}", EditorStyles.miniLabel);

            if (GUI.Button(new Rect(r.x + 6, r.y + 43, 70, 18), "Abrir")) SpeciesEditorWindow.OpenAndSelect(s);
            if (GUI.Button(new Rect(r.x + 80, r.y + 43, r.width - 86, 18), "+ Evolución")) { _addFrom = s; _addLevel = 16; }
        }

        private void DrawEdge(SpeciesData from, Rect a, Rect b, int level, int index, List<string> warnings)
        {
            var p1 = new Vector3(a.xMax, a.y + a.height / 2, 0);
            var p2 = new Vector3(b.x, b.y + b.height / 2, 0);
            var mid = new Vector3((p1.x + p2.x) / 2, (p1.y + p2.y) / 2, 0);

            if (Event.current.type == EventType.Repaint)
            {
                Handles.color = EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.7f) : new Color(0, 0, 0, 0.7f);
                Handles.DrawAAPolyLine(3f, p1, new Vector3(mid.x, p1.y, 0), new Vector3(mid.x, p2.y, 0), p2);
                Handles.DrawAAPolyLine(3f, new Vector3(p2.x - 8, p2.y - 5, 0), p2, new Vector3(p2.x - 8, p2.y + 5, 0));
            }

            // Sobre la flecha: el nivel (editable) si es por nivel; debajo, cómo evoluciona en corto; ✎ abre el detalle.
            var entry = from.Evolutions[index];
            bool selected = _editFrom == from && _editIndex == index;
            if (entry.method == EvolutionMethod.Level)
            {
                GUI.Label(new Rect(mid.x - 44, p2.y - 20, 22, 16), "Nv", EditorStyles.miniLabel);
                int newLevel = EditorGUI.IntField(new Rect(mid.x - 24, p2.y - 20, 34, 16), level);
                if (newLevel != level)
                    ContentAssets.Edit(from, so => so.FindProperty("evolutions").GetArrayElementAtIndex(index)
                        .FindPropertyRelative("requiredLevel").intValue = Mathf.Clamp(newLevel, 1, 100));
            }
            string shortText = EvolutionText.Short(entry);
            var labelRect = new Rect(mid.x - 70, p2.y + 2, 150, 16);
            if (selected) EditorGUI.DrawRect(labelRect, EditorTheme.WithAlpha(EditorTheme.Species, 0.35f));
            GUI.Label(labelRect, new GUIContent(shortText, "Evoluciona " + EvolutionText.Describe(entry)), EditorStyles.miniLabel);
            if (GUI.Button(new Rect(mid.x - 62, p2.y - 20, 16, 16), new GUIContent("✎", "Editar cómo evoluciona (método y condiciones)")))
            { _editFrom = from; _editIndex = index; _addFrom = null; }
            if (entry.method == EvolutionMethod.Item && string.IsNullOrWhiteSpace(entry.itemId))
                warnings.Add($"La evolución de {from.DisplayName} es con un objeto, pero no tiene objeto elegido.");
            foreach (var c in entry.conditions ?? new SpeciesData.EvolutionConditionData[0])
            {
                var problem = c != null ? EvolutionText.Problem(c) : null;
                if (problem != null) warnings.Add($"Evolución de {from.DisplayName}: una condición «{Etiquetas.Enum(c.check.ToString())}» — {problem}.");
            }
            if (entry.method == EvolutionMethod.LevelUp && (entry.conditions == null || entry.conditions.Length == 0))
                warnings.Add($"La evolución de {from.DisplayName} es «al subir de nivel» sin condiciones: evolucionará al subir cualquier nivel. Añade alguna condición (✎).");
            if (GUI.Button(new Rect(mid.x + 12, p2.y - 20, 18, 16), "✕") &&
                EditorUtility.DisplayDialog("Quitar evolución", $"¿Quitar la evolución de {from.DisplayName}?", "Quitar", "Cancelar"))
            {
                ContentAssets.Edit(from, so => so.FindProperty("evolutions").DeleteArrayElementAtIndex(index));
                // Si el panel de edición mira a esta especie, que siga en la MISMA evolución (o se cierre si era esta).
                if (_editFrom == from)
                {
                    if (index == _editIndex) _editFrom = null;
                    else if (index < _editIndex) _editIndex--;
                }
                GUIUtility.ExitGUI();   // la lista cambió en mitad del dibujo: se vuelve a pintar desde cero
            }

            // Coherencia de niveles: una etapa no debería evolucionar antes que la anterior.
            var parent = ParentOf(from);
            if (parent != null)
                foreach (var c in Children(parent))
                    if (c.target == from && level < c.level && entry.method == CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Level)
                        warnings.Add($"{from.DisplayName} evoluciona al Nv.{level}, pero ella misma se obtiene al Nv.{c.level}: la evolución ocurriría enseguida.");
            if (level < 1 && entry.method == CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Level)
                warnings.Add($"La evolución de {from.DisplayName} tiene nivel {level}: debe ser al menos 1.");
        }

        private static string ItemName(string id)
        {
            var item = string.IsNullOrWhiteSpace(id) ? null : ContentAssets.FindById<ItemData>(id);
            return item != null ? item.DisplayName : (string.IsNullOrWhiteSpace(id) ? "¿objeto?" : id);
        }

        private void DrawAddPanel()
        {
            if (_addFrom == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Añadir evolución a {_addFrom.DisplayName}", EditorStyles.boldLabel);
            var candidates = _species.Where(s => s != _addFrom).ToList();
            var names = candidates.ConvertAll(s => ContentAssets.Label(s)).ToArray();
            if (names.Length == 0) { EditorGUILayout.HelpBox("Crea otra especie primero.", MessageType.Info); return; }

            _addTarget = Mathf.Clamp(EditorGUILayout.Popup("Evoluciona en", _addTarget, names), 0, names.Length - 1);
            _addLevel = EditorGUILayout.IntField("Nivel", _addLevel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Añadir", GUILayout.Width(70)))
            {
                var target = candidates[_addTarget];
                var from = _addFrom;
                int lvl = Mathf.Clamp(_addLevel, 1, 100);
                ContentAssets.Edit(from, so =>
                {
                    var arr = so.FindProperty("evolutions");
                    arr.arraySize++;
                    var e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                    e.FindPropertyRelative("target").objectReferenceValue = target;
                    e.FindPropertyRelative("requiredLevel").intValue = lvl;
                    e.FindPropertyRelative("method").intValue = (int)EvolutionMethod.Level;
                    e.FindPropertyRelative("itemId").stringValue = "";
                    e.FindPropertyRelative("minFriendship").intValue = 0;
                    e.FindPropertyRelative("conditions").arraySize = 0;
                });
                // Si la especie añadida encabezaba su propia cadena, ahora forma parte de esta.
                _editFrom = from; _editIndex = from.Evolutions.Length - 1;
                _addFrom = null;
            }
            if (GUILayout.Button("Cancelar", GUILayout.Width(70))) _addFrom = null;
            EditorGUILayout.EndHorizontal();
        }
    
        // ---------------- Panel de una evolución ----------------

        private static readonly EvolutionMethod[] Methods =
            { EvolutionMethod.Level, EvolutionMethod.LevelUp, EvolutionMethod.Friendship, EvolutionMethod.Item, EvolutionMethod.Trade };

        private void DrawEvolutionDetail()
        {
            if (_editFrom == null) return;
            var so = new SerializedObject(_editFrom);
            so.Update();
            var arr = so.FindProperty("evolutions");
            if (_editIndex < 0 || _editIndex >= arr.arraySize) { _editFrom = null; return; }
            var el = arr.GetArrayElementAtIndex(_editIndex);
            var target = el.FindPropertyRelative("target").objectReferenceValue as SpeciesData;

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorTheme.Section($"✎ {_editFrom.DisplayName} → {(target != null ? target.DisplayName : "?")}", EditorTheme.Species);
            if (GUILayout.Button("Cerrar", GUILayout.Width(70))) { _editFrom = null; EditorGUILayout.EndHorizontal(); return; }
            EditorGUILayout.EndHorizontal();
            EditorTheme.Tip("Evoluciona " + EvolutionText.Describe(_editFrom.Evolutions[_editIndex]) + ".", EditorTheme.Species, "🌱");

            // (el panel lateral ya tiene su propia barra: aquí no hace falta otra)

            // --- Plantillas ---
            if (GUILayout.Button(new GUIContent("Plantillas clásicas ▾", "Rellena el método y las condiciones con un caso de los juegos (mantiene la especie destino)."), GUILayout.Width(180)))
            {
                var menu = new GenericMenu();
                foreach (var t in EvolutionText.Templates)
                {
                    var tt = t;
                    menu.AddItem(new GUIContent($"{t.Name}   ({t.Example})"), false, () =>
                        ContentAssets.Edit(_editFrom, s2 => EvolutionText.Apply(s2.FindProperty("evolutions").GetArrayElementAtIndex(_editIndex), tt)));
                }
                menu.ShowAsContext();
            }

            // --- Cómo se dispara ---
            var method = el.FindPropertyRelative("method");
            int mIdx = Math.Max(0, Array.IndexOf(Methods, (EvolutionMethod)method.intValue));
            int newIdx = EditorGUILayout.Popup(Etiquetas.Field("method"), mIdx, Methods.Select(m => Etiquetas.Enum(m.ToString())).ToArray());
            method.intValue = (int)Methods[newIdx];
            var m2 = Methods[newIdx];

            var lvl = el.FindPropertyRelative("requiredLevel");
            lvl.intValue = Mathf.Clamp(EditorGUILayout.IntField(m2 == EvolutionMethod.Level ? "Nivel" : "Nivel mínimo (0 = cualquiera)", lvl.intValue), m2 == EvolutionMethod.Level ? 1 : 0, 100);
            if (m2 == EvolutionMethod.Friendship)
            {
                var fr = el.FindPropertyRelative("minFriendship");
                fr.intValue = EditorGUILayout.IntSlider("Amistad mínima", fr.intValue <= 0 ? 220 : fr.intValue, 1, 255);
            }
            if (m2 == EvolutionMethod.Item || m2 == EvolutionMethod.Trade)
                ItemPopup(el.FindPropertyRelative("itemId"), m2 == EvolutionMethod.Item ? "Objeto que se usa" : "Llevando (opcional)", m2 == EvolutionMethod.Trade);

            // --- Condiciones extra ---
            var conds = el.FindPropertyRelative("conditions");
            EditorGUILayout.LabelField($"Condiciones extra ({conds.arraySize}) — todas deben cumplirse a la vez", EditorStyles.boldLabel);
            int remove = -1;
            for (int i = 0; i < conds.arraySize; i++)
            {
                var c = conds.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var check = c.FindPropertyRelative("check");
                var kinds = (EvolutionConditionKind[])Enum.GetValues(typeof(EvolutionConditionKind));
                int k = EditorGUILayout.Popup(check.intValue, kinds.Select(x => Etiquetas.Enum(x.ToString())).ToArray());
                if (k != check.intValue) { EvolutionText.Reset(c); check.intValue = k; }
                var neg = c.FindPropertyRelative("negate");
                neg.boolValue = GUILayout.Toggle(neg.boolValue, new GUIContent("Al revés", "Se cumple cuando NO pasa (ej.: SIN llevar un objeto)."), GUILayout.Width(80));
                if (GUILayout.Button("✕", GUILayout.Width(24))) remove = i;
                EditorGUILayout.EndHorizontal();
                DrawConditionFields(c, (EvolutionConditionKind)check.intValue);
                EditorGUILayout.EndVertical();
            }
            if (remove >= 0) conds.DeleteArrayElementAtIndex(remove);
            if (GUILayout.Button("+ Condición", GUILayout.Width(120)))
            {
                conds.arraySize++;
                var c = conds.GetArrayElementAtIndex(conds.arraySize - 1);
                EvolutionText.Reset(c);
                c.FindPropertyRelative("check").intValue = (int)EvolutionConditionKind.TimeOfDay;
            }
            so.ApplyModifiedProperties();
        }

        private static void DrawConditionFields(SerializedProperty c, EvolutionConditionKind kind)
        {
            switch (kind)
            {
                case EvolutionConditionKind.MinLevel:
                    c.FindPropertyRelative("value").intValue = Mathf.Clamp(EditorGUILayout.IntField("Nivel", c.FindPropertyRelative("value").intValue), 1, 100); break;
                case EvolutionConditionKind.MinFriendship:
                    c.FindPropertyRelative("value").intValue = EditorGUILayout.IntSlider("Amistad", c.FindPropertyRelative("value").intValue, 0, 255); break;
                case EvolutionConditionKind.Chance:
                    c.FindPropertyRelative("value").intValue = EditorGUILayout.IntSlider(new GUIContent("% de individuos",
                        "Cada monstruo tiene una 'personalidad' fija: el mismo individuo siempre evoluciona igual."), c.FindPropertyRelative("value").intValue, 1, 99); break;
                case EvolutionConditionKind.HoldsItem: ItemPopup(c.FindPropertyRelative("itemId"), "Objeto equipado", false); break;
                case EvolutionConditionKind.Gender:
                    c.FindPropertyRelative("value").intValue = EditorGUILayout.Popup("Género", c.FindPropertyRelative("value").intValue == 2 ? 1 : 0,
                        new[] { "♂ Macho", "♀ Hembra" }) == 1 ? 2 : 1; break;
                case EvolutionConditionKind.KnowsMove: ObjectRow<MoveData>(c.FindPropertyRelative("move"), "Movimiento"); break;
                case EvolutionConditionKind.KnowsMoveOfType: case EvolutionConditionKind.PartyHasType: ObjectRow<ElementTypeData>(c.FindPropertyRelative("type"), "Tipo"); break;
                case EvolutionConditionKind.PartyHasSpecies: ObjectRow<SpeciesData>(c.FindPropertyRelative("species"), "Especie"); break;
                case EvolutionConditionKind.Nature: ObjectRow<NatureData>(c.FindPropertyRelative("nature"), "Naturaleza"); break;
                case EvolutionConditionKind.TimeOfDay: EnumRow<DayTime>(c.FindPropertyRelative("time"), "Momento"); break;
                case EvolutionConditionKind.StatRelation: EnumRow<StatRelation>(c.FindPropertyRelative("relation"), "Comparación"); break;
                case EvolutionConditionKind.MapWeather:
                {
                    var w = c.FindPropertyRelative("weatherId");
                    var list = ContentAssets.LoadAll<WeatherData>();
                    var ids = list.Select(x => x.Id).ToList();
                    int cur = Math.Max(0, ids.IndexOf(w.stringValue));
                    if (list.Count == 0) { EditorGUILayout.HelpBox("Crea climas en CTEditor → Combate → Climas.", MessageType.Info); break; }
                    w.stringValue = ids[EditorGUILayout.Popup("Clima", cur, list.Select(x => x.DisplayName).ToArray())];
                    break;
                }
                case EvolutionConditionKind.AtLocation:
                    c.FindPropertyRelative("text").stringValue = EditorGUILayout.TextField(new GUIContent("Lugar (id)", "El id del mapa o zona. Lo usará el editor de mapas."), c.FindPropertyRelative("text").stringValue); break;
                case EvolutionConditionKind.GameFlag:
                    c.FindPropertyRelative("text").stringValue = EditorGUILayout.TextField(new GUIContent("Marca (id)", "Una marca de la partida que activan los eventos (ej. vencio_alto_mando)."), c.FindPropertyRelative("text").stringValue); break;
            }
        }

        private static void ObjectRow<T>(SerializedProperty p, string label) where T : UnityEngine.Object
            => p.objectReferenceValue = EditorGUILayout.ObjectField(label, p.objectReferenceValue, typeof(T), false);

        private static void EnumRow<T>(SerializedProperty p, string label) where T : struct
        {
            var names = Enum.GetNames(typeof(T));
            p.intValue = EditorGUILayout.Popup(label, Mathf.Clamp(p.intValue, 0, names.Length - 1), names.Select(Etiquetas.Enum).ToArray());
        }

        private static void ItemPopup(SerializedProperty p, string label, bool optional)
        {
            var items = ContentAssets.LoadAll<ItemData>();
            var ids = new List<string>(); var names = new List<string>();
            if (optional) { ids.Add(""); names.Add("(ninguno)"); }
            foreach (var it in items) { ids.Add(it.Id); names.Add(ContentAssets.Label(it)); }
            if (ids.Count == 0) { EditorGUILayout.HelpBox("Crea objetos en CTEditor → Objetos → Todos los objetos.", MessageType.Info); return; }
            int cur = ids.IndexOf(p.stringValue ?? "");
            if (cur < 0 && !string.IsNullOrEmpty(p.stringValue)) { ids.Insert(0, p.stringValue); names.Insert(0, "⚠ " + p.stringValue + " (no existe)"); cur = 0; }
            p.stringValue = ids[EditorGUILayout.Popup(label, Math.Max(0, cur), names.ToArray())];
        }
}
}