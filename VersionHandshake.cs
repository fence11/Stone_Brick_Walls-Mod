using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;

namespace PieceManagerModTemplate
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    public static class RegisterAndCheckVersion
    {
        private static void Prefix(ZNetPeer peer, ref ZNet __instance)
        {
            try
            {
                // Register version check call
                Log.Info("Registering version RPC handler");
                peer.m_rpc.Register($"{PieceManagerModTemplatePlugin.ModName}_VersionCheck", new Action<ZRpc, ZPackage>(RpcHandlers.RPC_PieceManagerModTemplate_Version));

                // Make calls to check versions
                Log.Info("Invoking version check");
                ZPackage zpackage = new();
                zpackage.Write(PieceManagerModTemplatePlugin.ModVersion);
                peer.m_rpc.Invoke($"{PieceManagerModTemplatePlugin.ModName}_VersionCheck", zpackage);
            }
            catch (Exception e)
            {
                Log.Unknown("starting the version check", e);
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
    public static class VerifyClient
    {
        private static bool Prefix(ZRpc rpc, ZPackage pkg, ref ZNet __instance)
        {
            try
            {
                if (!__instance.IsServer() || RpcHandlers.ValidatedPeers.Contains(rpc)) return true;
                // Disconnect peer if they didn't send mod version at all
                Log.Warning($"Peer ({rpc.m_socket.GetHostName()}) never sent version or couldn't due to previous disconnect, disconnecting");
                rpc.Invoke("Error", 3);
                return false; // Prevent calling underlying method
            }
            catch (Exception e)
            {
                Log.Unknown("verifying a connecting player", e);
                return true; // let the game handle the connection normally
            }
        }

        private static void Postfix(ZNet __instance)
        {
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), $"{PieceManagerModTemplatePlugin.ModName}RequestAdminSync", new ZPackage());
            }
            catch (Exception e)
            {
                Log.Unknown("requesting admin sync", e);
            }
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError))]
    public class ShowConnectionError
    {
        private static void Postfix(FejdStartup __instance)
        {
            try
            {
                if (__instance.m_connectionFailedPanel.activeSelf)
                {
                    __instance.m_connectionFailedError.fontSizeMax = 25;
                    __instance.m_connectionFailedError.fontSizeMin = 15;
                    __instance.m_connectionFailedError.text += "\n" + PieceManagerModTemplatePlugin.ConnectionError;
                }
            }
            catch (Exception e)
            {
                Log.Unknown("showing the connection error", e);
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
    public static class RemoveDisconnectedPeerFromVerified
    {
        private static void Prefix(ZNetPeer peer, ref ZNet __instance)
        {
            try
            {
                if (!__instance.IsServer()) return;
                // Remove peer from validated list
                Log.Info($"Peer ({peer.m_rpc.m_socket.GetHostName()}) disconnected, removing from validated list");
                _ = RpcHandlers.ValidatedPeers.Remove(peer.m_rpc);
            }
            catch (Exception e)
            {
                Log.Unknown("handling a disconnect", e);
            }
        }
    }

    public static class RpcHandlers
    {
        public static readonly List<ZRpc> ValidatedPeers = new();

        public static void RPC_PieceManagerModTemplate_Version(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                string? version = pkg.ReadString();
                Log.Info("Version check, local: " + PieceManagerModTemplatePlugin.ModVersion + ",  remote: " + version);
                if (version != PieceManagerModTemplatePlugin.ModVersion)
                {
                    PieceManagerModTemplatePlugin.ConnectionError = $"{PieceManagerModTemplatePlugin.ModName} Installed: {PieceManagerModTemplatePlugin.ModVersion}\n Needed: {version}";
                    if (!ZNet.instance.IsServer()) return;
                    // Different versions - force disconnect client from server
                    Log.Error($"Peer ({rpc.m_socket.GetHostName()}) has incompatible version, disconnecting...");
                    rpc.Invoke("Error", 3);
                }
                else if (!ZNet.instance.IsServer())
                {
                    // Enable mod on client if versions match
                    Log.Info("Received same version from server!");
                }
                else
                {
                    // Add client to validated list
                    Log.Info($"Adding peer ({rpc.m_socket.GetHostName()}) to validated list");
                    ValidatedPeers.Add(rpc);
                }
            }
            catch (EndOfStreamException e)
            {
                Log.Error($"Received a broken version message from a peer: {e.Message}");
            }
            catch (Exception e)
            {
                Log.Unknown("checking the mod version", e);
            }
        }
    }
}
