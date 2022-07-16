using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Photon.Pun;
using UnityEngine;
using HarmonyLib;

namespace ModdingUtils.AIMinion.Patches
{
    [HarmonyPatch(typeof(ProjectileInit))]
    class ProjectileInit_PatchGetCorrectPlayerOffline
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> RPCMethods()
        {
            //UnityEngine.Debug.Log("Fetching Offline Methods");
            yield return AccessTools.Method(typeof(ProjectileInit), "OFFLINE_Init");
            yield return AccessTools.Method(typeof(ProjectileInit), "OFFLINE_Init_SeparateGun");
            yield return AccessTools.Method(typeof(ProjectileInit), "OFFLINE_Init_noAmmoUse");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ConvertToPlayerID(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            var getPlayers = AccessTools.Field(typeof(PlayerManager), nameof(PlayerManager.players));
            var getItem = AccessTools.Method(typeof(List<Player>), "get_Item", new Type[] { typeof(int) });

            var getByPlayerID = AccessTools.Method(typeof(PlayerManager), "GetPlayerWithID", new Type[] { typeof(int) });

            for (int i = 0; i < codes.Count(); i++)
            {
                if ((codes[i].opcode == OpCodes.Ldfld) && ((FieldInfo)codes[i].operand == getPlayers) && (codes[i + 1].opcode == OpCodes.Ldarg_1) && (codes[i + 2].opcode == OpCodes.Callvirt) && ((MethodInfo)codes[i + 2].operand == getItem))
                {
                    codes.RemoveAt(i);
                    codes[i + 1] = new CodeInstruction(OpCodes.Callvirt, getByPlayerID);
                    //UnityEngine.Debug.Log("Using GetPlayerWithID for offline methods now.");
                }
            }

            return codes.AsEnumerable();
        }
    }

    [HarmonyPatch(typeof(ProjectileInit))]
    class ProjectileInit_PatchGetCorrectPlayerRPCs
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> RPCMethods()
        {
            //UnityEngine.Debug.Log("Fetching RPC Methods.");
            yield return AccessTools.Method(typeof(ProjectileInit), "RPCA_Init", new Type[] { typeof(int), typeof(int), typeof(float), typeof(float) });
            yield return AccessTools.Method(typeof(ProjectileInit), "RPCA_Init_SeparateGun", new Type[] { typeof(int), typeof(int), typeof(int), typeof(float), typeof(float) });
            yield return AccessTools.Method(typeof(ProjectileInit), "RPCA_Init_noAmmoUse", new Type[] { typeof(int), typeof(int), typeof(float), typeof(float) });
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ConvertToPlayerID(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            var getByActorID = AccessTools.Method(typeof(PlayerManager), "GetPlayerWithActorID", new Type[] { typeof(int) });
            var getByPlayerID = AccessTools.Method(typeof(PlayerManager), "GetPlayerWithID", new Type[] { typeof(int) });

            foreach (var ins in instructions)
            {
                if (!((ins.opcode == OpCodes.Callvirt) && ((MethodInfo)ins.operand == getByActorID)))
                {
                    yield return ins;
                }
                else
                {
                    yield return new CodeInstruction(OpCodes.Callvirt, getByPlayerID);
                    //UnityEngine.Debug.Log("Using GetPlayerWithID for RPCs now.");
                }
            }
        }
    }

    [HarmonyPatch]
    class Gun_PatchSendPlayerID
    {
        static Type GetNestedIDoBlockTransitionType()
        {
            var nestedTypes = typeof(Gun).GetNestedTypes(BindingFlags.Instance | BindingFlags.NonPublic);
            Type nestedType = null;

            foreach (var type in nestedTypes)
            {
                if (type.Name.Contains("FireBurst"))
                {
                    nestedType = type;
                    break;
                }
            }

            return nestedType;
        }
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(GetNestedIDoBlockTransitionType(), "MoveNext");
        }

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            //for (var i = 0; i < codes.Count; i++)
            //{
            //    UnityEngine.Debug.Log($"{i}: {codes[i].opcode}, {codes[i].operand}");
            //}

            //UnityEngine.Debug.Log("Getting Player Data");
            var getViewComponent = AccessTools.Method(typeof(GameObject), nameof(GameObject.GetComponent), generics: new Type[] { typeof(PhotonView) });

            // Stuff for Getting the player

            var dataPlayer = AccessTools.Field(typeof(CharacterData), nameof(CharacterData.player));
            var dataView = AccessTools.Field(typeof(CharacterData), nameof(CharacterData.view));

            var playerID = AccessTools.Field(typeof(Player), nameof(Player.playerID));

            // The RPCs to change it for
            string[] rpcs = new string[] { "RPCA_Init", "RPCA_Init_noAmmoUse", "RPCA_Init_SeparateGun" };

            for (int i = 0; i < codes.Count(); i++)
            {
                // Replace the RPC calls with the new ones
                if (codes[i].opcode == OpCodes.Ldstr && (rpcs.Contains(((string)codes[i].operand))))
                {
                    if (!((codes[i - 1].opcode == OpCodes.Callvirt) && ((MethodInfo)codes[i - 1].operand == getViewComponent)))
                    {
                        continue;
                    }

                    while (!(codes[i].opcode == OpCodes.Ldfld && ((FieldInfo)codes[i].operand == dataView)))
                    {
                        i++;
                    }

                    codes[i] = new CodeInstruction(OpCodes.Ldfld, dataPlayer);
                    codes[i + 1] = new CodeInstruction(OpCodes.Ldfld, playerID);

                }
            }

            //for (int i = 0; i < codes.Count; i++)
            //{
            //    UnityEngine.Debug.Log($"{i}: {codes[i].opcode}, {codes[i].operand}");
            //}

            return codes.AsEnumerable();
        }


    }
}
