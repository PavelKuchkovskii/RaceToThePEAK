using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakRace.Core;

/// <summary>
/// Keeps RaceToThePeak and PEAK Unlimited from toggling two MenuWindows from
/// the same F2 press. No PEAK Unlimited setting or gameplay patch is changed.
/// </summary>
internal static class PeakUnlimitedCompatibility
{
    private const string PluginTypeName = "PEAKUnlimited.Plugin";
    private const string ConfigurationUiTypeName = "PEAKUnlimited.Configuration.ModConfigurationUI";

    internal static void Apply(Harmony harmony)
    {
        Type unlimitedPluginType = AccessTools.TypeByName(PluginTypeName);
        MethodInfo unlimitedUpdate = unlimitedPluginType == null
            ? null
            : AccessTools.Method(unlimitedPluginType, "Update");
        MethodInfo prefix = AccessTools.Method(
            typeof(PeakUnlimitedCompatibility),
            nameof(BeforePeakUnlimitedUpdate));

        if (unlimitedUpdate == null || prefix == null)
        {
            Plugin.Log.LogInfo("PEAK Unlimited was not detected; F2 compatibility patch is not needed.");
            return;
        }

        harmony.Patch(unlimitedUpdate, prefix: new HarmonyMethod(prefix));
        Plugin.Log.LogInfo("PEAK Unlimited F2 menu conflict protection enabled.");
    }

    internal static void CloseConfigurationMenu()
    {
        MenuWindow[] windows = Resources.FindObjectsOfTypeAll<MenuWindow>();
        foreach (MenuWindow window in windows)
        {
            if (window == null || window.GetType().FullName != ConfigurationUiTypeName)
            {
                continue;
            }

            FieldInfo visibleField = AccessTools.Field(window.GetType(), "_visible");
            bool reportsVisible = visibleField?.GetValue(window) is true;
            bool registered = MenuWindow.AllActiveWindows.Contains(window);
            if (!reportsVisible && !window.isOpen && !window.inputActive && !registered)
            {
                continue;
            }

            // PEAK Unlimited keeps its own visibility flag in addition to the
            // MenuWindow state, so both must be cleared to release the cursor.
            visibleField?.SetValue(window, false);
            window.ForceClose();
        }
    }

    private static bool BeforePeakUnlimitedUpdate()
    {
        // RaceToThePeak owns F2 for its host panels. PEAK Unlimited continues
        // to work normally on every other frame and on any other configured key.
        return !Plugin.ShouldReserveF2
            || Keyboard.current == null
            || !Keyboard.current.f2Key.wasPressedThisFrame;
    }
}
