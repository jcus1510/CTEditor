using System.Collections.Generic;
using UnityEngine;
using CTEditor.SharedKernel.ValueObjects;
using CTEditor.SharedKernel.Abstractions;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;
using CTEditor.GameDefinition.Infrastructure.Acl;
using CTEditor.Party.Domain;
using CTEditor.Adventure.Domain;
using CTEditor.Bootstrap.Platform;
using SpeciesDef = CTEditor.GameDefinition.Domain.Species.Species;

namespace CTEditor.Bootstrap
{
    /// <summary>
    /// LA PARTIDA DEL JUGADOR en la escena: su equipo, su PC, su mochila y su dinero (PlayerSave).
    ///
    /// Es la única fuente de verdad del jugador: vive entre combates (y entre escenas si lo marcas).
    /// El combate recibe fotos de su equipo y, al acabar, los resultados vuelven aquí (niveles, PS,
    /// PP, estados, capturas, dinero...). Toda la lógica es pura (CTEditor.Adventure); este componente
    /// solo decide CÓMO empieza la partida:
    ///
    ///   • Equipo prearmado: un equipo listo (con su dinero y su mochila) — ideal para probar.
    ///   • Desde cero: un solo monstruo inicial; lo demás, a capturarlo.
    ///   • Equipo escrito aquí: la lista de miembros de abajo (formato antiguo).
    /// </summary>
    public sealed class PartyHolder : MonoBehaviour
    {
        public enum StartMode { Preset, Starter, Manual }

        /// <summary>La partida "actual". Cualquier escena de combate la toma de aquí.</summary>
        public static PartyHolder Current { get; private set; }

        [Header("Cómo empieza la partida")]
        [Tooltip("Equipo prearmado (recomendado para probar), desde cero con un inicial, o la lista de abajo.")]
        [SerializeField] private StartMode startMode = StartMode.Preset;
        [Tooltip("El equipo prearmado con el que se empieza (CTEditor → Personajes → Equipos prearmados).")]
        [SerializeField] private TeamPresetData teamPreset;
        [Tooltip("Desde cero: el monstruo inicial.")]
        [SerializeField] private SpeciesData starterSpecies;
        [Tooltip("Desde cero: su nivel.")]
        [SerializeField, Min(1)] private int starterLevel = 5;
        [SerializeField] private string playerName = "Rojo";

        [Header("Equipo escrito aquí (solo con «Equipo escrito aquí»)")]
        [SerializeField] private BattlerSpec[] members;

        [Header("Mochila inicial (desde cero / equipo escrito aquí)")]
        [Tooltip("Objetos con los que empieza el jugador (id y cantidad). El equipo prearmado trae su propia mochila.")]
        [SerializeField] private StartingItem[] startingItems =
        {
            new StartingItem { itemId = "potion", quantity = 5 },
            new StartingItem { itemId = "poke_ball", quantity = 10 },
        };

        [Header("Mundo")]
        [Tooltip("Hora del juego: la del reloj del ordenador (como en los juegos desde la 2ª gen.) o una fija para probar " +
                 "evoluciones de día o de noche.")]
        [SerializeField] private bool useSystemClock = true;
        [Tooltip("Hora fija (0-23) si no se usa el reloj del ordenador.")]
        [SerializeField, Range(0, 23)] private int fixedHour = 12;
        [Tooltip("Lugar de inicio (id). Lo usarán los mapas; sirve ya para evoluciones «en un lugar».")]
        [SerializeField] private string startLocationId = "";

        [Header("Opciones")]
        [Tooltip("Mantener la partida viva al cambiar de escena (mundo ↔ combate).")]
        [SerializeField] private bool persistAcrossScenes = true;
        [Tooltip("Semilla del azar (IVs, naturalezas, combates). 0 = distinta en cada partida; otro valor = siempre igual (útil para probar).")]
        [SerializeField] private int geneticsSeed = 0;

        [System.Serializable]
        public struct StartingItem
        {
            [ContentIdReference(typeof(ItemData))] public string itemId;
            [Min(1)] public int quantity;
        }

        private PlayerSave _save;
        private IRng _rng;
        private readonly List<string> _problems = new List<string>();

        /// <summary>La partida del jugador (se crea la primera vez que se pide).</summary>
        public PlayerSave Save { get { if (_save == null) Build(); return _save; } }

        /// <summary>El contenido del juego ya traducido (para combates y pantallas).</summary>
        public GameData Data => ContentLibrary.GameData;

