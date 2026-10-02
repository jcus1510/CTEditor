using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.Content
{
    /// <summary>What a column holds: how it is checked, and where (and how) it points to other content.</summary>
    public enum ColumnKind
    {
        /// <summary>Free text (a name, a phrase).</summary>
        Text,
        /// <summary>Long text (descriptions): a bigger box.</summary>
        LongText,
        Int,
        /// <summary>A number with decimals (comma or point).</summary>
        Number,
        /// <summary>si / no.</summary>
        Bool,
        /// <summary>A colour «EE8130» or a name.</summary>
        Color,
        /// <summary>One of a few fixed words (Options).</summary>
        Choice,
        /// <summary>The id of another piece of content (Target).</summary>
        Ref,
        /// <summary>Ids separated with «|»: «grass|poison».</summary>
        RefList,
        /// <summary>«nivel:movimiento» separated with «|»: «1:tackle|7:leech_seed».</summary>
        LevelRefs,
        /// <summary>«id:cantidad» separated with «|»: the bag «potion:3|antidote:1».</summary>
        RefAmounts,
        /// <summary>Evolutions: «ivysaur@16», «raichu@objeto:thunder_stone», several with «|».</summary>
        Evolutions,
        /// <summary>A team: «staryu@18[tackle/water_gun]{sitrus_berry} | starmie@21[...]».</summary>
        Team,
        /// <summary>Ids separated with «/»: «surf/recover/toxic».</summary>
        SlashRefs,
        /// <summary>«clave:valor» pairs that do not point anywhere («attack:1|speed:2»).</summary>
        Pairs,
        /// <summary>Effects and rules written as text («estado:burn@10»): the ids of Targets found inside count as uses.</summary>
        Script,
    }

    /// <summary>One column of a category: its name in the CSV, its label, what it holds and whether it is needed.</summary>
    public sealed class ColumnSpec
    {
        public string Name { get; }
        public string Label { get; }
        public ColumnKind Kind { get; }
        /// <summary>The categories it points to (Ref, lists, Script...). Team: species, moves, items in that order.</summary>
        public IReadOnlyList<string> Targets { get; }
        public bool Required { get; }
        public string Help { get; }
        public IReadOnlyList<string> Options { get; }
        public double? Min { get; }
        public double? Max { get; }
        /// <summary>The section of the form where it goes («Base», «Combate», «Pokédex»...).</summary>
        public string Group { get; }

        public ColumnSpec(string name, string label, ColumnKind kind = ColumnKind.Text, string group = "General", string help = null,
            bool required = false, string[] targets = null, string[] options = null, double? min = null, double? max = null)
        {
            Name = name; Label = label ?? name; Kind = kind; Group = group ?? "General"; Help = help ?? "";
            Required = required; Targets = targets ?? new string[0]; Options = options ?? new string[0]; Min = min; Max = max;
        }

        public bool PointsTo(string category) => Targets.Contains(category);
        public bool IsReference => Targets.Count > 0;
    }

    /// <summary>
    /// A KIND of content (species, moves, items...): its CSV file, how to call one («una especie»), its columns and how
    /// it is shown. The columns not listed are kept as they are (packs and Excel can add their own).
    /// </summary>
    public sealed class CategorySchema
    {
        public string Key { get; }
        public string File { get; }
        /// <summary>Singular, for messages: «especie».</summary>
        public string Noun { get; }
        public string Title { get; }
        public bool Feminine { get; }
        public string IdColumn { get; }
        public string NameColumn { get; }
        public IReadOnlyList<ColumnSpec> Columns { get; }
        /// <summary>Type chart: the other column names are ids of this category (renaming a type renames its column).</summary>
        public string HeaderTarget { get; }
        /// <summary>What it is for, shown in the «i» of its window.</summary>
        public string Intro { get; }
        public string Icon { get; }
        /// <summary>Ids the game knows without a row (the classic growth curves): references to them are fine.</summary>
        public IReadOnlyList<string> BuiltIn { get; set; } = new string[0];

        public CategorySchema(string key, string file, string noun, string title, bool feminine, IEnumerable<ColumnSpec> columns,
            string intro = null, string icon = "base", string idColumn = "id", string nameColumn = "nombre", string headerTarget = null)
        {
            Key = key; File = file; Noun = noun; Title = title; Feminine = feminine; IdColumn = idColumn; NameColumn = nameColumn;
            Columns = columns.ToList(); Intro = intro ?? ""; Icon = icon; HeaderTarget = headerTarget;
        }

        public ColumnSpec Column(string name) => Columns.FirstOrDefault(c => c.Name == name);
        public string Un => Feminine ? "una" : "un";
        public string Nuevo => Feminine ? "Nueva" : "Nuevo";
        public string El => Feminine ? "la" : "el";

        /// <summary>The kind of a column, also for the ones the schema does not know (text).</summary>
        public ColumnSpec ColumnOrText(string name) => Column(name) ?? new ColumnSpec(name, name);
    }

    /// <summary>
    /// The categories of a CTEditor project, as the packs and the Excel sheets write them («datos/especies.csv»...). New
    /// modules add theirs with <see cref="Register"/>.
    /// </summary>
    public static class ContentSchemas
    {
        public const string Types = "tipos", TypeChart = "tabla_tipos", Species = "especies", Moves = "movimientos",
            Abilities = "habilidades", Items = "objetos", Natures = "naturalezas", EggGroups = "grupos_huevo", Curves = "curvas",
            Statuses = "estados", Weathers = "climas", SideEffects = "efectos_lado", Hazards = "trampas", Trainers = "entrenadores",
            Teams = "equipos", Sets = "sets", Rules = "reglas", Zones = "zonas";

        private static readonly string[] Battle = { Statuses, Weathers, Types, Moves, Items, Abilities, Species, SideEffects, Hazards };
        private static readonly string[] Stats = { "hp", "attack", "defense", "sp_attack", "sp_defense", "speed" };

        private static readonly List<CategorySchema> List = new List<CategorySchema>();

        public static IReadOnlyList<CategorySchema> All => List;

        public static CategorySchema Find(string key) => List.FirstOrDefault(c => c.Key == key);

        public static CategorySchema ByFile(string file) =>
            List.FirstOrDefault(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase));

        public static void Register(CategorySchema schema)
        {
            List.RemoveAll(c => c.Key == schema.Key);
            List.Add(schema);
        }

        private static ColumnSpec Id(string help = "Id único, sin espacios (ej. bulbasaur).") =>
            new ColumnSpec("id", "Id", ColumnKind.Text, "General", help, required: true);
        private static ColumnSpec Name() => new ColumnSpec("nombre", "Nombre", ColumnKind.Text, "General", "Nombre visible.", required: true);
        private static ColumnSpec English() => new ColumnSpec("nombre_en", "Nombre en inglés", ColumnKind.Text, "General", "Para Showdown y los sets.");
        private static ColumnSpec Stat(string name, string label) =>
            new ColumnSpec(name, label, ColumnKind.Int, "Estadísticas base", "Estadística base (1-255).", required: true, min: 1, max: 255);
        private static ColumnSpec Yes(string name, string label, string group, string help = null) => new ColumnSpec(name, label, ColumnKind.Bool, group, help);

        static ContentSchemas()
        {
            Register(new CategorySchema(Types, "tipos.csv", "tipo", "Tipos", false, new[]
            {
                Id("Id único (ej. fire)."), Name(),
                new ColumnSpec("color", "Color", ColumnKind.Color, "General", "Color en hexadecimal (ej. EE8130)."),
            }, "Los tipos y su color. La eficacia entre tipos se edita en la tabla de tipos.", "base"));

            Register(new CategorySchema(TypeChart, "tabla_tipos.csv", "fila de la tabla", "Tabla de tipos", true, new[]
            {
                new ColumnSpec("ataca\\defiende", "Ataca", ColumnKind.Ref, "General", "El tipo que ataca.", required: true, targets: new[] { Types }),
            }, "Primera columna: el tipo que ATACA; las demás, los que DEFIENDEN. 2 = muy eficaz, 0,5 = poco eficaz, 0 = inmune, vacío = normal.",
                "rejilla", idColumn: "ataca\\defiende", nameColumn: "ataca\\defiende", headerTarget: Types));

            Register(new CategorySchema(Species, "especies.csv", "especie", "Especies", true, new[]
            {
                Id(), Name(), English(),
                new ColumnSpec("tipos", "Tipos", ColumnKind.RefList, "General", "Uno o dos tipos: grass|poison.", required: true, targets: new[] { Types }),
                Stat("ps", "PS"), Stat("ataque", "Ataque"), Stat("defensa", "Defensa"), Stat("atq_esp", "Ataque esp."), Stat("def_esp", "Defensa esp."), Stat("velocidad", "Velocidad"),
                new ColumnSpec("habilidad", "Habilidad", ColumnKind.Ref, "Combate", "Habilidad principal (vacío = ninguna).", targets: new[] { Abilities }),
                new ColumnSpec("habilidad_2", "Habilidad 2", ColumnKind.Ref, "Combate", "Segunda habilidad posible.", targets: new[] { Abilities }),
                new ColumnSpec("habilidad_oculta", "Habilidad oculta", ColumnKind.Ref, "Combate", "Habilidad oculta.", targets: new[] { Abilities }),
                new ColumnSpec("curva", "Curva de experiencia", ColumnKind.Ref, "Crecimiento", "Curva de XP (vacío = media).", targets: new[] { Curves }),
                new ColumnSpec("exp_base", "Experiencia base", ColumnKind.Int, "Crecimiento", "XP que da derrotarla (ej. 64).", min: 0),
                new ColumnSpec("ratio_captura", "Ratio de captura", ColumnKind.Int, "Crecimiento", "1-255: más alto, más fácil.", min: 1, max: 255),
                new ColumnSpec("evs", "EVs que da", ColumnKind.Pairs, "Crecimiento", "sp_attack:1|speed:1."),
                new ColumnSpec("aprende", "Aprende por nivel", ColumnKind.LevelRefs, "Movimientos", "nivel:movimiento: 1:tackle|3:growl.", targets: new[] { Moves }),
                new ColumnSpec("mt", "MT / MO", ColumnKind.RefList, "Movimientos", "Movimientos por MT.", targets: new[] { Moves }),
                new ColumnSpec("tutor", "Tutor", ColumnKind.RefList, "Movimientos", "Movimientos del tutor.", targets: new[] { Moves }),
                new ColumnSpec("huevo", "Huevo", ColumnKind.RefList, "Movimientos", "Movimientos huevo.", targets: new[] { Moves }),
                new ColumnSpec("grupos_huevo", "Grupos huevo", ColumnKind.RefList, "Crianza", "monster|plant.", targets: new[] { EggGroups }),
                new ColumnSpec("evoluciona", "Evoluciona", ColumnKind.Evolutions, "Evolución",
                    "especie@16 · especie@objeto:thunder_stone · especie@amistad · especie@intercambio(:objeto).", targets: new[] { Species, Items, Moves }),
                new ColumnSpec("stats_extra", "Estadísticas extra", ColumnKind.Pairs, "Estadísticas base", "suerte:50|carisma:30."),
                new ColumnSpec("numero", "N.º Pokédex", ColumnKind.Int, "Pokédex", "0 = sin número.", min: 0),
                new ColumnSpec("categoria", "Categoría", ColumnKind.Text, "Pokédex", "Semilla (se ve «Pokémon Semilla»)."),
                new ColumnSpec("altura", "Altura (m)", ColumnKind.Number, "Pokédex", "0,7.", min: 0),
                new ColumnSpec("peso", "Peso (kg)", ColumnKind.Number, "Pokédex", "6,9.", min: 0),
                new ColumnSpec("color", "Color", ColumnKind.Text, "Pokédex", "verde, rojo, azul..."),
                new ColumnSpec("hembras", "% de hembras", ColumnKind.Text, "Crianza", "0-100 o sin_genero."),
                Yes("legendario", "Legendario", "Pokédex"),
                new ColumnSpec("descripcion", "Descripción", ColumnKind.LongText, "Pokédex", "Texto de la Pokédex."),
                new ColumnSpec("forma_de", "Forma de", ColumnKind.Ref, "Formas", "Especie base de esta variante.", targets: new[] { Species }),
                new ColumnSpec("objeto_variante", "Objeto de la variante", ColumnKind.Ref, "Formas", "Objeto que la activa.", targets: new[] { Items }),
                new ColumnSpec("formas", "Formas de combate", ColumnKind.Script, "Formas", "Megas, modo Daruma...", targets: new[] { Items, Abilities, Types, Moves }),
                new ColumnSpec("cambios_forma", "Nombres de las formas", ColumnKind.Script, "Formas", "", targets: new[] { Items }),
            }, "Las especies: tipos, estadísticas base, habilidades, cómo crecen, qué aprenden, cómo evolucionan y su entrada en la Pokédex.", "estrella"));

            Register(new CategorySchema(Moves, "movimientos.csv", "movimiento", "Movimientos", false, new[]
            {
                Id(), Name(), English(),
                new ColumnSpec("tipo", "Tipo", ColumnKind.Ref, "General", "Tipo del movimiento.", required: true, targets: new[] { Types }),
                new ColumnSpec("categoria", "Categoría", ColumnKind.Choice, "General", "físico, especial o estado.", options: new[] { "fisico", "especial", "estado" }),
                new ColumnSpec("potencia", "Potencia", ColumnKind.Int, "Daño", "0 = sin daño.", min: 0),
                new ColumnSpec("precision", "Precisión", ColumnKind.Text, "Daño", "1-100, o «nunca» (no falla nunca)."),
                new ColumnSpec("pp", "PP", ColumnKind.Int, "General", "", min: 1, max: 64),
                new ColumnSpec("prioridad", "Prioridad", ColumnKind.Int, "General", "", min: -7, max: 7),
                new ColumnSpec("objetivo", "Objetivo", ColumnKind.Text, "General", "rival, propio, todos..."),
                new ColumnSpec("golpes", "Golpes", ColumnKind.Text, "Daño", "1, 2 o 2-5."),
                new ColumnSpec("critico", "Etapa de crítico", ColumnKind.Int, "Daño", "", min: 0),
                new ColumnSpec("dos_turnos", "Dos turnos", ColumnKind.Text, "Daño", "carga, recarga, vuelo... (vacío o no = un turno)."),
                Yes("contacto", "Contacto", "Daño"),
                new ColumnSpec("daño_especial", "Daño especial", ColumnKind.Script, "Daño", "Daño fijo, por nivel..."),
                Yes("respeta_inmunidad", "Respeta inmunidad", "Daño"),
                new ColumnSpec("potencia_mod", "Cambios de potencia", ColumnKind.Script, "Daño", "x2 [si rival.estado=paralysis].", targets: Battle),
                new ColumnSpec("formula_potencia", "Fórmula de potencia", ColumnKind.Script, "Daño", ""),
                new ColumnSpec("requisitos", "Requisitos", ColumnKind.Script, "Efectos", "rival.estado=sleep.", targets: Battle),
                new ColumnSpec("stat_ataque", "Estadística de ataque", ColumnKind.Text, "Daño", ""),
                new ColumnSpec("stat_defensa", "Estadística de defensa", ColumnKind.Text, "Daño", ""),
                Yes("ataca_con_rival", "Usa el ataque del rival", "Daño"),
                new ColumnSpec("etiquetas", "Etiquetas", ColumnKind.Text, "General", "puño, sonido, mordisco..."),
                new ColumnSpec("efectos", "Efectos", ColumnKind.Script, "Efectos", "estado:burn@10.", targets: Battle),
                new ColumnSpec("efecto_z", "Efecto Z", ColumnKind.Script, "Efectos", "", targets: Battle),
                new ColumnSpec("tipo_clima", "Tipo según el clima", ColumnKind.Script, "Efectos", "", targets: Battle),
                new ColumnSpec("animacion", "Animación", ColumnKind.Text, "General", ""),
            }, "Los movimientos: tipo, categoría, potencia, precisión, PP y sus efectos.", "estrella"));

            Register(new CategorySchema(Abilities, "habilidades.csv", "habilidad", "Habilidades", true, new[]
            {
                Id(), Name(), English(),
                new ColumnSpec("categoria", "Categoría", ColumnKind.Choice, "General", "Para buscarla (vacío = se deduce de sus efectos).", options: AbilityCategories.All),
                new ColumnSpec("efectos", "Efectos", ColumnKind.Script, "Efectos", "cuándo: qué (siempre: anula_clima).", targets: Battle),
            }, "Las habilidades: cada una es una lista de efectos «cuándo / si / qué».", "estrella"));

            Register(new CategorySchema(Items, "objetos.csv", "objeto", "Objetos", false, new[]
            {
                Id(), Name(), English(),
                new ColumnSpec("descripcion", "Descripción", ColumnKind.LongText, "General", ""),
                new ColumnSpec("categoria", "Categoría", ColumnKind.Choice, "General", "Dónde va en la mochila: Medicinas, Poké Balls, Bayas...", options: ItemCategories.All),
                new ColumnSpec("precio", "Precio", ColumnKind.Int, "General", "", min: 0),
                Yes("en_combate", "Se usa en combate", "Uso"), Yes("fuera_combate", "Se usa fuera de combate", "Uso"),
                Yes("se_gasta", "Se gasta", "Uso"), Yes("es_baya", "Es una baya", "Uso"),
                new ColumnSpec("efectos", "Efectos", ColumnKind.Script, "Efectos", "al_usar: cura 20.", targets: Battle),
            }, "Los objetos: precio, dónde se usan y sus efectos (curar, capturar, equipados...).", "estrella"));

            Register(new CategorySchema(Natures, "naturalezas.csv", "naturaleza", "Naturalezas", true, new[]
            {
                Id(), Name(), English(),
                new ColumnSpec("sube", "Sube", ColumnKind.Choice, "General", "Estadística que sube.", options: Stats),
                new ColumnSpec("baja", "Baja", ColumnKind.Choice, "General", "Estadística que baja.", options: Stats),
                new ColumnSpec("porcentaje", "Porcentaje", ColumnKind.Int, "General", "10 = ±10 %.", min: 0, max: 100),
            }, "Las naturalezas: qué estadística sube y cuál baja.", "estrella"));

            Register(new CategorySchema(EggGroups, "grupos_huevo.csv", "grupo huevo", "Grupos huevo", false, new[]
            {
                Id(), Name(),
                new ColumnSpec("color", "Color", ColumnKind.Color, "General", ""),
                Yes("no_cria", "No cría", "General", "Los de este grupo no pueden criar (Desconocido)."),
                new ColumnSpec("descripcion", "Descripción", ColumnKind.LongText, "General", ""),
            }, "Los grupos huevo: quién puede criar con quién.", "estrella"));

            Register(new CategorySchema(Curves, "curvas.csv", "curva", "Curvas de experiencia", true, new[]
            {
                Id(), Name(),
                new ColumnSpec("forma", "Forma", ColumnKind.Text, "General", "rapida, media, lenta, erratica..."),
                new ColumnSpec("nivel_max", "Nivel máximo", ColumnKind.Int, "General", "", min: 1),
                new ColumnSpec("multiplicador_xp", "Multiplicador", ColumnKind.Number, "General", "", min: 0),
                new ColumnSpec("tabla", "Tabla propia", ColumnKind.Script, "General", ""),
                new ColumnSpec("tramos", "Tramos", ColumnKind.Script, "General", ""),
            }, "Cuánta experiencia hace falta para cada nivel.", "estrella")
            { BuiltIn = new[] { "fast", "medium_fast", "medium_slow", "slow", "erratic", "fluctuating", "medium" } });

            Register(new CategorySchema(Statuses, "estados.csv", "estado", "Estados", false, new[]
            {
                Id(), Name(),
                Yes("volatil", "Volátil", "General", "Se suma al principal y se va al retirarse."),
                new ColumnSpec("daño_por_turno", "Daño por turno (%)", ColumnKind.Number, "Efecto"),
                Yes("daño_creciente", "Daño creciente", "Efecto"), Yes("cura_por_turno", "Cura en vez de dañar", "Efecto"),
                new ColumnSpec("prob_no_actuar", "No actuar (%)", ColumnKind.Number, "Efecto", min: 0, max: 100),
                Yes("se_cura_al_retirarse", "Se cura al retirarse", "Duración"),
                new ColumnSpec("duracion", "Duración (turnos)", ColumnKind.Int, "Duración", min: 0),
                new ColumnSpec("prob_recuperarse", "Recuperarse (%)", ColumnKind.Number, "Duración", min: 0, max: 100),
                new ColumnSpec("autodaño", "Autodaño (%)", ColumnKind.Number, "Efecto"),
                new ColumnSpec("se_convierte_en", "Se convierte en", ColumnKind.Ref, "Duración", "", targets: new[] { Statuses }),
                new ColumnSpec("modificadores", "Modificadores", ColumnKind.Pairs, "Efecto", "estadistica:multiplicador."),
                new ColumnSpec("tipos_inmunes", "Tipos inmunes", ColumnKind.RefList, "Efecto", "", targets: new[] { Types }),
                new ColumnSpec("duracion_max", "Duración máxima", ColumnKind.Int, "Duración", min: 0),
                Yes("impide_cambio", "Impide cambiarse", "Efecto"), Yes("bloquea_ataques", "Bloquea ataques", "Efecto"),
                Yes("aguanta", "Aguanta con 1 PS", "Efecto"), Yes("cura_al_rival", "Cura al rival", "Efecto"),
                Yes("mas_dificil_si_repite", "Más difícil si se repite", "Efecto"),
                new ColumnSpec("captura_x", "Captura ×", ColumnKind.Number, "Efecto"),
            }, "Los estados alterados (quemado, veneno, confusión...).", "estrella"));

            Register(new CategorySchema(Weathers, "climas.csv", "clima", "Climas", false, new[]
            {
                Id(), Name(),
                new ColumnSpec("turnos", "Turnos", ColumnKind.Int, "General", min: 0),
                new ColumnSpec("potencia_por_tipo", "Potencia por tipo", ColumnKind.Script, "General", "fire:1,5|water:0,5.", targets: new[] { Types }),
                new ColumnSpec("daño_por_turno", "Daño por turno (%)", ColumnKind.Number, "General"),
                new ColumnSpec("tipos_inmunes", "Tipos inmunes", ColumnKind.RefList, "General", "", targets: new[] { Types }),
                new ColumnSpec("color", "Color", ColumnKind.Color, "General"),
            }, "Los climas: qué tipos potencian, si dañan y a quién no.", "estrella"));

            Register(new CategorySchema(SideEffects, "efectos_lado.csv", "efecto de lado", "Efectos de lado", false, new[]
            {
                Id(), Name(), new ColumnSpec("color", "Color", ColumnKind.Color),
                new ColumnSpec("turnos", "Turnos", ColumnKind.Int, min: 0),
                new ColumnSpec("mult_fisico", "× daño físico", ColumnKind.Number), new ColumnSpec("mult_especial", "× daño especial", ColumnKind.Number),
                Yes("sin_bajadas", "Sin bajadas", "General"), Yes("sin_estados", "Sin estados", "General"),
                new ColumnSpec("mult_velocidad", "× velocidad", ColumnKind.Number),
            }, "Reflejo, Pantalla de Luz, Viento Afín...", "estrella"));

            Register(new CategorySchema(Hazards, "trampas.csv", "trampa", "Trampas de campo", true, new[]
            {
                Id(), Name(), new ColumnSpec("color", "Color", ColumnKind.Color),
                new ColumnSpec("capas", "Capas", ColumnKind.Int, min: 1),
                new ColumnSpec("daño_por_capa", "Daño por capa", ColumnKind.Script),
                new ColumnSpec("tipo_daño", "Tipo del daño", ColumnKind.Ref, targets: new[] { Types }),
                new ColumnSpec("estado_por_capa", "Estado por capa", ColumnKind.Script, targets: new[] { Statuses }),
                new ColumnSpec("estadistica", "Estadística", ColumnKind.Text), new ColumnSpec("etapas", "Etapas", ColumnKind.Int),
                new ColumnSpec("tipos_inmunes", "Tipos inmunes", ColumnKind.RefList, targets: new[] { Types }),
                new ColumnSpec("inmune_por_habilidad", "Inmune por habilidad", ColumnKind.RefList, targets: new[] { Abilities }),
                new ColumnSpec("la_retiran", "La retiran", ColumnKind.RefList, targets: new[] { Moves }),
            }, "Púas, Trampa Rocas, Red Viscosa...", "estrella"));

            Register(new CategorySchema(Trainers, "entrenadores.csv", "entrenador", "Entrenadores", false, new[]
            {
                Id(), Name(),
                new ColumnSpec("clase", "Clase", ColumnKind.Text, "General", "Joven, Líder de gimnasio..."),
                new ColumnSpec("nivel_ia", "Nivel de IA", ColumnKind.Int, "IA", "1-7.", min: 0, max: 7),
                new ColumnSpec("ia", "IA", ColumnKind.Text, "IA"),
                Yes("usa_objetos", "Usa objetos", "IA"),
                new ColumnSpec("mochila", "Mochila", ColumnKind.RefAmounts, "IA", "super_potion:1.", targets: new[] { Items }),
                new ColumnSpec("curar_bajo", "Cura por debajo de (%)", ColumnKind.Int, "IA", min: 0, max: 100),
                Yes("puede_cambiar", "Puede cambiar", "IA"),
                new ColumnSpec("movimientos_auto", "Movimientos", ColumnKind.Text, "IA"),
                new ColumnSpec("dinero_base", "Dinero base", ColumnKind.Int, "General", min: 0),
                new ColumnSpec("equipo", "Equipo", ColumnKind.Team, "Equipo", "especie@nivel[mov/mov]{objeto} | ...", required: true, targets: new[] { Species, Moves, Items, Natures, Abilities }),
                new ColumnSpec("frase_inicio", "Frase al empezar", ColumnKind.LongText, "Frases"),
                new ColumnSpec("frase_derrota", "Frase al perder", ColumnKind.LongText, "Frases"),
                new ColumnSpec("frase_victoria", "Frase al ganar", ColumnKind.LongText, "Frases"),
            }, "Los entrenadores: su equipo, cómo piensan, cuánto pagan y qué dicen.", "inicio"));

            Register(new CategorySchema(Teams, "equipos.csv", "equipo", "Equipos prearmados", false, new[]
            {
                Id(), Name(), new ColumnSpec("descripcion", "Descripción", ColumnKind.LongText),
                new ColumnSpec("dinero", "Dinero", ColumnKind.Int, min: 0),
                new ColumnSpec("equipo", "Equipo", ColumnKind.Team, "Equipo", "", required: true, targets: new[] { Species, Moves, Items, Natures, Abilities }),
                new ColumnSpec("mochila", "Mochila", ColumnKind.RefAmounts, "Equipo", "", targets: new[] { Items }),
            }, "Equipos listos para empezar o probar combates.", "inicio"));

            Register(new CategorySchema(Sets, "sets.csv", "set", "Sets de competición", false, new[]
            {
                Id(),
                new ColumnSpec("especie", "Especie", ColumnKind.Ref, "General", "", required: true, targets: new[] { Species }),
                new ColumnSpec("formato", "Formato", ColumnKind.Text), new ColumnSpec("nombre", "Nombre", ColumnKind.Text),
                new ColumnSpec("puntuacion", "Puntuación", ColumnKind.Int, min: 0),
                new ColumnSpec("objeto", "Objeto", ColumnKind.RefList, "General", "Uno o varios a elegir (a,b).", targets: new[] { Items }),
                new ColumnSpec("habilidad", "Habilidad", ColumnKind.RefList, "General", "Una o varias a elegir.", targets: new[] { Abilities }),
                new ColumnSpec("naturaleza", "Naturaleza", ColumnKind.RefList, "General", "Una o varias a elegir.", targets: new[] { Natures }),
                new ColumnSpec("evs", "EVs", ColumnKind.Text), new ColumnSpec("ivs", "IVs", ColumnKind.Text),
                new ColumnSpec("movimientos", "Movimientos", ColumnKind.SlashRefs, "General", "surf/recover/toxic.", targets: new[] { Moves }),
            }, "Los sets de Smogon que usan las IA más fuertes.", "inicio", nameColumn: "nombre"));

            Register(new CategorySchema(Rules, "reglas.csv", "regla", "Reglas del juego", true, new[] { Id(), Name() },
                "Las perillas globales: equipo, nivel máximo, fórmula de daño, IV/EV...", "ajustes"));

            Register(new CategorySchema(Zones, "zonas.csv", "zona", "Zonas salvajes", true, new[]
            {
                Id(), Name(), new ColumnSpec("especies", "Especies", ColumnKind.Script, "General", "", targets: new[] { Species }),
            }, "Zonas salvajes del juego de Unity (los mapas de la aplicación tienen las suyas).", "zona"));
        }
    }
}
