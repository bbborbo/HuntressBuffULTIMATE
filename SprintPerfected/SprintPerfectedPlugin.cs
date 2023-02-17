using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API;
using R2API.Utils;
using RoR2;
using RoR2.Skills;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SprintPerfected
{
    [BepInDependency("com.bepis.r2api.language")]

    [BepInPlugin(
        "com.Borbo.HuntressBuffULTIMATE",
        "HuntressBuffULTIMATE",
        "1.1.1")]

    [R2APISubmoduleDependency(nameof(LanguageAPI))]
    public class SprintPerfectedPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        internal static PluginInfo pluginInfo;
        public static ConfigFile CustomConfigFile { get; set; }
        public static ConfigEntry<bool> NerfSprint { get; set; }
        public static ConfigEntry<float> EnergyDrinkBase { get; set; }
        public static ConfigEntry<float> EnergyDrinkStack { get; set; }
        public static ConfigEntry<string> AgileList { get; set; }

        public void Awake()
        {
            pluginInfo = Info;
            Log = Logger;
            InitializeConfig();
            Hooks();
        }

        private void InitializeConfig()
        {
            CustomConfigFile = new ConfigFile(Paths.ConfigPath + "\\HuntressBuffULTIMATE.cfg", true);
            NerfSprint = CustomConfigFile.Bind<bool>("HUNTRESS BUFF ULTIMATE", "Should Sprint Be Nerfed or Buffed", true, 
                "This determines whether HUNTRESS BUFF ULTIMATE should BUFF sprint, or nerf it. " +
                "IF TRUE: All characters are unable to sprint during states that are non-agile and are not canceled by sprinting. " +
                "IF FALSE: All skills are now Agile and are no longer canceled by sprinting.");

            EnergyDrinkBase = CustomConfigFile.Bind<float>("Sprint Nerf", "Energy Drink BASE Speed Bonus", drinkSpeedBonusBase * 100, 
                "How much bonus speed Energy Drink should grant while sprinting, on the first stack. Vanilla is 25.");
            EnergyDrinkStack = CustomConfigFile.Bind<float>("Sprint Nerf", "Energy Drink STACK Speed Bonus", drinkSpeedBonusStack * 100,
                "How much bonus speed Energy Drink should grant while sprinting, on every stack past the first. Vanilla is 25.");
            AgileList = CustomConfigFile.Bind<string>("Sprint Nerf", "Agile List", "",
                "List of skill names to make agile, separated by comma. Accepts nameTokens. if sprint buff is enabled, works as a blacklist instead.");
        }

        public static float drinkSpeedBonusBase = 0.35f; //0.25
        public static float drinkSpeedBonusStack = 0.35f; //0.25
        void Hooks()
        {
            Log.LogDebug($"You chose Sprint {(NerfSprint.Value ? "Nerf" : "Buff")}! Based and shoepilled!");
            On.RoR2.Skills.SkillCatalog.SetSkillDefs += MakeSomethingAgile;
            if (NerfSprint.Value || AgileList.Value != "") IL.RoR2.Skills.SkillDef.OnFixedUpdate += SkillDefFixedUpdate; // if strictly buffed, no need to patch

            SkillDef acridPoisonSkill = Resources.Load<SkillDef>("skilldefs/crocobody/CrocoPassivePoison");
            SkillDef acridBlightSkill = Resources.Load<SkillDef>("skilldefs/crocobody/CrocoPassiveBlight");
            acridPoisonSkill.cancelSprintingOnActivation = false;
            acridBlightSkill.cancelSprintingOnActivation = false;

            drinkSpeedBonusBase = EnergyDrinkBase.Value / 100f;
            drinkSpeedBonusStack = EnergyDrinkStack.Value / 100f;
            LanguageAPI.Add("ITEM_SPRINTBONUS_DESC",
                $"<style=cIsUtility>Sprint speed</style> is improved by <style=cIsUtility>{Tools.ConvertDecimal(drinkSpeedBonusBase)}</style> " +
                $"<style=cStack>(+{Tools.ConvertDecimal(drinkSpeedBonusStack)} per stack)</style>.");
            RecalculateStatsAPI.GetStatCoefficients += (self, args) => // made to work with holydll
            {
                int count = self.inventory.GetItemCount(RoR2Content.Items.SprintBonus);
                if (count > 0 && self.isSprinting) args.moveSpeedMultAdd += ((drinkSpeedBonusStack - 0.25f) * (count - 1) + (drinkSpeedBonusBase - 0.25f)) / self.sprintingSpeedMultiplier;
            };
        }

        private void SkillDefFixedUpdate(ILContext il) // this is the same on hook just now as an IL hook
        {
            ILCursor c = new ILCursor(il);
            ILLabel end = null;
            c.GotoNext(x => x.MatchRet());
            c.GotoPrev(x => x.MatchBrfalse(out end)); // IL_0073
            c.Index = 0;
            c.GotoNext(MoveType.AfterLabel, x => x.MatchBrfalse(out _), x => x.MatchLdarg(1)); // IL_0029
            c.Emit(OpCodes.Pop).Emit(OpCodes.Ldc_I4_1); // idk whats going on before this point but sotp, keep `skillSlot.stateMachine.state?.GetType() != self.activationState.stateType`
            c.GotoNext(MoveType.After, x => x.MatchCallOrCallvirt<EntityStateMachine>(nameof(EntityStateMachine.SetNextStateToMain)));
            c.Emit(OpCodes.Br, end); // ends after main set
            c.GotoNext(x => x.MatchBrfalse(end)); // hey remember IL_0073
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<bool, SkillDef, bool>>((orig, self) => orig || self.cancelSprintingOnActivation); // brfalse so both needs to be false
            c.GotoNext(x => x.MatchCallOrCallvirt<CharacterBody>("set_" + nameof(CharacterBody.isSprinting)));
            c.Emit(OpCodes.Pop).Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<SkillDef, bool>>(self =>
            {
                if (self.forceSprintDuringState) return true;
                if (self.cancelSprintingOnActivation) return false;
                Log.LogDebug("?? Something went wrong");
                return false;
            });
        }

        private void MakeSomethingAgile(On.RoR2.Skills.SkillCatalog.orig_SetSkillDefs orig, SkillDef[] newSkillDefs)
        {
            List<string> list = AgileList.Value.Split(',').ToList().ConvertAll(x => x.Trim());
            string txt = "Skills List: ";
            for (int i = 0; i < newSkillDefs.Length; i++)
            {
                SkillDef skillDef = newSkillDefs[i];
                if (skillDef?.skillNameToken != null && skillDef.skillNameToken.Trim().Length > 0) // null check
                {
                    txt += $"{(i > 0 ? ", " : "")}'{skillDef.skillNameToken}'";
                    if ((NerfSprint.Value ? list.Contains(skillDef.skillNameToken) : !list.Contains(skillDef.skillNameToken)) // list check
                        && !skillDef.canceledFromSprinting // already agile (not canceled from sprinting)
                        && (skillDef.keywordTokens == null || skillDef.keywordTokens.Length == 0 || skillDef.keywordTokens.Any(x => x == "KEYWORD_AGILE"))) // already agile... somehow
                    {
                        string skillDescriptionToken = skillDef.skillDescriptionToken;
                        string skillDescription = Language.GetString(skillDescriptionToken, "EN");
                        if (skillDescriptionToken != skillDescription && !skillDescription.Contains("Agile"))
                            LanguageAPI.AddOverlay(skillDescriptionToken, "<style=cIsUtility>Agile</style>. " + skillDescription);

                        string keywordToken = "KEYWORD_AGILE";
                        if (skillDef.keywordTokens == null || skillDef.keywordTokens.Length == 0)
                            skillDef.keywordTokens = new string[] { keywordToken };
                        else HGArrayUtilities.ArrayAppend(ref skillDef.keywordTokens, ref keywordToken);
                        skillDef.cancelSprintingOnActivation = false;
                    }
                }
            }
            Log.LogInfo(txt);
            orig(newSkillDefs);
        }
    }
}
