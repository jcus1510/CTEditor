using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Infrastructure.Catalog;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.GameDefinition.Editor
{
    /// <summary>
    /// Editor de OBJETOS (menú CTEditor → Objetos → Todos los objetos). Trae una biblioteca de objetos clásicos (medicinas,
    /// revivir, curas de estado, éteres, bolas, piedras evolutivas, objetos X, objetos equipables y
    /// bayas) y deja inventar los tuyos combinando sus efectos.
    /// </summary>
    public sealed class ItemEditorWindow : ContentEditorWindow<ItemData>
    {
        [MenuItem(EditorMenus.Items + "Todos los objetos", false, EditorMenus.ItemsOrder + 1)]
        public static void Open() => OpenWindow<ItemEditorWindow>("Objetos");

        protected override string Category => ContentFolders.Items;
        protected override string Noun => "objeto";
        protected override string Intro =>
            "Medicinas, bolas, piedras evolutivas, objetos de combate y objetos para equipar. Un objeto puede combinar varios efectos.";

        public override string[] GuideSteps => new[]
        {
            "Pulsa «Crear los objetos clásicos» para tener una base (Poción, Poké Ball, Piedra Fuego, Restos…).",
            "Elige una ficha y cambia lo que quieras: precio, cuánto cura, qué estado cura…",
            "Para inventar uno, parte de una plantilla parecida y combina efectos (p. ej. curar + curar estado).",
            "Las piedras evolutivas se conectan desde la especie: en su evolución elige «Objeto» y este objeto.",
            "Los objetos equipables se asignan a cada monstruo (PartyHolder / rival) en «Objeto equipado».",
        };

        // Color de cada categoría (chips del editor y de la lista).
        // En la lista: una marca con el color de su categoría; al pasar el ratón, su descripción.
        protected override Color? RowMark(ItemData d) => ColorOf(d.Category);
        protected override string RowTooltip(ItemData d)
            => string.IsNullOrEmpty(d.Description) ? ContentAssets.Label(d) : $"{ContentAssets.Label(d)} — {d.Description}";

        public static Color ColorOf(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Medicine: return new Color(0.35f, 0.8f, 0.45f);
                case ItemCategory.Revive: return new Color(0.95f, 0.8f, 0.3f);
                case ItemCategory.StatusCure: return new Color(0.55f, 0.75f, 0.95f);
                case ItemCategory.PpRestore: return new Color(0.7f, 0.55f, 0.95f);
                case ItemCategory.Ball: return new Color(0.95f, 0.4f, 0.35f);
                case ItemCategory.Evolution: return new Color(0.95f, 0.6f, 0.2f);
                case ItemCategory.BattleBoost: return new Color(0.9f, 0.45f, 0.7f);
                case ItemCategory.Held: return new Color(0.45f, 0.75f, 0.7f);
                case ItemCategory.Key: return new Color(0.6f, 0.6f, 0.6f);
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        public static string NameOf(ItemCategory c) => Etiquetas.Enum(c.ToString());

        // ---------------- Biblioteca clásica ----------------

        public sealed class Preset
        {
            public string Id, Name, Summary;
            public ItemCategory Cat;
            public int Price;
            public Action<SerializedObject> Fill;
        }

        private static Preset P(ItemCategory cat, string id, string name, int price, string summary, Action<SerializedObject> fill)
            => new Preset { Cat = cat, Id = id, Name = name, Price = price, Summary = summary, Fill = fill };

        public static readonly Preset[] Library =
        {
            // Medicinas
            P(ItemCategory.Medicine, "potion", "Poción", 200, "Cura 20 PS.", so => Int(so, "healHp", 20)),
            P(ItemCategory.Medicine, "super_potion", "Superpoción", 700, "Cura 60 PS.", so => Int(so, "healHp", 60)),
            P(ItemCategory.Medicine, "hyper_potion", "Hiperpoción", 1500, "Cura 120 PS.", so => Int(so, "healHp", 120)),
            P(ItemCategory.Medicine, "max_potion", "Poción Máxima", 2500, "Cura todos los PS.", so => Flt(so, "healPercent", 100)),
            P(ItemCategory.Medicine, "full_restore", "Restaurar Todo", 3000, "Cura todos los PS y cualquier estado.",
                so => { Flt(so, "healPercent", 100); Bool(so, "curesAllStatus", true); }),
            // Revivir
            P(ItemCategory.Revive, "revive", "Revivir", 2000, "Revive con la mitad de los PS.", so => { Bool(so, "revives", true); Flt(so, "reviveHpPercent", 50); }),
            P(ItemCategory.Revive, "max_revive", "Revivir Máximo", 4000, "Revive con todos los PS.", so => { Bool(so, "revives", true); Flt(so, "reviveHpPercent", 100); }),
            // Curas de estado
            P(ItemCategory.StatusCure, "antidote", "Antídoto", 100, "Cura el envenenamiento.", so => Str(so, "curesStatusId", "poison|toxic")),
            P(ItemCategory.StatusCure, "burn_heal", "Antiquemar", 300, "Cura las quemaduras.", so => Str(so, "curesStatusId", "burn")),
            P(ItemCategory.StatusCure, "paralyze_heal", "Antiparalizador", 300, "Cura la parálisis.", so => Str(so, "curesStatusId", "paralysis")),
            P(ItemCategory.StatusCure, "awakening", "Despertar", 250, "Despierta.", so => Str(so, "curesStatusId", "sleep")),
            P(ItemCategory.StatusCure, "ice_heal", "Antihielo", 250, "Descongela.", so => Str(so, "curesStatusId", "freeze")),
            P(ItemCategory.StatusCure, "full_heal", "Cura Total", 400, "Cura cualquier estado.", so => Bool(so, "curesAllStatus", true)),
            // PP
            P(ItemCategory.PpRestore, "ether", "Éter", 1200, "Recupera 10 PP de un movimiento.", so => Int(so, "restorePp", 10)),
            P(ItemCategory.PpRestore, "max_ether", "Éter Máximo", 2000, "Recupera todos los PP de un movimiento.", so => Int(so, "restorePp", 999)),
            P(ItemCategory.PpRestore, "elixir", "Elixir", 3000, "Recupera 10 PP de todos los movimientos.", so => { Int(so, "restorePp", 10); Bool(so, "restorePpAllMoves", true); }),
            P(ItemCategory.PpRestore, "max_elixir", "Elixir Máximo", 4500, "Recupera todos los PP de todos los movimientos.", so => { Int(so, "restorePp", 999); Bool(so, "restorePpAllMoves", true); }),
            // Bolas
            P(ItemCategory.Ball, "poke_ball", "Poké Ball", 200, "Bola básica (×1).", so => Ball(so, 1f)),
            P(ItemCategory.Ball, "great_ball", "Super Ball", 600, "Mejor que la Poké Ball (×1,5).", so => Ball(so, 1.5f)),
            P(ItemCategory.Ball, "ultra_ball", "Ultra Ball", 800, "Muy buena (×2).", so => Ball(so, 2f)),
            P(ItemCategory.Ball, "master_ball", "Master Ball", 0, "Captura siempre.", so => Ball(so, 255f)),
            // Piedras evolutivas (se usan desde la evolución de la especie)
            P(ItemCategory.Evolution, "fire_stone", "Piedra Fuego", 3000, "Hace evolucionar a ciertas especies.", Stone),
            P(ItemCategory.Evolution, "water_stone", "Piedra Agua", 3000, "Hace evolucionar a ciertas especies.", Stone),
            P(ItemCategory.Evolution, "thunder_stone", "Piedra Trueno", 3000, "Hace evolucionar a ciertas especies.", Stone),
            P(ItemCategory.Evolution, "leaf_stone", "Piedra Hoja", 3000, "Hace evolucionar a ciertas especies.", Stone),
            P(ItemCategory.Evolution, "moon_stone", "Piedra Lunar", 3000, "Hace evolucionar a ciertas especies.", Stone),
            P(ItemCategory.Evolution, "sun_stone", "Piedra Solar", 3000, "Hace evolucionar a ciertas especies (Gloom, Sunkern).", Stone),
            // Objetos que hacen evolucionar al intercambiarse llevándolos (2ª gen.)
            P(ItemCategory.Held, "kings_rock", "Roca del Rey", 5000, "Sus ataques pueden hacer retroceder (10 %). Llevándolo al intercambiarse: Slowpoke y Poliwhirl evolucionan.",
                so => { Held(so); Flt(so, "heldFlinchChance", 10f); }),
            P(ItemCategory.Held, "metal_coat", "Revestimiento Metálico", 2000, "Movimientos de Acero ×1,2. Llevándolo al intercambiarse: Onix y Scyther evolucionan.", so => TypeBoost(so, "steel")),
            P(ItemCategory.Held, "dragon_scale", "Escama Dragón", 2000, "Llevándolo al intercambiarse: Seadra evoluciona.", Held),
            P(ItemCategory.Held, "up_grade", "Mejora", 2000, "Llevándolo al intercambiarse: Porygon evoluciona.", Held),
            // 3.ª y 4.ª gen.: piedras nuevas y objetos que hacen evolucionar llevándolos (al intercambiar o al subir de nivel)
            P(ItemCategory.Evolution, "dusk_stone", "Piedra Noche", 3000, "Hace evolucionar a Murkrow, Misdreavus...", Stone),
            P(ItemCategory.Evolution, "shiny_stone", "Piedra Día", 3000, "Hace evolucionar a Togetic, Roselia...", Stone),
            P(ItemCategory.Evolution, "dawn_stone", "Piedra Alba", 3000, "Hace evolucionar a Kirlia macho y Snorunt hembra.", Stone),
            P(ItemCategory.Held, "oval_stone", "Piedra Oval", 2000, "Llevándola al subir de nivel de día: Happiny evoluciona.", Held),
            P(ItemCategory.Held, "razor_claw", "Garra Afilada", 2000, "Sube el índice de crítico. Llevándola al subir de nivel de noche: Sneasel evoluciona.",
                so => { Held(so); Int(so, "heldCritStageBonus", 1); }),
            P(ItemCategory.Held, "razor_fang", "Colmillo Agudo", 2000, "Sus ataques pueden hacer retroceder (10 %). Llevándolo al subir de nivel de noche: Gligar evoluciona.",
                so => { Held(so); Flt(so, "heldFlinchChance", 10f); }),
            P(ItemCategory.Held, "protector", "Protector", 2000, "Llevándolo al intercambiarse: Rhydon evoluciona.", Held),
            P(ItemCategory.Held, "electirizer", "Electrizador", 2000, "Llevándolo al intercambiarse: Electabuzz evoluciona.", Held),
            P(ItemCategory.Held, "magmarizer", "Magmatizador", 2000, "Llevándolo al intercambiarse: Magmar evoluciona.", Held),
            P(ItemCategory.Held, "dubious_disc", "Disco Extraño", 2000, "Llevándolo al intercambiarse: Porygon2 evoluciona.", Held),
            P(ItemCategory.Held, "reaper_cloth", "Tela Terrible", 2000, "Llevándola al intercambiarse: Dusclops evoluciona.", Held),
            P(ItemCategory.Held, "deep_sea_tooth", "Diente Marino", 2000, "Llevándolo al intercambiarse: Clamperl evoluciona a Huntail.", Held),
            P(ItemCategory.Held, "deep_sea_scale", "Escama Marina", 2000, "Llevándola al intercambiarse: Clamperl evoluciona a Gorebyss.", Held),
            // Objetos de combate
            P(ItemCategory.BattleBoost, "x_attack", "Ataque X", 1000, "+2 al Ataque durante el combate.", so => Boost(so, "attack")),
            P(ItemCategory.BattleBoost, "x_defense", "Defensa X", 2000, "+2 a la Defensa.", so => Boost(so, "defense")),
            P(ItemCategory.BattleBoost, "x_sp_atk", "Ataque Esp. X", 1000, "+2 al Ataque Especial.", so => Boost(so, "sp_attack")),
            P(ItemCategory.BattleBoost, "x_sp_def", "Defensa Esp. X", 2000, "+2 a la Defensa Especial.", so => Boost(so, "sp_defense")),
            P(ItemCategory.BattleBoost, "x_speed", "Velocidad X", 1000, "+2 a la Velocidad.", so => Boost(so, "speed")),
            P(ItemCategory.BattleBoost, "x_accuracy", "Precisión X", 1000, "+2 a la Precisión.", so => Boost(so, "accuracy")),
            // Equipables
            P(ItemCategory.Held, "leftovers", "Restos", 4000, "Recupera 1/16 de PS cada turno.", so => { Held(so); Flt(so, "heldEndOfTurnHealPercent", 6.25f); }),
            P(ItemCategory.Held, "charcoal", "Carbón", 3000, "Movimientos de Fuego ×1,2.", so => TypeBoost(so, "fire")),
            P(ItemCategory.Held, "mystic_water", "Agua Mística", 3000, "Movimientos de Agua ×1,2.", so => TypeBoost(so, "water")),
            P(ItemCategory.Held, "miracle_seed", "Semilla Milagro", 3000, "Movimientos de Planta ×1,2.", so => TypeBoost(so, "grass")),
            P(ItemCategory.Held, "magnet", "Imán", 3000, "Movimientos de Eléctrico ×1,2.", so => TypeBoost(so, "electric")),
            // El resto de potenciadores de tipo (2.ª gen. en adelante): los reparte la IA a partir de nivel Élite.
            P(ItemCategory.Held, "black_belt", "Cinturón Negro", 3000, "Movimientos de Lucha ×1,2.", so => TypeBoost(so, "fighting")),
            P(ItemCategory.Held, "black_glasses", "Gafas de Sol", 3000, "Movimientos de Siniestro ×1,2.", so => TypeBoost(so, "dark")),
            P(ItemCategory.Held, "hard_stone", "Piedra Dura", 3000, "Movimientos de Roca ×1,2.", so => TypeBoost(so, "rock")),
            P(ItemCategory.Held, "never_melt_ice", "Antiderretir", 3000, "Movimientos de Hielo ×1,2.", so => TypeBoost(so, "ice")),
            P(ItemCategory.Held, "dragon_fang", "Colmillo Dragón", 3000, "Movimientos de Dragón ×1,2.", so => TypeBoost(so, "dragon")),
            P(ItemCategory.Held, "poison_barb", "Flecha Venenosa", 3000, "Movimientos de Veneno ×1,2.", so => TypeBoost(so, "poison")),
            P(ItemCategory.Held, "sharp_beak", "Pico Afilado", 3000, "Movimientos de Volador ×1,2.", so => TypeBoost(so, "flying")),
            P(ItemCategory.Held, "silk_scarf", "Pañuelo Seda", 3000, "Movimientos de Normal ×1,2.", so => TypeBoost(so, "normal")),
            P(ItemCategory.Held, "silver_powder", "Polvo Plata", 3000, "Movimientos de Bicho ×1,2.", so => TypeBoost(so, "bug")),
            P(ItemCategory.Held, "soft_sand", "Arena Fina", 3000, "Movimientos de Tierra ×1,2.", so => TypeBoost(so, "ground")),
            P(ItemCategory.Held, "spell_tag", "Hechizo", 3000, "Movimientos de Fantasma ×1,2.", so => TypeBoost(so, "ghost")),
            P(ItemCategory.Held, "twisted_spoon", "Cuchara Torcida", 3000, "Movimientos de Psíquico ×1,2.", so => TypeBoost(so, "psychic")),
            P(ItemCategory.Held, "muscle_band", "Cinta Fuerte", 3000, "Movimientos físicos ×1,1.",
                so => HeldMod(so, 1.1f, new Condition(ConditionKind.MoveCategory, text: "Physical"))),
            P(ItemCategory.Held, "wise_glasses", "Gafas Especiales", 3000, "Movimientos especiales ×1,1.",
                so => HeldMod(so, 1.1f, new Condition(ConditionKind.MoveCategory, text: "Special"))),
            P(ItemCategory.Held, "oran_berry", "Baya Aranja", 100, "Con la mitad de PS o menos, recupera 10 PS (se consume).",
                so => { Held(so); Flt(so, "heldTriggerHpPercent", 50); Int(so, "heldTriggerHealHp", 10); Bool(so, "heldConsumedOnTrigger", true); }),
            P(ItemCategory.Held, "sitrus_berry", "Baya Zidra", 200, "Con la mitad de PS o menos, recupera 1/4 de PS (se consume).",
                so => { Held(so); Flt(so, "heldTriggerHpPercent", 50); Flt(so, "heldTriggerHealPercent", 25); Bool(so, "heldConsumedOnTrigger", true); }),
            // ===== 5.ª y 6.ª gen.: objetos de COMPETICIÓN (los reparte la IA Maestro / Injusto) =====
            P(ItemCategory.Held, "choice_band", "Cinta Elección", 4000, "Ataque ×1,5, pero solo puede usar el primer movimiento que elija hasta que se retire.",
                so => { Held(so); Stats(so, "attack:x1,5"); Bool(so, "heldChoiceLock", true); }),
            P(ItemCategory.Held, "choice_specs", "Gafas Elección", 4000, "Ataque Esp. ×1,5, pero solo puede usar el primer movimiento que elija.",
                so => { Held(so); Stats(so, "sp_attack:x1,5"); Bool(so, "heldChoiceLock", true); }),
            P(ItemCategory.Held, "choice_scarf", "Pañuelo Elección", 4000, "Velocidad ×1,5, pero solo puede usar el primer movimiento que elija.",
                so => { Held(so); Stats(so, "speed:x1,5"); Bool(so, "heldChoiceLock", true); }),
            P(ItemCategory.Held, "life_orb", "Vidasfera", 4000, "Sus ataques ×1,3, pero pierde 1/10 de sus PS cada vez que hace daño.",
                so => { HeldMod(so, 1.3f); Flt(so, "heldAttackRecoilPercent", 10f); }),
            P(ItemCategory.Held, "focus_sash", "Banda Focus", 4000, "Con los PS al máximo, aguanta con 1 PS un golpe que le debilitaría (se gasta).",
                so => { Held(so); Bool(so, "heldSurviveFromFullHp", true); }),
            P(ItemCategory.Held, "assault_vest", "Chaleco Asalto", 4000, "Def. Esp. ×1,5, pero no puede usar movimientos de estado.",
                so => { Held(so); Stats(so, "sp_defense:x1,5"); Bool(so, "heldBlocksStatusMoves", true); }),
            P(ItemCategory.Held, "rocky_helmet", "Casco Dentado", 4000, "Quien le golpea con contacto pierde 1/6 de sus PS.",
                so => { Held(so); Flt(so, "heldContactDamagePercent", 16.67f); }),
            P(ItemCategory.Held, "weakness_policy", "Seguro Debilidad", 4000, "Si recibe un golpe muy eficaz: Ataque y Ataque Esp. +2 (se gasta).",
                so => { Held(so); OnHit(so, "attack:+2 [si propio.eficacia>1] | sp_attack:+2 [si propio.eficacia>1]"); Bool(so, "heldOnHitConsumed", true); }),
            P(ItemCategory.Held, "air_balloon", "Globo Helio", 4000, "Inmune a Tierra hasta que le golpean (entonces revienta).",
                so => { Held(so); Bool(so, "heldAirBalloon", true); }),
            P(ItemCategory.Held, "expert_belt", "Cinta Experto", 4000, "Sus golpes muy eficaces hacen ×1,2.",
                so => { Held(so); Flt(so, "heldSuperEffectiveBoost", 1.2f); }),
            P(ItemCategory.Held, "eviolite", "Mineral Evolutivo", 4000, "Defensa y Def. Esp. ×1,5 si aún puede evolucionar.",
                so => { Held(so); Stats(so, "defense:x1,5 [si propio.puede_evolucionar] | sp_defense:x1,5 [si propio.puede_evolucionar]"); }),
            P(ItemCategory.Held, "black_sludge", "Lodo Negro", 4000, "Si es de tipo Veneno recupera 1/16 de PS por turno; si no, pierde 1/8.",
                so => { Held(so); Bool(so, "heldBlackSludge", true); }),
            P(ItemCategory.Held, "flame_orb", "Llamasfera", 4000, "Al final del turno le quema (útil con Agallas).",
                so => { Held(so); Str(so, "heldSelfStatusEndOfTurn", "burn"); }),
            P(ItemCategory.Held, "toxic_orb", "Toxisfera", 4000, "Al final del turno le envenena gravemente (útil con Antídoto / Ímpetu Tóxico).",
                so => { Held(so); Str(so, "heldSelfStatusEndOfTurn", "toxic"); }),
            P(ItemCategory.Held, "lum_berry", "Baya Ziuela", 200, "Se cura de cualquier estado en cuanto lo sufre (se consume).",
                so => { Held(so); Bool(so, "heldCuresAnyStatus", true); }),
            P(ItemCategory.Held, "scope_lens", "Periscopio", 4000, "Sube el índice de crítico.",
                so => { Held(so); Int(so, "heldCritStageBonus", 1); }),
            P(ItemCategory.Held, "wide_lens", "Lupa", 4000, "Precisión de sus movimientos ×1,1.",
                so => { Held(so); Flt(so, "heldAccuracyMultiplier", 1.1f); }),
            P(ItemCategory.Held, "bright_powder", "Polvo Brillo", 4000, "Los que le atacan tienen precisión ×0,9.",
                so => { Held(so); Flt(so, "heldEvasionMultiplier", 0.9f); }),
            P(ItemCategory.Held, "quick_claw", "Garra Rápida", 4000, "20 % de actuar el primero dentro de su prioridad.",
                so => { Held(so); Flt(so, "heldQuickClawChance", 20f); }),
            P(ItemCategory.Held, "shell_bell", "Campana Concha", 4000, "Recupera 1/8 del daño que hace.",
                so => { Held(so); Flt(so, "heldHealOnDamagePercent", 12.5f); }),
            P(ItemCategory.Held, "damp_rock", "Roca Lluvia", 4000, "Su lluvia dura 3 turnos más.", so => { Held(so); Int(so, "heldWeatherTurnsBonus", 3); }),
            P(ItemCategory.Held, "heat_rock", "Roca Calor", 4000, "Su sol dura 3 turnos más.", so => { Held(so); Int(so, "heldWeatherTurnsBonus", 3); }),
            P(ItemCategory.Held, "smooth_rock", "Roca Lisa", 4000, "Su tormenta de arena dura 3 turnos más.", so => { Held(so); Int(so, "heldWeatherTurnsBonus", 3); }),
            P(ItemCategory.Held, "icy_rock", "Roca Helada", 4000, "Su granizo dura 3 turnos más.", so => { Held(so); Int(so, "heldWeatherTurnsBonus", 3); }),
            P(ItemCategory.Held, "light_clay", "Refleluz", 4000, "Sus pantallas (Reflejo, Pantalla de Luz) duran 3 turnos más.", so => { Held(so); Int(so, "heldScreenTurnsBonus", 3); }),
            P(ItemCategory.Held, "pixie_plate", "Tabla Duende", 4000, "Movimientos de Hada ×1,2.", so => TypeBoost(so, "fairy")),
            // Bayas de resistencia: un golpe muy eficaz de su tipo hace la mitad (se consumen).
            Berry("occa_berry", "Baya Caoca", "fire", "Fuego"), Berry("passho_berry", "Baya Pasio", "water", "Agua"),
            Berry("wacan_berry", "Baya Gualot", "electric", "Eléctrico"), Berry("rindo_berry", "Baya Tamar", "grass", "Planta"),
            Berry("yache_berry", "Baya Rimoya", "ice", "Hielo"), Berry("chople_berry", "Baya Pomaro", "fighting", "Lucha"),
            Berry("kebia_berry", "Baya Kebia", "poison", "Veneno"), Berry("shuca_berry", "Baya Acardo", "ground", "Tierra"),
            Berry("coba_berry", "Baya Kouba", "flying", "Volador"), Berry("payapa_berry", "Baya Payapa", "psychic", "Psíquico"),
            Berry("tanga_berry", "Baya Yecana", "bug", "Bicho"), Berry("charti_berry", "Baya Alcho", "rock", "Roca"),
            Berry("kasib_berry", "Baya Drasi", "ghost", "Fantasma"), Berry("haban_berry", "Baya Anjiro", "dragon", "Dragón"),
            Berry("colbur_berry", "Baya Dillo", "dark", "Siniestro"), Berry("babiri_berry", "Baya Baribá", "steel", "Acero"),
            Berry("roseli_berry", "Baya Hibis", "fairy", "Hada"), Berry("chilan_berry", "Baya Chilan", "normal", "Normal"),
            // 5.ª y 6.ª gen.: objetos que hacen evolucionar al intercambiarse llevándolos
            P(ItemCategory.Held, "prism_scale", "Escama Bella", 2000, "Llevándola al intercambiarse: Feebas evoluciona.", Held),
            P(ItemCategory.Held, "whipped_dream", "Dulce de Nata", 2000, "Llevándolo al intercambiarse: Swirlix evoluciona.", Held),
            P(ItemCategory.Held, "sachet", "Saquito Fragante", 2000, "Llevándolo al intercambiarse: Spritzee evoluciona.", Held),
            // Amistad
            P(ItemCategory.Vitamin, "pomeg_berry", "Baya Grana", 100, "Sube la amistad.", so => { Bool(so, "usableInBattle", false); Int(so, "friendshipChange", 10); }),
        };

        /// <summary>Deja la ficha "en blanco" (sin efectos) antes de aplicar una plantilla.</summary>
        private static void Reset(SerializedObject so)
        {
            Int(so, "healHp", 0); Flt(so, "healPercent", 0); Bool(so, "curesAllStatus", false); Str(so, "curesStatusId", "");
            Bool(so, "revives", false); Flt(so, "reviveHpPercent", 50); Int(so, "restorePp", 0); Bool(so, "restorePpAllMoves", false);
            Int(so, "friendshipChange", 0); Flt(so, "catchMultiplier", 0); Str(so, "battleStatId", ""); Int(so, "battleStages", 0);
            so.FindProperty("heldPowerModifiers").arraySize = 0;
            Flt(so, "heldEndOfTurnHealPercent", 0); Flt(so, "heldTriggerHpPercent", 0); Int(so, "heldTriggerHealHp", 0);
            Flt(so, "heldTriggerHealPercent", 0); Bool(so, "heldConsumedOnTrigger", true);
            // 5.ª y 6.ª gen.: todo a cero (así una plantilla no hereda nada de lo que hubiera).
            so.FindProperty("heldStatMultipliers").arraySize = 0; so.FindProperty("heldOnHitStats").arraySize = 0;
            Bool(so, "heldChoiceLock", false); Flt(so, "heldAttackRecoilPercent", 0); Bool(so, "heldSurviveFromFullHp", false);
            Flt(so, "heldContactDamagePercent", 0); Bool(so, "heldOnHitConsumed", false); Bool(so, "heldAirBalloon", false);
            Bool(so, "heldBlocksStatusMoves", false); Int(so, "heldCritStageBonus", 0); Flt(so, "heldAccuracyMultiplier", 1f);
            Flt(so, "heldEvasionMultiplier", 1f); Str(so, "heldResistBerryType", ""); Bool(so, "heldCuresAnyStatus", false);
            Str(so, "heldSelfStatusEndOfTurn", ""); Flt(so, "heldFlinchChance", 0); Flt(so, "heldHealOnDamagePercent", 0);
            Int(so, "heldWeatherTurnsBonus", 0); Int(so, "heldScreenTurnsBonus", 0); Bool(so, "heldBlackSludge", false);
            Flt(so, "heldQuickClawChance", 0); Flt(so, "heldSuperEffectiveBoost", 1f);
            Bool(so, "usableInBattle", true); Bool(so, "usableOutsideBattle", true); Bool(so, "consumable", true);
        }

        public static void Fill(SerializedObject so, Preset p)
        {
            Reset(so);
            so.FindProperty("category").intValue = (int)p.Cat;
            so.FindProperty("price").intValue = p.Price;
            so.FindProperty("description").stringValue = p.Summary;
            p.Fill(so);
        }

        private static void Int(SerializedObject so, string f, int v) => so.FindProperty(f).intValue = v;
        private static void Flt(SerializedObject so, string f, float v) => so.FindProperty(f).floatValue = v;
        private static void Bool(SerializedObject so, string f, bool v) => so.FindProperty(f).boolValue = v;
        private static void Str(SerializedObject so, string f, string v) => so.FindProperty(f).stringValue = v;
        private static void Ball(SerializedObject so, float m) { Flt(so, "catchMultiplier", m); Bool(so, "usableOutsideBattle", false); }
        private static void Stone(SerializedObject so) { Bool(so, "usableInBattle", false); }
        private static void Boost(SerializedObject so, string stat) { Str(so, "battleStatId", stat); Int(so, "battleStages", 2); Bool(so, "usableOutsideBattle", false); }
        private static void Held(SerializedObject so) { Bool(so, "usableInBattle", false); Bool(so, "usableOutsideBattle", false); Bool(so, "consumable", false); }
        private static void HeldMod(SerializedObject so, float mult, params Condition[] c)
        {
            Held(so);
            ConditionText.WriteModifiers(so.FindProperty("heldPowerModifiers"), new List<(float, Condition[])> { (mult, c) });
        }
        private static void TypeBoost(SerializedObject so, string type) => HeldMod(so, 1.2f, new Condition(ConditionKind.MoveType, text: type));
        // Estadísticas con condiciones ("attack:x1,5 | defense:x1,5 [si propio.puede_evolucionar]") y cambios al recibir golpe.
        private static void Stats(SerializedObject so, string text) => Csv.CsvSchemas.WriteStatMods(so, text, "heldStatMultipliers");
        private static void OnHit(SerializedObject so, string text) => Csv.CsvSchemas.WriteOnHit(so, text, "heldOnHitStats");
        private static Preset Berry(string id, string name, string type, string typeName)
            => P(ItemCategory.Held, id, name, 200, type == "normal"
                    ? $"El primer golpe de tipo {typeName} que recibe hace la mitad (se consume)."
                    : $"El primer golpe MUY EFICAZ de tipo {typeName} que recibe hace la mitad (se consume).",
                so => { Held(so); Str(so, "heldResistBerryType", type); });

        /// <summary>Crea los objetos clásicos que falten. Público: lo usa el Centro de Contenido.</summary>
        public static int CreateClassicSet()
        {
            int created = 0;
            foreach (var p in Library)
            {
                var preset = p;
                if (ContentAssets.CreateIfMissing<ItemData>(ContentFolders.Items, p.Id, p.Name, so => Fill(so, preset)))
                    created++;
            }
            AssetDatabase.SaveAssets();
            return created;
        }

        private int _preset;

        protected override IReadOnlyList<(string id, string name, string group)> Templates
            => Library.Select(p => (p.Id, p.Name, NameOf(p.Cat))).ToList();

        protected override void ApplyTemplate(SerializedObject so, string id)
        {
            var p = Library.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            Fill(so, p);
            so.FindProperty("displayName").stringValue = p.Name;
        }

        protected override void DrawBulkPresets()
        {
            if (GUILayout.Button($"Crear los {Library.Length} objetos clásicos")) FinishBulk(CreateClassicSet(), "objetos");
        }

        protected override void DrawPresets(ItemData d)
        {
            EditorGUILayout.LabelField("Plantilla (reemplaza los efectos; el id se mantiene)", EditorStyles.boldLabel);
            var labels = Library.Select(p => $"{NameOf(p.Cat)}/{p.Name}").ToArray();
            EditorGUILayout.BeginHorizontal();
            _preset = EditorGUILayout.Popup(_preset, labels);
            if (GUILayout.Button("Aplicar", GUILayout.Width(70)))
            {
                var p = Library[_preset];
                EditSelected(so => { Fill(so, p); if (string.IsNullOrWhiteSpace(d.DisplayName) || d.DisplayName == d.Id) so.FindProperty("displayName").stringValue = p.Name; });
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(Library[_preset].Summary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
        }

        protected override void DrawPreview(ItemData d)
        {
            EditorTheme.Chip($"{NameOf(d.Category).ToUpperInvariant()} · {d.Price} ₽", ColorOf(d.Category));
            var lines = Describe(d);
            EditorGUILayout.LabelField("Qué hace", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lines.Count == 0 ? "Todavía no hace nada (todos sus efectos están en cero)." : "• " + string.Join("\n• ", lines),
                lines.Count == 0 ? MessageType.Warning : MessageType.Info);

            foreach (var w in Check(d)) EditorGUILayout.HelpBox(w, MessageType.Warning);

            if (d.Category == ItemCategory.Evolution)
            {
                var users = ContentAssets.LoadAll<SpeciesData>().Where(s => s.Evolutions != null && s.Evolutions.Any(e =>
                    e.method == CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Item && e.itemId == d.Id)).ToList();
                EditorGUILayout.HelpBox(users.Count == 0
                    ? "Ninguna especie evoluciona con este objeto todavía. En la especie, añade una evolución con método «Objeto» y elige este."
                    : "Hace evolucionar a: " + string.Join(", ", users.Select(u => u.DisplayName)), MessageType.None);
            }
        }

        /// <summary>El objeto en frases.</summary>
        public static List<string> Describe(ItemData d)
        {
            var l = new List<string>();
            if (d.HealHp > 0) l.Add($"Cura {d.HealHp} PS.");
            if (d.HealPercent > 0) l.Add(d.HealPercent >= 100 ? "Cura todos los PS." : $"Cura el {d.HealPercent:0.#}% de los PS.");
            if (d.CuresAllStatus) l.Add("Cura cualquier estado.");
            else if (!string.IsNullOrWhiteSpace(d.CuresStatusId)) l.Add($"Cura: {d.CuresStatusId.Replace("|", ", ")}.");
            if (d.Revives) l.Add($"Revive con el {d.ReviveHpPercent:0}% de los PS.");
            if (d.RestorePp > 0) l.Add($"Recupera {(d.RestorePp >= 99 ? "todos los" : d.RestorePp.ToString())} PP de {(d.RestorePpAllMoves ? "todos los movimientos" : "un movimiento")}.");
            if (d.FriendshipChange != 0) l.Add($"{(d.FriendshipChange > 0 ? "Sube" : "Baja")} la amistad {Math.Abs(d.FriendshipChange)} puntos.");
            if (d.CatchMultiplier > 0) l.Add(d.CatchMultiplier >= 255 ? "Bola: captura siempre." : $"Bola: probabilidad de captura ×{d.CatchMultiplier:0.##}.");
            if (!string.IsNullOrWhiteSpace(d.BattleStatId) && d.BattleStages != 0)
                l.Add($"En combate: {(d.BattleStages > 0 ? "+" : "")}{d.BattleStages} a {StatLabels.NameOf(d.BattleStatId)}.");
            if (d.HeldPowerModifiers != null)
                foreach (var m in d.HeldPowerModifiers.Where(m => m != null))
                    l.Add($"Equipado: potencia ×{m.multiplier:0.##} {ConditionText.Describe((m.conditions ?? new ConditionData[0]).Select(ConditionText.FromData))}.");
            if (d.HeldEndOfTurnHealPercent > 0) l.Add($"Equipado: recupera el {d.HeldEndOfTurnHealPercent:0.##}% de PS al final de cada turno.");
            if (d.HeldTriggerHpPercent > 0)
                l.Add($"Equipado: con el {d.HeldTriggerHpPercent:0}% de PS o menos, recupera " +
                      (d.HeldTriggerHealHp > 0 ? $"{d.HeldTriggerHealHp} PS" : $"el {d.HeldTriggerHealPercent:0.#}% de PS") +
                      (d.HeldConsumedOnTrigger ? " y se consume." : "."));
            // 5.ª y 6.ª gen.
            if (d.HeldStatMultipliers != null)
                foreach (var m in d.HeldStatMultipliers.Where(m => m != null))
                    l.Add($"Equipado: {StatLabels.NameOf(m.statId)} ×{m.multiplier:0.##}" +
                          (m.conditions != null && m.conditions.Length > 0 ? $" {ConditionText.Describe(m.conditions.Select(ConditionText.FromData))}" : "") + ".");
            if (d.HeldChoiceLock) l.Add("Equipado: solo puede usar el primer movimiento que elija hasta que se retire.");
            if (d.HeldAttackRecoilPercent > 0) l.Add($"Equipado: pierde el {d.HeldAttackRecoilPercent:0.##}% de sus PS cada vez que hace daño.");
            if (d.HeldSurviveFromFullHp) l.Add("Equipado: con los PS al máximo aguanta con 1 PS un golpe mortal (se gasta).");
            if (d.HeldContactDamagePercent > 0) l.Add($"Equipado: quien le golpea con contacto pierde el {d.HeldContactDamagePercent:0.##}% de sus PS.");
            if (d.HeldOnHitStats != null)
                foreach (var h in d.HeldOnHitStats.Where(h => h != null))
                    l.Add($"Equipado: al recibir un golpe, {StatLabels.NameOf(h.statId)} {(h.stages > 0 ? "+" : "")}{h.stages}" +
                          (h.conditions != null && h.conditions.Length > 0 ? $" {ConditionText.Describe(h.conditions.Select(ConditionText.FromData))}" : "") +
                          (d.HeldOnHitConsumed ? " (se gasta)." : "."));
            if (d.HeldAirBalloon) l.Add("Equipado: inmune a Tierra hasta que le golpean (revienta).");
            if (d.HeldBlocksStatusMoves) l.Add("Equipado: no puede usar movimientos de estado.");
            if (d.HeldCritStageBonus > 0) l.Add($"Equipado: +{d.HeldCritStageBonus} al índice de crítico.");
            if (d.HeldAccuracyMultiplier != 1f) l.Add($"Equipado: precisión de sus movimientos ×{d.HeldAccuracyMultiplier:0.##}.");
            if (d.HeldEvasionMultiplier != 1f) l.Add($"Equipado: los que le atacan tienen precisión ×{d.HeldEvasionMultiplier:0.##}.");
            if (!string.IsNullOrWhiteSpace(d.HeldResistBerryType)) l.Add($"Equipado: reduce a la mitad un golpe muy eficaz de tipo '{d.HeldResistBerryType}' (se gasta).");
            if (d.HeldCuresAnyStatus) l.Add("Equipado: se cura de cualquier estado al sufrirlo (se gasta).");
            if (!string.IsNullOrWhiteSpace(d.HeldSelfStatusEndOfTurn)) l.Add($"Equipado: al final del turno se pone el estado '{d.HeldSelfStatusEndOfTurn}'.");
            if (d.HeldFlinchChance > 0) l.Add($"Equipado: {d.HeldFlinchChance:0.#}% de hacer retroceder con sus ataques.");
            if (d.HeldHealOnDamagePercent > 0) l.Add($"Equipado: recupera el {d.HeldHealOnDamagePercent:0.##}% del daño que hace.");
            if (d.HeldWeatherTurnsBonus > 0) l.Add($"Equipado: el clima que pone dura {d.HeldWeatherTurnsBonus} turnos más.");
            if (d.HeldScreenTurnsBonus > 0) l.Add($"Equipado: sus pantallas duran {d.HeldScreenTurnsBonus} turnos más.");
            if (d.HeldBlackSludge) l.Add("Equipado: si es de tipo Veneno recupera 1/16 por turno; si no, pierde 1/8.");
            if (d.HeldQuickClawChance > 0) l.Add($"Equipado: {d.HeldQuickClawChance:0.#}% de actuar el primero.");
            if (d.HeldSuperEffectiveBoost != 1f) l.Add($"Equipado: sus golpes muy eficaces ×{d.HeldSuperEffectiveBoost:0.##}.");
            if (d.Category == ItemCategory.Evolution) l.Add("Hace evolucionar a las especies que lo indiquen en su evolución.");
            l.Add($"Se usa {(d.UsableInBattle && d.UsableOutsideBattle ? "dentro y fuera del combate" : d.UsableInBattle ? "solo en combate" : d.UsableOutsideBattle ? "solo fuera del combate" : "solo equipado o como objeto clave")}" +
                  (d.Consumable ? " y se gasta." : " y no se gasta."));
            return l;
        }

        private static HashSet<string> _evoItems;
        private static int _evoVersion = -1;

        /// <summary>Ids de objetos que usa alguna evolución (con objeto, por intercambio o «lleva: objeto»).</summary>
        public static HashSet<string> EvolutionItems()
        {
            if (_evoItems != null && _evoVersion == ContentAssets.Version) return _evoItems;
            var set = new HashSet<string>();
            foreach (var sp in ContentAssets.LoadAll<SpeciesData>())
                foreach (var e in sp.Evolutions ?? new SpeciesData.EvolutionEntry[0])
                {
                    if (!string.IsNullOrWhiteSpace(e.itemId)) set.Add(e.itemId.Trim());
                    foreach (var c in e.conditions ?? new SpeciesData.EvolutionConditionData[0])
                        if (c != null && !string.IsNullOrWhiteSpace(c.itemId)) set.Add(c.itemId.Trim());
                }
            _evoItems = set;
            _evoVersion = ContentAssets.Version;
            return set;
        }

        public static List<string> Check(ItemData d)
        {
            var w = new List<string>();
            if (d.CatchMultiplier > 0 && !d.UsableInBattle) w.Add("Es una bola pero no se puede usar en combate: nunca servirá para capturar.");
            // Los objetos que hacen EVOLUCIONAR (al llevarlos o al intercambiar: Roca del Rey, Revestimiento Metálico, Escama
            // Bella, Saquito Fragante...) no necesitan efecto en combate: su efecto es la evolución.
            if (d.Category == ItemCategory.Held && !(d.HeldPowerModifiers?.Length > 0 || d.HeldEndOfTurnHealPercent > 0 || d.HeldTriggerHpPercent > 0
                                                     || CTEditor.GameDefinition.Infrastructure.Acl.ItemMapper.Extras(d).DoesSomething)
                && !EvolutionItems().Contains(d.Id ?? ""))
                w.Add("Es de categoría «Equipable» pero no tiene efectos al llevarlo (ni hace evolucionar a ninguna especie).");
            if (d.HeldTriggerHpPercent > 0 && d.HeldTriggerHealHp <= 0 && d.HeldTriggerHealPercent <= 0)
                w.Add("Se activa con poca vida pero no cura nada.");
            if (!string.IsNullOrWhiteSpace(d.CuresStatusId))
                foreach (var s in d.CuresStatusId.Split('|'))
                    if (s.Trim().Length > 0 && ContentAssets.FindById<StatusConditionData>(s.Trim()) == null)
                        w.Add($"Cura el estado '{s.Trim()}', que no existe.");
            return w;
        }
    }
}
