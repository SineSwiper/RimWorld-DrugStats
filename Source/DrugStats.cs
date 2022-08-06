using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace DrugStats {
    [DefOf]
    public static class StatCategoryDefOf {
        public static StatCategoryDef Drug;
        public static StatCategoryDef DrugTolerance;
        public static StatCategoryDef DrugAddiction;
        public static StatCategoryDef DrugOverdose;
        public static StatCategoryDef CapacityEffects;
    }

    [StaticConstructorOnStartup]
    public class Base {
        public static FieldInfo StatDrawEntry_displayOrderWithinCategory_Field;
        public static FieldInfo StatDrawEntry_value_Field;

        public static bool TweakExistingStatDrawEntriesForDrugs(StatDrawEntry statDrawEntry, ThingDef drug) {
            // [Reflection prep] statDrawEntry.displayOrderWithinCategory / value
            if (StatDrawEntry_displayOrderWithinCategory_Field == null) {
                StatDrawEntry_displayOrderWithinCategory_Field = AccessTools.Field(typeof(StatDrawEntry), "displayOrderWithinCategory");
                StatDrawEntry_value_Field                      = AccessTools.Field(typeof(StatDrawEntry), "value");
            }

            string label = statDrawEntry.LabelCap;
            foreach (string checkKey in new[] {
                "ToleranceGain", "ToleranceFallRate", "MinimumToleranceForAddiction", "Addictiveness", "RandomODChance", "SafeDoseInterval", "Joy"
            }) {
                string checkLabel = checkKey.Translate().CapitalizeFirst();
                if (label != checkLabel) continue;

                // Move things over to DrugTolerance
                if      (Regex.IsMatch(checkKey, "^(?:Minimum)?Tolerance|^Addictiveness$")) {
                    statDrawEntry.category = StatCategoryDefOf.DrugTolerance;
                    // don't report if there isn't tolerance gain
                    if (DrugStatsUtility.GetToleranceGain(drug) == 0f) return false;
                }
                // Or DrugOverdose
                else if (checkKey == "RandomODChance") {
                    statDrawEntry.category = StatCategoryDefOf.DrugOverdose;
                    // don't report if it's zero
                    if ((float)StatDrawEntry_value_Field.GetValue(statDrawEntry) == 0) return false;
                }
                // Move SafeDoseInterval up, right after Chemical
                else if (checkKey == "SafeDoseInterval") {
                    StatDrawEntry_displayOrderWithinCategory_Field.SetValue(statDrawEntry, 2484);
                }
                else if (checkKey == "Joy") {
                    statDrawEntry.category = StatCategoryDefOf.CapacityEffects;
                }
            }

            return true;
        }

        public static IEnumerable<StatDrawEntry> SpecialDisplayStatsForDrug (ThingDef drug) {
            CompProperties_Drug comp = DrugStatsUtility.GetDrugComp(drug);
            if (comp == null) yield break;

            // This is probably a mistake, so don't report it.  (CuproPanda's Drinks does this for some reason.)
            if (comp.chemical == null) yield break;

            // Basic drug info
            foreach (StatDrawEntry value in BasicDrugStats(drug)) yield return value;

            // The rest
            if (comp.chemical != null) {
                foreach (StatDrawEntry value in DrugToleranceStats(drug)) yield return value;
                foreach (StatDrawEntry value in DrugAddictionStats(drug)) yield return value;
                foreach (StatDrawEntry value in DrugOverdoseStats (drug)) yield return value;
            }
        }

        public static IEnumerable<StatDrawEntry> BasicDrugStats (ThingDef drug) {
            var category = StatCategoryDefOf.Drug;
            CompProperties_Drug comp = DrugStatsUtility.GetDrugComp(drug);
            
            IngestionOutcomeDoer_GiveHediff highOutcomeDoer  = DrugStatsUtility.GetDrugHighGiver(drug);
            if (highOutcomeDoer != null) yield return FindHediffRisks(highOutcomeDoer.hediffDef, "HighBenefitsRisks", category, 2400);

            // DrugStatsUtility won't include this if it's not addictive
            if (!comp.Addictive) {
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Chemical".Translate(),
                    reportText:  "Stat_Thing_Drug_Chemical_Desc".Translate(),
                    valueString: comp.chemical.LabelCap,
                    displayPriorityWithinCategory: 2490
                );
            }

            // Combat enhancing drug
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_CombatEnhancingDrug_Name".Translate(),
                reportText:  "Stat_Thing_Drug_CombatEnhancingDrug_Desc".Translate(),
                valueString: comp.isCombatEnhancingDrug.ToStringYesNo(),
                displayPriorityWithinCategory: 2482
            );
        }

        public static IEnumerable<StatDrawEntry> DrugToleranceStats (ThingDef drug) {
            var category = StatCategoryDefOf.DrugTolerance;
            CompProperties_Drug comp = DrugStatsUtility.GetDrugComp(drug);

            // Nothing to report
            if (DrugStatsUtility.GetToleranceGain(drug) == 0f) yield break;

            HediffDef                       toleranceHediff       = DrugStatsUtility.GetTolerance     (drug);
            IngestionOutcomeDoer_GiveHediff toleranceOutcomeDoer  = DrugStatsUtility.GetToleranceGiver(drug);

            if (toleranceHediff != null) yield return FindHediffRisks(toleranceHediff, "ToleranceRisks", category, 2400);

            if (toleranceOutcomeDoer != null) {
                // [Reflection] toleranceOutcomeDoer.divideByBodySize
                FieldInfo divideByBodySizeField = AccessTools.Field(typeof(IngestionOutcomeDoer_GiveHediff), "divideByBodySize");
                bool divideByBodySize = (bool)divideByBodySizeField.GetValue(toleranceOutcomeDoer);

                // Severity affected by body size
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_SeverityUsesBodySize_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_SeverityUsesBodySize_Desc".Translate(),
                    valueString: divideByBodySize.ToStringYesNo(),
                    displayPriorityWithinCategory: 2439
                );

                // Doses before addiction
                float dosesBeforeAddiction = comp.minToleranceToAddict / toleranceOutcomeDoer.severity;
                if (dosesBeforeAddiction <= 1f) dosesBeforeAddiction = 0;  // cannot take partial doses

                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_DosagesBeforeAddiction_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_DosagesBeforeAddiction_Desc".Translate(),
                    valueString: dosesBeforeAddiction.ToStringDecimalIfSmall(),
                    displayPriorityWithinCategory: 2426
                );
            }
        }

        public static IEnumerable<StatDrawEntry> DrugAddictionStats (ThingDef drug) {
            var category = StatCategoryDefOf.DrugAddiction;

            // Nothing to report
            if (!drug.IsAddictiveDrug) yield break;

            HediffDef addictionHediff = DrugStatsUtility.GetChemical(drug)?.addictionHediff;
            if (addictionHediff != null) yield return FindHediffRisks(addictionHediff, "AddictionRisks", category, 2400);
        }

        public static IEnumerable<StatDrawEntry> DrugOverdoseStats (ThingDef drug) {
            var category = StatCategoryDefOf.DrugOverdose;
            CompProperties_Drug comp = DrugStatsUtility.GetDrugComp(drug);

            // Nothing to report
            if (!comp.CanCauseOverdose && comp.largeOverdoseChance == 0f) yield break;

            HediffDef overdoseHediff = HediffDefOf.DrugOverdose;

            // NOTE: Some of these are static values to inform the user, and to be consistent with the
            // DrugTolerance layout.

            if (comp.overdoseSeverityOffset.TrueMax > 0f) {
                // Overdose severity for using
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_OverdoseGainPerDose_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_OverdoseGainPerDose_Desc".Translate(),
                    valueString: "PerDay".Translate(string.Join(" ~ ",
                        comp.overdoseSeverityOffset.TrueMin.ToStringPercent(),
                        comp.overdoseSeverityOffset.TrueMax.ToStringPercent()
                    )),
                    displayPriorityWithinCategory: 2370
                );

                // Overdose fall rate
                HediffCompProperties_SeverityPerDay overdoseSeverityPerDay = overdoseHediff.CompProps<HediffCompProperties_SeverityPerDay>();
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_OverdoseFallRate_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_OverdoseFallRate_Desc".Translate(),
                    valueString: "PerDay".Translate( Math.Abs(overdoseSeverityPerDay.severityPerDay).ToStringPercent() ),
                    displayPriorityWithinCategory: 2360
                );

                // Severity affected by body size
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Thing_Drug_SeverityUsesBodySize_Name".Translate(),
                    reportText:  "Stat_Thing_Drug_SeverityUsesBodySize_Desc".Translate(),
                    valueString: true.ToStringYesNo(),
                    displayPriorityWithinCategory: 2350
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
                    displayPriorityWithinCategory: 2340
                );
            }

            // Doses before overdose
            float dosesBeforeOverdose = overdoseHediff.stages[1].minSeverity / comp.overdoseSeverityOffset.TrueMax;
            if (dosesBeforeOverdose <= 1f)     dosesBeforeOverdose = 0;  // cannot take partial doses
            if (comp.largeOverdoseChance > 0f) dosesBeforeOverdose = 0;  // cannot be considered safe with a large overdose chance

            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_Thing_Drug_DosagesBeforeOverdose_Name".Translate(),
                reportText:  "Stat_Thing_Drug_DosagesBeforeOverdose_Desc".Translate(),
                valueString: dosesBeforeOverdose.ToStringDecimalIfSmall(),
                displayPriorityWithinCategory: 2330
            );

            yield return FindHediffRisks(overdoseHediff, "OverdoseRisks", category, 2300);
        }

        public static StatDrawEntry FindHediffRisks (HediffDef hediff, string labelKey, StatCategoryDef category, int displayPriority = 2000) {
            var riskKeys    = new List<string> {};
            var riskReports = new List<string> {};
            riskReports.Add(("Stat_Thing_Drug_" + labelKey + "_Desc").Translate());

            var hyperlinks = new List<Dialog_InfoCard.Hyperlink> { new Dialog_InfoCard.Hyperlink(hediff) };

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

            // Report on thoughts tied to the hediff
            string thoughtsReport = "";  // keep it empty to check later
            foreach (ThoughtDef thought in DefDatabase<ThoughtDef>.AllDefs.Where(td => td.hediff == hediff)) {
                foreach (ThoughtStage stage in thought.stages) {
                    if (thought.stages.Count > 1 && (stage.label.NullOrEmpty() || !stage.visible)) continue;

                    string label = stage.label.NullOrEmpty() ? (string)thought.LabelCap : stage.LabelCap;

                    var effects = new List<string> {};
                    if (stage.baseMoodEffect    != 0) effects.Add( "Stat_Thought_MoodEffect"   .Translate( ((int)stage.baseMoodEffect   ).ToStringWithSign() ) );
                    if (stage.baseOpinionOffset != 0) effects.Add( "Stat_Thought_OpinionOffset".Translate( ((int)stage.baseOpinionOffset).ToStringWithSign() ) );

                    thoughtsReport += "    " + label + ": " + GenText.ToCommaList(effects) + "\n";
                }
            }

            if (!thoughtsReport.NullOrEmpty()) {
                riskKeys.Add("Thoughts".Translate());
                thoughtsReport = "Thoughts".Translate() + ":\n" + thoughtsReport.Trim('\n');
                riskReports.Add(thoughtsReport);
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
                valueString: riskKeys.Count == 0 ? "None".Translate() : GenText.ToCommaList(riskKeys),
                hyperlinks:  hyperlinks,
                displayPriorityWithinCategory: displayPriority
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
                if      (hg_rde.baseMtbDays > 0)
                    return "Stat_MTB_Hediff".Translate(hg_rde.hediff.Named("HEDIFF")) + ": " + ToStringDaysToPeriod(hg_rde.baseMtbDays);

                else if (hg_rde.severityToMtbDaysCurve is SimpleCurve curve) {
                    StringBuilder sb = new();
                    sb.AppendLine( "Stat_MTB_Hediff".Translate(hg_rde.hediff.Named("HEDIFF")) + ": " );
                    foreach (CurvePoint point in curve.Points) {
                        sb.AppendLine("    " + point.x.ToStringPercent() + ": " + (point.y > 0 && point.y < 35000 ? ToStringDaysToPeriod(point.y) : "Never".Translate()) );
                    }
                    return sb.ToString();
                }
            }

            return "Stat_CanCause_Hediff".Translate(hediffGiver.hediff.Named("HEDIFF"));
        }
    }

    [HarmonyPatch]
    [StaticConstructorOnStartup]
    public class HarmonyPatches {
        static HarmonyPatches() {
            new Harmony("SineSwiper.DrugStats").PatchAll();
            Log.Message("[DrugStats] Harmony patches complete");
        }

        // Override the CompProperties_Drug.SpecialDisplayStats method to display our own stats
        [HarmonyPatch(typeof(DrugStatsUtility), nameof(DrugStatsUtility.SpecialDisplayStats))]
        [HarmonyPostfix]
        private static IEnumerable<StatDrawEntry> DrugStatsUtility_SpecialDisplayStats_Postfix (IEnumerable<StatDrawEntry> values, ThingDef def) {
            // Cycle through the entries, and tweak certain ones
            foreach (StatDrawEntry value in values) {
                if (!Base.TweakExistingStatDrawEntriesForDrugs(value, def)) continue;
                yield return value;
            }

            foreach (StatDrawEntry value in Base.SpecialDisplayStatsForDrug(def)) yield return value;
        }

        // Tweak StatCategories in IngestibleProperties.SpecialDisplayStats
        [HarmonyPatch(typeof(IngestibleProperties), "SpecialDisplayStats")]
        [HarmonyPostfix]
        private static IEnumerable<StatDrawEntry> IngestibleProperties_SpecialDisplayStats_Postfix (IEnumerable<StatDrawEntry> values, IngestibleProperties __instance) {
            ThingDef def = __instance.parent;

            // Cycle through the entries, and tweak certain ones
            foreach (StatDrawEntry value in values) {
                if (def.IsDrug && !Base.TweakExistingStatDrawEntriesForDrugs(value, def)) continue;
                yield return value;
            }
        }

        // Move all IngestionOutcomeDoer_OffsetNeed.SpecialDisplayStats to Effects
        [HarmonyPatch(typeof(IngestionOutcomeDoer_OffsetNeed), nameof(IngestionOutcomeDoer_OffsetNeed.SpecialDisplayStats))]
        [HarmonyPostfix]
        private static IEnumerable<StatDrawEntry> IngestionOutcomeDoer_OffsetNeed_SpecialDisplayStats_Postfix (IEnumerable<StatDrawEntry> values) {
            // Cycle through the entries, and move all StatCategories
            foreach (StatDrawEntry value in values) {
                value.category = StatCategoryDefOf.CapacityEffects;
                yield return value;
            }
        }

        // Ditto for Psyfocus
        [HarmonyPatch(typeof(IngestionOutcomeDoer_OffsetPsyfocus), nameof(IngestionOutcomeDoer_OffsetPsyfocus.SpecialDisplayStats))]
        [HarmonyPostfix]
        private static IEnumerable<StatDrawEntry> IngestionOutcomeDoer_OffsetPsyfocus_SpecialDisplayStats_Postfix (IEnumerable<StatDrawEntry> values) {
            // Cycle through the entries, and move all StatCategories
            foreach (StatDrawEntry value in values) {
                value.category = StatCategoryDefOf.CapacityEffects;
                yield return value;
            }
        }

        // Additional checks in GetSafeDoseInterval
        [HarmonyPatch(typeof(DrugStatsUtility), nameof(DrugStatsUtility.GetSafeDoseInterval))]
        [HarmonyPostfix]
        private static void GetSafeDoseInterval_Postfix (ThingDef d, ref float __result) {
            // Add check for largeOverdoseChance
            CompProperties_Drug drugComp = DrugStatsUtility.GetDrugComp(d);
            if (drugComp != null && drugComp.largeOverdoseChance > 0f) __result = -1f;

            // This is in GetSafeDoseIntervalReadout (as the partial dose check), but it should really be checked here
            IngestionOutcomeDoer_GiveHediff toleranceGiver = DrugStatsUtility.GetToleranceGiver(d);
            float addictSeverityRatio = toleranceGiver != null ? drugComp.minToleranceToAddict / toleranceGiver.severity : 0.0f;
            if (addictSeverityRatio != 0f && addictSeverityRatio < 1f) __result = -1f;
        }

        // Override to make GetSafeDoseIntervalReadout simpler
        [HarmonyPatch(typeof(DrugStatsUtility), nameof(DrugStatsUtility.GetSafeDoseIntervalReadout))]
        [HarmonyPrefix]
        private static bool GetSafeDoseIntervalReadout_Override (ThingDef d, ref string __result) {
            float safeDoseInterval = DrugStatsUtility.GetSafeDoseInterval(d);

            __result =
                safeDoseInterval ==  0f ? "AlwaysSafe".Translate() :
                safeDoseInterval == -1f ?  "NeverSafe".Translate() :
                "PeriodDays".Translate(safeDoseInterval.ToString("F1"))
            ;

            // Never use the original method
            return false;
        }

        // TODO: Port FindHediffRisks over to Xenobionic Patcher
    }
}
