using System;
using RoR2;
using BepInEx;
using R2API;
using System.Reflection;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SeikoML;
using UnityEngine;
using System.Collections.Generic;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine.Networking;

namespace SeikoML_Compatibility_Layer
{
	[BepInDependency("com.bepis.r2api",BepInDependency.DependencyFlags.HardDependency)]
	[BepInPlugin("com.vyklade.seikomlcompat", "SeikoML Compatibility Layer", "1.0")]
	public class CompatLayer : BaseUnityPlugin
	{
		private List<object> ModInstances { get; set; } = new List<object>();
		public void Awake()
		{
			var gamePath = System.IO.Path.GetDirectoryName(Directory.GetCurrentDirectory());
			var modsPath = System.IO.Path.Combine(gamePath, "Risk of Rain 2", "BepInEx", "plugins", "SeikoMLCompat");

			if (!Directory.Exists(modsPath))
			{
				Logger.LogMessage($"[RoR2ML] No mods installed. Please install mods to {modsPath}");
				return;
			}

			foreach (string archiveFileName in Directory.EnumerateFiles(modsPath, "*.zip"))
			{
				using (var zip = ZipFile.OpenRead(archiveFileName))
				{
					var modZipEntry = zip.GetEntry("Mod.dll");
					if (modZipEntry == null)
					{
						continue;
					}
					using (var file = modZipEntry.Open())
					using (var fileMemoryStream = new MemoryStream())
					{
						file.CopyTo(fileMemoryStream);
						var modAssembly = Assembly.Load(fileMemoryStream.ToArray());
						try
						{
							var modAssemblyTypes = modAssembly.GetTypes();
							var modClasses = modAssemblyTypes.Where(x => x.GetInterfaces().Contains(typeof(ISeikoMod)));
							var manifest = zip.GetEntry("manifest.json");
							if (manifest == null) name = modAssembly.GetName().ToString();
							foreach (var modClass in modClasses)
							{
								ModInstances.Add(Activator.CreateInstance(modClass));
							}
						}
						catch (ReflectionTypeLoadException ex)
						{
							// now look at ex.LoaderExceptions - this is an Exception[], so:
							foreach (Exception inner in ex.LoaderExceptions)
							{
								// write details of "inner", in particular inner.Message
								Debug.Log(inner.Message);
								Debug.Log(inner);
							}
						}
					}
				}
			}

			On.RoR2.DotController.InflictDot += (orig, vobj, aobv, di, dur, dmgmult) =>
			{
				dmgmult = ModifyDoT.Damage(vobj, aobv, di, dmgmult);
				dur = ModifyDoT.Duration(vobj, aobv, di, dur);
				orig(vobj, aobv, di, dur, dmgmult);
			};

			SurvivorAPI.SurvivorCatalogReady += (x, y) =>
			{
				foreach (ISeikoMod mod in ModInstances)
				{
					mod.OnStart();
				}
				foreach (var sur in ModLoader.SurvivorMods)
				{
					SurvivorAPI.SurvivorDefinitions.Add(sur.RegisterModSurvivor());
				}
			};
		}
		public static void ProcessDoT(GameObject victimObject, GameObject attackerObject, DotController.DotIndex dotIndex, float duration = 8f, float damageMultiplier = 1f)
		{
			{
				damageMultiplier = ModifyDoT.Damage(victimObject, attackerObject, dotIndex, damageMultiplier);
				duration = ModifyDoT.Duration(victimObject, attackerObject, dotIndex, duration);
				if (!NetworkServer.active)
				{
					Debug.LogWarning("[Server] function 'System.Void RoR2.DotController::InflictDot(UnityEngine.GameObject,UnityEngine.GameObject,RoR2.DotController/DotIndex,System.Single,System.Single)' called on client");
					return;
				}
				if (victimObject && attackerObject)
				{
					DotController component;
					if (!DotController.dotControllerLocator.TryGetValue(victimObject.GetInstanceID(), out component))
					{
						GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(Resources.Load<GameObject>("Prefabs/NetworkedObjects/DotController"));
						component = gameObject.GetComponent<DotController>();
						component.victimObject = victimObject;
						component.recordedVictimInstanceId = victimObject.GetInstanceID();
						DotController.dotControllerLocator.Add(component.recordedVictimInstanceId, component);
						NetworkServer.Spawn(gameObject);
					}
					component.AddDot(attackerObject, duration, dotIndex, damageMultiplier);
				}
			}
		}
	}
}
