using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Photon.Realtime;
using ExitGames.Client.Photon; // Ensure your project references the DLL containing this namespace

namespace PhotonAppIDRedirector
{
    [BepInPlugin("photon-redirect", "Photon Redirector", "1.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static string PhotonAppid { get; private set; }
        public static string PhotonRegion { get; private set; }

        private void Awake()
        {

            PhotonAppid = Config.Bind("General", "PhotonAppID", "PASTE_HERE", "Set your Photon App ID here.").Value;
            //https://vibrantlink.com/chinacloudpun/#using_the_chinese_mainland_region
            PhotonRegion = Config.Bind("General", "PhotonRegion", "", "Set your Photon region here (e.g., us, eu, tr, cn), see https://doc.photonengine.com/pun/current/connection-and-authentication/regions#photon-cloud-for-gaming for more.").Value;

            if (string.IsNullOrWhiteSpace(PhotonAppid) || PhotonAppid == "PASTE_HERE")
            {
                Logger.LogError("PhotonAppID is missing or invalid!!");
                Logger.LogInfo("Skipping Redirection.");
                return;
            }

            Harmony harmony = new Harmony("auth.patch");
            harmony.PatchAll();

            Logger.LogInfo("Plugin Loaded!");
            Logger.LogInfo("Using AppID: " + PhotonAppid);

            if (string.IsNullOrWhiteSpace(PhotonRegion))
            {
                Logger.LogWarning("PhotonRegion is not set. Using default region selection logic by the game, AppID will be redirected if game uses Photon.");
            }
            else
            {
                Logger.LogInfo("Using Region: " + PhotonRegion + (PhotonRegion.ToLowerInvariant() == "cn" ? " (China Mainland - ns.photonengine.cn will be used)" : ""));
            }
        }
    }


    [HarmonyPatch(typeof(LoadBalancingClient), "ConnectToRegionMaster")]
    public class RegionPatch
    {
        static bool Prefix(LoadBalancingClient __instance, ref string region)
        {
            if (!string.IsNullOrWhiteSpace(Plugin.PhotonRegion))
            {
                BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogWarning($"Overriding region from '{region}' to '{Plugin.PhotonRegion}'");
                region = Plugin.PhotonRegion; // Modify the region parameter
            }
            return true; // Let the original method run with the modified region
        }
    }

    [HarmonyPatch(typeof(LoadBalancingPeer), "OpAuthenticate")]
    public class PhotonPatch
    {
        static void Prefix(ref string appId, ref AuthenticationValues authValues)
        {
            appId = Plugin.PhotonAppid;
            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"Redirecting Photon Traffic"); // .LogInfo($"Redirecting Photon Traffic to AppID: {appId}"); << for debugging
            if (authValues != null)
            {
                BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"PhotonPatch: Overriding AuthType from '{authValues.AuthType}' to '{CustomAuthenticationType.None}'.");
                authValues.AuthType = CustomAuthenticationType.None;
            }
        }
    }

    [HarmonyPatch(typeof(LoadBalancingClient), nameof(LoadBalancingClient.ConnectToNameServer))]
    public class ConnectToNameServerPostfixPatch
    {
        static void Postfix(LoadBalancingClient __instance)
        {
            // Only apply patch if PhotonRegion is specifically "cn" (case-insensitive)
            if (Plugin.PhotonRegion?.ToLowerInvariant() == "cn")
            {
                BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"[Hook] ConnectToNameServer Postfix: PhotonRegion is 'cn'. Attempting to set CloudRegion to '{Plugin.PhotonRegion}' via reflection.");
                try
                {
                    var cloudRegionProp = AccessTools.Property(typeof(LoadBalancingClient), "CloudRegion");
                    if (cloudRegionProp != null && cloudRegionProp.CanWrite)
                    {
                        cloudRegionProp.SetValue(__instance, Plugin.PhotonRegion);
                        BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"[Hook] ConnectToNameServer Postfix: Successfully set CloudRegion using property setter to '{Plugin.PhotonRegion}' for 'cn' region.");
                    }
                    // Per user's simplification, no 'else' for property not found or not writable
                }
                catch (Exception e)
                {
                    BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogError($"[Hook] ConnectToNameServer Postfix: Error setting CloudRegion via reflection for 'cn' region: {e.ToString()}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(LoadBalancingClient), "GetNameServerAddress")] 
    public class GetNameServerAddressPatch
    {
        static void Prefix(LoadBalancingClient __instance, ref string __result) 
        {
            string configuredRegionLower = Plugin.PhotonRegion?.ToLowerInvariant();

            if (configuredRegionLower == "cn")
            {
                BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"GetNameServerAddressPatch: Plugin.PhotonRegion is 'cn'. Overriding returned NameServerAddress from '{__instance.NameServerHost}' to 'ns.photonengine.cn'.");
                __instance.NameServerHost = "ns.photonengine.cn";
            }
        }
        static void Postfix(LoadBalancingClient __instance, ref string __result) 
        {
            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"GetNameServerAddressPatch: Entered. Original returned address: '{__result}' for instance {__instance.GetHashCode()}");
        }
    }


    [HarmonyPatch(typeof(LoadBalancingClient), nameof(LoadBalancingClient.DebugReturn))]
    public class DebugReturnPatch
    {
        static bool Prefix(LoadBalancingClient __instance, DebugLevel level, string message)
        {
            __instance.LoadBalancingPeer.DebugOut = DebugLevel.ALL;

            BepInEx.Logging.Logger.CreateLogSource("PhotonInternal").Log(ToBepInExLogLevel(level), $"[P:{level}] {message}");
            return true; 
        }

        private static BepInEx.Logging.LogLevel ToBepInExLogLevel(DebugLevel photonLevel)
        {
            switch (photonLevel)
            {
                case DebugLevel.OFF:
                    return BepInEx.Logging.LogLevel.None;
                case DebugLevel.ERROR:
                    return BepInEx.Logging.LogLevel.Error;
                case DebugLevel.WARNING:
                    return BepInEx.Logging.LogLevel.Warning;
                case DebugLevel.INFO:
                    return BepInEx.Logging.LogLevel.Info;
                case DebugLevel.ALL:
                    return BepInEx.Logging.LogLevel.Debug; 
                default:
                    return BepInEx.Logging.LogLevel.Message;
            }
        }
    }

}
