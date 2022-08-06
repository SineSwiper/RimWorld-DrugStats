using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace DrugStats {
    public class DrugPolicyMaker : GameComponent {
        public const string autoPolicyLabel = "Responsible (Auto)";

        public Game game;
        
        public DrugPolicyMaker (Game game) {
            this.game = game;
        }

        public override void FinalizeInit () {
            CreateOrUpdateResponsibleDrugPolicy();
        }

        public void CreateOrUpdateResponsibleDrugPolicy () {
            DrugPolicyDatabase db = game.drugPolicyDatabase;
            DrugPolicy drugPolicy = db.AllPolicies.FirstOrDefault(dp => dp.label == autoPolicyLabel);
            if (drugPolicy == null) {
                drugPolicy = db.MakeNewDrugPolicy();
                drugPolicy.label = autoPolicyLabel;
            }
            else {
                // Add new drugs, if necessary
                drugPolicy.InitializeIfNeeded( overwriteExisting: false );
            }

            // Reset values
            for (int i = 0; i < drugPolicy.Count; ++i) {
                DrugPolicyEntry entry = drugPolicy[i];

                entry.allowedForAddiction = false;
                entry.allowedForJoy       = false;
                entry.allowScheduled      = false;
                entry.daysFrequency       = 1f;
                entry.onlyIfJoyBelow      = 1f;
                entry.onlyIfMoodBelow     = 1f;
                entry.takeToInventory     = 0;
            }

            // Set new policy
            for (int i = 0; i < drugPolicy.Count; ++i) {
                DrugPolicyEntry entry = drugPolicy[i];
                ThingDef        drug  = entry.drug;
                
                IngestionOutcomeDoer_GiveHediff highGiver = DrugStatsUtility.GetDrugHighGiver(drug);
                float highFall     = DrugStatsUtility.GetHighOffsetPerDay(drug) * -1;
                float highDuration = highGiver != null && highFall > 0f ? highGiver.severity / highFall : 0f;

                float safeDoseInterval = DrugStatsUtility.GetSafeDoseInterval(drug);
                bool  drugSafe         = safeDoseInterval != -1;

                entry.allowedForAddiction = drug.IsAddictiveDrug;
                entry.allowedForJoy       = !drug.IsAddictiveDrug && drug.IsPleasureDrug && safeDoseInterval == 0f;

                // Don't mess with medical drugs (ie: Luciferium and other random life-changing drugs)
                if (!drug.IsNonMedicalDrug) continue;

                // Set scheduling
                if (drugSafe && !entry.allowedForJoy && highDuration > 0f) {
                    entry.allowScheduled  = true;
                    entry.daysFrequency   = Mathf.Max(safeDoseInterval, highDuration);
                    entry.onlyIfJoyBelow  = Mathf.Max( Mathf.Min( 1f - drug.ingestible.joy, 0.5f ), 0.2f );
                    entry.onlyIfMoodBelow = 0.35f;
                }
            }
        }
    }
}
