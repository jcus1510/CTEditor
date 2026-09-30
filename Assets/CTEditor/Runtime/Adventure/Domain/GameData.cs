using System;
using System.Collections.Generic;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.GameDefinition.Domain.Abilities;
using CTEditor.GameDefinition.Domain.Catalog;
using CTEditor.GameDefinition.Domain.Growth;
using CTEditor.GameDefinition.Domain.Items;
using CTEditor.GameDefinition.Domain.Hazards;
using CTEditor.GameDefinition.Domain.Battlefield;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Rules;
using CTEditor.GameDefinition.Domain.Stats;
using CTEditor.GameDefinition.Domain.Status;
using CTEditor.GameDefinition.Domain.Types;
using CTEditor.GameDefinition.Domain.Weather;
using CTEditor.GameDefinition.Domain.Trainers;
using CTEditor.Party.Domain;
using CTEditor.Battle.Domain;
using CTEditor.Battle.Domain.Formulas;
using CTEditor.Battle.Domain.Turn;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Adventure.Domain
{
    /// <summary>
    /// TODO el contenido del juego en un solo paquete (especies, movimientos, estados, objetos, reglas...),
    /// ya traducido a dominio puro. La capa de partida lo recibe hecho: le da igual si vino de las
    /// fichas de Unity (ContentLibrary), de un CSV o de una prueba. Así toda la lógica de partida y
    /// combate se puede probar sin abrir Unity.
    ///
    /// Solo especies, movimientos, tabla de tipos y reglas son obligatorios; lo demás, si falta, queda
    /// vacío (un juego sin estados o sin objetos también es válido).
    /// </summary>
    public sealed class GameData
    {
        public ICatalog<SpeciesDef> Species { get; }
        public ICatalog<Move> Moves { get; }
        public ICatalog<StatusConditionDefinition> Statuses { get; }
        public ICatalog<AbilityDefinition> Abilities { get; }
        public ICatalog<ItemDefinition> Items { get; }
        public ICatalog<WeatherDefinition> Weathers { get; }
        public ICatalog<Nature> Natures { get; }
        public ICatalog<GrowthCurve> Curves { get; }
        /// <summary>Los tipos (para mostrar su nombre: "Fuego" en vez de "fire").</summary>
        public ICatalog<ElementType> Types { get; }
        /// <summary>Trampas de campo (Púas, Trampa Rocas...).</summary>
        public ICatalog<HazardDefinition> Hazards { get; }
        /// <summary>Efectos de lado (Reflejo, Pantalla de Luz, Neblina...).</summary>
        public ICatalog<SideConditionDefinition> SideConditions { get; }
        public TypeChart TypeChart { get; }
        public Ruleset Ruleset { get; }
        public IStatGrowthFormula Growth { get; }

        private readonly Dictionary<int, AiProfile> _aiProfiles = new Dictionary<int, AiProfile>();
        private readonly Dictionary<string, AiProfile> _customAi = new Dictionary<string, AiProfile>();

        /// <summary>El nivel de IA 1-7 del autor (sus fichas «Niveles de IA») o el clásico si no lo definió.</summary>
        public AiProfile AiProfileFor(int level)
        {
            level = Math.Max(AiProfile.MinLevel, Math.Min(AiProfile.MaxLevel, level));
            return _aiProfiles.TryGetValue(level, out var p) ? p : AiProfile.Classic(level);
        }

        /// <summary>Una IA por su id (personalizada o de nivel). Null si no existe.</summary>
        public AiProfile AiProfileById(string id)
            => !string.IsNullOrWhiteSpace(id) && _customAi.TryGetValue(id.Trim(), out var p) ? p : null;

        /// <summary>El perfil de IA de un entrenador: su IA personalizada si la tiene (y existe); si no, la de su nivel 1-7.</summary>
        public AiProfile AiProfileFor(TrainerDefinition trainer)
            => AiProfileById(trainer?.AiProfileId) ?? AiProfileFor(trainer?.AiLevel ?? 1);

        public GameData(
            ICatalog<SpeciesDef> species,
            ICatalog<Move> moves,
            TypeChart typeChart,
            Ruleset ruleset,
            ICatalog<StatusConditionDefinition> statuses = null,
            ICatalog<AbilityDefinition> abilities = null,
            ICatalog<ItemDefinition> items = null,
            ICatalog<WeatherDefinition> weathers = null,
            ICatalog<Nature> natures = null,
            ICatalog<GrowthCurve> curves = null,
            IStatGrowthFormula growth = null,
            ICatalog<ElementType> types = null,
            ICatalog<HazardDefinition> hazards = null,
            ICatalog<SideConditionDefinition> sideConditions = null,
            IEnumerable<AiProfile> aiProfiles = null)
        {
            if (aiProfiles != null)
                foreach (var p in aiProfiles)
                {
                    if (p == null) continue;
                    if (!string.IsNullOrEmpty(p.Id)) _customAi[p.Id] = p;   // cualquier IA se puede pedir por id
                    if (!p.IsCustom) _aiProfiles[p.Level] = p;              // las personalizadas NO sustituyen a su nivel
                }
            Species = species ?? throw new ArgumentNullException(nameof(species));
            Moves = moves ?? throw new ArgumentNullException(nameof(moves));
            TypeChart = typeChart ?? throw new ArgumentNullException(nameof(typeChart));
            Ruleset = ruleset ?? throw new ArgumentNullException(nameof(ruleset));
            Statuses = statuses ?? MemoryCatalog<StatusConditionDefinition>.Empty();
            Abilities = abilities ?? MemoryCatalog<AbilityDefinition>.Empty();
            Items = items ?? MemoryCatalog<ItemDefinition>.Empty();
            Weathers = weathers ?? MemoryCatalog<WeatherDefinition>.Empty();
            Natures = natures ?? MemoryCatalog<Nature>.Empty();
            Curves = curves ?? MemoryCatalog<GrowthCurve>.Empty();
            Types = types ?? MemoryCatalog<ElementType>.Empty();
            Hazards = hazards ?? MemoryCatalog<HazardDefinition>.Empty();
            SideConditions = sideConditions ?? MemoryCatalog<SideConditionDefinition>.Empty();
            Growth = growth ?? new ClassicStatGrowthFormula();
        }

        /// <summary>Las reglas de la aventura (huir, capturar, dinero...).</summary>
        public AdventureRules Rules => Ruleset.Adventure;

        // ---------------- Consultas cómodas ----------------

        private static GrowthCurve _defaultCurve;

        /// <summary>La curva de experiencia de una especie (o la "Normal" clásica si no eligió ninguna).</summary>
        public GrowthCurve CurveFor(SpeciesDef species)
        {
            if (species != null && species.GrowthCurveId.HasValue && Curves.TryGet(species.GrowthCurveId.Value, out var curve))
                return curve;
            return _defaultCurve ?? (_defaultCurve = GrowthCurvePresets.MediumFast(
                new Id<GrowthCurve>("default_medium_fast"), "Normal", 100));
        }

        public bool TryGetSpecies(Id<SpeciesDef> id, out SpeciesDef species) => Species.TryGet(id, out species);

        public SpeciesDef SpeciesOf(MonsterInstance mon)
            => mon != null && Species.TryGet(mon.SpeciesId, out var s) ? s : null;

        /// <summary>Nombre visible de un monstruo: su mote o, si no tiene, el nombre de su especie.</summary>
        public string NameOf(MonsterInstance mon)
        {
            if (mon == null) return "";
            if (!string.IsNullOrWhiteSpace(mon.Nickname)) return mon.Nickname;
            var s = SpeciesOf(mon);
            return s != null ? s.DisplayName : mon.SpeciesId.Value;
        }

        public string MoveName(Id<Move> id) => Moves.TryGet(id, out var m) ? m.DisplayName : id.Value;

        /// <summary>Nombre visible de una trampa de campo ("Púas"); si no está en el catálogo, su id.</summary>
        public string HazardName(string id)
            => !string.IsNullOrEmpty(id) && Hazards.TryGet(new Id<HazardDefinition>(id), out var h) ? h.DisplayName : id;

        /// <summary>Nombre visible de un efecto de lado ("Reflejo"); si no está en el catálogo, su id.</summary>
        public string SideConditionName(string id)
            => !string.IsNullOrEmpty(id) && SideConditions.TryGet(new Id<SideConditionDefinition>(id), out var d) ? d.DisplayName : id;

        /// <summary>Nombre visible de un tipo ("Fuego"); si no está en el catálogo, su id.</summary>
        public string TypeName(Id<ElementType> id) => Types.TryGet(id, out var t) ? t.DisplayName : id.Value;

        /// <summary>Nombre visible de una habilidad ("Intimidación"); si no está en el catálogo, su id.</summary>
        public string AbilityName(string id)
            => !string.IsNullOrEmpty(id) && Abilities.TryGet(new Id<AbilityDefinition>(id), out var a) ? a.DisplayName : id;

        public string ItemName(string id)
            => !string.IsNullOrEmpty(id) && Items.TryGet(new Id<ItemDefinition>(id), out var it) ? it.DisplayName : id;

        public int MaxPpOf(Id<Move> id) => Moves.TryGet(id, out var m) ? Math.Max(1, m.MaxPp) : 1;

        public bool TryGetItem(string id, out ItemDefinition item)
        {
            item = null;
            return !string.IsNullOrWhiteSpace(id) && Items.TryGet(new Id<ItemDefinition>(id), out item);
        }

        /// <summary>
        /// El contexto para las condiciones de evolución: hora, lugar, clima y marcas de la partida, su
        /// equipo y cómo saber el tipo de un movimiento o de una especie.
        /// </summary>
        public EvolutionContext EvolutionContextFor(PlayerSave save)
        {
            var ctx = new EvolutionContext
            {
                MoveType = id => Moves.TryGet(id, out var m) ? m.Type : (Id<ElementType>?)null,
                SpeciesTypes = id => Species.TryGet(id, out var sp) ? sp.Types : null,
            };
            if (save != null)
            {
                ctx.Hour = save.World.Hour;
                ctx.LocationId = save.World.LocationId ?? "";
                ctx.MapWeatherId = save.World.MapWeatherId ?? "";
                ctx.Party = save.Party.Members;
                ctx.HasFlag = save.World.HasFlag;
            }
            return ctx;
        }

        // ---------------- Combate ----------------

        /// <summary>
        /// El resolvedor de turnos con TODO el contenido y las reglas del juego (captura clásica,
        /// experiencia clásica, PP y Forcejeo del Ruleset, climas, objetos...).
        /// </summary>
        public TurnResolver CreateResolver(IRng rng)
        {
            Id<Move>? struggle = string.IsNullOrWhiteSpace(Ruleset.StruggleMoveId) || !Moves.Contains(new Id<Move>(Ruleset.StruggleMoveId))
                ? (Id<Move>?)null : new Id<Move>(Ruleset.StruggleMoveId);
            return new TurnResolver(Moves, TypeChart, new ClassicDamageFormula(), rng, Statuses,
                new ClassicCatchFormula(), Abilities, new ClassicXpFormula(), awardEffortValues: Ruleset.MaxEvPerStat > 0,
                usePp: Ruleset.UsePp, struggleMove: struggle, weathers: Weathers, rules: BattleRules.From(Ruleset), items: Items, hazards: Hazards, sideConditions: SideConditions);
        }

        /// <summary>
        /// La FOTO de combate de un monstruo con su estado ACTUAL (snapshot-in). El id de combate es el
        /// mismo id del monstruo: así los resultados vuelven al individuo correcto.
        /// </summary>
        public BattleParticipant Snapshot(MonsterInstance mon)
        {
            var species = SpeciesOf(mon) ?? throw new InvalidOperationException(
                $"La especie '{mon.SpeciesId.Value}' no existe en el contenido.");
            return new BattleParticipant(
                new Id<BattleParticipant>(mon.Id.Value), mon.SpeciesId, mon.Level.Value,
                mon.Stats, mon.CurrentHp, species.Types, mon.Moves, mon.Status, species.AbilityFor(mon.AbilitySlot),
                species.BaseExpYield, species.EvYield, mon.CurrentPp, mon.Friendship, mon.HeldItem, species.CatchRate, species.Dex.WeightKg,
                mon.Gender, species.Evolutions.Count > 0);
        }
    }

    /// <summary>Azar por defecto de la capa de partida (System.Random). Con semilla = reproducible.</summary>
    public sealed class DefaultRng : IRng
    {
        private readonly Random _random;
        public DefaultRng() { _random = new Random(); }
        public DefaultRng(int seed) { _random = new Random(seed); }
        public int Next(int minInclusive, int maxExclusive) => maxExclusive <= minInclusive ? minInclusive : _random.Next(minInclusive, maxExclusive);
        public float NextFloat() => (float)_random.NextDouble();
    }
}
