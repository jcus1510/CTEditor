using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using Object = UnityEngine.Object;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>Cómo tratar filas cuyo id ya existe.</summary>
    public enum ImportMode
    {
        CreateAndUpdate, // crea las nuevas y actualiza las existentes (lo normal al editar en Excel)
        CreateOnly       // solo crea las que faltan; no toca lo que ya existe (packs de contenido)
    }

    /// <summary>
    /// Contexto de una importación: encuentra fichas por id (las que ya existen y las que se crean en
    /// esta misma importación) y conoce los ids "planeados" (que existirán cuando se aplique), para que
    /// una especie pueda evolucionar a otra que viene en el mismo archivo.
    /// </summary>
    public sealed class ImportContext
    {
        private readonly Dictionary<Type, Dictionary<string, ScriptableObject>> _byType = new Dictionary<Type, Dictionary<string, ScriptableObject>>();
        private readonly Dictionary<Type, HashSet<string>> _planned = new Dictionary<Type, HashSet<string>>();

        private Dictionary<string, ScriptableObject> Map(Type t)
        {
            if (_byType.TryGetValue(t, out var map)) return map;
            map = new Dictionary<string, ScriptableObject>();
            foreach (var a in ContentAssets.LoadAll(t))
                if (a is IContentAsset c && !string.IsNullOrWhiteSpace(c.Id) && !map.ContainsKey(c.Id)) map[c.Id] = a;
            _byType[t] = map;
            return map;
        }

        public ScriptableObject Find(Type t, string id) => id != null && Map(t).TryGetValue(id, out var a) ? a : null;
        public T Find<T>(string id) where T : ScriptableObject => Find(typeof(T), id) as T;

        public void Register(ScriptableObject asset)
        {
            if (asset is IContentAsset c && !string.IsNullOrWhiteSpace(c.Id)) Map(asset.GetType())[c.Id] = asset;
        }

        public void Plan(Type t, string id)
        {
            if (!_planned.TryGetValue(t, out var set)) _planned[t] = set = new HashSet<string>();
            set.Add(id);
        }

        /// <summary>¿Existe (o existirá tras esta importación) una ficha de ese tipo con ese id?</summary>
        public bool Exists(Type t, string id) => Find(t, id) != null || (_planned.TryGetValue(t, out var s) && s.Contains(id));
        public bool Exists<T>(string id) => Exists(typeof(T), id);

        /// <summary>
        /// Busca una ficha referenciada. Si no existe ni existirá, error. Si existirá pero aún no (plan),
        /// devuelve null sin error.
        /// </summary>
        public T Require<T>(string id, string what) where T : ScriptableObject
        {
            var found = Find<T>(id);
            if (found != null || Exists<T>(id)) return found;
            throw new CsvCellException($"{what} '{id}' no existe.");
        }

        /// <summary>Avisos no bloqueantes de la fila que se está procesando (p. ej. habilidad aún no creada).</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>Una columna de un esquema: su cabecera, ayuda, y cómo leerla y escribirla.</summary>
    public sealed class CsvColumn<TData>
    {
        public string Header, Help;
        /// <summary>Otros nombres aceptados al IMPORTAR (p. ej. la cabecera antigua en inglés).</summary>
        public string[] Aliases = new string[0];

        /// <summary>La cabecera de la tabla que corresponde a esta columna (o null si no está).</summary>
        public string MatchIn(IEnumerable<string> headers)
        {
            foreach (var h in headers)
            {
                if (string.Equals(h, Header, StringComparison.OrdinalIgnoreCase)) return h;
                foreach (var a in Aliases) if (string.Equals(h, a, StringComparison.OrdinalIgnoreCase)) return h;
            }
            return null;
        }
        /// <summary>Se aplica en una SEGUNDA pasada (referencias a fichas del mismo archivo: evoluciones).</summary>
        public bool Deferred;
        public Func<TData, string> Get;
        public Action<SerializedObject, string, ImportContext> Set;
    }

    /// <summary>Lo que ocurrirá con una fila al aplicar (calculado sin tocar nada).</summary>
    public sealed class RowPlan
    {
        public int Line;
        public string Id;
        public bool IsNew, Skipped;
        public Dictionary<string, string> Cells;
        public readonly List<(string column, string before, string after)> Changes = new List<(string, string, string)>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool HasChanges => IsNew || Changes.Count > 0;
    }

    /// <summary>Contrato común (sin genéricos) para que la ventana maneje todas las categorías igual.</summary>
    public abstract class CsvSchema
    {
        /// <summary>Nombre del archivo (p. ej. "especies.csv").</summary>
        public abstract string FileName { get; }
        public abstract string Title { get; }
        /// <summary>Orden de importación (primero lo que otros referencian: tipos antes que movimientos...).</summary>
        public abstract int Order { get; }
        public abstract CsvTable Export();
        public abstract void RegisterPlanned(CsvTable table, ImportContext ctx);
        public abstract List<RowPlan> Plan(CsvTable table, ImportContext ctx, ImportMode mode);
        public abstract int Apply(List<RowPlan> plan, ImportContext ctx);
        /// <summary>Ayuda de las columnas (para la hoja de instrucciones).</summary>
        public abstract IEnumerable<(string header, string help)> ColumnHelp { get; }

        /// <summary>Cabeceras que reconoce (para adivinar a qué categoría pertenece un CSV suelto).</summary>
        public virtual IEnumerable<string> AcceptedHeaders => ColumnHelp.Select(h => h.header);
    }

    /// <summary>
    /// Esquema de una categoría de fichas (TData) descrito como LISTA DE COLUMNAS. El motor genérico
    /// hace exportar, planear (comparar sin tocar nada) y aplicar (crear/actualizar).
    /// </summary>
    public class CsvSchema<TData> : CsvSchema where TData : ScriptableObject, IContentAsset
    {
        private readonly string _file, _title, _category;
        private readonly int _order;
        protected readonly List<CsvColumn<TData>> Columns = new List<CsvColumn<TData>>();

        public CsvSchema(string file, string title, string category, int order)
        {
            _file = file; _title = title; _category = category; _order = order;
        }

        public override string FileName => _file;
        public override string Title => _title;
        public override int Order => _order;
        public override IEnumerable<(string header, string help)> ColumnHelp => Columns.Select(c => (c.Header, c.Help));

        public CsvSchema<TData> Col(string header, string help, Func<TData, string> get,
            Action<SerializedObject, string, ImportContext> set, bool deferred = false, params string[] aliases)
        {
            Columns.Add(new CsvColumn<TData> { Header = header, Help = help, Get = get, Set = set, Deferred = deferred, Aliases = aliases ?? new string[0] });
            return this;
        }

        /// <summary>Todas las cabeceras aceptadas (principales y alias), para detectar la categoría de un archivo.</summary>
        public override IEnumerable<string> AcceptedHeaders
        {
            get { foreach (var c in Columns) { yield return c.Header; foreach (var a in c.Aliases) yield return a; } }
        }

        public override CsvTable Export()
        {
            var table = new CsvTable(Columns.Select(c => c.Header));
            var items = ContentAssets.LoadAll<TData>();
            items.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
            foreach (var item in items)
            {
                var row = new Dictionary<string, string>();
                foreach (var c in Columns) row[c.Header] = c.Get(item) ?? "";
                table.AddRow(row);
            }
            return table;
        }

        public override void RegisterPlanned(CsvTable table, ImportContext ctx)
        {
            foreach (var row in table.Rows)
                if (row.TryGetValue("id", out var id) && !string.IsNullOrWhiteSpace(id)) ctx.Plan(typeof(TData), id.Trim());
        }

        public override List<RowPlan> Plan(CsvTable table, ImportContext ctx, ImportMode mode)
        {
            var plans = new List<RowPlan>();
            var present = Columns.Select(c => (col: c, header: c.MatchIn(table.Headers))).Where(x => x.header != null).ToList();
            var seen = new HashSet<string>();

            for (int r = 0; r < table.Rows.Count; r++)
            {
                var cells = table.Rows[r];
                var plan = new RowPlan { Line = table.LineNumbers[r], Cells = cells };
                plans.Add(plan);

                plan.Id = cells.TryGetValue("id", out var id) ? id.Trim() : "";
                if (plan.Id.Length == 0) { plan.Errors.Add("Falta el id."); continue; }
                if (!seen.Add(plan.Id)) { plan.Errors.Add($"El id '{plan.Id}' está repetido en el archivo."); continue; }

                var existing = ctx.Find<TData>(plan.Id);
                plan.IsNew = existing == null;
                bool repairing = false;
                if (!plan.IsNew && mode == ImportMode.CreateOnly)
                {
                    // «Solo lo que falta» no toca lo existente... salvo que tenga REFERENCIAS ROTAS (p. ej. un
                    // entrenador cuyo equipo apuntaba a especies que se borraron y se volvieron a crear): esa
                    // ficha se repara con los datos de la hoja, que la nombran por id.
                    if (!ContentAssets.HasBrokenReferences(existing)) { plan.Skipped = true; continue; }
                    repairing = true;
                }

                // Se aplica la fila sobre una COPIA temporal: así sabemos si hay errores y qué cambiaría,
                // sin tocar ninguna ficha real. Cualquier fallo (no solo los de formato) se queda en ESTA
                // fila: el resto del archivo se sigue analizando y se puede aplicar.
                TData temp = null;
                try
                {
                    temp = existing != null ? Object.Instantiate(existing) : ScriptableObject.CreateInstance<TData>();
                    var so = new SerializedObject(temp);
                    ctx.Warnings.Clear();
                    foreach (var (col, header) in present)
                    {
                        try { col.Set(so, cells[header], ctx); }
                        catch (CsvCellException e) { plan.Errors.Add($"[{col.Header}] {e.Message}"); }
                        catch (Exception e) { plan.Errors.Add(InternalError(col.Header, plan, e)); }
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                    plan.Warnings.AddRange(ctx.Warnings);

                    if (existing != null)
                        foreach (var (col, header) in present)
                        {
                            try
                            {
                                string before = col.Get(existing) ?? "";
                                string after = col.Deferred ? (cells[header] ?? "") : (col.Get(temp) ?? "");
                                if (!string.Equals(before, after, StringComparison.Ordinal)) plan.Changes.Add((col.Header, before, after));
                            }
                            catch (Exception e) { plan.Errors.Add(InternalError(col.Header, plan, e)); }
                        }
                }
                catch (Exception e) { plan.Errors.Add(InternalError("fila", plan, e)); }
                finally { if (temp != null) Object.DestroyImmediate(temp); }

                // Reparación: solo si la hoja cambia algo (si la referencia rota no es de nada que la hoja escriba,
                // p. ej. un sprite, la fila se trata como «ya existe»).
                if (repairing)
                {
                    if (plan.Changes.Count == 0 && plan.Errors.Count == 0) plan.Skipped = true;
                    else plan.Warnings.Insert(0, "Tenía referencias rotas (fichas borradas o vueltas a crear): se repara con los datos de esta hoja.");
                }
            }
            return plans;
        }

        public override int Apply(List<RowPlan> plan, ImportContext ctx)
        {
            int applied = 0;
            var toApply = plan.Where(p => p.Errors.Count == 0 && !p.Skipped && p.HasChanges).ToList();

            // Pasada 1: crear/actualizar con todas las columnas normales.
            var assets = new Dictionary<RowPlan, TData>();
            // Un fallo en una fila la marca con error y se sigue con las demás (no deja la importación a medias).
            foreach (var p in toApply)
            {
                try
                {
                    var asset = ctx.Find<TData>(p.Id) ?? ContentAssets.Create<TData>(_category, p.Id, p.Id);
                    ContentAssets.Edit(asset, so =>
                    {
                        foreach (var col in Columns.Where(c => !c.Deferred))
                        {
                            var h = col.MatchIn(p.Cells.Keys);
                            if (h != null) col.Set(so, p.Cells[h], ctx);
                        }
                    });
                    ctx.Register(asset);
                    assets[p] = asset;
                    applied++;
                }
                catch (Exception e) { p.Errors.Add(InternalError("aplicar", p, e)); }
            }

            // Pasada 2: columnas diferidas (ya existen todas las fichas del archivo).
            foreach (var p in toApply)
            {
                if (!assets.TryGetValue(p, out var asset)) continue;
                var deferred = Columns.Where(c => c.Deferred && c.MatchIn(p.Cells.Keys) != null).ToList();
                if (deferred.Count == 0) continue;
                try { ContentAssets.Edit(asset, so => { foreach (var col in deferred) col.Set(so, p.Cells[col.MatchIn(p.Cells.Keys)], ctx); }); }
                catch (Exception e) { p.Errors.Add(InternalError("aplicar", p, e)); }
            }
            return applied;
        }

        // Error inesperado (un fallo del programa, no del Excel): se enseña en la fila y el detalle va a la Consola.
        private string InternalError(string where, RowPlan plan, Exception e)
        {
            Debug.LogError($"[Excel] {FileName}, línea {plan.Line}, id '{plan.Id}', {where}: {e}");
            return $"[{where}] Error interno ({e.GetType().Name}: {e.Message}). El detalle está en la Consola.";
        }

        // ---------------- Ayudas para definir columnas ----------------

        public static void SetInt(SerializedObject so, string field, string cell, string what, int min = int.MinValue)
        {
            if (!CsvTable.TryInt(cell, out int v)) throw new CsvCellException($"'{cell}' no es un número entero ({what}).");
            if (v < min) throw new CsvCellException($"{what} debe ser al menos {min}.");
            so.FindProperty(field).intValue = v;
        }

        public static void SetFloat(SerializedObject so, string field, string cell, string what)
        {
            if (!CsvTable.TryNumber(cell, out float v)) throw new CsvCellException($"'{cell}' no es un número ({what}).");
            so.FindProperty(field).floatValue = v;
        }

        public static void SetBool(SerializedObject so, string field, string cell, string what)
        {
            if (!CsvTable.TryBool(cell, out bool v)) throw new CsvCellException($"'{cell}' no es sí/no ({what}).");
            so.FindProperty(field).boolValue = v;
        }
    }
}
