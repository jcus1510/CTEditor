using System.Linq;
using NUnit.Framework;
using CTEditor.GameDefinition.Domain.Moves;
using CTEditor.GameDefinition.Domain.Conditions;
using CTEditor.GameDefinition.Editor;
using CTEditor.GameDefinition.Editor.Csv;
using CTEditor.GameDefinition.Infrastructure.ScriptableObjects;

namespace CTEditor.Tests.EditMode
{
    /// <summary>
    /// Excel/CSV: lectura y escritura compatibles con Excel en español, y los formatos de celda
    /// (efectos, learnset, evoluciones...) con ida y vuelta sin pérdidas.
    /// </summary>
    public class CsvTests
    {
        // ---------------- Pack: restaurar / reparar una sola ficha ----------------

        [Test]
        public void Pack_filter_keeps_headers_and_only_the_requested_ids()
        {
            var t = CsvTable.Parse("id;nombre\nconfusion;Confusión\nember;Ascuas\n tackle ;Placaje\n");
            var one = CTEditor.GameDefinition.Editor.PackTools.Filter(t, new[] { "confusion", "tackle" });
            Assert.AreEqual(2, one.Headers.Count);
            Assert.AreEqual(2, one.Rows.Count);                 // el id se compara sin espacios
            Assert.AreEqual("Confusión", one.Rows[0]["nombre"]);
            Assert.AreEqual(3, CTEditor.GameDefinition.Editor.PackTools.Filter(t, null).Rows.Count); // null = todas
            Assert.AreEqual(0, CTEditor.GameDefinition.Editor.PackTools.Filter(t, new string[0]).Rows.Count);
        }

        // ---------------- CsvTable ----------------

        [Test]
        public void Round_trip_keeps_separators_quotes_and_line_breaks_inside_cells()
        {
            var t = new CsvTable(new[] { "id", "nombre", "nota" });
            t.AddRow(new System.Collections.Generic.Dictionary<string, string> { ["id"] = "a", ["nombre"] = "Uno;Dos", ["nota"] = "dice \"hola\"\ny adiós" });
            var back = CsvTable.Parse(t.ToText());
            Assert.AreEqual(1, back.Rows.Count);
            Assert.AreEqual("Uno;Dos", back.Rows[0]["nombre"]);
            Assert.AreEqual("dice \"hola\"\ny adiós", back.Rows[0]["nota"]);
        }

        [Test]
        public void Detects_comma_separator_BOM_and_sep_line_and_skips_empty_rows()
        {
            var comma = CsvTable.Parse("﻿id,power\r\nember,40\r\n,\r\n");
            Assert.AreEqual(new[] { "id", "power" }, comma.Headers.ToArray());
            Assert.AreEqual(1, comma.Rows.Count);
            Assert.AreEqual("40", comma.Rows[0]["POWER"], "las cabeceras no distinguen mayúsculas");

            var sep = CsvTable.Parse("sep=,\nid,x\nb,2\n");
            Assert.AreEqual("2", sep.Rows[0]["x"]);
            Assert.AreEqual(3, sep.LineNumbers[0], "la línea cuenta desde el principio del archivo");
        }

        [Test]
        public void Numbers_accept_comma_or_dot_and_are_written_with_comma()
        {
            Assert.IsTrue(CsvTable.TryNumber("12,5", out float a)); Assert.AreEqual(12.5f, a, 1e-4);
            Assert.IsTrue(CsvTable.TryNumber("12.5", out float b)); Assert.AreEqual(12.5f, b, 1e-4);
            Assert.IsFalse(CsvTable.TryInt("2,5", out _));
            Assert.AreEqual("0,5", CsvTable.Number(0.5f));
            Assert.AreEqual("2", CsvTable.Number(2f));
            Assert.IsTrue(CsvTable.TryBool("Sí", out bool yes) && yes);
            Assert.IsFalse(CsvTable.TryBool("quizás", out _));
        }

        // ---------------- Celdas ----------------

