using System.Collections.Generic;
using UnityEditor;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Los estados clásicos (8 principales/volátiles de siempre + 4 volátiles del Lote B + 23 de la 2.ª-4.ª gen.) como plantillas reutilizables: los usa la ventana de Estados (botón por
    /// plantilla y "crear todos") y el Centro de Contenido. Los ids coinciden con los que usan las
    /// habilidades clásicas (Insomnio → "sleep", Electricidad Estática → "paralysis"...).
    /// </summary>
    public static class ClassicStatusPresets
    {
        public static readonly (string id, string name)[] All =
        {
            ("burn", "Quemado"), ("poison", "Veneno"), ("toxic", "Tóxico"), ("paralysis", "Parálisis"),
            ("sleep", "Dormido"), ("freeze", "Congelado"), ("confusion", "Confusión"), ("drowsy", "Somnoliento"),
            // Lote B: volátiles que se SUMAN al estado principal.
            ("trapped", "Atrapado"), ("leech_seed", "Drenadoras"), ("protect", "Protegido"), ("endure", "Aguante"),
            // Lote E: volátiles de la 2.ª, 3.ª y 4.ª generación (los usan los movimientos del pack 1-4).
            ("infatuation", "Enamorado"), ("taunt", "Mofa"), ("torment", "Tormento"), ("heal_block", "Anticura"),
            ("embargo", "Embargo"), ("gastro_acid", "Bilis"), ("nightmare", "Pesadilla"), ("curse", "Maldición"),
            ("perish_song", "Canto Mortal"), ("destiny_bond", "Mismo Destino"), ("grudge", "Rabia"),
            ("magic_coat", "Capa Mágica"), ("snatch", "Robo"), ("charge", "Carga"), ("magnet_rise", "Levitón"),
            ("roost", "Respiro"), ("identified", "Identificado"), ("miracle_eye", "Gran Ojo"), ("lock_on", "Fijar Blanco"),
            ("imprison", "Cerca"), ("ingrain", "Arraigo"), ("aqua_ring", "Acua Aro"), ("cant_escape", "Sin escapatoria"),
            // Lote F: 5.ª y 6.ª generación.
            ("kings_shield", "Escudo Real"), ("spiky_shield", "Barrera Espinosa"), ("smacked_down", "Derribado"), ("fairy_lock", "Cerrojo Feérico"),
            // 7.ª generación.
            ("baneful_bunker", "Búnker"),
        };

        // 5.ª y 6.ª gen.: protecciones con castigo y otros volátiles nuevos.
        private static bool FillGen6(SerializedObject so, string kind)
        {
            string name;
            switch (kind)
            {
                case "kings_shield": name = "Escudo Real"; break;
                case "spiky_shield": name = "Barrera Espinosa"; break;
                case "smacked_down": name = "Derribado"; break;
                case "fairy_lock": name = "Cerrojo Feérico"; break;
                case "baneful_bunker": name = "Búnker"; break;
                default: return false;
            }
            // Base: un volátil «vacío» (se reutiliza el relleno de la 4.ª gen. con Sin escapatoria y se ajusta).
            FillGen4(so, "cant_escape");
            void B(string field, bool v) { var p = so.FindProperty(field); if (p != null) p.boolValue = v; }
            void I(string field, int v) { var p = so.FindProperty(field); if (p != null) p.intValue = v; }
            void F(string field, float v) { var p = so.FindProperty(field); if (p != null) p.floatValue = v; }
            void S(string field, string v) { var p = so.FindProperty(field); if (p != null) p.stringValue = v; }
            S("displayName", name);
            B("preventsSwitch", kind == "fairy_lock");
            I("durationTurns", kind == "fairy_lock" ? 2 : kind == "smacked_down" ? 0 : 1);
            bool shield = kind == "kings_shield" || kind == "spiky_shield" || kind == "baneful_bunker";
            B("blocksIncomingMoves", shield); B("harderWhenRepeated", shield);
            // Escudo Real: solo para ataques con daño; quien le toca pierde 2 de Ataque. Barrera Espinosa: 1/8 de PS.
            B("protectOnlyDamaging", kind == "kings_shield");
            S("protectContactStat", kind == "kings_shield" ? "attack" : "");
            I("protectContactStages", kind == "kings_shield" ? -2 : 0);
            F("protectContactDamagePercent", kind == "spiky_shield" ? 12.5f : 0f);
            S("protectContactStatus", kind == "baneful_bunker" ? "poison" : "");   // Búnker: envenena a quien le toca
            B("grounded", kind == "smacked_down");   // Antiaéreo / Mil Flechas: le afecta Tierra aunque vuele
            return true;
        }

        // Pone a cero los campos de la 5.ª-6.ª gen. (una plantilla no hereda nada).
        private static void ResetGen6(SerializedObject so)
        {
            var a = so.FindProperty("protectOnlyDamaging"); if (a != null) a.boolValue = false;
            var b = so.FindProperty("protectContactStat"); if (b != null) b.stringValue = "";
            var c = so.FindProperty("protectContactStages"); if (c != null) c.intValue = 0;
            var d = so.FindProperty("protectContactDamagePercent"); if (d != null) d.floatValue = 0f;
            var e = so.FindProperty("protectContactStatus"); if (e != null) e.stringValue = "";
        }

        /// <summary>
        /// Plantillas de los volátiles de la 3.ª y 4.ª gen.: (nombre, duración, duración máx., % impide actuar,
        /// % daño por turno, el residual cura, impide cambiar, y qué comportamiento especial activa).
        /// </summary>
        private static readonly Dictionary<string, (string name, int dur, float prevent, float residual, bool heals, bool noSwitch, string[] flags, string type, string[] types)> Gen4 =
            new Dictionary<string, (string, int, float, float, bool, bool, string[], string, string[])>
        {
            ["infatuation"] = ("Enamorado", 0, 50f, 0f, false, false, new[] { "requiresOppositeGender" }, null, null),
            ["taunt"]       = ("Mofa", 3, 0f, 0f, false, false, new[] { "blocksStatusMoves" }, null, null),
            ["torment"]     = ("Tormento", 0, 0f, 0f, false, false, new[] { "blocksRepeatedMove" }, null, null),
            ["heal_block"]  = ("Anticura", 5, 0f, 0f, false, false, new[] { "blocksHealing" }, null, null),
            ["embargo"]     = ("Embargo", 5, 0f, 0f, false, false, new[] { "blocksItems" }, null, null),
            ["gastro_acid"] = ("Bilis", 0, 0f, 0f, false, false, new[] { "suppressesAbility" }, null, null),
            ["nightmare"]   = ("Pesadilla", 0, 0f, 25f, false, false, new string[0], null, null),
            ["curse"]       = ("Maldición", 0, 0f, 25f, false, false, new string[0], null, null),
            ["perish_song"] = ("Canto Mortal", 4, 0f, 0f, false, false, new[] { "faintsWhenEnds" }, null, null),
            ["destiny_bond"]= ("Mismo Destino", 2, 0f, 0f, false, false, new[] { "destinyBond" }, null, null),
            ["grudge"]      = ("Rabia", 2, 0f, 0f, false, false, new[] { "grudge" }, null, null),
            ["magic_coat"]  = ("Capa Mágica", 1, 0f, 0f, false, false, new[] { "reflectsStatusMoves" }, null, null),
            ["snatch"]      = ("Robo", 1, 0f, 0f, false, false, new[] { "stealsBoostMoves" }, null, null),
            ["charge"]      = ("Carga", 2, 0f, 0f, false, false, new[] { "boostConsumed" }, "boost:electric", null),
            ["magnet_rise"] = ("Levitón", 5, 0f, 0f, false, false, new string[0], "immune", new[] { "ground" }),
            ["roost"]       = ("Respiro", 1, 0f, 0f, false, false, new string[0], "lose:flying", null),
            ["identified"]  = ("Identificado", 0, 0f, 0f, false, false, new[] { "identified" }, "hittable", new[] { "normal", "fighting" }),
            ["miracle_eye"] = ("Gran Ojo", 0, 0f, 0f, false, false, new[] { "identified" }, "hittable", new[] { "psychic" }),
            ["lock_on"]     = ("Fijar Blanco", 2, 0f, 0f, false, false, new[] { "sureHit" }, null, null),
            ["imprison"]    = ("Cerca", 0, 0f, 0f, false, false, new[] { "imprisons" }, null, null),
            ["ingrain"]     = ("Arraigo", 0, 0f, 6.25f, true, true, new[] { "grounded" }, null, null),
            ["aqua_ring"]   = ("Acua Aro", 0, 0f, 6.25f, true, false, new string[0], null, null),
            ["cant_escape"] = ("Sin escapatoria", 0, 0f, 0f, false, true, new string[0], null, null),
        };

        private static readonly string[] Gen4Flags =
        {
            "blocksStatusMoves", "blocksRepeatedMove", "blocksHealing", "blocksItems", "suppressesAbility", "faintsWhenEnds",
            "destinyBond", "grudge", "reflectsStatusMoves", "stealsBoostMoves", "boostConsumed", "identified", "sureHit",
            "imprisons", "grounded", "requiresOppositeGender",
        };

        private static ElementTypeData[] TypesById(IEnumerable<string> ids)
        {
            var found = new List<ElementTypeData>();
            foreach (var id in ids ?? new string[0])
            {
                var t = ContentAssets.FindById<ElementTypeData>(id);
                if (t != null) found.Add(t);
            }
            return found.ToArray();
        }

        private static void SetRefs(SerializedObject so, string field, ElementTypeData[] refs)
        {
            var p = so.FindProperty(field); if (p == null) return;
            p.arraySize = refs.Length;
            for (int i = 0; i < refs.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = refs[i];
        }

        // Rellena un volátil de la 3.ª/4.ª gen. (ficha completa: pone a cero todo lo que no use).
        private static bool FillGen4(SerializedObject so, string kind)
        {
            if (!Gen4.TryGetValue(kind, out var g)) return false;
            void B(string field, bool v) { var p = so.FindProperty(field); if (p != null) p.boolValue = v; }
            void I(string field, int v) { var p = so.FindProperty(field); if (p != null) p.intValue = v; }
            void F(string field, float v) { var p = so.FindProperty(field); if (p != null) p.floatValue = v; }
            void S(string field, string v) { var p = so.FindProperty(field); if (p != null) p.stringValue = v; }

            S("displayName", g.name);
            B("isVolatile", true); B("clearedOnSwitch", true);
            F("residualDamagePercent", g.residual); B("progressiveResidual", false); B("residualHeals", g.heals);
            F("actionPreventionChance", g.prevent); I("durationTurns", g.dur); I("durationMaxTurns", 0);
            F("recoveryChancePerTurn", 0f); F("selfDamageOnPreventedPercent", 0f); S("transformsToStatus", "");
            B("preventsSwitch", g.noSwitch); B("blocksIncomingMoves", false); B("survivesLethalHit", false);
            B("residualHealsOpponent", false); B("harderWhenRepeated", false); F("statusCatchMultiplier", 1f);
            SetRefs(so, "immuneTypes", new ElementTypeData[0]);
            var mods = so.FindProperty("passiveModifiers"); if (mods != null) mods.arraySize = 0;

            foreach (var f in Gen4Flags) B(f, System.Array.IndexOf(g.flags, f) >= 0);
            S("residualRequiresStatus", kind == "nightmare" ? "sleep" : "");
            // Tipo potenciado (Carga), tipo que pierde (Respiro), inmunidades (Levitón) y a quién alcanza (Profecía).
            var boost = g.type != null && g.type.StartsWith("boost:") ? ContentAssets.FindById<ElementTypeData>(g.type.Substring(6)) : null;
            var bp = so.FindProperty("boostedType"); if (bp != null) bp.objectReferenceValue = boost;
            F("boostMultiplier", boost != null ? 2f : 1f);
            var lose = g.type != null && g.type.StartsWith("lose:") ? ContentAssets.FindById<ElementTypeData>(g.type.Substring(5)) : null;
            var lp = so.FindProperty("suppressedType"); if (lp != null) lp.objectReferenceValue = lose;
            SetRefs(so, "extraTypeImmunities", g.type == "immune" ? TypesById(g.types) : new ElementTypeData[0]);
            SetRefs(so, "hittableByTypes", g.type == "hittable" ? TypesById(g.types) : new ElementTypeData[0]);
            return true;
        }

        /// <summary>¿Es volátil en la plantilla clásica? (se usa también para avisar si una ficha antigua no lo está).</summary>
        public static bool IsClassicVolatile(string id)
            => id == "confusion" || id == "drowsy" || id == "trapped" || id == "leech_seed" || id == "protect" || id == "endure"
               || Gen4.ContainsKey(id) || id == "kings_shield" || id == "spiky_shield" || id == "smacked_down" || id == "fairy_lock" || id == "baneful_bunker";

        /// <summary>Rellena los campos de comportamiento de un estado según la plantilla (no toca el id).</summary>
        public static bool Fill(SerializedObject so, string kind)
        {
            string display, transformsTo = "";
            float residual, prevention, recovery = 0f, selfDamage = 0f;
            bool progressive;
            int duration;
            string passiveStat = null;
            float passiveMul = 1f;
            ResetGen6(so);

            switch (kind)
            {
                case "burn":      display = "Quemado";     residual = 6.25f; progressive = false; prevention = 0f;   duration = 0; passiveStat = "attack"; passiveMul = 0.5f; break;
                case "poison":    display = "Veneno";      residual = 12.5f; progressive = false; prevention = 0f;   duration = 0; break;
                case "toxic":     display = "Tóxico";      residual = 6.25f; progressive = true;  prevention = 0f;   duration = 0; break;
                case "paralysis": display = "Parálisis";   residual = 0f;    progressive = false; prevention = 25f;  duration = 0; passiveStat = "speed"; passiveMul = 0.5f; break;
                case "sleep":     display = "Dormido";     residual = 0f;    progressive = false; prevention = 100f; duration = 3; recovery = 33f; break;
                case "freeze":    display = "Congelado";   residual = 0f;    progressive = false; prevention = 100f; duration = 0; recovery = 20f; break;
                case "confusion": display = "Confusión";   residual = 0f;    progressive = false; prevention = 33f;  duration = 4; recovery = 33f; selfDamage = 12.5f; break;
                case "drowsy":    display = "Somnoliento"; residual = 0f;    progressive = false; prevention = 0f;   duration = 1; transformsTo = "sleep"; break;
                case "trapped":   display = "Atrapado";    residual = 12.5f; progressive = false; prevention = 0f;   duration = 4; break;
                case "leech_seed":display = "Drenadoras";  residual = 12.5f; progressive = false; prevention = 0f;   duration = 0; break;
                case "protect":   display = "Protegido";   residual = 0f;    progressive = false; prevention = 0f;   duration = 1; break;
                case "endure":    display = "Aguante";     residual = 0f;    progressive = false; prevention = 0f;   duration = 1; break;
                default: return FillGen4(so, kind) || FillGen6(so, kind);
            }

            so.FindProperty("displayName").stringValue = display;
            so.FindProperty("residualDamagePercent").floatValue = residual;
            so.FindProperty("progressiveResidual").boolValue = progressive;
            so.FindProperty("actionPreventionChance").floatValue = prevention;
            so.FindProperty("durationTurns").intValue = duration;
            so.FindProperty("recoveryChancePerTurn").floatValue = recovery;
            so.FindProperty("selfDamageOnPreventedPercent").floatValue = selfDamage;
            so.FindProperty("transformsToStatus").stringValue = transformsTo;

            // Lote B: tipo de estado y comportamientos especiales (todo explícito: la plantilla deja la
            // ficha completa, sin valores heredados de lo que tuviera antes).
            void B(string field, bool v) { var p = so.FindProperty(field); if (p != null) p.boolValue = v; }
            void I(string field, int v) { var p = so.FindProperty(field); if (p != null) p.intValue = v; }
            bool vol = IsClassicVolatile(kind);
            B("isVolatile", vol);
            B("clearedOnSwitch", vol);
            I("durationMaxTurns", kind == "confusion" ? 5 : kind == "trapped" ? 5 : 0);
            if (kind == "confusion") so.FindProperty("durationTurns").intValue = 2; // 2-5 turnos (moderno)
            B("preventsSwitch", kind == "trapped");
            B("residualHealsOpponent", kind == "leech_seed");
            B("blocksIncomingMoves", kind == "protect");
            B("survivesLethalHit", kind == "endure");
            B("harderWhenRepeated", kind == "protect" || kind == "endure");
            foreach (var f in Gen4Flags) B(f, false);   // plantilla completa: nada heredado
            // Captura: dormido/congelado ×2,5; paralizado/envenenado/quemado ×1,5 (clásico moderno).
            var catchP = so.FindProperty("statusCatchMultiplier");
            if (catchP != null)
                catchP.floatValue = kind == "sleep" || kind == "freeze" ? 2.5f
                    : kind == "paralysis" || kind == "poison" || kind == "toxic" || kind == "burn" ? 1.5f : 1f;
            if (kind == "confusion") so.FindProperty("recoveryChancePerTurn").floatValue = 0f; // la duración decide

            // Inmunidades por tipo clásicas (solo las de los tipos que existan en el proyecto).
            string[] immune;
            switch (kind)
            {
                case "burn": immune = new[] { "fire" }; break;
                case "poison": case "toxic": immune = new[] { "poison", "steel" }; break;
                case "paralysis": immune = new[] { "electric" }; break;
                case "freeze": immune = new[] { "ice" }; break;
                case "leech_seed": immune = new[] { "grass" }; break;
                default: immune = new string[0]; break;
            }
            var immuneProp = so.FindProperty("immuneTypes");
            if (immuneProp != null)
            {
                var found = new List<ElementTypeData>();
                foreach (var id in immune)
                {
                    var t = ContentAssets.FindById<ElementTypeData>(id);
                    if (t != null) found.Add(t);
                }
                immuneProp.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++) immuneProp.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            }

            var mods = so.FindProperty("passiveModifiers");
            if (passiveStat == null)
            {
                mods.arraySize = 0;
            }
            else
            {
                mods.arraySize = 1;
                var element = mods.GetArrayElementAtIndex(0);
                element.FindPropertyRelative("statId").stringValue = passiveStat;
                element.FindPropertyRelative("multiplier").floatValue = passiveMul;
            }
            return true;
        }

        /// <summary>Crea los estados clásicos que falten. Devuelve cuántos creó.</summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var (id, name) in All)
            {
                string kind = id;
                if (ContentAssets.CreateIfMissing<StatusConditionData>(ContentFolders.Status, id, name, so => Fill(so, kind)))
                    created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
