using BepInEx;
using BepInEx.Configuration;
using JetBrains.Annotations;
using MonoMod.Cil;
using R2API;
using R2API.Utils;
using RoR2;
using RoR2.Skills;
using System;
using System.Collections.Generic;
using System.Text;
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
        public static ConfigFile CustomConfigFile { get; set; }
        public static ConfigEntry<bool> NerfSprint { get; set; }
        public static ConfigEntry<float> EnergyDrinkBase { get; set; }
        public static ConfigEntry<float> EnergyDrinkStack { get; set; }

        public void Awake()
        {
            InitializeConfig();

            if (NerfSprint.Value)
            {
                SprintNerf();
            }
            else
            {
                SprintBuff();
            }
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
        }

        #region sprint buff
        void SprintBuff()
        {
            Debug.Log("You chose Sprint Buff! Based and shoepilled!");
            On.RoR2.Skills.SkillCatalog.SetSkillDefs += MakeEverythingAgile;
        }

        private void MakeEverythingAgile(On.RoR2.Skills.SkillCatalog.orig_SetSkillDefs orig, SkillDef[] newSkillDefs)
        {
            for (int i = 0; i < newSkillDefs.Length; i++)
            {
                SkillDef skillDef = newSkillDefs[i];
                if (skillDef != null && skillDef.canceledFromSprinting == false)
                {
                    bool b = false;
                    if (skillDef.keywordTokens == null || skillDef.keywordTokens.Length == 0)
                        b = true;

                    if (!b)
                    {
                        foreach(string s in skillDef.keywordTokens)
                            if(s == "KEYWORD_AGILE")
                            {
                                b = true;
                                break;
                            }
                    }

                    if (b)
                    {
                        string skillDescriptionToken = skillDef.skillDescriptionToken;
                        string skillDescription = Language.GetString(skillDescriptionToken, "EN");
                        if (skillDescriptionToken != skillDescription && !skillDescription.Contains("Agile"))
                        {
                            LanguageAPI.AddOverlay(skillDescriptionToken, "<style=cIsUtility>Agile</style>. " + skillDescription);
                        }

                        string keywordToken = "KEYWORD_AGILE";
                        if (skillDef.keywordTokens == null || skillDef.keywordTokens.Length == 0)
                        {
                            skillDef.keywordTokens = new string[] { keywordToken };
                        }
                        else
                        {
                            HGArrayUtilities.ArrayAppend(ref skillDef.keywordTokens, ref keywordToken);
                        }

                        skillDef.cancelSprintingOnActivation = false;
                    }
                }
            }
            orig(newSkillDefs);
        }
        #endregion

        #region sprint nerf

        public static float drinkSpeedBonusBase = 0.35f; //0.25
        public static float drinkSpeedBonusStack = 0.35f; //0.25
        void SprintNerf()
        {
            Debug.Log("You chose Sprint Nerf! Based and shoepilled!");
            On.RoR2.Skills.SkillDef.OnFixedUpdate += SkillDefFixedUpdate;

            SkillDef acridPoisonSkill = Resources.Load<SkillDef>("skilldefs/crocobody/CrocoPassivePoison");
            SkillDef acridBlightSkill = Resources.Load<SkillDef>("skilldefs/crocobody/CrocoPassiveBlight");
            acridPoisonSkill.cancelSprintingOnActivation = false;
            acridBlightSkill.cancelSprintingOnActivation = false;


            LanguageAPI.Add("ITEM_SPRINTBONUS_DESC",
                $"<style=cIsUtility>Sprint speed</style> is improved by <style=cIsUtility>{Tools.ConvertDecimal(drinkSpeedBonusBase)}</style> " +
                $"<style=cStack>(+{Tools.ConvertDecimal(drinkSpeedBonusStack)} per stack)</style>.");

            drinkSpeedBonusBase = EnergyDrinkBase.Value / 100;
            drinkSpeedBonusStack = EnergyDrinkStack.Value / 100;
            IL.RoR2.CharacterBody.RecalculateStats += DrinkBuff;
        }

        private void DrinkBuff(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            int countLoc = -1;
            c.GotoNext(MoveType.After,
                x => x.MatchLdsfld("RoR2.RoR2Content/Items", "SprintBonus"),
                x => x.MatchCallOrCallvirt<RoR2.Inventory>(nameof(RoR2.Inventory.GetItemCount)),
                x => x.MatchStloc(out countLoc)
                );

            c.GotoNext(MoveType.After,
                x => x.MatchLdcR4(out _),
                x => x.MatchLdloc(countLoc),
                x => x.MatchConvR4()
                );
            c.EmitDelegate<Func<float, float, float>>((speedBonus, itemCount) =>
            {
                float newSpeedBonus = 0;
                if (itemCount > 0)
                {
                    newSpeedBonus = drinkSpeedBonusBase + (drinkSpeedBonusStack * (itemCount - 1));
                }
                return newSpeedBonus;
            });
            c.Remove();
        }

        private void SkillDefFixedUpdate(On.RoR2.Skills.SkillDef.orig_OnFixedUpdate orig, RoR2.Skills.SkillDef self, [NotNull] GenericSkill skillSlot)
        {
            if (skillSlot.stateMachine == null)
            {
                orig(self, skillSlot);
                return;
            }

            skillSlot.RunRecharge(Time.fixedDeltaTime);
            if (skillSlot.stateMachine.state?.GetType() == self.activationState.stateType)
            {
                if (self.canceledFromSprinting && skillSlot.characterBody.isSprinting)
                {
                    skillSlot.stateMachine.SetNextStateToMain();
                }
                else
                {
                    if (self.forceSprintDuringState)
                    {
                        skillSlot.characterBody.isSprinting = true;
                    }
                    else if (self.cancelSprintingOnActivation)
                    {
                        skillSlot.characterBody.isSprinting = false;
                    }
                }
            }
        }
        #endregion
    }
}