        [Test]
        public void Effects_round_trip()
        {
            string cell = "estado:burn@10|drenar:50|retroceso_ps:25|stat_propio:speed:+2|stat:attack:-1@30|amedrentar@30";
            var effects = CsvCodecs.ParseEffects(cell);
            Assert.AreEqual(6, effects.Count);
            Assert.AreEqual(MoveEffectKind.InflictStatus, effects[0].Kind);
            Assert.AreEqual(10f, effects[0].Chance);
            Assert.AreEqual(EffectTarget.Self, effects[3].Target);
            Assert.AreEqual(2, effects[3].Stages);
            Assert.AreEqual(cell, CsvCodecs.FormatEffects(effects));
        }

        [Test]
        public void Hazard_and_force_switch_effects_round_trip()
        {
            // Trampa en el lado rival, quitar las propias, quitar una concreta del rival y Rugido.
            string cell = "trampa:spikes|quitar_trampas|quitar_trampas_rival:stealth_rock|forzar_cambio";
            var fx = CsvCodecs.ParseEffects(cell);
            Assert.AreEqual(4, fx.Count);
            Assert.AreEqual(MoveEffectKind.SetHazard, fx[0].Kind);
            Assert.AreEqual("spikes", fx[0].Hazard);
            Assert.AreEqual(MoveEffectKind.ClearHazards, fx[1].Kind);
            Assert.AreEqual(EffectTarget.Self, fx[1].Target);
            Assert.AreEqual(EffectTarget.Opponent, fx[2].Target);
            Assert.AreEqual("stealth_rock", fx[2].Hazard);
            Assert.AreEqual(MoveEffectKind.ForceSwitch, fx[3].Kind);
            Assert.AreEqual(cell, CsvCodecs.FormatEffects(fx));
        }

        [Test]
        public void Combat_closure_effects_round_trip()
        {
            // Reflejo, Niebla (los dos lados), Foco Energía, Sustituto, Anulación, Otra Vez, Saña, Furia,
            // Metrónomo, Espejo, Mimético, Transformación, Conversión, Ida y Vuelta, Relevo y Teletransporte.
            string cell = "lado:reflect|reiniciar_etapas|reiniciar_etapas_rival|foco|sustituto|anular:4|otra_vez:3|desenfreno:3:confusion|furia" +
                          "|metronomo|espejo|mimetico|transformarse|cambiar_tipo:fire|cambio_propio|relevo|teletransporte";
            var fx = CsvCodecs.ParseEffects(cell);
            Assert.AreEqual(17, fx.Count);
            Assert.AreEqual(MoveEffectKind.SetSideCondition, fx[0].Kind);
            Assert.AreEqual("reflect", fx[0].Side);
            Assert.AreEqual(EffectTarget.Self, fx[0].Target);
            Assert.AreEqual(EffectTarget.Opponent, fx[2].Target);
            Assert.AreEqual(2, fx[3].Stages, "Foco Energía: +2 por defecto");
            Assert.AreEqual(25f, fx[4].Amount, "Sustituto: 25 % por defecto");
            Assert.AreEqual(4, fx[5].Turns);
            Assert.AreEqual("confusion", fx[7].Status);
            Assert.AreEqual("fire", fx[13].TypeId);
            Assert.AreEqual(0, fx[14].Stages);
            Assert.AreEqual(1, fx[15].Stages, "Relevo pasa las etapas");
            Assert.AreEqual(cell, CsvCodecs.FormatEffects(fx));

            Assert.AreEqual((FixedDamageKind.ReturnPhysical, 0), CsvCodecs.ParseSpecialDamage("devolver_fisico"));
            Assert.AreEqual((FixedDamageKind.ReturnSpecial, 150), CsvCodecs.ParseSpecialDamage("devolver_especial:150"));
            Assert.AreEqual("venganza", CsvCodecs.FormatSpecialDamage(FixedDamageKind.Bide, 0));
        }

