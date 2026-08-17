namespace PeakRace.Core;

/// <summary>
/// One synchronized campfire slot belongs to each racer. Chaos intentionally
/// lives outside this enum because it is an additional one-charge slot.
/// </summary>
internal enum CampfireAbility
{
    None = 0,
    Adrenaline = 1,
    Shield = 2,
    Exhaust = 3,
    SecondWind = 4,
    CatchUp = 5,
    Recall = 6,
    ChaosHorn = 7,
    GhostRunner = 8,
    MegaLaunch = 9
}

internal enum ChaosEffect
{
    FullStamina = 0,
    InfiniteStamina = 1,
    GlobalAdrenaline = 2,
    GlobalUnconscious = 3,
    PlayerSwap = 4,
    PreviousCampfire = 5,
    MiddleCampfire = 6
}

internal enum MegaLaunchFoodChanceTier
{
    Leader = 0,
    Middle = 1,
    NearLast = 2,
    Last = 3,
    FarBehind = 4
}

internal static class CampfireAbilityInfo
{
    internal static string GetName(CampfireAbility ability)
    {
        return ability switch
        {
            CampfireAbility.Adrenaline => "ADRENALINE",
            CampfireAbility.Shield => "SHIELD",
            CampfireAbility.Exhaust => "EXHAUST",
            CampfireAbility.SecondWind => "SECOND WIND",
            CampfireAbility.CatchUp => "CATCH UP",
            CampfireAbility.Recall => "RECALL",
            CampfireAbility.ChaosHorn => "CHAOS HORN",
            CampfireAbility.GhostRunner => "GHOST RUNNER",
            CampfireAbility.MegaLaunch => "MEGA LAUNCH",
            _ => "EMPTY"
        };
    }

    internal static bool IsPassive(CampfireAbility ability)
    {
        return ability is CampfireAbility.Shield
            or CampfireAbility.SecondWind
            or CampfireAbility.CatchUp;
    }

    internal static bool IsReusable(CampfireAbility ability)
    {
        return ability == CampfireAbility.MegaLaunch;
    }
}