        /// <summary>El azar de la partida (con semilla si la pusiste).</summary>
        public IRng Rng => _rng ?? (_rng = geneticsSeed != 0 ? new SystemRng(geneticsSeed) : new SystemRng());

        /// <summary>Atajos cómodos.</summary>
        public CTEditor.Party.Domain.Party Party => Save?.Party;
        public Bag Bag => Save?.Bag;

        /// <summary>Avisos al crear la partida (especies u objetos que no existen...).</summary>
        public IReadOnlyList<string> Problems => _problems;

        private void Awake()
        {
            // Singleton persistente: el primero gana; si ya hay uno (volviste de combate), este sobra.
            if (Current != null && Current != this) { Destroy(gameObject); return; }
            Current = this;
            if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
            Build();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Crea la partida una sola vez (idempotente).</summary>
        public void Build()
        {
            if (_save != null) return;
            var data = Data;
            if (data == null) { Debug.LogError("[CTEditor] Falta contenido: necesitas la tabla de tipos y las reglas del juego (Centro de Contenido)."); return; }
            _problems.Clear();

            switch (startMode)
            {
                case StartMode.Preset when teamPreset != null:
                    _save = TeamBuilder.NewGame(TrainerMapper.ToDomain(teamPreset), data, Rng, playerName, _problems);
                    break;
                case StartMode.Starter when starterSpecies != null:
                    _save = TeamBuilder.NewGameWithStarter(new Id<SpeciesDef>(starterSpecies.Id), starterLevel, data, Rng, playerName, Items(), _problems);
                    break;
                default:
                    // Escenas de antes (solo con la lista de miembros): se usa la lista sin avisar.
                    bool hasList = members != null && System.Array.Exists(members, m => m != null && m.Species != null);
                    if (startMode != StartMode.Manual && !hasList)
                        _problems.Add(startMode == StartMode.Preset ? "No elegiste un equipo prearmado: se usa la lista de miembros." : "No elegiste el monstruo inicial: se usa la lista de miembros.");
                    _save = BuildManual(data);
                    break;
            }

            _save.World.LocationId = startLocationId ?? "";
            UpdateClock();
            foreach (var p in _problems) Debug.LogWarning("[CTEditor] " + p);
            if (_save.Party.Count == 0) Debug.LogError("[CTEditor] La partida empezó sin ningún monstruo. Elige un equipo prearmado o un inicial en «Partida del jugador».");
        }

        private void Update() => UpdateClock();

        /// <summary>Pone la hora del juego (reloj del ordenador u hora fija).</summary>
        public void UpdateClock()
        {
            if (_save != null) _save.World.Hour = useSystemClock ? System.DateTime.Now.Hour : fixedHour;
        }

        /// <summary>Empieza la partida de nuevo (mismo modo de inicio): equipo, dinero y mochila como al principio.</summary>
        public void ResetGame()
        {
            _save = null;
            ContentLibrary.Reload();
            Build();
        }

        // El formato antiguo: la lista de BattlerSpec del Inspector.
        private PlayerSave BuildManual(GameData data)
        {
            var save = new PlayerSave(data.Ruleset, playerName);
            if (members != null)
                foreach (var spec in members)
                {
                    var mon = BattlerBuilder.Create(spec, save.NewMonsterId().Value, data.Ruleset, data.Growth, Rng, out _);
                    if (mon != null) save.Receive(mon);
                }
            foreach (var (id, qty) in Items())
                if (data.TryGetItem(id, out _)) save.Bag.Add(id, qty);
                else _problems.Add($"El objeto '{id}' de la mochila inicial no existe.");
            return save;
        }

        private List<(string, int)> Items()
        {
            var list = new List<(string, int)>();
            if (startingItems != null)
                foreach (var it in startingItems)
                    if (!string.IsNullOrWhiteSpace(it.itemId)) list.Add((it.itemId.Trim(), Mathf.Max(1, it.quantity)));
            return list;
        }

        /// <summary>Busca un monstruo del jugador (equipo o PC) por id. Null si no está.</summary>
        public MonsterInstance FindMonster(string id) => Save?.Find(new Id<MonsterInstance>(id));

        /// <summary>Nombre visible de un monstruo del jugador (mote o especie).</summary>
        public string NameOf(MonsterInstance mon) => Data != null ? Data.NameOf(mon) : mon?.SpeciesId.Value;
    }
}
