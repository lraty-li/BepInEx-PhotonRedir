using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Photon.Realtime;

namespace PhotonAppIDRedirector
{
    [BepInPlugin("photon-redirect", "Photon Redirector", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static string PhotonAppid { get; private set; }

        private void Awake()
        {
            // Load App ID from config
            PhotonAppid = Config.Bind("General", "PhotonAppID", "PASTE_HERE", "Set your Photon App ID here.").Value;

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
        }
    }

    [HarmonyPatch(typeof(LoadBalancingPeer), "OpAuthenticate")]
    public class PhotonPatch
    {
        static void Prefix(ref string appId, ref AuthenticationValues authValues)
        {
            //if (string.IsNullOrWhiteSpace(Plugin.PhotonAppid) || Plugin.PhotonAppid == "PASTE_HERE")
            //{
            //    Plugin.Instance.Logger.LogError("PhotonAppID is not set. Skipping redirection.");
            //    return;
            //}

            appId = Plugin.PhotonAppid;
            BepInEx.Logging.Logger.CreateLogSource("Photon Redirector").LogInfo($"Redirecting Photon Traffic"); // .LogInfo($"Redirecting Traffic to: (appId)")

            if (authValues != null)
            {
                authValues.AuthType = (CustomAuthenticationType)byte.MaxValue;
            }
        }
    }
}