        [Test]
        public void Bad_cells_raise_readable_errors()
        {
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseEffects("volar:3"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseEffects("estado:burn@150"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseLearnset("tackle"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseEvolutions("ivysaur"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseHits("5-2"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseCategory("magico"));
            Assert.Throws<CsvCellException>(() => CsvCodecs.ParseTarget("luna"));
        }

        [Test]
        public void Lists_learnsets_evolutions_and_enums_round_trip()
        {
            var learn = CsvCodecs.ParseLearnset("1:tackle | 7:leech_seed");
            Assert.AreEqual((7, "leech_seed"), learn[1]);
            Assert.AreEqual("1:tackle|7:leech_seed", CsvCodecs.FormatLearnset(learn));

            var evo = CsvCodecs.ParseEvolutions("ivysaur@16|raichu@objeto:thunder_stone|crobat@amistad|alakazam@intercambio");
            Assert.AreEqual("ivysaur", evo[0].Species);
            Assert.AreEqual(16, evo[0].Level);
            Assert.AreEqual("thunder_stone", evo[1].ItemId);
            Assert.AreEqual(CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Friendship, evo[2].Method);
            Assert.AreEqual(CTEditor.GameDefinition.Domain.Species.EvolutionMethod.Trade, evo[3].Method);
            Assert.AreEqual("ivysaur@16|raichu@objeto:thunder_stone|crobat@amistad|alakazam@intercambio", CsvCodecs.FormatEvolutions(evo));

            Assert.AreEqual((2, 5), CsvCodecs.ParseHits("2-5"));
            Assert.AreEqual("2-5", CsvCodecs.FormatHits(2, 5));
            Assert.AreEqual(MoveCategory.Special, CsvCodecs.ParseCategory("Especial"));
            Assert.AreEqual(TwoTurnKind.Recharge, CsvCodecs.ParseTwoTurn(CsvCodecs.FormatTwoTurn(TwoTurnKind.Recharge)));
            Assert.AreEqual(MoveTarget.Self, CsvCodecs.ParseTarget(CsvCodecs.FormatTarget(MoveTarget.Self)));
            Assert.AreEqual(new[] { ("attack", 1), ("speed", 2) }, CsvCodecs.ParsePairs("attack:1|speed:2", "evs").ToArray());
        }

        // ---------------- Esquema automático ----------------

        [Test]
        public void Reflective_schema_builds_one_column_per_saved_field()
        {
            var nature = new CsvReflectiveSchema<NatureData>("n.csv", "N", "Natures", 1);
            var headers = nature.ColumnHelp.Select(h => h.header).ToList();
            Assert.AreEqual("id", headers[0]);
            Assert.AreEqual("nombre", headers[1]);
            CollectionAssert.Contains(headers, "sube");        // cabeceras en español
            CollectionAssert.Contains(headers, "porcentaje");

            var curve = new CsvReflectiveSchema<GrowthCurveData>("c.csv", "C", "Curves", 1);
            var segments = curve.ColumnHelp.First(h => h.header == "tramos");
            StringAssert.Contains("desde_nivel:formula", segments.help);
            var formula = curve.ColumnHelp.First(h => h.header == "forma");
            StringAssert.Contains("Errática", formula.help);
        }

        [Test]
        public void All_schemas_have_unique_files_and_an_id_column()
        {
            var all = CsvSchemas.All();
            Assert.AreEqual(all.Count, all.Select(s => s.FileName).Distinct().Count());
            foreach (var s in all.Where(s => !(s is TypeChartCsvSchema)))
                Assert.AreEqual("id", s.ColumnHelp.First().header, s.FileName);
            Assert.IsTrue(all.Select(s => s.Order).SequenceEqual(all.Select(s => s.Order).OrderBy(o => o)));
        }

        // ---------------- Lote A: condiciones y modificadores en texto ----------------

        [Test]
        public void Conditions_round_trip_through_their_short_text()
        {
            string[] samples =
            {
                "rival.vida<=50", "propio.estado", "!propio.estado", "rival.estado=poison", "rival.tipo=water",
                "clima=rain", "propio.amistad>=200", "propio.nivel>30", "propio.dif_nivel>0", "rival.etapa:attack<0",
                "rival.ya_actuo", "mov.tipo=fire", "mov.categoria=especial", "mov.potencia<=60", "mov.contacto",
                "mov.etiqueta=puño", "azar<30"
            };
            foreach (var text in samples)
            {
                var c = ConditionText.Parse(text);
                Assert.AreEqual(text, ConditionText.Format(c), text);
                StringAssert.Contains("", ConditionText.Describe(c)); // siempre hay una frase
            }
            Assert.AreEqual(ConditionKind.HpPercent, ConditionText.Parse("rival.vida<50").Kind);
            Assert.AreEqual(Comparison.Less, ConditionText.Parse("rival.vida<50").Comparison);
            Assert.AreEqual(ConditionSubject.Other, ConditionText.Parse("rival.vida<50").Subject);
            Assert.IsTrue(ConditionText.Parse("no propio.estado").Negate);
            Assert.Throws<System.FormatException>(() => ConditionText.Parse("rival.suerte>3"));
            Assert.Throws<System.FormatException>(() => ConditionText.Parse("rival.vida<"));
        }

        [Test]
        public void Effects_with_conditions_shared_rolls_and_new_kinds_round_trip()
        {
            string cell = "stat_propio:attack:+1@10|&stat_propio:defense:+1|curar_rival:50|curar_estado|curar_estado_rival:burn|clima:rain:8|stat_propio:attack:+2 [si propio.vida<=50 & clima=sun]";
            var fx = CsvCodecs.ParseEffects(cell);
            Assert.AreEqual(7, fx.Count);
            Assert.IsTrue(fx[1].Shared);
            Assert.AreEqual(MoveEffectKind.Heal, fx[2].Kind);
            Assert.AreEqual(EffectTarget.Opponent, fx[2].Target);
            Assert.AreEqual(MoveEffectKind.CureStatus, fx[3].Kind);
            Assert.AreEqual("burn", fx[4].Status);
            Assert.AreEqual(8, fx[5].WeatherTurns);
            Assert.AreEqual(2, fx[6].Conditions.Count);
            Assert.AreEqual(cell, CsvCodecs.FormatEffects(fx));
        }

        [Test]
        public void Power_modifiers_round_trip()
        {
            var mods = ConditionText.ParseModifiers("x2 [si propio.estado] | x1,5 [si clima=rain & mov.tipo=water] | x0,5");
            Assert.AreEqual(3, mods.Count);
            Assert.AreEqual(1.5f, mods[1].multiplier, 1e-4);
            Assert.AreEqual(2, mods[1].conditions.Length);
            Assert.AreEqual(0, mods[2].conditions.Length);
            Assert.AreEqual("x2 [si propio.estado] | x1,5 [si clima=rain & mov.tipo=water] | x0,5",
                ConditionText.FormatModifiers(mods.Select(m => (m.multiplier, (System.Collections.Generic.IReadOnlyList<Condition>)m.conditions))));
            Assert.Throws<System.FormatException>(() => ConditionText.ParseModifiers("doble si llueve"));
        }

        [Test]
        public void Team_cells_round_trip_with_all_optional_parts()
        {
            string cell = "pidgey@5 | onix@14[tackle/rock_throw]{oran_berry}~adamant#31\"Rocoso\"";
            var team = CsvTeamCodecs.ParseTeam(cell);
            Assert.AreEqual(2, team.Count);
            Assert.AreEqual("pidgey", team[0].Species);
            Assert.AreEqual(5, team[0].Level);
            Assert.AreEqual(-1, team[0].Iv);
            Assert.AreEqual("onix", team[1].Species);
            CollectionAssert.AreEqual(new[] { "tackle", "rock_throw" }, team[1].Moves.ToArray());
            Assert.AreEqual("oran_berry", team[1].Held);
            Assert.AreEqual("adamant", team[1].Nature);
            Assert.AreEqual(31, team[1].Iv);
            Assert.AreEqual("Rocoso", team[1].Nickname);
            Assert.AreEqual("pidgey@5|onix@14[tackle/rock_throw]{oran_berry}~adamant#31\"Rocoso\"", CsvTeamCodecs.FormatTeam(team));
            Assert.Throws<CsvCellException>(() => CsvTeamCodecs.ParseTeam("pidgey"));
        }

        [Test]
        public void Zone_cells_parse_ranges_and_weights()
        {
            var z = CsvTeamCodecs.ParseZone("pidgey@2-5:50 | pikachu@3");
            Assert.AreEqual(("pidgey", 2, 5, 50), z[0]);
            Assert.AreEqual(("pikachu", 3, 3, 10), z[1]);
            Assert.AreEqual("pidgey@2-5:50|pikachu@3:10", CsvTeamCodecs.FormatZone(z));
        }
    }
}
