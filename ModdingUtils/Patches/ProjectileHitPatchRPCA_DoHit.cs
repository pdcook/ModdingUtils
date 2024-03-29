﻿using System;
using HarmonyLib;
using ModdingUtils.Extensions;
using ModdingUtils.RoundsEffects;
using Photon.Pun;
using UnityEngine;

namespace ModdingUtils.Patches
{
	[Serializable]
	[HarmonyPatch(typeof(ProjectileHit), "Start")]
	class ProjectileHitPatchStart
	{
		private static void Prefix(ProjectileHit __instance)
        {
			__instance.GetAdditionalData().startTime = Time.time;
        }
	}
	[Serializable]
    [HarmonyPatch(typeof(ProjectileHit), "RPCA_DoHit")]
    class ProjectileHitPatchRPCA_DoHit
    {
		// prefix to prevent unwanted bullet on bullet collisions
		private static bool Prefix(ProjectileHit __instance, Vector2 hitPoint, Vector2 hitNormal, Vector2 vel, int viewID, int colliderID, bool wasBlocked, out bool __state)
        {
			__state = new bool();

			if (Time.time < __instance.GetAdditionalData().startTime + __instance.GetAdditionalData().inactiveDelay || ((__instance.ownPlayer != null && __instance.ownPlayer.GetComponent<Holding>().holdable.GetComponent<Gun>() != null) && (Time.time < __instance.GetAdditionalData().startTime + __instance.ownPlayer.GetComponent<Holding>().holdable.GetComponent<Gun>().GetAdditionalData().inactiveDelay)))
            {
				__state = false;
				return false; // don't run DoHit if the initial delay is not over

			}
			__state = true;
			return true;
        }
		// postfix to run HitEffect s and WasHitEffect s and HitSurfaceEffect s
		// as well as to apply punching
		private static void Postfix(ProjectileHit __instance, Vector2 hitPoint, Vector2 hitNormal, Vector2 vel, int viewID, int colliderID, bool wasBlocked, bool __state)
        {
			if (!__state)
            {
				return;
            }

			HitInfo hitInfo = new HitInfo();
			hitInfo.point = hitPoint;
			hitInfo.normal = hitNormal;
			hitInfo.collider = null;
			if (viewID != -1)
			{
				PhotonView photonView = PhotonNetwork.GetPhotonView(viewID);
				hitInfo.collider = photonView.GetComponentInChildren<Collider2D>();
				hitInfo.transform = photonView.transform;
			}
			else if (colliderID != -1)
			{
				hitInfo.collider = MapManager.instance.currentMap.Map.GetComponentsInChildren<Collider2D>()[colliderID];
				hitInfo.transform = hitInfo.collider.transform;
			}

			// if the bullet hit a collider, run the hit effects
			if (hitInfo.collider && __instance.gameObject.GetComponentInChildren<StopRecursion>() == null)
            {
				HitSurfaceEffect[] hitSurfaceEffects = __instance.ownPlayer.data.stats.GetAdditionalData().HitSurfaceEffects;
				foreach (HitSurfaceEffect hitSurfaceEffect in hitSurfaceEffects)
				{
					hitSurfaceEffect.Hit(hitPoint, hitNormal, vel);
				}
            }				

			HealthHandler healthHandler = null;
			if (hitInfo.transform)
			{
				healthHandler = hitInfo.transform.GetComponent<HealthHandler>();
			}

			if (healthHandler == null) { return; }

			// if there's a healthHandler then try to run the HitEffects
			CharacterStatModifiers characterStatModifiers = healthHandler.GetComponent<CharacterStatModifiers>();
			if (characterStatModifiers == null) { return; }

			Player damagingPlayer = __instance.ownPlayer;
			Player damagedPlayer = ((CharacterData)Traverse.Create(characterStatModifiers).Field("data").GetValue()).player;

			bool selfDamage = damagingPlayer != null && damagingPlayer.transform.root == damagedPlayer.transform;

			WasHitEffect[] wasHitEffects = characterStatModifiers.GetAdditionalData().WasHitEffects;
			HitEffect[] hitEffects = damagingPlayer.data.stats.GetAdditionalData().HitEffects;

			// run HitEffects if the bullet was not blocked
			if (!wasBlocked)
            {
				foreach (WasHitEffect wasHitEffect in wasHitEffects)
				{
					wasHitEffect.WasDealtDamage(__instance.transform.forward * __instance.damage * __instance.dealDamageMultiplierr, selfDamage);
				}

				foreach (HitEffect hitEffect in hitEffects)
				{
					hitEffect.DealtDamage(__instance.transform.forward * __instance.damage * __instance.dealDamageMultiplierr, selfDamage, damagedPlayer);
				}
			}

			// apply punching
			if (__instance.ownPlayer.data.stats.GetAdditionalData().punch && wasBlocked)
			{
				// run HitEffects

				foreach (WasHitEffect wasHitEffect in wasHitEffects)
				{
					wasHitEffect.WasDealtDamage(__instance.transform.forward * __instance.damage * __instance.dealDamageMultiplierr, selfDamage);
				}

				foreach (HitEffect hitEffect in hitEffects)
				{
					hitEffect.DealtDamage(__instance.transform.forward * __instance.damage * __instance.dealDamageMultiplierr, selfDamage, damagedPlayer);
				}

				if (__instance.isAllowedToSpawnObjects)
				{
					GamefeelManager.GameFeel(__instance.transform.forward * 3f * __instance.shake);
					DynamicParticles.instance.PlayBulletHit(__instance.damage, __instance.transform, hitInfo, __instance.projectileColor);
					for (int i = 0; i < __instance.objectsToSpawn.Length; i++)
					{
						ObjectsToSpawn.SpawnObject(__instance.transform, hitInfo, __instance.objectsToSpawn[i], healthHandler, __instance.team, __instance.damage, (SpawnedAttack)Traverse.Create(__instance).Field("spawnedAttack").GetValue(), false);
					}
					__instance.transform.position = hitInfo.point + hitInfo.normal * 0.01f;
				}

				bool flag = false;
				if (__instance.effects != null && __instance.effects.Count != 0)
				{
					for (int j = 0; j < __instance.effects.Count; j++)
					{
						HasToReturn hasToReturn = __instance.effects[j].DoHitEffect(hitInfo);
						if (hasToReturn == HasToReturn.hasToReturn)
						{
							flag = true;
						}
						if (hasToReturn == HasToReturn.hasToReturnNow)
						{
							return;
						}
					}
				}
				if (flag)
				{
					return;
				}
				if ((Action)Traverse.Create(__instance).Field("hitAction").GetValue() != null)
				{
					((Action)Traverse.Create(__instance).Field("hitAction").GetValue())();
				}
				if ((Action<HitInfo>)Traverse.Create(__instance).Field("hitActionWithData").GetValue() != null)
				{
					((Action<HitInfo>)Traverse.Create(__instance).Field("hitActionWithData").GetValue())(hitInfo);
				}

				__instance.gameObject.SetActive(false);
				PhotonNetwork.Destroy(__instance.gameObject);
			}

			return;

		}
	}
}
