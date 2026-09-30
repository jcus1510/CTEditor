using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor.Csv
{
    /// <summary>
    /// Los esquemas de Excel de cada categoría: qué columnas tiene cada archivo y cómo se leen/escriben.
    /// Especies, movimientos y tipos tienen columnas EN ESPAÑOL pensadas para editarse a mano; el resto
    /// usa el esquema automático (CsvReflectiveSchema), que cubre todos sus campos sin código extra.
    /// </summary>
    public static class CsvSchemas
    {
        /// <summary>Todos los esquemas, en orden de importación (primero lo que otros referencian).</summary>
        public static List<CsvSchema> All()
        {
            var list = new List<CsvSchema>
            {
                Types(), new TypeChartCsvSchema(),
                new CsvReflectiveSchema<StatusConditionData>("estados.csv", "Estados", ContentFolders.Status, 20),
                Abilities(),
                new CsvReflectiveSchema<WeatherData>("climas.csv", "Climas", ContentFolders.Weathers, 25),
                new CsvReflectiveSchema<HazardData>("trampas.csv", "Trampas de campo", ContentFolders.Hazards, 26),
                new CsvReflectiveSchema<SideConditionData>("efectos_lado.csv", "Efectos de lado", ContentFolders.SideConditions, 27),
                Items(),
                new CsvReflectiveSchema<NatureData>("naturalezas.csv", "Naturalezas", ContentFolders.Natures, 40),
                new CsvReflectiveSchema<GrowthCurveData>("curvas.csv", "Curvas de XP", ContentFolders.Curves, 50),
                new CsvReflectiveSchema<MechanicData>("mecanicas.csv", "Mecánicas especiales", ContentFolders.Mechanics, 58),
                new CsvReflectiveSchema<RulesetData>("reglas.csv", "Reglas", ContentFolders.Rulesets, 60),
                Moves(), Species(),
                Trainers(), Zones(), TeamPresets(),
                new CsvReflectiveSchema<EggGroupData>("grupos_huevo.csv", "Grupos huevo", ContentFolders.EggGroups, 15),
                new CsvReflectiveSchema<AiLevelData>("niveles_ia.csv", "Niveles de IA", ContentFolders.AiLevels, 88),
            };
            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            return list;
        }

        // ---------------- Tipos ----------------

        public static CsvSchema<ElementTypeData> Types()
            => new CsvSchema<ElementTypeData>("tipos.csv", "Tipos", ContentFolders.Types, 10)
                .Col("id", "Id único (ej. fire).", d => d.Id, (so, v, c) => so.FindProperty("id").stringValue = v)
                .Col("nombre", "Nombre visible (ej. Fuego).", d => d.DisplayName, (so, v, c) => so.FindProperty("displayName").stringValue = v)
                .Col("color", "Color en hexadecimal (ej. EE8130).", d => ToHex(d.Color), (so, v, c) =>
                {
                    if (string.IsNullOrWhiteSpace(v)) return;
                    try { so.FindProperty("color").colorValue = TypeChartTools.FromHex(v.Trim().TrimStart('#')); }
                    catch (Exception) { throw new CsvCellException($"'{v}' no es un color hexadecimal (ej. EE8130)."); }
                });

        private static string ToHex(Color c)
            => $"{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";

        // ---------------- Movimientos ----------------

        public static CsvSchema<MoveData> Moves()
            => new CsvSchema<MoveData>("movimientos.csv", "Movimientos", ContentFolders.Moves, 70)
                .Col("id", "Id único (ej. ember).", d => d.Id, (so, v, c) => so.FindProperty("id").stringValue = v)
                .Col("nombre", "Nombre visible.", d => d.DisplayName, (so, v, c) => so.FindProperty("displayName").stringValue = v)
                .Col("tipo", "Id del tipo (ej. fire).", d => d.Type != null ? d.Type.Id : "",
                    (so, v, c) => so.FindProperty("type").objectReferenceValue = c.Require<ElementTypeData>(v, "El tipo"))
                .Col("categoria", "fisico, especial o estado.", d => CsvCodecs.FormatCategory(d.Category),
                    (so, v, c) => so.FindProperty("category").enumValueIndex = (int)CsvCodecs.ParseCategory(v))
                .Col("potencia", "0 para movimientos de estado.", d => d.Power.ToString(),
                    (so, v, c) => CsvSchema<MoveData>.SetInt(so, "power", v, "potencia", 0))
                .Col("precision", "0-100, o 'nunca' si nunca falla.", d => d.NeverMisses ? "nunca" : CsvTable.Number(d.Accuracy), (so, v, c) =>
                {
                    bool never = v.Trim().ToLowerInvariant() is "nunca" or "-" or "never";
                    so.FindProperty("neverMisses").boolValue = never;
                    if (!never) CsvSchema<MoveData>.SetFloat(so, "accuracy", v, "precisión");
                })
                .Col("pp", "Usos máximos (ej. 25).", d => d.MaxPp.ToString(), (so, v, c) => CsvSchema<MoveData>.SetInt(so, "maxPp", v, "PP", 1))
                .Col("prioridad", "0 normal; +1 Ataque Rápido.", d => d.Priority.ToString(), (so, v, c) => CsvSchema<MoveData>.SetInt(so, "priority", v, "prioridad"))
                .Col("objetivo", "rival, propio, rivales, aliado o todos.", d => CsvCodecs.FormatTarget(d.Target),
                    (so, v, c) => so.FindProperty("target").enumValueIndex = (int)CsvCodecs.ParseTarget(v))
                .Col("golpes", "1, 2, o un rango 2-5.", d => CsvCodecs.FormatHits(d.MinHits, d.MaxHits), (so, v, c) =>
                {
                    var (min, max) = CsvCodecs.ParseHits(v);
                    so.FindProperty("minHits").intValue = min;
                    so.FindProperty("maxHits").intValue = max;
                })
                .Col("critico", "0 normal; 1 o más = crítico alto.", d => d.CritStage.ToString(), (so, v, c) => CsvSchema<MoveData>.SetInt(so, "critStage", v, "crítico", 0))
                .Col("dos_turnos", "no, carga o recarga.", d => CsvCodecs.FormatTwoTurn(d.TwoTurn),
                    (so, v, c) => so.FindProperty("twoTurn").enumValueIndex = (int)CsvCodecs.ParseTwoTurn(v))
                .Col("contacto", "si / no.", d => d.MakesContact ? "si" : "no", (so, v, c) => CsvSchema<MoveData>.SetBool(so, "makesContact", v, "contacto"))
                .Col("daño_especial", "Vacío = normal. fijo:40 (siempre 40 PS), nivel (PS = nivel del usuario), mitad (mitad de los PS del rival), ko (KO directo), devolver_fisico / devolver_especial (Contraataque / Manto Espejo: el doble del daño recibido; :150 = 150 %), venganza (aguanta 2 turnos y devuelve el doble), esfuerzo (deja al rival con tus PS), devolver:150 (Represión Metal: el último daño recibido ×1,5).",
                    d => CsvCodecs.FormatSpecialDamage(d.FixedDamage, d.FixedDamageAmount), (so, v, c) =>
                    {
                        var (kind, amount) = CsvCodecs.ParseSpecialDamage(v);
                        so.FindProperty("fixedDamage").intValue = (int)kind;
                        so.FindProperty("fixedDamageAmount").intValue = amount;
                    })
                .Col("respeta_inmunidad", "Solo movimientos de estado: si = no afecta a inmunes por tipo (Onda Trueno vs Tierra).",
                    d => d.RespectsTypeImmunity ? "si" : "no", (so, v, c) => CsvSchema<MoveData>.SetBool(so, "respectsTypeImmunity", v, "respeta_inmunidad"))
                .Col("potencia_mod", "Multiplicadores con condiciones: x2 [si propio.estado] | x1,5 [si clima=rain]. Vacío = ninguno.",
                    d => ReadMods(d.PowerModifiers), (so, v, c) => WriteMods(so, "powerModifiers", v))
                .Col("formula_potencia", "Opcional, reemplaza la potencia. Variables: potencia, nivel, nivel_rival, vida, vida_rival, amistad, velocidad, velocidad_rival, peso, peso_rival, seguidos, reserva, pp, subidas_rival, ps, ps_rival, azar (0-100). Funciones: min, max, pow, paso(x,t), si(c,a,b). Ej.: 150*vida/100",
                    d => d.PowerFormula ?? "", (so, v, c) =>
                    {
                        string f = (v ?? "").Trim();
                        if (f.Length > 0 && !CTEditor.GameDefinition.Domain.Formulas.MathExpression.TryParse(f, Move.PowerFormulaVariables, out _, out var err))
                            throw new CsvCellException("Fórmula con error: " + err);
                        so.FindProperty("powerFormula").stringValue = f;
                    })
                .Col("stat_ataque", "Vacío = la de su categoría. Ej.: defense (Golpe Cuerpo con Defensa), suerte (estadística inventada).",
                    d => d.AttackStat ?? "", (so, v, c) => so.FindProperty("attackStat").stringValue = (v ?? "").Trim())
                .Col("stat_defensa", "Vacío = la de su categoría. Psicocarga: defense.",
                    d => d.DefenseStat ?? "", (so, v, c) => so.FindProperty("defenseStat").stringValue = (v ?? "").Trim())
                .Col("ataca_con_rival", "si = usa la estadística de ataque DEL RIVAL (Juego Sucio).",
                    d => d.AttackStatFromTarget ? "si" : "no", (so, v, c) => CsvSchema<MoveData>.SetBool(so, "attackStatFromTarget", v, "ataca_con_rival"))
                .Col("etiquetas", "Etiquetas libres: puño|sonido|mordisco. Del motor: rompe_proteccion, ignora_etapas, ignora_inmunidad, tipo_extra:flying, eficaz_contra:water.",
                    d => CsvCodecs.JoinList(d.Tags ?? new string[0]), (so, v, c) =>
                    {
                        var tags = CsvCodecs.SplitList(v);
                        var arr = so.FindProperty("tags");
                        arr.arraySize = tags.Count;
                        for (int i = 0; i < tags.Count; i++) arr.GetArrayElementAtIndex(i).stringValue = tags[i];
                    })
                .Col("requisitos", "Solo funciona si se cumple (vacío = siempre). Ej.: rival.estado=sleep (Comesueños) · propio.estado=sleep (Ronquido). Varias con &.",
                    d => ConditionText.FormatAll((d.Requirements ?? new ConditionData[0]).Where(c => c != null).Select(ConditionText.FromData)),
                    (so, v, c) =>
                    {
                        List<Condition> conds;
                        try { conds = ConditionText.ParseAll(v); }
                        catch (FormatException e) { throw new CsvCellException(e.Message); }
                        ConditionText.WriteAll(so.FindProperty("requirements"), conds);
                    }, false)
                .Col("efectos", "estado:burn@10 | drenar:50 | retroceso:33 | retroceso_ps:25 | curar:50 | curar_rival:50 | curar_estado | clima:rain | stat:attack:-1 | stat_propio:speed:+2 | amedrentar@30 · condiciones: [si rival.vida<50] · mismo dado que el anterior: &efecto",
                    d => CsvCodecs.FormatEffects(ReadEffects(d)), WriteEffects)
                .Col("tipo_clima", "Tipo según el clima (Meteorobola): rain:water | sun:fire | hail:ice | sandstorm:rock. Vacío = siempre el suyo.",
                    d => string.Join("|", d.TypeByWeather.Where(t => t != null && t.type != null).Select(t => t.weatherId + ":" + t.type.Id)),
                    (so, v, c) =>
                    {
                        var arr = so.FindProperty("typeByWeather");
                        var pairs = CsvCodecs.SplitList(v).Select(x => x.Split(':')).Where(x => x.Length == 2).ToList();
                        var rows = new List<(string, ElementTypeData)>();
                        foreach (var pr in pairs)
                        {
                            var t = c.Find<ElementTypeData>(pr[1].Trim());
                            if (t != null) rows.Add((pr[0].Trim(), t)); else c.Warnings.Add($"El tipo '{pr[1]}' no existe.");
                        }
                        arr.arraySize = rows.Count;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            arr.GetArrayElementAtIndex(i).FindPropertyRelative("weatherId").stringValue = rows[i].Item1;
                            arr.GetArrayElementAtIndex(i).FindPropertyRelative("type").objectReferenceValue = rows[i].Item2;
                        }
                    })
                .Col("animacion", "Segundos de animación (solo presentación).", d => CsvTable.Number(d.AnimationSeconds),
                    (so, v, c) => { if (!string.IsNullOrWhiteSpace(v)) CsvSchema<MoveData>.SetFloat(so, "animationSeconds", v, "animación"); });

        private static IEnumerable<ParsedEffect> ReadEffects(MoveData d)
            => (d.SecondaryEffects ?? new MoveData.MoveEffectData[0]).Where(e => e != null).Select(e => new ParsedEffect
            {
                Kind = e.kind, Target = e.target, Chance = e.chancePercent, Status = e.statusId,
                Amount = e.amountPercent, Stat = e.statStatId, Stages = e.statStages,
                Conditions = (e.conditions ?? new ConditionData[0]).Where(c => c != null).Select(ConditionText.FromData).ToList(),
                Shared = e.sharesPreviousRoll, Weather = e.weatherId, WeatherTurns = e.weatherTurns, Hazard = e.hazardId,
                Turns = e.turns, Side = e.sideConditionId, TypeId = e.typeId, Text = e.text
            });

        private static void WriteEffects(SerializedObject so, string cell, ImportContext ctx)
        {
            var effects = CsvCodecs.ParseEffects(cell);
            var arr = so.FindProperty("secondaryEffects");
            arr.arraySize = effects.Count;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e.Kind == MoveEffectKind.InflictStatus && !ctx.Exists<StatusConditionData>(e.Status))
                    ctx.Warnings.Add($"El estado '{e.Status}' aún no existe (créalo en Estados).");
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("chancePercent").floatValue = e.Chance;
                el.FindPropertyRelative("kind").enumValueIndex = (int)e.Kind;
                el.FindPropertyRelative("target").enumValueIndex = (int)e.Target;
                el.FindPropertyRelative("statusId").stringValue = e.Status ?? "";
                el.FindPropertyRelative("amountPercent").floatValue = e.Amount;
                el.FindPropertyRelative("statStatId").stringValue = e.Stat ?? "";
                el.FindPropertyRelative("statStages").intValue = e.Stages;
                el.FindPropertyRelative("weatherId").stringValue = e.Weather ?? "";
                el.FindPropertyRelative("weatherTurns").intValue = e.WeatherTurns;
                el.FindPropertyRelative("hazardId").stringValue = e.Hazard ?? "";
                el.FindPropertyRelative("turns").intValue = e.Turns;
                el.FindPropertyRelative("sideConditionId").stringValue = e.Side ?? "";
                el.FindPropertyRelative("typeId").stringValue = e.TypeId ?? "";
                var textProp = el.FindPropertyRelative("text");
                if (textProp != null) textProp.stringValue = e.Text ?? "";
                if (e.Kind == MoveEffectKind.SetSideCondition && !ctx.Exists<SideConditionData>(e.Side))
                    ctx.Warnings.Add($"El efecto de lado '{e.Side}' aún no existe (créalo en Efectos de lado).");
                if (e.Kind == MoveEffectKind.Rampage && !string.IsNullOrEmpty(e.Status) && !ctx.Exists<StatusConditionData>(e.Status))
                    ctx.Warnings.Add($"El estado '{e.Status}' aún no existe (créalo en Estados).");
                if (e.Kind == MoveEffectKind.SetHazard && !ctx.Exists<HazardData>(e.Hazard))
                    ctx.Warnings.Add($"La trampa '{e.Hazard}' aún no existe (créala en Trampas de campo).");
                el.FindPropertyRelative("sharesPreviousRoll").boolValue = e.Shared;
                ConditionText.WriteAll(el.FindPropertyRelative("conditions"), e.Conditions);
                if (e.Kind == MoveEffectKind.SetWeather && !ctx.Exists<WeatherData>(e.Weather))
                    ctx.Warnings.Add($"El clima '{e.Weather}' aún no existe (créalo en Climas).");
            }
        }

        // ---------------- Modificadores de potencia (movimientos y habilidades) ----------------

        private static string ReadMods(PowerModifierData[] mods)
            => ConditionText.FormatModifiers((mods ?? new PowerModifierData[0]).Where(m => m != null)
                .Select(m => (m.multiplier, (IReadOnlyList<Condition>)(m.conditions ?? new ConditionData[0]).Where(c => c != null).Select(ConditionText.FromData).ToList())));

        private static void WriteMods(SerializedObject so, string field, string cell)
        {
            List<(float, Condition[])> mods;
            try { mods = ConditionText.ParseModifiers(cell); }
            catch (FormatException e) { throw new CsvCellException(e.Message); }
            ConditionText.WriteModifiers(so.FindProperty(field), mods);
        }

        // ---------------- Objetos: automático + columna de potencia equipada ----------------

        public static CsvSchema Items()
        {
            var schema = new CsvReflectiveSchema<ItemData>("objetos.csv", "Objetos", ContentFolders.Items, 35);
            schema.Col("equipado_potencia", "Ej.: x1,2 [si mov.tipo=fire]",
                d => ReadMods(d.HeldPowerModifiers), (so, v, c) => WriteMods(so, "heldPowerModifiers", v))
                  // 5.ª y 6.ª gen.: listas con condiciones.
                  .Col("equipado_stats", "Ej.: attack:x1,5 (Cinta Elección) | sp_defense:x1,5 (Chaleco Asalto) | defense:x1,5 [si propio.puede_evolucionar]",
                    d => ReadStatMods(d.HeldStatMultipliers), (so, v, c) => WriteStatMods(so, v, "heldStatMultipliers"))
                  .Col("equipado_al_recibir_golpe", "Ej.: attack:+2 [si propio.eficacia>1] | sp_attack:+2 [si propio.eficacia>1] (Seguro Debilidad)",
                    d => ReadOnHit(d.HeldOnHitStats), (so, v, c) => WriteOnHit(so, v, "heldOnHitStats"));
            return schema;
        }

        // ---------------- Habilidades: automático + columnas de potencia ----------------

        public static CsvSchema Abilities()
        {
            var schema = new CsvReflectiveSchema<AbilityData>("habilidades.csv", "Habilidades", ContentFolders.Abilities, 30);
            schema.Col("potencia_al_atacar", "Ej.: x1,5 [si mov.potencia<=60] | x1,2 [si mov.etiqueta=puño]",
                    d => ReadMods(d.OffensivePowerModifiers), (so, v, c) => WriteMods(so, "offensivePowerModifiers", v))
                  .Col("potencia_al_recibir", "Ej.: x0,5 [si mov.contacto]  ('propio' = quien tiene la habilidad)",
                    d => ReadMods(d.DefensivePowerModifiers), (so, v, c) => WriteMods(so, "defensivePowerModifiers", v))
                  // 3.ª y 4.ª gen.: listas con condiciones (no caben en el formato automático).
                  .Col("precision_mod", "Precisión de SUS movimientos. Ej.: x1,3 (Ojo Compuesto) | x0,8 [si mov.categoria=fisico] (Entusiasmo)",
                    d => ReadMods(d.AccuracyModifiers), (so, v, c) => WriteMods(so, "accuracyModifiers", v))
                  .Col("evasion_mod", "Precisión de los que le atacan. Ej.: x0,8 [si clima=sandstorm] (Velo Arena)",
                    d => ReadMods(d.EvasionModifiers), (so, v, c) => WriteMods(so, "evasionModifiers", v))
                  .Col("stats_condicionales", "Ej.: speed:x2 [si clima=sun] | attack:x0,5 [si propio.turnos_campo<5]",
                    d => ReadStatMods(d.ConditionalStats), (so, v, c) => WriteStatMods(so, v))
                  .Col("al_recibir_golpe", "Ej.: attack:+1 [si mov.tipo=dark] (Justiciero)",
                    d => ReadOnHit(d.OnHitStats), (so, v, c) => WriteOnHit(so, v));
            return schema;
        }

        // "stat:x2 [si ...] | stat:x0,5" <-> ConditionalStatData[]
        private static string ReadStatMods(AbilityData.ConditionalStatData[] list)
            => string.Join(" | ", (list ?? new AbilityData.ConditionalStatData[0]).Where(m => m != null).Select(m =>
                m.statId + ":" + ReadMods(new[] { new PowerModifierData { multiplier = m.multiplier, conditions = m.conditions } })));

        internal static void WriteStatMods(SerializedObject so, string cell, string field = "conditionalStats")
        {
            var parts = CsvCodecs.SplitList(cell);
            var prop = so.FindProperty(field);
            prop.arraySize = parts.Count;
            for (int i = 0; i < parts.Count; i++)
            {
                string raw = parts[i].Trim();
                int colon = raw.IndexOf(':');
                if (colon <= 0) throw new CsvCellException($"'{raw}': usa stat:xN [si condición] (ej. speed:x2 [si clima=sun]).");
                List<(float, Condition[])> mods;
                try { mods = ConditionText.ParseModifiers(raw.Substring(colon + 1)); }
                catch (FormatException e) { throw new CsvCellException(e.Message); }
                if (mods.Count != 1) throw new CsvCellException($"'{raw}': un multiplicador por stat.");
                var el = prop.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("statId").stringValue = raw.Substring(0, colon).Trim();
                el.FindPropertyRelative("multiplier").floatValue = mods[0].Item1;
                ConditionText.WriteAll(el.FindPropertyRelative("conditions"), mods[0].Item2);
            }
        }

        // "attack:+1 [si mov.tipo=dark]" <-> OnHitStatData[]
        private static string ReadOnHit(AbilityData.OnHitStatData[] list)
            => string.Join(" | ", (list ?? new AbilityData.OnHitStatData[0]).Where(m => m != null).Select(m =>
            {
                var conds = (m.conditions ?? new ConditionData[0]).Where(c => c != null).Select(ConditionText.FromData).ToList();
                return m.statId + ":" + (m.stages > 0 ? "+" : "") + m.stages + (conds.Count > 0 ? " [si " + ConditionText.FormatAll(conds) + "]" : "");
            }));

        internal static void WriteOnHit(SerializedObject so, string cell, string field = "onHitStats")
        {
            var parts = CsvCodecs.SplitList(cell);
            var prop = so.FindProperty(field);
            prop.arraySize = parts.Count;
            for (int i = 0; i < parts.Count; i++)
            {
                string raw = parts[i].Trim();
                var conds = new List<Condition>();
                int br = raw.IndexOf('[');
                if (br >= 0)
                {
                    int close = raw.LastIndexOf(']');
                    if (close < br) throw new CsvCellException($"'{raw}': falta cerrar el corchete ].");
                    try { conds = ConditionText.ParseAll(raw.Substring(br + 1, close - br - 1)); }
                    catch (FormatException e) { throw new CsvCellException(e.Message); }
                    raw = raw.Substring(0, br).Trim();
                }
                var p = raw.Split(':');
                if (p.Length != 2 || !CsvTable.TryInt(p[1].Replace("+", "").Trim(), out int stages))
                    throw new CsvCellException($"'{parts[i]}': usa stat:etapas [si condición] (ej. attack:+1 [si mov.tipo=dark]).");
                var el = prop.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("statId").stringValue = p[0].Trim();
                el.FindPropertyRelative("stages").intValue = stages;
                ConditionText.WriteAll(el.FindPropertyRelative("conditions"), conds);
            }
        }

        // ---------------- Especies ----------------

        public static CsvSchema<SpeciesData> Species()
            => new CsvSchema<SpeciesData>("especies.csv", "Especies", ContentFolders.Species, 80)
                .Col("id", "Id único (ej. bulbasaur).", d => d.Id, (so, v, c) => so.FindProperty("id").stringValue = v)
                .Col("nombre", "Nombre visible.", d => d.DisplayName, (so, v, c) => so.FindProperty("displayName").stringValue = v)
                .Col("tipos", "Uno o dos ids de tipo: grass|poison.", d => CsvCodecs.JoinList((d.Types ?? new ElementTypeData[0]).Where(t => t != null).Select(t => t.Id)), (so, v, c) =>
                {
                    var ids = CsvCodecs.SplitList(v);
                    if (ids.Count == 0) throw new CsvCellException("Una especie necesita al menos un tipo.");
                    var arr = so.FindProperty("types");
                    arr.arraySize = ids.Count;
                    for (int i = 0; i < ids.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = c.Require<ElementTypeData>(ids[i], "El tipo");
                })
                .Col("ps", "Estadística base.", d => d.Hp.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "hp", v, "PS", 1))
                .Col("ataque", "Estadística base.", d => d.Attack.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "attack", v, "ataque", 0))
                .Col("defensa", "Estadística base.", d => d.Defense.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "defense", v, "defensa", 0))
                .Col("atq_esp", "Estadística base.", d => d.SpAttack.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "spAttack", v, "ataque especial", 0))
                .Col("def_esp", "Estadística base.", d => d.SpDefense.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "spDefense", v, "defensa especial", 0))
                .Col("velocidad", "Estadística base.", d => d.Speed.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "speed", v, "velocidad", 0))
                .Col("habilidad", "Id de la habilidad (vacío = ninguna).", d => d.AbilityId, (so, v, c) =>
                {
                    if (!string.IsNullOrWhiteSpace(v) && !c.Exists<AbilityData>(v.Trim())) c.Warnings.Add($"La habilidad '{v}' aún no existe.");
                    so.FindProperty("abilityId").stringValue = v.Trim();
                })
                .Col("curva", "Id de la curva de XP (vacío = Media).", d => d.GrowthCurveId, (so, v, c) =>
                {
                    if (!string.IsNullOrWhiteSpace(v) && !c.Exists<GrowthCurveData>(v.Trim())) c.Warnings.Add($"La curva '{v}' aún no existe.");
                    so.FindProperty("growthCurveId").stringValue = v.Trim();
                })
                .Col("exp_base", "XP que da derrotarla (ej. 64).", d => d.BaseExpYield.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "baseExpYield", v, "exp_base", 1))
                .Col("ratio_captura", "Ratio de captura 1-255 (más alto = más fácil). Ej.: Caterpie 255, iniciales 45, legendarios 3.",
                    d => d.CatchRate.ToString(), (so, v, c) => CsvSchema<SpeciesData>.SetInt(so, "catchRate", v, "ratio de captura", 1), false, "catch_rate", "catchRate")
                .Col("evs", "EVs que da: sp_attack:1|speed:1.", d => CsvCodecs.FormatPairs((d.EvYield ?? new SpeciesData.EvYieldEntryData[0]).Select(e => (e.statId, e.amount))), (so, v, c) =>
                {
                    var pairs = CsvCodecs.ParsePairs(v, "evs");
                    var arr = so.FindProperty("evYield");
                    arr.arraySize = pairs.Count;
                    for (int i = 0; i < pairs.Count; i++)
                    {
                        arr.GetArrayElementAtIndex(i).FindPropertyRelative("statId").stringValue = pairs[i].key;
                        arr.GetArrayElementAtIndex(i).FindPropertyRelative("amount").intValue = pairs[i].value;
                    }
                })
                .Col("aprende", "Learnset nivel:movimiento: 1:tackle|3:growl|7:leech_seed.", d => CsvCodecs.FormatLearnset((d.Learnset ?? new SpeciesData.LearnableMoveEntry[0]).Where(e => e.move != null).Select(e => (e.level, e.move.Id))), (so, v, c) =>
                {
                    var entries = CsvCodecs.ParseLearnset(v);
                    var arr = so.FindProperty("learnset");
                    arr.arraySize = entries.Count;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var el = arr.GetArrayElementAtIndex(i);
                        el.FindPropertyRelative("move").objectReferenceValue = c.Require<MoveData>(entries[i].move, "El movimiento");
                        el.FindPropertyRelative("level").intValue = entries[i].level;
                    }
                })
                .Col("evoluciona", "especie@16 · especie@objeto:thunder_stone · especie@amistad(:220) · especie@intercambio(:objeto) · especie@subir. " +
                                   "Condiciones extra con +: espeon@amistad+hora:dia · hitmonlee@20+stats:atq>def · weavile@subir+lleva:razor_claw+hora:noche. Varias con |.",
                    d => CsvCodecs.FormatEvolutions((d.Evolutions ?? new SpeciesData.EvolutionEntry[0]).Where(e => e.target != null).Select(e =>
                        new CsvCodecs.ParsedEvolution
                        {
                            Species = e.target.Id, Method = e.method, Level = e.requiredLevel, ItemId = e.itemId ?? "", Friendship = e.minFriendship,
                            Conditions = (e.conditions ?? new SpeciesData.EvolutionConditionData[0]).Where(c => c != null).Select(c => new CsvCodecs.ParsedCondition
                                { Kind = c.check, Value = c.value, Id = c.IdFor(), Time = c.time, Relation = c.relation, Negate = c.negate }).ToList(),
                        })),
                    (so, v, c) =>
                    {
                        var entries = CsvCodecs.ParseEvolutions(v);
                        var arr = so.FindProperty("evolutions");
                        arr.arraySize = entries.Count;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var e = entries[i];
                            var el = arr.GetArrayElementAtIndex(i);
                            el.FindPropertyRelative("target").objectReferenceValue = c.Require<SpeciesData>(e.Species, "La especie");
                            el.FindPropertyRelative("requiredLevel").intValue = e.Level;
                            el.FindPropertyRelative("method").intValue = (int)e.Method;
                            el.FindPropertyRelative("itemId").stringValue = e.ItemId ?? "";
                            el.FindPropertyRelative("minFriendship").intValue = e.Friendship;
                            if (!string.IsNullOrEmpty(e.ItemId) && !c.Exists<ItemData>(e.ItemId))
                                c.Warnings.Add($"El objeto '{e.ItemId}' aún no existe (créalo en Objetos).");
                            var conds = el.FindPropertyRelative("conditions");
                            conds.arraySize = e.Conditions.Count;
                            for (int k = 0; k < e.Conditions.Count; k++)
                                WriteCondition(conds.GetArrayElementAtIndex(k), e.Conditions[k], c);
                        }
                    }, deferred: true)
                .Col("stats_extra", "Estadísticas inventadas: suerte:50|carisma:30.", d => CsvCodecs.FormatPairs((d.CustomStats ?? new SpeciesData.CustomStatValue[0]).Select(s => (s.statId, s.value))), (so, v, c) =>
                {
                    var pairs = CsvCodecs.ParsePairs(v, "stats_extra");
                    var arr = so.FindProperty("customStats");
                    arr.arraySize = pairs.Count;
                    for (int i = 0; i < pairs.Count; i++)
                    {
                        arr.GetArrayElementAtIndex(i).FindPropertyRelative("statId").stringValue = pairs[i].key;
                        arr.GetArrayElementAtIndex(i).FindPropertyRelative("value").intValue = pairs[i].value;
                    }
                })
                // --- Pokédex (columnas opcionales: los archivos antiguos sin ellas se importan igual) ---
                .Col("numero", "Número de Pokédex (0 = sin número).", d => d.DexNumber.ToString(),
                    (so, v, c) => { if (!string.IsNullOrWhiteSpace(v)) CsvSchema<SpeciesData>.SetInt(so, "dexNumber", v, "número", 0); })
                .Col("categoria", "Categoría: Semilla (se ve «Pokémon Semilla»).", d => d.Category, (so, v, c) => so.FindProperty("category").stringValue = (v ?? "").Trim())
                .Col("altura", "Altura en metros (0,7).", d => CsvTable.Number(d.HeightM),
                    (so, v, c) => { if (!string.IsNullOrWhiteSpace(v)) CsvSchema<SpeciesData>.SetFloat(so, "heightM", v, "altura"); })
                .Col("peso", "Peso en kilos (6,9).", d => CsvTable.Number(d.WeightKg),
                    (so, v, c) => { if (!string.IsNullOrWhiteSpace(v)) CsvSchema<SpeciesData>.SetFloat(so, "weightKg", v, "peso"); })
                .Col("color", "Color principal: verde, rojo, azul...", d => d.DexColor, (so, v, c) => so.FindProperty("dexColor").stringValue = (v ?? "").Trim())
                .Col("hembras", "% de hembras (0-100) o sin_genero.", d => d.FemalePercent < 0 ? "sin_genero" : CsvTable.Number(d.FemalePercent),
                    (so, v, c) =>
                    {
                        string t = (v ?? "").Trim().ToLowerInvariant();
                        if (t.Length == 0) return;
                        if (t.StartsWith("sin")) { so.FindProperty("femalePercent").floatValue = -1f; return; }
                        CsvSchema<SpeciesData>.SetFloat(so, "femalePercent", v, "hembras");
                    })
                .Col("legendario", "si / no.", d => d.Legendary ? "si" : "no",
                    (so, v, c) => { if (!string.IsNullOrWhiteSpace(v)) CsvSchema<SpeciesData>.SetBool(so, "legendary", v, "legendario"); })
                .Col("descripcion", "Texto de la Pokédex.", d => d.DexDescription, (so, v, c) => so.FindProperty("dexDescription").stringValue = (v ?? "").Trim())
                .Col("habilidad_2", "Segunda habilidad (vacío = solo una).", d => d.SecondAbilityId, (so, v, c) => SetAbility(so, "secondAbilityId", v, c))
                .Col("habilidad_oculta", "Habilidad oculta (vacío = ninguna).", d => d.HiddenAbilityId, (so, v, c) => SetAbility(so, "hiddenAbilityId", v, c))
                .Col("mt", "Movimientos por MT/MO separados por |. Ej.: toxic|hidden_power|protect", d => MoveList(d.MachineMoves),
                    (so, v, c) => SetMoveList(so, "machineMoves", v, c))
                .Col("tutor", "Movimientos de tutor separados por |.", d => MoveList(d.TutorMoves), (so, v, c) => SetMoveList(so, "tutorMoves", v, c))
                .Col("huevo", "Movimientos huevo separados por |.", d => MoveList(d.EggMoves), (so, v, c) => SetMoveList(so, "eggMoves", v, c))
                .Col("grupos_huevo", "Hasta 2 grupos huevo separados por |. Ej.: monster|plant", d => string.Join("|", (d.EggGroups ?? new EggGroupData[0]).Where(g => g != null).Select(g => g.Id)),
                    (so, v, c) =>
                    {
                        var ids = CsvCodecs.SplitList(v);
                        var arr = so.FindProperty("eggGroups");
                        var found = new List<EggGroupData>();
                        foreach (var id in ids)
                        {
                            var g = c.Find<EggGroupData>(id);
                            if (g != null) found.Add(g);
                            else if (!c.Exists<EggGroupData>(id)) c.Warnings.Add($"El grupo huevo '{id}' no existe (créalos en Criaturas → Grupos huevo).");
                        }
                        arr.arraySize = found.Count;
                        for (int i = 0; i < found.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
                    }, true);

        private static void SetAbility(SerializedObject so, string field, string v, ImportContext c)
        {
            v = (v ?? "").Trim();
            if (v.Length > 0 && !c.Exists<AbilityData>(v)) c.Warnings.Add($"La habilidad '{v}' aún no existe.");
            so.FindProperty(field).stringValue = v;
        }

        private static string MoveList(MoveData[] moves)
            => string.Join("|", (moves ?? new MoveData[0]).Where(m => m != null).Select(m => m.Id));

        private static void SetMoveList(SerializedObject so, string field, string v, ImportContext c)
        {
            var ids = CsvCodecs.SplitList(v).Distinct().ToList();
            var arr = so.FindProperty(field);
            var list = new List<MoveData>();
            foreach (var id in ids)
            {
                var m = c.Find<MoveData>(id);
                if (m != null) list.Add(m);
                else if (!c.Exists<MoveData>(id)) c.Warnings.Add($"El movimiento '{id}' no existe: se omitió de '{field}'.");
            }
            arr.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
        }

        // ---------------- Entrenadores, zonas y equipos prearmados ----------------

        private static string TeamText(TeamMemberData[] team)
            // Por IDS (referencia viva o id de respaldo): un miembro con la referencia rota NO se pierde al exportar.
            => CsvTeamCodecs.FormatTeam((team ?? new TeamMemberData[0]).Where(m => m != null && m.SpeciesKey.Length > 0).Select(m => new CsvTeamCodecs.ParsedMember
            {
                Species = m.SpeciesKey, Level = m.level, Held = m.heldItem ?? "", Nature = m.NatureKey,
                Iv = m.fixedIvs, Nickname = m.nickname ?? "",
                Gender = m.gender == MemberGender.Male ? "m" : m.gender == MemberGender.Female ? "h" : "",
                Moves = m.MoveKeys().ToList(),
            }));

        private static void SetTeam(SerializedObject so, string field, string cell, ImportContext c)
        {
            var members = CsvTeamCodecs.ParseTeam(cell);
            var arr = so.FindProperty(field);
            arr.arraySize = members.Count;
            for (int i = 0; i < members.Count; i++)
            {
                var p = members[i];
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("species").objectReferenceValue = c.Require<SpeciesData>(p.Species, "La especie");
                el.FindPropertyRelative("speciesId").stringValue = p.Species;
                el.FindPropertyRelative("level").intValue = p.Level;
                var moves = el.FindPropertyRelative("moves");
                var moveIds = el.FindPropertyRelative("moveIds");
                moves.arraySize = p.Moves.Count;
                moveIds.arraySize = p.Moves.Count;
                for (int k = 0; k < p.Moves.Count; k++)
                {
                    moves.GetArrayElementAtIndex(k).objectReferenceValue = c.Require<MoveData>(p.Moves[k], "El movimiento");
                    moveIds.GetArrayElementAtIndex(k).stringValue = p.Moves[k];
                }
                if (p.Held.Length > 0 && !c.Exists<ItemData>(p.Held)) c.Warnings.Add($"El objeto '{p.Held}' aún no existe.");
                el.FindPropertyRelative("heldItem").stringValue = p.Held;
                el.FindPropertyRelative("nature").objectReferenceValue = p.Nature.Length > 0 ? c.Require<NatureData>(p.Nature, "La naturaleza") : null;
                el.FindPropertyRelative("natureId").stringValue = p.Nature;
                el.FindPropertyRelative("fixedIvs").intValue = p.Iv < 0 ? -1 : System.Math.Min(31, p.Iv);
                el.FindPropertyRelative("nickname").stringValue = p.Nickname;
                el.FindPropertyRelative("gender").enumValueIndex = p.Gender == "m" ? (int)MemberGender.Male : p.Gender == "h" ? (int)MemberGender.Female : 0;
            }
        }

        // Escribe una condición de evolución leída del Excel, buscando lo que nombra (movimiento, tipo...).
        private static void WriteCondition(SerializedProperty el, CsvCodecs.ParsedCondition p, ImportContext c)
        {
            EvolutionText.Reset(el);
            el.FindPropertyRelative("check").intValue = (int)p.Kind;
            el.FindPropertyRelative("value").intValue = p.Value;
            el.FindPropertyRelative("time").intValue = (int)p.Time;
            el.FindPropertyRelative("relation").intValue = (int)p.Relation;
            el.FindPropertyRelative("negate").boolValue = p.Negate;
            switch (p.Kind)
            {
                case Domain.Species.EvolutionConditionKind.HoldsItem:
                    el.FindPropertyRelative("itemId").stringValue = p.Id;
                    if (!c.Exists<ItemData>(p.Id)) c.Warnings.Add($"El objeto '{p.Id}' aún no existe (créalo en Objetos).");
                    break;
                case Domain.Species.EvolutionConditionKind.KnowsMove:
                    el.FindPropertyRelative("move").objectReferenceValue = c.Require<MoveData>(p.Id, "El movimiento"); break;
                case Domain.Species.EvolutionConditionKind.KnowsMoveOfType:
                case Domain.Species.EvolutionConditionKind.PartyHasType:
                    el.FindPropertyRelative("type").objectReferenceValue = c.Require<ElementTypeData>(p.Id, "El tipo"); break;
                case Domain.Species.EvolutionConditionKind.PartyHasSpecies:
                    el.FindPropertyRelative("species").objectReferenceValue = c.Require<SpeciesData>(p.Id, "La especie"); break;
                case Domain.Species.EvolutionConditionKind.Nature:
                    el.FindPropertyRelative("nature").objectReferenceValue = c.Require<NatureData>(p.Id, "La naturaleza"); break;
                case Domain.Species.EvolutionConditionKind.MapWeather:
                    el.FindPropertyRelative("weatherId").stringValue = p.Id;
                    if (!c.Exists<WeatherData>(p.Id)) c.Warnings.Add($"El clima '{p.Id}' aún no existe (créalo en Climas).");
                    break;
                case Domain.Species.EvolutionConditionKind.AtLocation:
                case Domain.Species.EvolutionConditionKind.GameFlag:
                    el.FindPropertyRelative("text").stringValue = p.Id; break;
            }
        }

        private const string TeamHelp = "Miembros separados por |. especie@nivel y, opcional: %m / %h (macho/hembra) [mov1/mov2] {objeto} ~naturaleza #iv \"mote\". Ej.: pidgey@5%h | onix@14[tackle/rock_throw]{oran_berry}";

        public static CsvSchema<TrainerData> Trainers()
            => new CsvSchema<TrainerData>("entrenadores.csv", "Entrenadores", ContentFolders.Trainers, 90)
                .Col("id", "Id único (ej. lider_brock).", d => d.Id, (so, v, c) => so.FindProperty("id").stringValue = v)
                .Col("nombre", "Nombre del entrenador.", d => d.DisplayName, (so, v, c) => so.FindProperty("displayName").stringValue = v)
                .Col("clase", "Clase (Joven, Cazabichos, Líder de gimnasio...).", d => d.TrainerClass, (so, v, c) => so.FindProperty("trainerClass").stringValue = v)
                .Col("ia", "Nivel de IA: novato, listo o experto.", d => d.Ai == Domain.Trainers.TrainerAi.Expert ? "experto"
                        : d.Ai == Domain.Trainers.TrainerAi.Smart ? "listo" : "novato", (so, v, c) =>
                {
                    string s = (v ?? "").Trim().ToLowerInvariant();
                    so.FindProperty("ai").enumValueIndex =
                        s == "" || s.StartsWith("list") || s.StartsWith("inteligente") || s == "smart" ? 1
                        : s.StartsWith("exp") ? 2
                        : s.StartsWith("nov") || s.StartsWith("al") || s == "random" || s == "azar" ? 0
                        : throw new CsvCellException($"'{v}' no es una IA válida: usa novato, listo o experto.");
                })
                .Col("nivel_ia", "Nivel de IA 1-7: 1 novato, 2 aficionado, 3 veterano, 4 élite, 5 campeón, 6 maestro, 7 injusto. " +
                                 "O el id de una IA PERSONALIZADA (ia_brock...). Vacío = según «ia».",
                    d => !string.IsNullOrWhiteSpace(d.AiProfileId) ? d.AiProfileId.Trim() : d.AiLevel > 0 ? d.AiLevel.ToString() : "", (so, v, c) =>
                    {
                        string raw = (v ?? "").Trim(), t = raw.ToLowerInvariant();
                        // ¿Es el id de una IA (personalizada)? Manda sobre el nivel; el nivel se toma de esa IA.
                        if (t.Length > 0 && !CsvTable.TryInt(t, out _) && c.Exists<AiLevelData>(raw))
                        {
                            var ai = c.Find<AiLevelData>(raw);
                            so.FindProperty("aiProfileId").stringValue = raw;
                            if (ai != null) so.FindProperty("aiLevel").intValue = ai.Level;
                            return;
                        }
                        int lvl = t == "" ? 0 : t.StartsWith("nov") ? 1 : t.StartsWith("afi") ? 2 : t.StartsWith("vet") ? 3 : t.StartsWith("él") || t.StartsWith("el") ? 4
                                : t.StartsWith("cam") ? 5 : t.StartsWith("mae") ? 6 : t.StartsWith("inj") ? 7
                                : CsvTable.TryInt(t, out int n) && n >= 0 && n <= 7 ? n
                                : throw new CsvCellException($"'{v}' no es un nivel de IA: usa 1-7, novato … injusto, o el id de una IA personalizada.");
                        so.FindProperty("aiLevel").intValue = lvl;
                        so.FindProperty("aiProfileId").stringValue = "";
                    })
                .Col("usa_objetos", "si / no: ¿usa los objetos de su mochila?", d => d.UseItems ? "si" : "no",
                    (so, v, c) => CsvSchema<TrainerData>.SetBool(so, "useItems", v, "usa_objetos"))
                .Col("mochila", "objeto:cantidad separados por |. Ej.: hyper_potion:2 | full_heal:1",
                    d => CsvCodecs.FormatPairs((d.Items ?? new BagEntryData[0]).Where(i => i != null && !string.IsNullOrWhiteSpace(i.itemId)).Select(i => (i.itemId, i.quantity))),
                    (so, v, c) =>
                    {
                        var pairs = CsvCodecs.ParsePairs(v, "la mochila");
                        var arr = so.FindProperty("items");
                        arr.arraySize = pairs.Count;
                        for (int i = 0; i < pairs.Count; i++)
                        {
                            if (!c.Exists<ItemData>(pairs[i].key)) c.Warnings.Add($"El objeto '{pairs[i].key}' aún no existe.");
                            arr.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue = pairs[i].key;
                            arr.GetArrayElementAtIndex(i).FindPropertyRelative("quantity").intValue = System.Math.Max(1, pairs[i].value);
                        }
                    })
                .Col("curar_bajo", "Se cura con este % de PS o menos (1-100). Clásico: 25.", d => d.HealBelowPercent.ToString(),
                    (so, v, c) => CsvSchema<TrainerData>.SetInt(so, "healBelowPercent", v, "curar_bajo", 1))
                .Col("puede_cambiar", "si / no: ¿puede cambiar de monstruo? (solo la IA experta lo hace).", d => d.CanSwitch ? "si" : "no",
                    (so, v, c) => CsvSchema<TrainerData>.SetBool(so, "canSwitch", v, "puede_cambiar"))
                .Col("movimientos_auto", "Miembros sin movimientos escritos: ia (según su IA), clasico (4 últimos), equilibrado, fuerte o competitivo.",
                    d => d.MovesetStyle == Domain.Trainers.MovesetStyle.Classic ? "clasico" : d.MovesetStyle == Domain.Trainers.MovesetStyle.Balanced ? "equilibrado"
                       : d.MovesetStyle == Domain.Trainers.MovesetStyle.Strong ? "fuerte"
                       : d.MovesetStyle == Domain.Trainers.MovesetStyle.Competitive ? "competitivo" : "ia",
                    (so, v, c) =>
                    {
                        string t = (v ?? "").Trim().ToLowerInvariant();
                        so.FindProperty("movesetStyle").enumValueIndex =
                            t == "" || t == "ia" || t.StartsWith("seg") ? 0 : t.StartsWith("cl") ? 1 : t.StartsWith("eq") ? 2 : t.StartsWith("fu") || t.StartsWith("dif") ? 3
                            : t.StartsWith("comp") ? 4
                            : throw new CsvCellException($"'{v}' no vale: usa ia, clasico, equilibrado, fuerte o competitivo.");
                    })
                .Col("dinero_base", "Premio = este valor × nivel de su último monstruo.", d => d.BaseMoney.ToString(), (so, v, c) => CsvSchema<TrainerData>.SetInt(so, "baseMoney", v, "dinero base", 0))
                .Col("equipo", TeamHelp, d => TeamText(d.Team), (so, v, c) => SetTeam(so, "team", v, c))
                .Col("frase_inicio", "Lo que dice al empezar.", d => d.IntroLine, (so, v, c) => so.FindProperty("introLine").stringValue = v)
                .Col("frase_derrota", "Lo que dice al perder.", d => d.DefeatLine, (so, v, c) => so.FindProperty("defeatLine").stringValue = v)
                .Col("frase_victoria", "Lo que dice si gana.", d => d.VictoryLine, (so, v, c) => so.FindProperty("victoryLine").stringValue = v);

        public static CsvSchema<EncounterZoneData> Zones()
            => new CsvSchema<EncounterZoneData>("zonas.csv", "Zonas salvajes", ContentFolders.Encounters, 92)
                .Col("id", "Id único (ej. ruta_1).", d => d.Id, (so, v, c) => so.FindProperty("id").stringValue = v)
                .Col("nombre", "Nombre visible.", d => d.DisplayName, (so, v, c) => so.FindProperty("displayName").stringValue = v)
                .Col("especies", "especie@min-max:frecuencia separadas por |. Ej.: pidgey@2-5:50 | rattata@2-4:50",
                    d => CsvTeamCodecs.FormatZone((d.Entries ?? new EncounterEntryData[0]).Where(e => e != null && e.SpeciesKey.Length > 0)
                        .Select(e => (e.SpeciesKey, e.minLevel, e.maxLevel, e.weight))),
                    (so, v, c) =>
                    {
                        var entries = CsvTeamCodecs.ParseZone(v);
                        var arr = so.FindProperty("entries");
                        arr.arraySize = entries.Count;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var el = arr.GetArrayElementAtIndex(i);
                            el.FindPropertyRelative("species").objectReferenceValue = c.Require<SpeciesData>(entries[i].species, "La especie");
                            el.FindPropertyRelative("speciesId").stringValue = entries[i].species;
                            el.FindPropertyRelative("minLevel").intValue = System.Math.Max(1, entries[i].min);
                            el.FindPropertyRelative("maxLevel").intValue = System.Math.Max(1, entries[i].max);
                            el.FindPropertyRelative("weight").intValue = System.Math.Max(1, entries[i].weight);
                        }
                    });

        public static CsvSchema<TeamPresetData> TeamPresets()
            => new CsvSchema<TeamPresetData>("equipos.csv", "Equipos prearmados", ContentFolders.Teams, 95)
                .Col("id", "Id único (ej. equipo_prueba_50).", d => d.Id, (so, v, c) => so.FindProperty("id").stringValue = v)
                .Col("nombre", "Nombre visible.", d => d.DisplayName, (so, v, c) => so.FindProperty("displayName").stringValue = v)
                .Col("descripcion", "Para qué sirve.", d => d.Description, (so, v, c) => so.FindProperty("description").stringValue = v)
                .Col("dinero", "Dinero inicial (-1 = el de las reglas).", d => d.Money.ToString(), (so, v, c) => CsvSchema<TeamPresetData>.SetInt(so, "money", v, "dinero", -1))
                .Col("equipo", TeamHelp, d => TeamText(d.Members), (so, v, c) => SetTeam(so, "members", v, c))
                .Col("mochila", "objeto:cantidad separados por |. Ej.: potion:5 | poke_ball:10",
                    d => CsvCodecs.FormatPairs((d.Items ?? new BagEntryData[0]).Where(i => i != null && !string.IsNullOrWhiteSpace(i.itemId)).Select(i => (i.itemId, i.quantity))),
                    (so, v, c) =>
                    {
                        var pairs = CsvCodecs.ParsePairs(v, "la mochila");
                        var arr = so.FindProperty("items");
                        arr.arraySize = pairs.Count;
                        for (int i = 0; i < pairs.Count; i++)
                        {
                            if (!c.Exists<ItemData>(pairs[i].key)) c.Warnings.Add($"El objeto '{pairs[i].key}' aún no existe.");
                            arr.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue = pairs[i].key;
                            arr.GetArrayElementAtIndex(i).FindPropertyRelative("quantity").intValue = System.Math.Max(1, pairs[i].value);
                        }
                    });
    }

    /// <summary>
    /// La TABLA DE TIPOS como matriz de Excel: primera columna = tipo que ATACA, cabecera = tipo que
    /// DEFIENDE, celdas = multiplicador (vacío = ×1). Es la forma más cómoda de verla y editarla.
    /// </summary>
    public sealed class TypeChartCsvSchema : CsvSchema
    {
        private const string Corner = "ataca\\defiende";
        public override string FileName => "tabla_tipos.csv";
        public override string Title => "Tabla de tipos";
        public override int Order => 15;
        public override IEnumerable<(string header, string help)> ColumnHelp => new[]
        {
            (Corner, "Primera columna: el tipo que ATACA. Cabecera: los tipos que DEFIENDEN."),
            ("(celdas)", "2 = muy eficaz, 0,5 = poco eficaz, 0 = inmune, vacío = normal (×1)."),
        };

        public override CsvTable Export()
        {
            var types = ContentAssets.LoadAll<ElementTypeData>().Where(t => !string.IsNullOrEmpty(t.Id)).OrderBy(Classic).ToList();
            var map = TypeChartTools.ReadAll(TypeChartTools.FindChart());
            var table = new CsvTable(new[] { Corner }.Concat(types.Select(t => t.Id)));
            foreach (var atk in types)
            {
                var row = new Dictionary<string, string> { [Corner] = atk.Id };
                foreach (var def in types)
                    row[def.Id] = map.TryGetValue((atk, def), out var m) ? CsvTable.Number(m) : "";
                table.AddRow(row);
            }
            return table;
        }

        private static int Classic(ElementTypeData t)
        {
            for (int i = 0; i < TypeChartTools.ClassicTypes.Length; i++) if (TypeChartTools.ClassicTypes[i].id == t.Id) return i;
            return 1000;
        }

        public override void RegisterPlanned(CsvTable table, ImportContext ctx) { }

        public override List<RowPlan> Plan(CsvTable table, ImportContext ctx, ImportMode mode)
        {
            var plans = new List<RowPlan>();
            var map = TypeChartTools.ReadAll(TypeChartTools.FindChart());
            string first = table.Headers.Count > 0 ? table.Headers[0] : Corner;

            for (int r = 0; r < table.Rows.Count; r++)
            {
                var cells = table.Rows[r];
                var plan = new RowPlan { Line = table.LineNumbers[r], Cells = cells, Id = cells.TryGetValue(first, out var a) ? a.Trim() : "" };
                plans.Add(plan);
                if (!ctx.Exists<ElementTypeData>(plan.Id)) { plan.Errors.Add($"El tipo atacante '{plan.Id}' no existe."); continue; }
                var atk = ctx.Find<ElementTypeData>(plan.Id);

                foreach (var h in table.Headers.Skip(1))
                {
                    if (!ctx.Exists<ElementTypeData>(h)) { plan.Errors.Add($"El tipo defensor '{h}' (cabecera) no existe."); continue; }
                    string cell = cells.TryGetValue(h, out var v) ? v.Trim() : "";
                    float after = 1f;
                    if (cell.Length > 0 && !CsvTable.TryNumber(cell, out after)) { plan.Errors.Add($"[{h}] '{cell}' no es un número."); continue; }
                    var def = ctx.Find<ElementTypeData>(h);
                    bool written = atk != null && def != null && map.TryGetValue((atk, def), out _);
                    // «Solo lo que falta»: las casillas que ya tienes escritas no se tocan (solo se rellenan las vacías).
                    if (written && mode == ImportMode.CreateOnly) continue;
                    float before = written ? map[(atk, def)] : 1f;
                    if (Math.Abs(before - after) > 1e-4) plan.Changes.Add((h, CsvTable.Number(before), CsvTable.Number(after)));
                }
            }
            return plans;
        }

        public override int Apply(List<RowPlan> plan, ImportContext ctx)
        {
            var chart = TypeChartTools.GetOrCreateChart();
            int changed = 0;
            foreach (var p in plan.Where(p => p.Errors.Count == 0))
            {
                var atk = ctx.Find<ElementTypeData>(p.Id);
                foreach (var (column, _, after) in p.Changes)
                {
                    var def = ctx.Find<ElementTypeData>(column);
                    if (atk == null || def == null || !CsvTable.TryNumber(after, out float v)) continue;
                    TypeChartTools.Set(chart, atk, def, v);
                    changed++;
                }
            }
            return changed;
        }

    }
}
