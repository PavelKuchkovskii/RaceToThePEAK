using Peak.Afflictions;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>Copies authored Energy Drink and Lollipop effects from PEAK's item prefabs.</summary>
internal static class CampfireItemEffectApplicator
{
    internal static void ApplyAdrenaline(Character character)
    {
        if (character == null || character.refs?.afflictions == null)
        {
            return;
        }

        bool appliedLollipop = ApplyItem(character, "lollipop");
        bool appliedEnergyDrink = ApplyItem(character, "energy", "drink");

        // Keep the ability functional on renamed/modded item databases while
        // preferring PEAK's exact serialized effects whenever they are present.
        if (!appliedLollipop)
        {
            character.refs.afflictions.AddAffliction(
                new Affliction_InfiniteStamina(10f));
        }
        if (!appliedEnergyDrink)
        {
            character.refs.afflictions.AddAffliction(new Affliction_FasterBoi
            {
                totalTime = 15f,
                moveSpeedMod = 0.25f,
                climbSpeedMod = 0.25f,
                drowsyOnEnd = 0.25f
            });
        }
    }

    private static bool ApplyItem(Character character, params string[] nameTokens)
    {
        ItemDatabase database = SingletonAsset<ItemDatabase>.Instance;
        if (database == null)
        {
            return false;
        }

        Item prefab = database.itemLookup.Values.FirstOrDefault(item =>
        {
            string normalized = Normalize(item != null ? item.gameObject.name : string.Empty);
            return nameTokens.All(token => normalized.Contains(Normalize(token)));
        });
        if (prefab == null)
        {
            Plugin.Log.LogWarning(
                $"Could not find authored item effects for {string.Join(" ", nameTokens)}.");
            return false;
        }

        bool applied = false;
        foreach (Action_ApplyAffliction action in
            prefab.GetComponentsInChildren<Action_ApplyAffliction>(true))
        {
            if (!RunsOnConsumption(action))
            {
                continue;
            }

            if (action.affliction != null)
            {
                character.refs.afflictions.AddAffliction(action.affliction);
                applied = true;
            }
            if (action.extraAfflictions == null)
            {
                continue;
            }

            foreach (Affliction affliction in action.extraAfflictions)
            {
                if (affliction != null)
                {
                    character.refs.afflictions.AddAffliction(affliction);
                    applied = true;
                }
            }
        }

        foreach (Action_ApplyInfiniteStamina action in
            prefab.GetComponentsInChildren<Action_ApplyInfiniteStamina>(true))
        {
            if (RunsOnConsumption(action))
            {
                character.refs.afflictions.AddAffliction(
                    new Affliction_InfiniteStamina(action.buffTime));
                applied = true;
            }
        }

        foreach (Action_GiveExtraStamina action in
            prefab.GetComponentsInChildren<Action_GiveExtraStamina>(true))
        {
            if (RunsOnConsumption(action))
            {
                character.AddExtraStamina(action.amount);
                applied = true;
            }
        }

        foreach (Action_ModifyStatus action in
            prefab.GetComponentsInChildren<Action_ModifyStatus>(true))
        {
            if (!RunsOnConsumption(action))
            {
                continue;
            }

            if (action.changeAmount < 0f)
            {
                character.refs.afflictions.SubtractStatus(
                    action.statusType,
                    Mathf.Abs(action.changeAmount));
            }
            else
            {
                character.refs.afflictions.AddStatus(
                    action.statusType,
                    Mathf.Abs(action.changeAmount));
            }
            applied = true;
        }

        return applied;
    }

    private static bool RunsOnConsumption(ItemAction action)
    {
        return action != null && (action.OnConsumed || action.OnCastFinished);
    }

    private static string Normalize(string value)
    {
        StringBuilder result = new(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(char.ToLowerInvariant(character));
            }
        }
        return result.ToString();
    }
}
