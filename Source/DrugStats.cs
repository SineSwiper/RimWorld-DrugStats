using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace DrugStats {

    [StaticConstructorOnStartup]
    public class Base {
        // Caches
        static bool?  drugNotSafe              = null;
        static float? daysPerSafeDoseAddiction = null;
        static float? daysPerSafeDoseOverdose  = null;

        public static IEnumerable<StatDrawEntry> SpecialDisplayStatsForDrugs(CompProperties_Drug comp, StatRequest req) {
            // Variable prep
            ThingDef thingDef = (ThingDef)req.Def;
            var categories = new Dictionary <string, StatCategoryDef> {};
            foreach (string name in new List<string> { "Drug", "DrugTolerance", "DrugAddiction", "DrugOverdose" }) {
                categories.Add(name, DefDatabase<StatCategoryDef>.GetNamed(name));
            }

            // Multiple CompProperties_Drugs might exist, so set up the display priorities properly
            int compIndex     = thingDef.comps.IndexOf(comp);
            int displayOffset = (thingDef.comps.Count - compIndex) * 100;

            // This is probably a mistake, so don't report it.  (CuproPanda's Drinks does this for some reason.)
            if (comp.chemical == null && thingDef.comps.Sum(cp => cp is CompProperties_Drug ? 1 : 0) > 1) yield break;

            // Clear these out, just in case they weren't before
            drugNotSafe              = null;
            daysPerSafeDoseAddiction = null;
            daysPerSafeDoseOverdose  = null;

            // Basic drug info
            foreach (StatDrawEntry value in BasicDrugStats(comp, req, displayOffset)) yield return value;

            // The rest
            if (comp.chemical != null) {
                foreach (StatDrawEntry value in DrugToleranceStats(comp, req, displayOffset)) yield return value;
                foreach (StatDrawEntry value in DrugAddictionStats(comp, req, displayOffset)) yield return value;
                foreach (StatDrawEntry value in DrugOverdoseStats (comp, req, displayOffset)) yield return value;
            }

            yield return CalculateSafeDose(displayOffset);

            // Clear these out before we go
            drugNotSafe              = null;
            daysPerSafeDoseAddiction = null;
            daysPerSafeDoseOverdose  = null;
        }

        public static IEnumerable<StatDrawEntry> BasicDrugStats(CompProperties_Drug comp, StatRequest req, int displayOffset = 0) {
            ThingDef thingDef = (ThingDef)req.Def;
            var category = DefDatabase<StatCategoryDef>.GetNamed("Drug");
            
            HediffDef                       toleranceHediff  = comp.chemical?.toleranceHediff;
            IngestionOutcomeDoer_GiveHediff highOutcomeDoer  = null;

            highOutcomeDoer = (IngestionOutcomeDoer_GiveHediff)thingDef.ingestible.outcomeDoers.FirstOrFallback(
                iod => iod is IngestionOutcomeDoer_GiveHediff iod_gh &&
                iod_gh.hediffDef != null && (toleranceHediff == null || iod_gh.hediffDef != toleranceHediff) &&
                iod_gh.severity > 0
            );
            if (highOutcomeDoer != null) {
                yield return FindHediffRisks(highOutcomeDoer.hediffDef, "HighBenefitsRisks", category, displayOffset);
            }

            // Drug category
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_Category_Name".Translate(),
                reportText:  "Stat_Thing_Drug_Category_Desc".Translate(),
                valueString: ("DrugCategory_" + thingDef.ingestible.drugCategory).Translate(),
                displayPriorityWithinCategory: displayOffset + 100
            );
            

            // Chemical
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_Chemical_Name".Translate(),
                reportText:  "Stat_Thing_Drug_Chemical_Desc".Translate(),
                valueString: comp.chemical != null ? comp.chemical.LabelCap : "None".Translate(),
                displayPriorityWithinCategory: displayOffset + 99
            );

            // Combat enhancing drug
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_CombatEnhancingDrug_Name".Translate(),
                reportText:  "Stat_Thing_Drug_CombatEnhancingDrug_Desc".Translate(),
                valueString: comp.isCombatEnhancingDrug.ToStringYesNo(),
                displayPriorityWithinCategory: displayOffset + 60
            );
        }

        public static IEnumerable<StatDrawEntry> DrugToleranceStats(CompProperties_Drug comp, StatRequest req, int displayOffset = 0) {
            ThingDef thingDef = (ThingDef)req.Def;
            var category = DefDatabase<StatCategoryDef>.GetNamed("DrugTolerance");

            HediffDef                           toleranceHediff       = comp.chemical?.toleranceHediff;
            IngestionOutcomeDoer_GiveHediff     toleranceOutcomeDoer  = null;
            HediffCompProperties_SeverityPerDay toleranceSeverityComp = null;

            if (toleranceHediff != null) {
                toleranceOutcomeDoer = (IngestionOutcomeDoer_GiveHediff)thingDef.ingestible.outcomeDoers.FirstOrFallback(
                    iod => iod is IngestionOutcomeDoer_GiveHediff iod_gh &&
                    iod_gh.hediffDef != null && iod_gh.hediffDef == toleranceHediff
                );
                toleranceSeverityComp = (HediffCompProperties_SeverityPerDay)toleranceHediff.CompPropsFor(typeof(HediffComp_SeverityPerDay));

                yield return FindHediffRisks(toleranceHediff, "ToleranceRisks", category, displayOffset);
            }

            if (toleranceOutcomeDoer != null) {
                // Tolerance for using
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_ToleranceForUsing_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_ToleranceForUsing_Desc".Translate(),
                    valueString: toleranceOutcomeDoer.severity.ToStringPercent(),
                    displayPriorityWithinCategory: displayOffset + 98
                );

                // [Reflection] toleranceOutcomeDoer.divideByBodySize
                FieldInfo divideByBodySizeField = AccessTools.Field(typeof(IngestionOutcomeDoer_GiveHediff), "divideByBodySize");
                bool divideByBodySize = (bool)divideByBodySizeField.GetValue(toleranceOutcomeDoer);

                // Severity affected by body size
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_SeverityUsesBodySize_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_SeverityUsesBodySize_Desc".Translate(),
                    valueString: divideByBodySize.ToStringYesNo(),
                    displayPriorityWithinCategory: displayOffset + 97
                );
            }

            // Minimum tolerance to addict
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_MinToleranceToAddict_Name".Translate(),
                reportText:  "Stat_Thing_Drug_MinToleranceToAddict_Desc".Translate(),
                valueString: comp.minToleranceToAddict.ToStringPercent(),
                displayPriorityWithinCategory: displayOffset + 96
            );
            if (comp.minToleranceToAddict == 0f) drugNotSafe = true;

            // Tolerance decay per day
            if (toleranceSeverityComp != null) {
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_ToleranceDecay_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_ToleranceDecay_Desc".Translate(),
                    valueString: toleranceSeverityComp.severityPerDay.ToStringPercent(),
                    displayPriorityWithinCategory: displayOffset + 95
                );
            }

            // Doses before addiction
            if (toleranceOutcomeDoer != null) {
                float dosesBeforeAddiction = comp.minToleranceToAddict / toleranceOutcomeDoer.severity;
                if (dosesBeforeAddiction <= 1f) dosesBeforeAddiction = 0;  // cannot take partial doses
                drugNotSafe = drugNotSafe != null ?
                    ((bool)drugNotSafe || dosesBeforeAddiction == 0f) :
                    dosesBeforeAddiction == 0f
                ;

                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_DosagesBeforeAddiction_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_DosagesBeforeAddiction_Desc".Translate(),
                    valueString: dosesBeforeAddiction.ToStringDecimalIfSmall(),
                    displayPriorityWithinCategory: displayOffset + 94
                );

                if (toleranceSeverityComp != null) {
                    daysPerSafeDoseAddiction = toleranceOutcomeDoer.severity / toleranceSeverityComp.severityPerDay * -1;
                }
            }
        }

        public static IEnumerable<StatDrawEntry> DrugAddictionStats(CompProperties_Drug comp, StatRequest req, int displayOffset = 0) {
            ThingDef thingDef = (ThingDef)req.Def;
            var category = DefDatabase<StatCategoryDef>.GetNamed("DrugAddiction");

            HediffDef addictionHediff = comp.chemical?.addictionHediff;
            NeedDef   addictionNeed   = addictionHediff?.causesNeed;
            HediffCompProperties_SeverityPerDay addictionSeverityComp =
                (HediffCompProperties_SeverityPerDay)addictionHediff?.CompPropsFor(typeof(HediffComp_SeverityPerDay))
            ;

            // Addictiveness
            yield return new StatDrawEntry(
                category:    category,
                label:       "Addictiveness".Translate(),
                reportText:  "Stat_Thing_Drug_Addictiveness_Desc".Translate(),
                valueString: comp.addictiveness.ToStringPercent(),
                displayPriorityWithinCategory: displayOffset + 99
            );

            if (addictionSeverityComp != null) {
                // Addiction decay per day
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_AddictionDecay_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_AddictionDecay_Desc".Translate(),
                    valueString: addictionSeverityComp.severityPerDay.ToStringPercent(),
                    displayPriorityWithinCategory: displayOffset + 98
                );

                // Time to shake addiction
                float daysToShakeAddiction = addictionHediff.initialSeverity / addictionSeverityComp.severityPerDay * -1;
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_TimeToShakeAddiction_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_TimeToShakeAddiction_Desc".Translate(),
                    valueString: ToStringDaysToPeriod(daysToShakeAddiction),
                    displayPriorityWithinCategory: displayOffset + 98
                );
            }

            if (addictionHediff != null) {
                yield return FindHediffRisks(addictionHediff, "AddictionRisks", category, displayOffset);
            }
        }

        public static IEnumerable<StatDrawEntry> DrugOverdoseStats(CompProperties_Drug comp, StatRequest req, int displayOffset = 0) {
            ThingDef thingDef = (ThingDef)req.Def;
            var category = DefDatabase<StatCategoryDef>.GetNamed("DrugOverdose");

            // Nothing to report
            if (comp.overdoseSeverityOffset.TrueMax == 0f && comp.largeOverdoseChance == 0f) yield break;

            HediffDef overdoseHediff = HediffDefOf.DrugOverdose;

            // NOTE: Some of these are static values to inform the user, and to be consistent with the
            // DrugTolerance layout.

            if (comp.overdoseSeverityOffset.TrueMax > 0f) {
                // Overdose severity for using
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_OverdoseForUsing_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_OverdoseForUsing_Desc".Translate(),
                    valueString: string.Join(" - ",
                        comp.overdoseSeverityOffset.TrueMin.ToStringPercent(),
                        comp.overdoseSeverityOffset.TrueMax.ToStringPercent()
                    ),
                    displayPriorityWithinCategory: displayOffset + 99
                );

                // Severity affected by body size
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_SeverityUsesBodySize_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_SeverityUsesBodySize_Desc".Translate(),
                    valueString: true.ToStringYesNo(),
                    displayPriorityWithinCategory: displayOffset + 98
                );

                // Overdose severity levels
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_OverdoseSeverityLevels_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_OverdoseSeverityLevels_Desc".Translate(),
                    valueString: GenText.ToCommaList(new List<string> {
                        overdoseHediff.stages[1].minSeverity.ToStringPercent(),
                        overdoseHediff.stages[2].minSeverity.ToStringPercent(),
                        overdoseHediff.lethalSeverity.ToStringPercent()
                    }),
                    displayPriorityWithinCategory: displayOffset + 97
                );

                // Overdose decay per day
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_OverdoseDecay_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_OverdoseDecay_Desc".Translate(),
                    // XXX: This is documented on the wiki, but I don't know where in the code...
                    valueString: (-1f).ToStringPercent(),
                    displayPriorityWithinCategory: displayOffset + 96
                );
            }

            if (comp.largeOverdoseChance > 0f) {
                // Large overdose chance
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_LargeOverdoseChance_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_LargeOverdoseChance_Desc".Translate(),
                    valueString: comp.largeOverdoseChance.ToStringPercent(),
                    displayPriorityWithinCategory: displayOffset + 95
                );
            }

            // Doses before overdose
            float dosesBeforeOverdose = overdoseHediff.stages[1].minSeverity / comp.overdoseSeverityOffset.TrueMax;
            if (dosesBeforeOverdose <= 1f)     dosesBeforeOverdose = 0;  // cannot take partial doses
            if (comp.largeOverdoseChance > 0f) dosesBeforeOverdose = 0;  // cannot be considered safe with a large overdose chance
            drugNotSafe = drugNotSafe != null ?
                ((bool)drugNotSafe || dosesBeforeOverdose == 0f) :
                dosesBeforeOverdose == 0f
            ;

            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_DosagesBeforeOverdose_Name".Translate(),
                reportText:  "Stat_Thing_Drug_DosagesBeforeOverdose_Desc".Translate(),
                valueString: dosesBeforeOverdose.ToStringDecimalIfSmall(),
                displayPriorityWithinCategory: displayOffset + 85
            );

            daysPerSafeDoseOverdose = comp.overdoseSeverityOffset.TrueMax;

            yield return FindHediffRisks(overdoseHediff, "OverdoseRisks", category, displayOffset);
        }

        // TODO: Show math on CalculateSafeDose
        public static StatDrawEntry CalculateSafeDose (int displayOffset = 0) {
            var category = DefDatabase<StatCategoryDef>.GetNamed("Drug");

            if (drugNotSafe == null) drugNotSafe = false;
            float daysPerSafeDose = Mathf.Max(
                daysPerSafeDoseAddiction ?? 0,
                daysPerSafeDoseOverdose  ?? 0
            );

            // Time per safe dose
            return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_SafeDoseInterval_Name".Translate(),
                reportText:  "Stat_Thing_Drug_SafeDoseInterval_Desc".Translate(),
                valueString: (
                    (bool)drugNotSafe     ? "None"  .Translate().ToString() :
                    daysPerSafeDose == 0f ? "Always".Translate().ToString() :
                    ToStringDaysToPeriod(daysPerSafeDose)
                ),
                displayPriorityWithinCategory: displayOffset + 90
            );
        }

        public static StatDrawEntry FindHediffRisks (HediffDef hediff, string labelKey, StatCategoryDef category, int displayOffset = 0) {
            var riskKeys    = new List<string> {};
            var riskReports = new List<string> {};
            riskReports.Add(("Stat_Thing_Drug_" + labelKey + "_Desc").Translate());

            if (!hediff.everCurableByItem) {
                string incurable = "Incurable".Translate();
                riskKeys   .Add(incurable);
                riskReports.Add(incurable);
            }

            if (hediff.stages != null) {
                foreach (HediffStage stage in hediff.stages) {
                    if (hediff.stages.Count > 1 && (stage.label.NullOrEmpty() || !stage.becomeVisible)) continue;

                    string label = stage.label.NullOrEmpty() ? hediff.label : stage.label;
                    label = label.CapitalizeFirst();

                    string report = "";  // keep it empty to check later
                    List<StatDrawEntry> statDrawEntries = stage.SpecialDisplayStats().ToList();
                    foreach (StatDrawEntry statDrawEntry in statDrawEntries.OrderBy(sde => sde.DisplayPriorityWithinCategory)) {
                        report += "    " + statDrawEntry.LabelCap + ": " + statDrawEntry.ValueString + "\n";
                    }

                    // Other stats not reported from stage.SpecialDisplayStats()
                    if (stage.vomitMtbDays               > 0) report += "    " + "Stat_MTBVomit"      .Translate() + ": " + ToStringDaysToPeriod(stage.vomitMtbDays              ) + "\n";
                    if (stage.forgetMemoryThoughtMtbDays > 0) report += "    " + "Stat_MTBForget"     .Translate() + ": " + ToStringDaysToPeriod(stage.forgetMemoryThoughtMtbDays) + "\n";
                    if (stage.mentalBreakMtbDays         > 0) report += "    " + "Stat_MTBMentalBreak".Translate() + ": " + ToStringDaysToPeriod(stage.mentalBreakMtbDays        ) + "\n";
                    if (stage.deathMtbDays               > 0) report += "    " + "Stat_MTBDeath"      .Translate() + ": " + ToStringDaysToPeriod(stage.deathMtbDays              ) + "\n";

                    if (!stage.hediffGivers.NullOrEmpty()) {
                        foreach (HediffGiver hediffGiver in stage.hediffGivers) {
                            report += "    " + ReadHediffGiver(hediffGiver) + "\n";
                        }
                    }

                    if (report.NullOrEmpty()) continue;

                    report = label + ":\n" + report.Trim('\n');
                    riskKeys.Add(label);
                    riskReports.Add(report);
                }
            }

            if (!hediff.hediffGivers.NullOrEmpty()) {
                string report = "";
                foreach (HediffGiver hediffGiver in hediff.hediffGivers) {
                    report += ReadHediffGiver(hediffGiver) + "\n";
                    riskKeys.Add(hediffGiver.hediff.LabelCap);
                }
                riskReports.Add(report.Trim('\n'));
            }

            if (hediff.lethalSeverity >= 0) {
                riskKeys   .Add("Fatal".Translate());
                riskReports.Add("Stat_FatalAt".Translate( hediff.lethalSeverity.ToStringPercent() ));
            }

            return new StatDrawEntry(
                category:    category,
                label:       ("Stat_Thing_Drug_" + labelKey + "_Name").Translate(),
                reportText:  string.Join("\n\n", riskReports),
                valueString: riskKeys.Count == 0 ? (string)"None".Translate() : GenText.ToCommaList(riskKeys),
                hyperlinks: new List<Dialog_InfoCard.Hyperlink> { new Dialog_InfoCard.Hyperlink(hediff) },
                displayPriorityWithinCategory: displayOffset + 50
            );
        }

        // use this far too often
        private static string ToStringDaysToPeriod (float days) {
            return GenDate.ToStringTicksToPeriod(
                numTicks:     Mathf.RoundToInt(days * GenDate.TicksPerDay),
                allowSeconds: false
            );
        }

        private static string ReadHediffGiver (HediffGiver hediffGiver) {
            if      (hediffGiver is HediffGiver_Random hg_r) {
                return "Stat_MTB_Hediff".Translate(hg_r.hediff.Named("HEDIFF")) + ": " + ToStringDaysToPeriod(hg_r.mtbDays);
            }
            else if (hediffGiver is HediffGiver_RandomDrugEffect hg_rde) {
                return "Stat_MTB_Hediff".Translate(hg_rde.hediff.Named("HEDIFF")) + ": " + ToStringDaysToPeriod(hg_rde.baseMtbDays);
            }

            return "Stat_CanCause_Hediff".Translate(hediffGiver.hediff.Named("HEDIFF"));
        }
    }

    [StaticConstructorOnStartup]
    public class HarmonyPatches {
        static HarmonyPatches() {
            new Harmony("SineSwiper.DrugStats").PatchAll();
            Log.Message("[DrugStats] Harmony patches complete");
        }

        // Override the CompProperties_Drug.SpecialDisplayStats method to display our own stats.
        [HarmonyPatch(typeof(CompProperties_Drug), "SpecialDisplayStats")]
        private static class SpecialDisplayStatsPatch {
            [HarmonyPostfix]
            static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> values, CompProperties_Drug __instance, StatRequest req) {
                // Cycle through the entries
                string addictivenessCap = "Addictiveness".Translate().CapitalizeFirst();
                foreach (StatDrawEntry value in values) {
                    // Return all of them except for Addictiveness (the one stat CompProperties_Drug adds)
                    if (value.LabelCap != addictivenessCap) yield return value;
                }

                foreach (StatDrawEntry value in Base.SpecialDisplayStatsForDrugs(__instance, req)) yield return value;
            }
        }

    }
}
