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

    [HarmonyPatch(typeof(LoadBalancingClient), nameof(LoadBalancingClient.ConnectUsingSettings))]
    public class AppSettingsPatch
    {
        static void Prefix(LoadBalancingClient __instance, AppSettings appSettings)
        {
            if (appSettings == null) return;

            bool appidIsValid = !string.IsNullOrWhiteSpace(Plugin.PhotonAppid) && Plugin.PhotonAppid != "PASTE_HERE";
            string configuredRegionLower = Plugin.PhotonRegion?.ToLowerInvariant();

            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo("AppSettingsPatch: Entered. Original AppSettings:");
            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"  UseNameServer: {appSettings.UseNameServer}, Server: {appSettings.Server}, FixedRegion: {appSettings.FixedRegion}, AppId: {appSettings.AppIdRealtime}");

            if (configuredRegionLower == "cn")
            {
                BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"AppSettingsPatch: Region is 'cn'. Overriding NameServer to 'ns.photonengine.cn'.");
                appSettings.UseNameServer = true;
                appSettings.Server = "ns.photonengine.cn";
                appSettings.FixedRegion = Plugin.PhotonRegion;

                if (appidIsValid)
                {
                    appSettings.AppIdRealtime = Plugin.PhotonAppid;
                    BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"AppSettingsPatch: Set AppIdRealtime to '{Plugin.PhotonAppid}' for 'cn' region.");
                }
            }
            else if (appidIsValid)
            {
                if (appSettings.AppIdRealtime != Plugin.PhotonAppid)
                {
                    BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"AppSettingsPatch: Setting AppIdRealtime in AppSettings to '{Plugin.PhotonAppid}'.");
                    appSettings.AppIdRealtime = Plugin.PhotonAppid;
                }
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
    public class ConnectToNameServerPatch
    {
        static bool Prefix(LoadBalancingClient __instance)
        {
            // 获取调用方信息
            var stack = new System.Diagnostics.StackTrace();
            var caller = stack.GetFrame(2)?.GetMethod();
            string callerInfo = caller != null ? $"{caller.DeclaringType?.FullName}.{caller.Name}" : "unknown";
            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"[Hook] ConnectToNameServer intercepted! Caller: {callerInfo}");

            // 构造自定义 AppSettings
            var settings = new AppSettings
            {
                AppIdRealtime = Plugin.PhotonAppid,
                UseNameServer = true,
                Server = "ns.photonengine.cn:5058",
                FixedRegion = Plugin.PhotonRegion
            };

            // 调用 ConnectUsingSettings
            bool result = __instance.ConnectUsingSettings(settings);
            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"[Hook] Called ConnectUsingSettings instead, result: {result}");

            // 阻止原方法执行
            return false;
        }
    }

    [HarmonyPatch(typeof(LoadBalancingClient), "GetNameServerAddress")] 
    public class GetNameServerAddressPatch
    {
        static void Postfix(LoadBalancingClient __instance, ref string __result) 
        {
            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"GetNameServerAddressPatch: Entered. Original returned address: '{__result}' for instance {__instance.GetHashCode()}");

            string configuredRegionLower = Plugin.PhotonRegion?.ToLowerInvariant();
            string targetNameServerWithPort = "ns.photonengine.cn:5058"; // Explicitly add port 5058

            if (configuredRegionLower == "cn")
            {
                if (!string.Equals(__result, targetNameServerWithPort, StringComparison.OrdinalIgnoreCase))
                {
                    BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogWarning($"GetNameServerAddressPatch: Plugin.PhotonRegion is 'cn'. Overriding returned NameServerAddress from '{__result}' to '{targetNameServerWithPort}'.");
                    __result = targetNameServerWithPort; // Modify the return value
                }
                else
                {
                    BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"GetNameServerAddressPatch: Plugin.PhotonRegion is 'cn' and original NameServerAddress is already '{__result}'. No change made.");
                }
            }
        }
    }

    [HarmonyPatch(typeof(LoadBalancingClient), nameof(LoadBalancingClient.DebugReturn))]
    public class DebugReturnPatch
    {
        static bool Prefix(LoadBalancingClient __instance, DebugLevel level, string message)
        {
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
