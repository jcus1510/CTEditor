using System;
using System.Collections.Generic;
using System.Linq;

namespace CTEditor.World.Domain
{
    /// <summary>Un miembro del equipo de prueba.</summary>
    public sealed class TestMember
    {
        public string SpeciesId { get; set; }
        public int Level { get; set; }
        public TestMember(string speciesId, int level) { SpeciesId = speciesId ?? ""; Level = Math.Max(1, Math.Min(100, level)); }
    }

    /// <summary>
    /// PERFIL DE PRUEBA: con qué se empieza a jugar desde el editor, sin pasar por la historia: equipo, medallas,
    /// objetos, dinero, interruptores encendidos y la hora del día (o la del reloj). «Ruta 3 con 2 medallas por la
    /// noche» se prueba en un clic.
    /// </summary>
    public sealed class TestProfile
    {
        public const int MaxParty = 6, MaxBadges = 16;

        public string Id { get; }
        public string Name { get; set; }
        public List<TestMember> Party { get; } = new List<TestMember>();
        public int Badges { get; set; }
        public int Money { get; set; } = 3000;
        /// <summary>Objeto → cantidad.</summary>
        public Dictionary<string, int> Items { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Flags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Hora fija (null = la del reloj del ordenador).</summary>
        public TimeOfDay? Time { get; set; }

        public TestProfile(string id, string name) { Id = id; Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(); }

        public bool FlagOn(string flag) => !string.IsNullOrEmpty(flag) && Flags.Contains(flag);

        /// <summary>La hora con la que se juega (la fija o la del reloj).</summary>
        public TimeOfDay TimeNow(DateTime clock) => Time ?? EncounterResolver.TimeAt(clock.Hour);

        /// <summary>Problemas del perfil (equipo de más de 6, niveles, medallas...). Vacío = bien.</summary>
        public IReadOnlyList<string> Problems(Func<string, bool> speciesExists = null)
        {
            var list = new List<string>();
            if (Party.Count > MaxParty) list.Add($"El equipo tiene {Party.Count} miembros: como mucho {MaxParty}.");
            if (Badges < 0 || Badges > MaxBadges) list.Add($"Medallas: entre 0 y {MaxBadges}.");
            if (Money < 0) list.Add("El dinero no puede ser negativo.");
            foreach (var m in Party)
                if (string.IsNullOrEmpty(m.SpeciesId)) list.Add("Hay un miembro del equipo sin especie.");
                else if (speciesExists != null && !speciesExists(m.SpeciesId)) list.Add($"«{m.SpeciesId}» no está en los datos del proyecto.");
            foreach (var kv in Items.Where(kv => kv.Value <= 0)) list.Add($"«{kv.Key}»: cantidad {kv.Value}.");
            return list;
        }

        public TestProfile Clone(string id = null, string name = null)
        {
            var p = new TestProfile(id ?? Id, name ?? Name) { Badges = Badges, Money = Money, Time = Time };
            p.Party.AddRange(Party.Select(m => new TestMember(m.SpeciesId, m.Level)));
            foreach (var kv in Items) p.Items[kv.Key] = kv.Value;
            p.Flags.UnionWith(Flags);
            return p;
        }

        /// <summary>El de siempre si no hay ninguno: sin equipo, sin medallas, la hora del reloj.</summary>
        public static TestProfile Default() => new TestProfile("normal", "Normal (como al empezar)");
    }

    /// <summary>Dónde se guardan los perfiles de prueba del proyecto.</summary>
    public interface ITestProfileRepository
    {
        IReadOnlyList<TestProfile> Load();
        void Save(IReadOnlyList<TestProfile> profiles);
    }
}
