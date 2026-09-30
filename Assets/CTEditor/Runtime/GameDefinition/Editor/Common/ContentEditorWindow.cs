using System.Linq;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// VENTANA BASE de todos los editores de contenido (Template Method): define el esqueleto común
    /// y cada editor concreto solo rellena lo que lo hace especial.
    ///
    ///   Izquierda: buscador, lista de fichas, crear por id, duplicar / borrar / ubicar, y el botón de
    ///              "crear el set clásico" (DrawBulkPresets).
    ///   Derecha:   plantillas para la ficha seleccionada (DrawPresets), el inspector de Unity
    ///              incrustado, una vista previa (DrawPreview: gráficos, calculadoras) y la validación.
    ///
    /// Antes, cada ventana (Movimientos, Estados) repetía todo esto; con 6 editores más, la clase base
    /// evita 6 copias del mismo código (DRY) y hace que todos se comporten igual para el autor.
    ///
    /// 'where TData : ScriptableObject, IContentAsset' = restricción genérica: TData debe ser una ficha
    /// de Unity Y tener Id/DisplayName. Gracias a eso la base puede listar y buscar sin conocer el tipo.
    /// </summary>
    public abstract class ContentEditorWindow<TData> : EditorWindow
        where TData : ScriptableObject, IContentAsset
    {
        // --- Lo que define cada editor concreto ---

        /// <summary>Carpeta de GameContent/Resources donde viven estas fichas (ContentFolders.X).</summary>
        protected abstract string Category { get; }

        /// <summary>Cómo llamar a una ficha en la UI ("curva", "naturaleza"...).</summary>
        protected abstract string Noun { get; }

        /// <summary>Pasos de la GUÍA RÁPIDA de este editor (null = sin guía).</summary>
        public virtual string[] GuideSteps => null;

        /// <summary>Título grande de la ventana (por defecto, el sustantivo en plural).</summary>
        protected virtual string Title => char.ToUpperInvariant(Noun[0]) + Noun.Substring(1) + (Noun.EndsWith("d") || Noun.EndsWith("l") ? "es" : "s");

        // Género gramatical del sustantivo, para no escribir "una movimiento" o "un especie".
        private static readonly System.Collections.Generic.HashSet<string> FeminineNouns =
            new System.Collections.Generic.HashSet<string> { "regla", "naturaleza", "curva", "habilidad", "especie", "zona", "trampa" };
        protected bool Feminine => FeminineNouns.Contains(Noun);
        protected string Un => Feminine ? "una" : "un";
        protected string Nuevo => Feminine ? "Nueva" : "Nuevo";
        protected string Ningun => Feminine ? "ninguna" : "ningún";

        /// <summary>Texto de ayuda breve arriba de la ventana (qué es esto y para qué sirve).</summary>
        protected virtual string Intro => null;

        /// <summary>Botones para crear el conjunto clásico completo (columna izquierda).</summary>
        protected virtual void DrawBulkPresets() { }

        /// <summary>
        /// PLANTILLAS POR ID (opcional): si la ventana las declara, el editor ofrece solo
        ///   • «↻ Actualizar desde las plantillas»: todas o por sección, con confirmación; y
        ///   • «↺ Restaurar su plantilla» en la ficha elegida cuando su id es el de una plantilla.
        /// Así no hace falta borrar una ficha para volver a crearla (y no se rompen las referencias).
        /// </summary>
        protected virtual IReadOnlyList<(string id, string name, string group)> Templates => null;

        /// <summary>Rellena una ficha con la plantilla de ese id (no toca el id).</summary>
        protected virtual void ApplyTemplate(SerializedObject so, string id) { }

        /// <summary>Plantillas que rellenan la ficha seleccionada (antes del inspector).</summary>
        protected virtual void DrawPresets(TData selected) { }

        /// <summary>Vista previa bajo el inspector: gráficos, calculadoras, tablas.</summary>
        protected virtual void DrawPreview(TData selected) { }

        /// <summary>Hook: se llama al crear una ficha nueva vacía (para darle valores iniciales).</summary>
        protected virtual void OnCreated(SerializedObject so) { }

        protected virtual float ListWidth => 240f;

        /// <summary>Color de esta categoría (título, lista, secciones).</summary>
        protected Color Accent => EditorTheme.ForCategory(Category);

        /// <summary>Marca de color opcional en la fila de la lista (p. ej. el color del tipo de un movimiento).</summary>
        protected virtual Color? RowMark(TData item) => null;

        /// <summary>Orden de la lista (por defecto, alfabético). Especies: por número de Pokédex.</summary>
        protected virtual int CompareItems(TData a, TData b)
            => string.Compare(ContentAssets.Label(a), ContentAssets.Label(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>Texto al pasar el ratón por una fila (por defecto, su nombre completo).</summary>
        protected virtual string RowTooltip(TData item) => null;

        // ---------------- Filtros y orden (opcionales) ----------------

        /// <summary>Dibuja controles de filtro bajo el buscador (tipo, nivel...). Llama a FiltersChanged() si cambian.</summary>
        protected virtual void DrawFilters() { }

        /// <summary>¿Se ve esta ficha con los filtros actuales?</summary>
        protected virtual bool PassesFilter(TData item) => true;

        /// <summary>Opciones de orden («Nº Pokédex», «Peso»...). Null = solo el orden por defecto.</summary>
        protected virtual string[] SortOptions => null;

        /// <summary>Compara según la opción de orden elegida (índice en SortOptions).</summary>
        protected virtual int CompareBy(int sortIndex, TData a, TData b) => CompareItems(a, b);

        private int _sortIndex;
        private bool _sortDescending;

        /// <summary>Tras cambiar un filtro u orden: vuelve a ordenar y repinta.</summary>
        protected void FiltersChanged() { SortAll(); Repaint(); }

        private void SortAll()
        {
            if (SortOptions == null) _all.Sort(CompareItems);
            else
            {
                int i = Mathf.Clamp(_sortIndex, 0, SortOptions.Length - 1);
                _all.Sort((a, b) =>
                {
                    int c = CompareBy(i, a, b);
                    if (c == 0) c = CompareItems(a, b);
                    return _sortDescending ? -c : c;
                });
            }
        }

        // --- Estado de la ventana ---
        private List<TData> _all = new List<TData>();
        private UnityEditor.Editor _embedded;
        private List<ValidationIssue> _issues = new List<ValidationIssue>();
        private Vector2 _listScroll, _detailScroll;
        private string _search = "";
        private string _newId = "";

        // "¿Quién usa esto?" (se calcula al pedirlo, no en cada repintado: recorre todo el contenido).
        private bool _showUsages;
        private List<ContentReference> _usages;
        private TData _usagesFor;
        private string _renameTo = "";

        protected TData Selected { get; private set; }

        private double _revalidateAt = -1;

        // ~10 veces por segundo: si hace un rato que no se escribe, se valida (y la lista coge el nombre nuevo).
        private void OnInspectorUpdate()
        {
            if (_revalidateAt < 0 || EditorApplication.timeSinceStartup < _revalidateAt) return;
            _revalidateAt = -1;
            Revalidate();
            Repaint();
        }

        /// <summary>Abre (o enfoca) una ventana concreta. Úsalo desde el [MenuItem] de cada editor.</summary>
        protected static T OpenWindow<T>(string title) where T : ContentEditorWindow<TData>
        {
            var w = GetWindow<T>();
            w.titleContent = new GUIContent(title);
            w.minSize = new Vector2(760, 460);
            w.Refresh();
            w.Show();
            return w;
        }

        protected virtual void OnEnable()
        {
            wantsMouseMove = true; // para que los gráficos reaccionen al pasar el ratón
            Refresh();
        }

        protected virtual void OnDisable()
        {
            if (_embedded != null) DestroyImmediate(_embedded);
        }

        /// <summary>Vuelve a leer las fichas del proyecto (tras crear, borrar o presets en lote).</summary>
        public void Refresh()
        {
            _all = ContentAssets.LoadAll<TData>();
            SortAll();

            if (_all.Count == 0) Select(null);
            else if (Selected == null || !_all.Contains(Selected)) Select(_all[0]);
            else Revalidate();
        }

        /// <summary>Selecciona una ficha concreta desde fuera (p. ej. "Abrir" en otra herramienta).</summary>
        public void FocusOn(TData item)
        {
            Refresh();
            if (item != null) Select(item);
        }

        protected void Select(TData item)
        {
            Selected = item;
            if (_embedded != null) DestroyImmediate(_embedded);
            _embedded = item != null ? UnityEditor.Editor.CreateEditor(item) : null;
            GUI.FocusControl(null);
            Revalidate();
        }

        protected void Revalidate()
            => _issues = Selected != null ? ContentValidator.IssuesFor(Selected) : new List<ValidationIssue>();

        /// <summary>Aplica cambios por código a la ficha seleccionada (presets) y refresca la validación.</summary>
        protected void EditSelected(Action<SerializedObject> edit)
        {
            if (Selected == null) return;
            ContentAssets.Edit(Selected, edit);
            Revalidate();
            Repaint();
        }

        // 'protected' (no private): Unity encuentra OnGUI por REFLEXIÓN en la clase concreta, y la
        // reflexión no ve los métodos privados de una clase padre. Con private, la ventana saldría vacía.
        protected void OnGUI()
        {
            // Los gráficos siguen al ratón: redibujar al moverlo.
            if (Event.current.type == EventType.MouseMove) Repaint();

            EditorZoom.Begin(this);
            try
            {
                // Banda de título con el color de la categoría + guía rápida plegable.
                var color = Accent;
                EditorTheme.TitleBand(Title, Intro, color);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorZoom.Buttons();
                EditorGUILayout.EndHorizontal();
                EditorTheme.Guide(GetType().Name, GuideSteps, color);

                EditorGUILayout.BeginHorizontal();
                DrawList();
                DrawDetail();
                EditorGUILayout.EndHorizontal();
            }
            finally { EditorZoom.End(); }
        }

        // ---------------- Columna izquierda ----------------

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));

            _search = EditorGUILayout.TextField("Buscar", _search);
            DrawFilters();
            if (SortOptions != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Ordenar", GUILayout.Width(52));
                int si = EditorGUILayout.Popup(Mathf.Clamp(_sortIndex, 0, SortOptions.Length - 1), SortOptions);
                bool desc = GUILayout.Toggle(_sortDescending, new GUIContent(_sortDescending ? "↓" : "↑", "Invertir el orden"), EditorStyles.miniButton, GUILayout.Width(24));
                EditorGUILayout.EndHorizontal();
                if (si != _sortIndex || desc != _sortDescending) { _sortIndex = si; _sortDescending = desc; FiltersChanged(); }
            }

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            int shown = 0, hidden = 0;
            foreach (var item in _all)
            {
                if (item == null) continue;
                string label = ContentAssets.Label(item);
                if (!string.IsNullOrEmpty(_search) && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!PassesFilter(item)) { hidden++; continue; }
                shown++;
                // Fila: la seleccionada con fondo del color de la categoría; el nombre completo al pasar el ratón.
                var row = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                bool sel = item == Selected;
                if (sel) EditorGUI.DrawRect(row, EditorTheme.WithAlpha(Accent, 0.35f));
                else if (row.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(row, EditorTheme.WithAlpha(Accent, 0.12f));
                var mark = RowMark(item);
                if (mark.HasValue) EditorGUI.DrawRect(new Rect(row.x, row.y + 3, 4, row.height - 6), mark.Value);
                if (GUI.Button(new Rect(row.x + 8, row.y, row.width - 8, row.height),
                        new GUIContent(label, RowTooltip(item) ?? label), sel ? EditorStyles.boldLabel : EditorStyles.label))
                    Select(item);
            }
            if (shown == 0)
                EditorGUILayout.LabelField(_all.Count == 0 ? $"No hay {Ningun} {Noun} todavía." : "Sin resultados.", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
            if (hidden > 0) EditorGUILayout.LabelField($"{shown} de {shown + hidden} (hay filtros activos)", EditorStyles.miniLabel);

            // Crear por id: la ficha aparece directamente en su carpeta de GameContent.
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{Nuevo} {Noun} (escribe su id)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _newId = EditorGUILayout.TextField(_newId);
            GUI.enabled = !string.IsNullOrWhiteSpace(_newId);
            if (GUILayout.Button("Crear", GUILayout.Width(60))) CreateNew(_newId.Trim());
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Acciones sobre la seleccionada.
            GUI.enabled = Selected != null;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Duplicar")) DuplicateSelected();
            if (GUILayout.Button("Ubicar")) EditorGUIUtility.PingObject(Selected);
            if (GUILayout.Button("Borrar")) DeleteSelected();
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            EditorGUILayout.Space();
            DrawBulkPresets();
            DrawTemplateUpdate();

            if (GUILayout.Button("Refrescar")) Refresh();
            EditorGUILayout.EndVertical();
        }

        private void CreateNew(string id)
        {
            if (ContentAssets.FindById<TData>(id) != null)
            {
                EditorUtility.DisplayDialog("Id repetido", $"Ya existe {Un} {Noun} con id '{id}'. Elige otro.", "Vale");
                return;
            }
            var asset = ContentAssets.Create<TData>(Category, id, id, OnCreated);
            AssetDatabase.SaveAssets();
            _newId = "";
            Refresh();
            Select(asset);
        }

        private void DuplicateSelected()
        {
            if (Selected == null) return;
            string src = AssetDatabase.GetAssetPath(Selected);
            string dst = AssetDatabase.GenerateUniqueAssetPath(src);
            if (!AssetDatabase.CopyAsset(src, dst)) return;
            ContentAssets.ClearCache();

            var copy = AssetDatabase.LoadAssetAtPath<TData>(dst);
            string newId = Selected.Id + "_copia";
            ContentAssets.Edit(copy, so => ContentAssets.SetString(so, "id", newId));
            AssetDatabase.SaveAssets();
            Refresh();
            Select(copy);
        }

        // BORRAR = mandar a la PAPELERA (se puede recuperar con todas sus referencias intactas).
        // Antes se enseña QUIÉN la usa; «Borrar para siempre» queda como opción aparte.
        private void DeleteSelected()
        {
            if (Selected == null) return;
            var refs = ReferenceFinder.FindReferencesTo(Selected);
            string who = refs.Count == 0
                ? "Nadie la usa."
                : $"La usan {refs.Count} referencia(s):\n{ContentTrash.DescribeUsers(refs)}\n\n" +
                  "En la PAPELERA siguen funcionando y, al recuperarla, todo vuelve a su sitio. " +
                  "Si la borras PARA SIEMPRE, esas referencias quedan rotas aunque vuelvas a crear otra con el mismo id.";
            string tip = "\n\nSi solo quieres volver a los datos originales, usa «↺ Restaurar su plantilla» o «Restaurar desde el pack».";
            int choice = EditorUtility.DisplayDialogComplex($"Borrar {Noun}",
                $"¿Qué hacemos con '{ContentAssets.Label(Selected)}'?\n\n{who}{tip}",
                "🗑 A la papelera", "Cancelar", "Borrar para siempre");
            if (choice == 1) return;

            if (choice == 2)
            {
                if (refs.Count > 0 && !EditorUtility.DisplayDialog("Borrar para siempre",
                        $"Esto NO se puede deshacer y rompe {refs.Count} referencia(s). ¿Seguro?", "Borrar para siempre", "Cancelar"))
                    return;
                ContentTrash.DeleteForever(Selected);
            }
            else
            {
                string error = ContentTrash.MoveToTrash(Selected);
                if (!string.IsNullOrEmpty(error)) { EditorUtility.DisplayDialog("Papelera", "No se pudo mover a la papelera:\n" + error, "Vale"); return; }
                ShowNotification(new GUIContent("🗑 En la papelera. Recupérala desde Herramientas → Papelera."));
            }
            Selected = null;
            EditorCatalog.ClearCounts();
            Refresh();
        }

        // ---------------- Columna derecha ----------------

        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical();
            if (Selected == null)
            {
                EditorGUILayout.HelpBox($"Crea {Un} {Noun} a la izquierda (escribe un id y pulsa Crear), o usa el botón del set clásico.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            EditorTheme.Chip(ContentAssets.Label(Selected), Accent);
            DrawRestoreTemplate();
            DrawPresets(Selected);
            EditorTheme.Section("Datos de la ficha", Accent);

            EditorGUI.BeginChangeCheck();
            if (_embedded != null) _embedded.OnInspectorGUI();
            // Validar TODO el contenido en cada tecla hacía que escribir fuera lento: se valida al dejar de escribir.
            if (EditorGUI.EndChangeCheck()) _revalidateAt = EditorApplication.timeSinceStartup + 0.6;

            EditorGUILayout.Space();
            DrawPreview(Selected);

            EditorGUILayout.Space();
            DrawUsages();

            EditorTheme.Section("Validación", Accent);
            if (_issues == null || _issues.Count == 0)
                EditorTheme.Tip("Sin problemas: esta ficha está lista para usarse.", EditorTheme.Ok, "✔");
            else
                foreach (var issue in _issues)
                    EditorTheme.Tip(issue.Message, issue.Severity == IssueSeverity.Error ? EditorTheme.Bad : EditorTheme.Warn,
                        issue.Severity == IssueSeverity.Error ? "✖" : "⚠");

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ---------------- ¿Quién usa esto? + renombrar ----------------

        private void DrawUsages()
        {
            _showUsages = EditorGUILayout.Foldout(_showUsages, "¿Quién usa esta ficha?  ·  Renombrar id", true);
            if (!_showUsages) return;

            if (_usages == null || _usagesFor != Selected)
            {
                _usages = ReferenceFinder.FindReferencesTo(Selected);
                _usagesFor = Selected;
            }

            if (_usages.Count == 0)
                EditorGUILayout.LabelField("Nadie la usa todavía.", EditorStyles.miniLabel);
            foreach (var r in _usages)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{ContentAssets.Label(r.Owner)}  →  {r.Field}{(r.ById ? "  (por id)" : "")}");
                if (GUILayout.Button("Ubicar", GUILayout.Width(60))) EditorGUIUtility.PingObject(r.Owner);
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Buscar de nuevo")) _usages = null;

            // Renombrar: cambia el id y actualiza todas las referencias por id (y el nombre del archivo).
            EditorGUILayout.BeginHorizontal();
            _renameTo = EditorGUILayout.TextField("Nuevo id", _renameTo);
            GUI.enabled = !string.IsNullOrWhiteSpace(_renameTo) && _renameTo.Trim() != Selected.Id;
            if (GUILayout.Button("Renombrar", GUILayout.Width(90))) RenameSelected(_renameTo.Trim());
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void RenameSelected(string newId)
        {
            if (ContentAssets.FindById<TData>(newId) != null)
            {
                EditorUtility.DisplayDialog("Id repetido", $"Ya existe {Un} {Noun} con id '{newId}'.", "Vale");
                return;
            }
            int byId = 0;
            foreach (var r in ReferenceFinder.FindReferencesTo(Selected)) if (r.ById) byId++;
            if (!EditorUtility.DisplayDialog("Renombrar id",
                    $"Cambiar '{Selected.Id}' por '{newId}'.\nSe actualizarán {byId} referencia(s) por id en otras fichas.",
                    "Renombrar", "Cancelar")) return;

            int n = ReferenceFinder.RenameId(Selected, newId);
            _renameTo = "";
            _usages = null;
            Refresh();
            ShowNotification(new GUIContent($"Renombrada. {n} referencia(s) actualizada(s)."));
        }

        // ---------------- Utilidades para los editores concretos ----------------

        /// <summary>Dibuja una fila de botones; devuelve el índice pulsado o -1.</summary>
        protected static int ButtonRow(params string[] labels)
        {
            int clicked = -1;
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
                if (GUILayout.Button(labels[i])) clicked = i;
            EditorGUILayout.EndHorizontal();
            return clicked;
        }

        // ---------------- Actualizar desde las plantillas ----------------

        // Las fichas que ya existen y tienen plantilla: (ficha, plantilla).
        private List<(TData asset, (string id, string name, string group) template)> ExistingWithTemplate()
        {
            var list = new List<(TData, (string, string, string))>();
            var templates = Templates;
            if (templates == null) return list;
            var byId = new Dictionary<string, (string, string, string)>();
            foreach (var t in templates) if (!byId.ContainsKey(t.id)) byId[t.id] = t;
            foreach (var a in ContentAssets.LoadAll<TData>())
                if (!string.IsNullOrEmpty(a.Id) && byId.TryGetValue(a.Id, out var t)) list.Add((a, t));
            return list;
        }

        private void DrawTemplateUpdate()
        {
            if (Templates == null) return;
            var existing = ExistingWithTemplate();
            GUI.enabled = existing.Count > 0;
            if (GUILayout.Button(new GUIContent($"↻ Actualizar desde las plantillas ({existing.Count}) ▾",
                    "Vuelve a aplicar la plantilla clásica a las fichas que ya tienes (todas o por sección). El id y las referencias se mantienen: no hace falta borrar nada.")))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent($"Todas ({existing.Count})"), false, () => UpdateFromTemplates(existing, "todas las plantillas"));
                foreach (var g in existing.GroupBy(e => string.IsNullOrEmpty(e.template.group) ? "Clásicas" : e.template.group).OrderBy(g => g.Key))
                {
                    var items = g.ToList();
                    string group = g.Key;
                    menu.AddItem(new GUIContent($"Por sección/{group} ({items.Count})"), false, () => UpdateFromTemplates(items, $"la sección «{group}»"));
                }
                menu.ShowAsContext();
            }
            GUI.enabled = true;
        }

        private void UpdateFromTemplates(List<(TData asset, (string id, string name, string group) template)> items, string what)
        {
            if (items.Count == 0) return;
            var names = items.Take(12).Select(i => "• " + ContentAssets.Label(i.asset)).ToList();
            if (items.Count > 12) names.Add($"… y {items.Count - 12} más");
            if (!EditorUtility.DisplayDialog("Actualizar desde las plantillas",
                    $"Se volverán a aplicar las plantillas de {what} a {items.Count} {Noun}(s):\n\n{string.Join("\n", names)}\n\n" +
                    "Se SOBRESCRIBEN sus datos (también el nombre) con los de la plantilla. El id y todas las referencias se mantienen " +
                    "(las especies, entrenadores... que los usan siguen funcionando). Se puede deshacer con Ctrl+Z.",
                    "Actualizar", "Cancelar")) return;
            foreach (var (asset, template) in items) ContentAssets.Edit(asset, so => ApplyTemplate(so, template.id));
            AssetDatabase.SaveAssets();
            Refresh();
            Revalidate();
            ShowNotification(new GUIContent($"Actualizadas {items.Count} ficha(s)"));
        }

        private void DrawRestoreTemplate()
        {
            var templates = Templates;
            if (templates == null || Selected == null || string.IsNullOrEmpty(Selected.Id)) return;
            var match = templates.FirstOrDefault(t => t.id == Selected.Id);
            if (match.id == null) return;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent($"Viene de la plantilla «{match.name}»", "Su id coincide con una plantilla clásica."), EditorStyles.miniLabel);
            if (GUILayout.Button(new GUIContent("↺ Restaurar su plantilla", "Vuelve a poner los datos de la plantilla (el id y las referencias se mantienen)."),
                    EditorStyles.miniButton, GUILayout.Width(150))
                && EditorUtility.DisplayDialog("Restaurar plantilla",
                    $"¿Volver a poner los datos de la plantilla «{match.name}» en '{ContentAssets.Label(Selected)}'?\n\nSe sobrescriben sus datos (también el nombre). El id y las referencias se mantienen. Se puede deshacer con Ctrl+Z.",
                    "Restaurar", "Cancelar"))
            {
                string id = match.id;
                EditSelected(so => ApplyTemplate(so, id));
                ShowNotification(new GUIContent("Plantilla restaurada"));
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Tras crear fichas en lote: guardar, refrescar y avisar cuántas se crearon.</summary>
        protected void FinishBulk(int created, string what)
        {
            AssetDatabase.SaveAssets();
            Refresh();
            ShowNotification(new GUIContent(created > 0 ? $"Creadas {created} {what}" : $"Ya tenías todas las {what}"));
        }
    }
}
