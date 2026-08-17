using HarmonyLib;
using PeakRace.Core;

namespace PeakRace.Patch;

/// <summary>Hooks stamina spending and pass-out transitions for timed/passive effects.</summary>
[HarmonyPatch]
internal static class CampfireAbilityPatch
{
    [HarmonyPatch(typeof(Character), "UseStamina")]
    [HarmonyPrefix]
    private static void ApplyStaminaConsumptionMultipliers(
        Character __instance,
        ref float usage)
    {
        if (usage <= 0f || CampfireAbilityManager.Instance == null)
        {
            return;
        }

        if (CampfireAbilityManager.Instance.IsExhausted(__instance))
        {
            usage *= 1.4f;
        }
        if (CampfireAbilityManager.Instance.IsGhostRunnerAffected(__instance))
        {
            usage *= 1.2f;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddStamina))]
    [HarmonyPrefix]
    private static void ApplyCatchUpStaminaRecovery(Character __instance, ref float add)
    {
        if (add > 0f && CampfireAbilityManager.Instance != null)
        {
            add *= CampfireAbilityManager.Instance.GetCatchUpMultiplier(__instance);
        }
    }

    [HarmonyPatch(typeof(Character), "RPCA_PassOut")]
    [HarmonyPrefix]
    private static bool BlockPassOutDuringSecondWindImmunity(Character __instance)
    {
        CampfireAbilityManager manager = CampfireAbilityManager.Instance;
        return manager?.HasSecondWindImmunity(__instance) != true
            && manager?.HasMegaLaunchImmunity(__instance) != true;
    }

    [HarmonyPatch(typeof(Character), "RPCA_PassOut")]
    [HarmonyPostfix]
    private static void TriggerSecondWind(Character __instance)
    {
        if (__instance.photonView.IsMine
            && RaceSettingsManager.Current.Mode == RespawnMode.Pvp
            && CampfireAbilityManager.Instance?.GetAbility(__instance)
                == CampfireAbility.SecondWind)
        {
            __instance.GetComponent<CampfireAbilityState>()?.RequestSecondWind();
        }
    }

    [HarmonyPatch(typeof(CharacterMovement), "GetMovementForce")]
    [HarmonyPostfix]
    private static void ApplyCatchUpGroundSpeed(
        CharacterMovement __instance,
        ref float __result)
    {
        Character character = __instance.GetComponent<Character>();
        if (__result > 0f && CampfireAbilityManager.Instance != null)
        {
            __result *= CampfireAbilityManager.Instance.GetCatchUpMultiplier(character);
        }
    }

    [HarmonyPatch(typeof(CharacterMovement), "JumpRpc")]
    [HarmonyPrefix]
    private static void BeginCatchUpJump(
        CharacterMovement __instance,
        out float __state)
    {
        __state = __instance.jumpImpulse;
        Character character = __instance.GetComponent<Character>();
        if (CampfireAbilityManager.Instance != null)
        {
            __instance.jumpImpulse *= CampfireAbilityManager.Instance
                .GetCatchUpMultiplier(character);
        }
    }

    [HarmonyPatch(typeof(CharacterMovement), "JumpRpc")]
    [HarmonyPostfix]
    private static void EndCatchUpJump(
        CharacterMovement __instance,
        float __state)
    {
        __instance.jumpImpulse = __state;
    }

    [HarmonyPatch(typeof(CharacterClimbing), "FixedUpdate")]
    [HarmonyPrefix]
    private static void BeginCatchUpWallClimb(
        CharacterClimbing __instance,
        out float __state)
    {
        __state = __instance.climbSpeedMod;
        ApplyCatchUpClimbMultiplier(
            __instance.GetComponent<Character>(),
            ref __instance.climbSpeedMod);
    }

    [HarmonyPatch(typeof(CharacterClimbing), "FixedUpdate")]
    [HarmonyPostfix]
    private static void EndCatchUpWallClimb(
        CharacterClimbing __instance,
        float __state)
    {
        __instance.climbSpeedMod = __state;
    }

    [HarmonyPatch(typeof(CharacterRopeHandling), "Update")]
    [HarmonyPrefix]
    private static void BeginCatchUpRopeClimb(
        CharacterRopeHandling __instance,
        out float __state)
    {
        __state = __instance.climbSpeedMod;
        ApplyCatchUpClimbMultiplier(
            __instance.GetComponent<Character>(),
            ref __instance.climbSpeedMod);
    }

    [HarmonyPatch(typeof(CharacterRopeHandling), "Update")]
    [HarmonyPostfix]
    private static void EndCatchUpRopeClimb(
        CharacterRopeHandling __instance,
        float __state)
    {
        __instance.climbSpeedMod = __state;
    }

    [HarmonyPatch(typeof(CharacterVineClimbing), "FixedUpdate")]
    [HarmonyPrefix]
    private static void BeginCatchUpVineClimb(
        CharacterVineClimbing __instance,
        out float __state)
    {
        __state = __instance.climbSpeedMod;
        ApplyCatchUpClimbMultiplier(
            __instance.GetComponent<Character>(),
            ref __instance.climbSpeedMod);
    }

    [HarmonyPatch(typeof(CharacterVineClimbing), "FixedUpdate")]
    [HarmonyPostfix]
    private static void EndCatchUpVineClimb(
        CharacterVineClimbing __instance,
        float __state)
    {
        __instance.climbSpeedMod = __state;
    }

    private static void ApplyCatchUpClimbMultiplier(
        Character character,
        ref float climbSpeedModifier)
    {
        if (CampfireAbilityManager.Instance != null)
        {
            climbSpeedModifier *= CampfireAbilityManager.Instance
                .GetCatchUpMultiplier(character);
        }
    }
}
