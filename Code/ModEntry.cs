using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Reflection;
using StardewValley.Buildings;
using StardewValley.Objects;
using System;
using System.Collections.Generic;
using StardewValley.GameData.Buildings;
using System.Linq;
using System.IO;

namespace ultimatecoopnbarn
{
    public interface IManagedTokenString
    {
        bool IsValid { get; }
        string ValidationError { get; }
        bool IsReady { get; }
        string Value { get; }
        IEnumerable<int> UpdateContext();
    }
    public interface IContentPatcherAPI
    {
        bool IsConditionsApiReady { get; }
        IManagedTokenString ParseTokenString(IManifest manifest, string rawValue, ISemanticVersion formatVersion, string[] assumeModIds = null);
        void RegisterToken(IManifest mod, string name, Func<IEnumerable<string>> getValue);
    }
    public class ModEntry : Mod
    {
        public static ModEntry modInstance;
        public static IContentPack cpPack;
        internal const string UltimateCP = "bobkalonger.ultimatecoopnbarnCP_";
        internal const string SVExpandCP = "FlashShifter.StardewValleyExpandedCP_";
        internal const string UltimateBarn = $"{UltimateCP}UltimateBarn";
        internal const string UltimateCoop = $"{UltimateCP}UltimateCoop";
        internal const string SuperDenseBarn = $"{UltimateCP}SuperDenseBarn";
        internal const string SuperDenseCoop = $"{UltimateCP}SuperDenseCoop";
        private const string UltimatePremiumCoop = $"{SVExpandCP}PremiumCoop";
        private const string UltimatePremiumBarn = $"{SVExpandCP}PremiumBarn";
        public override void Entry(IModHelper helper)
        {
            modInstance = this;

            I18n.Init(Helper.Translation);

            var mi = Helper.ModRegistry.Get("bobkalonger.ultimatecoopnbarnCP");
            cpPack = mi.GetType().GetProperty("ContentPack")?.GetValue(mi) as IContentPack;

            Helper.Events.GameLoop.ReturnedToTitle += (s, e) =>
            {
                _vppConfigWatcher?.Dispose();
                _vppConfigWatcher = null;
                _vppDir = null;
                _cachedEnabled = null;
            };

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Player.Warped += PlayerOnWarped;
            helper.Events.GameLoop.DayStarted += OnDayStarted;

            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        ///<inheritdoc cref="IGameLoopEvents.GameLaunched"/>
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var cp = Helper.ModRegistry.GetApi<IContentPatcherAPI>("Pathoschild.ContentPatcher");
            if (cp is null)
            {
                Monitor.Log("Content Patcher not found; dynamic tokens will not be available.", LogLevel.Warn);
                return;
            }

            cp.RegisterToken(ModManifest, "UltimateMode", GetUltimateMode);
            cp.RegisterToken(ModManifest, "OvercrowdingConfigEnabled", () => new[] { OvercrowdingVPP() });
            
            _cp = cp;        
        }
        private IEnumerable<string> GetUltimateMode()
        {
            if (!Context.IsWorldReady)
            {
                return Array.Empty<string>();
            }
            return new[] { ComputeUltimateMode() };
        }
        private IContentPatcherAPI _cp;
        private string _lastMode;
        private string GetUpgradeConfig()
        {
            var config = cpPack?.ReadJsonFile<Dictionary<string, string>>("config.json");
            if (config != null && config.TryGetValue("Ultimate Building Upgrade", out string value))
                return value;
            return "Auto";
        }
        private string ComputeUltimateMode()
        {
            string upgradeChoice = GetUpgradeConfig();

            bool hasJMCB = Helper.ModRegistry.IsLoaded("jenf1.megacoopbarn");
            bool hasUARC = Helper.ModRegistry.IsLoaded("UncleArya.ResourceChickens");

            string result;

            if (upgradeChoice != "Auto")
            {
                string manual = upgradeChoice;

                bool validSelection = manual switch
                {
                    "SVE"  => Helper.ModRegistry.IsLoaded("FlashShifter.StardewValleyExpandedCP"),
                    "Giga" => Helper.ModRegistry.IsLoaded("bobkalonger.gigacoopnbarn"),
                    "Mega" => Helper.ModRegistry.IsLoaded("jenf1.megacoopbarn") || Helper.ModRegistry.IsLoaded("UncleArya.ResourceChickens"),
                    _      => false
                };

                if (validSelection)
                {
                    if (manual == "Mega")
                    {
                        if (hasJMCB && hasUARC) return "Both";
                        if (hasJMCB) return "Mega";
                        return "Giant";
                    }
                    else result = manual;
                }
                else
                {
                    Monitor.Log($"Config set to '{manual}' but that mod isn't installed, falling back on automatic behavior.", LogLevel.Warn);
                    result = ComputeAuto(hasJMCB, hasUARC);
                }
            }
            else
            {
                result = ComputeAuto(hasJMCB, hasUARC);
            }

            if (result != _lastMode)
            {
                _lastMode = result;
                Helper.GameContent.InvalidateCache("Data/Buildings");
            }

            return result;
        }

        private string ComputeAuto(bool hasJMCB, bool hasUARC)
        {
            if (Helper.ModRegistry.IsLoaded("FlashShifter.StardewValleyExpandedCP")) return "SVE";
            if (Helper.ModRegistry.IsLoaded("bobkalonger.gigacoopnbarn")) return "Giga";
            if (hasJMCB && hasUARC) return "Both";
            if (hasJMCB) return "Mega";
            if (hasUARC) return "Giant";
            return "Vanilla";
        }

        private void PlayerOnWarped(object sender, WarpedEventArgs e)
        {
            RemoveCustomlights(e.OldLocation);

            foreach (var b in e.NewLocation.buildings)
            {
                if (b.buildingType.Value == UltimateBarn)
                {
                    var ultimateLightBL = new Point(b.tileX.Value + 3, b.tileY.Value + 3);
                    var ll = new LightSource($"{UltimateCP}BarnLight_{b.tileX.Value}_{b.tileY.Value}_L", 4, ultimateLightBL.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(ll.Id, ll);

                    var ultimateLightBR = new Point(b.tileX.Value + 8, b.tileY.Value + 3);
                    var lr = new LightSource($"{UltimateCP}BarnLight_{b.tileX.Value}_{b.tileY.Value}_R", 4, ultimateLightBR.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(lr.Id, lr);
                }

                if (b.buildingType.Value == UltimateCoop)
                {
                    var ultimateLightC = new Point(b.tileX.Value + 6, b.tileY.Value + 2);
                    var lc = new LightSource($"{UltimateCP}CoopLight_{b.tileX.Value}_{b.tileY.Value}", 4, ultimateLightC.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(lc.Id, lc);
                }

                if (b.buildingType.Value == UltimatePremiumCoop)
                {
                    var ultimateLightPCP = new Point(b.tileX.Value + 6, b.tileY.Value + 2);
                    var lp = new LightSource($"{UltimateCP}PremiumCoopLight_patch_{b.tileX.Value}_{b.tileY.Value}", 4, ultimateLightPCP.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(lp.Id, lp);
                }                
            }
        }

        private static void RemoveCustomlights(GameLocation location)
        {
            if (location == null || !Context.IsWorldReady)
                return;

            var toRemove = Game1.currentLightSources.Keys
                .Where(k => k.StartsWith(UltimateCP))
                .ToList();
            foreach (var key in toRemove)
                Game1.currentLightSources.Remove(key);
        }

        private string _vppDir = null;
        private bool? _cachedEnabled = null;
        private FileSystemWatcher _vppConfigWatcher = null;

        private void InitVppWatcher()
        {
            var vppInfo = Helper.ModRegistry.Get("KediDili.VanillaPlusProfessions");
            _vppDir = vppInfo?.GetType()
                .GetProperty("DirectoryPath",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)
                ?.GetValue(vppInfo) as string;

            if (_vppDir == null) return;

            _vppConfigWatcher = new FileSystemWatcher(_vppDir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _vppConfigWatcher.Changed += (s, e) => _cachedEnabled = null;
        }

        private string OvercrowdingVPP()
        {
            if (!Helper.ModRegistry.IsLoaded("KediDili.VanillaPlusProfessions"))
                return "false";                
            
            if (_vppDir == null)
                InitVppWatcher();

            if (_vppDir == null)
                return "false";

            if (_cachedEnabled == null)
            {
                try
                {
                    string vppConfigPath = Path.Combine(_vppDir, "config.json");
                    if (!File.Exists(vppConfigPath))
                        return "false";
                
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(vppConfigPath));
                    if (!doc.RootElement.TryGetProperty("EnableOvercrowdingEdits", out var prop))
                        return "false";

                    _cachedEnabled = prop.ValueKind == System.Text.Json.JsonValueKind.True
                        || (prop.ValueKind == System.Text.Json.JsonValueKind.String
                            && prop.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);
                }
                catch
                {
                return "false";
                }
            }

            return _cachedEnabled == true ? "true" : "false";
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.buildingType.Value is not (UltimateBarn or UltimateCoop or SuperDenseBarn or SuperDenseCoop or UltimatePremiumBarn or UltimatePremiumCoop)) continue;
                if (building.daysUntilUpgrade.Value > 0) continue;

                GameLocation interior = building.GetIndoors();
                if (interior == null) continue;

                string upgradeKey = $"{ModManifest.UniqueID}/buildingKey";
                string currentLevel = building.buildingType.Value;

                building.modData.TryGetValue(upgradeKey, out string lastMovedLevel);
                if (lastMovedLevel == currentLevel) continue;

                if (building.buildingType.Value is UltimateBarn or SuperDenseBarn or UltimatePremiumBarn)
                    BarnItemMoves(interior);
                else if (building.buildingType.Value is UltimateCoop or SuperDenseCoop or UltimatePremiumCoop)
                    CoopItemMoves(interior);

                building.modData[upgradeKey] = currentLevel;
            }
        }

        private static List<(Vector2 tile, StardewValley.Object obj)> SpiralSearch(GameLocation location, string qualifiedId, Vector2 center, int maxRadius)
        {
            var results = new List<(Vector2, StardewValley.Object)>();

            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue;
                        
                        Vector2 tile = new Vector2(center.X + dx, center.Y + dy);
                        if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && obj.QualifiedItemId == qualifiedId)
                        {
                            results.Add((tile, obj));
                        }
                    }
                }
            }

            return results;
        }

        private static Vector2 LandingPadRect(GameLocation location, Microsoft.Xna.Framework.Rectangle landingPad)
        {
            for (int y = landingPad.Top; y < landingPad.Bottom; y++)
            {
                for (int x = landingPad.Left; x < landingPad.Right; x++)
                {
                    Vector2 candidate = new Vector2(x, y);
                    if (!location.IsTileBlockedBy(candidate, CollisionMask.Objects | CollisionMask.Furniture))
                    {
                        return candidate;
                    }
                }
            }
            return Vector2.Zero;
        }

        private static void BarnItemMoves(GameLocation interior)
        {
            if (interior.map == null) return;
            
            string[] excludedIds = { "(BC)99", "(O)178" };

            var barnItemMoves = interior.objects.Pairs
                .Where(p => !excludedIds.Contains(p.Value.QualifiedItemId))
                .ToList();
                
            var landingPad = new Microsoft.Xna.Framework.Rectangle(x: 21, y: 21, width: 21, height: 24);

            foreach (var pair in barnItemMoves)
            {
                Vector2 dest = LandingPadRect(interior, landingPad);
                if (dest == Vector2.Zero) continue;
                interior.removeObject(pair.Key, false);
                pair.Value.TileLocation = dest;
                interior.objects[dest] = pair.Value;
            }
        }
            
        private static void CoopItemMoves(GameLocation interior)
        {
            if (interior.map == null) return;

            string[] excludedIds = { "(BC)99", "(O)178" };
            
            Vector2[] incubatorDestinations =
            {
                new Vector2(2, 14),
                new Vector2(2, 22),
                new Vector2(2, 30),
                new Vector2(2, 38)
            };
                    
            Vector2 startCenter = new Vector2(
                interior.map.Layers[0].LayerWidth / 2,
                interior.map.Layers[0].LayerHeight / 2
            );

            var foundIncubators = SpiralSearch(interior, "(BC)101", startCenter, maxRadius: 50);
                    
            for (int i = 0; i < foundIncubators.Count && i < incubatorDestinations.Length; i++)
            {
                var (sourceTile, obj) = foundIncubators[i];
                Vector2 dest = incubatorDestinations[i];
                interior.removeObject(sourceTile, false);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;
            }

            string[] coopExcluded = excludedIds.Append("(BC)101").ToArray();
            var coopItemMoves = interior.objects.Pairs
                .Where(p => !coopExcluded.Contains(p.Value.QualifiedItemId))
                .ToList();

            var landingPad = new Microsoft.Xna.Framework.Rectangle(x: 20, y: 7, width: 16, height: 36);

            foreach (var pair in coopItemMoves)
            {
                        
                Vector2 dest = LandingPadRect(interior, landingPad);
                if (dest == Vector2.Zero) continue;
                interior.removeObject(pair.Key, false);
                pair.Value.TileLocation = dest;
                interior.objects[dest] = pair.Value;
            } 
        }  

        [HarmonyPatch(typeof(Building), nameof(Building.GetData))]
        public static class UltimateSignPatch
        {
            public static void Postfix(Building __instance, ref BuildingData __result)
            {
                if (__result == null)
                    return;

                if (modInstance.Helper.ModRegistry.IsLoaded("bobkalonger.BFS_util"))
                    return;
                
                if (__instance.upgradeName.Value is not (UltimateBarn or UltimateCoop or SuperDenseBarn or SuperDenseCoop))
                    return;

                if (__instance.upgradeName.Value == UltimateBarn)
                {
                    __result.UpgradeSignTile = new Vector2(3.5f, 4f);
                    __result.UpgradeSignHeight = 60f;
                }

                if (__instance.upgradeName.Value == SuperDenseBarn)
                {
                    __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                    __result.UpgradeSignHeight = 50f;
                }
                
                if (__instance.upgradeName.Value == UltimateCoop)
                {
                    __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                    __result.UpgradeSignHeight = 28f;
                }

                if (__instance.upgradeName.Value == SuperDenseCoop)
                {
                    __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                    __result.UpgradeSignHeight = 52f;
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.doesTileHaveProperty))]
        public static class UltimateCursorPatch
        {
            public static void Postfix(Building __instance, int tile_x, int tile_y, string property_name, ref string property_value, ref bool __result)
            {
                if (__instance.buildingType.Value == UltimateBarn && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tile_x == __instance.tileX.Value + __instance.humanDoor.X + 8 &&
                        tile_y == __instance.tileY.Value + __instance.humanDoor.Y &&
                        interior != null)
                    {
                        if (property_name == "Action")
                        {
                            property_value = "meow";
                            __result = true;
                        }
                    }
                }
                if (__instance.buildingType.Value == UltimateCoop && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tile_x == __instance.tileX.Value + __instance.humanDoor.X - 2 &&
                        tile_y == __instance.tileY.Value + __instance.humanDoor.Y - 2 &&
                        interior != null)
                    {
                        if (property_name == "Action")
                        {
                            property_value = "meow";
                            __result = true;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.doAction))]
        public static class UltimateDoorPatch
        {
            public static void Postfix(Building __instance, Vector2 tileLocation, Farmer who, ref bool __result)
            {
                if (who.ActiveObject != null && who.ActiveObject.IsFloorPathItem() && who.currentLocation != null && !who.currentLocation.terrainFeatures.ContainsKey(tileLocation))
                {
                    return;
                }

                if (__instance.buildingType.Value == UltimateBarn && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tileLocation.X == __instance.tileX.Value + __instance.humanDoor.X + 8 &&
                        tileLocation.Y == __instance.tileY.Value + __instance.humanDoor.Y &&
                        interior != null)
                    {
                        if (who.mount != null)
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:DismountBeforeEntering"));
                            __result = false;
                            return;
                        }
                        if (who.team.demolishLock.IsLocked())
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:CantEnter"));
                            __result = false;
                            return;
                        }
                        if (__instance.OnUseHumanDoor(who))
                        {
                            who.currentLocation.playSound("doorClose", tileLocation);
                            bool isStructure = __instance.indoors.Value != null;
                            Game1.warpFarmer(interior.NameOrUniqueName, interior.warps[1].X, interior.warps[1].Y - 1, Game1.player.FacingDirection, isStructure);
                        }

                        __result = true;
                        return;
                    }
                }
                if (__instance.buildingType.Value == UltimateCoop && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tileLocation.X == __instance.tileX.Value + __instance.humanDoor.X - 2 &&
                        tileLocation.Y == __instance.tileY.Value + __instance.humanDoor.Y - 2 &&
                        interior != null)
                    {
                        if (who.mount != null)
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:DismountBeforeEntering"));
                            __result = false;
                            return;
                        }
                        if (who.team.demolishLock.IsLocked())
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:CantEnter"));
                            __result = false;
                            return;
                        }
                        if (__instance.OnUseHumanDoor(who))
                        {
                            who.currentLocation.playSound("doorClose", tileLocation);
                            bool isStructure = __instance.indoors.Value != null;
                            Game1.warpFarmer(interior.NameOrUniqueName, interior.warps[1].X - 1, interior.warps[1].Y, Game1.player.FacingDirection, isStructure);
                        }

                        __result = true;
                        return;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.updateInteriorWarps))]
        public static class UltimateBarnWarpPatch
        {
            public static void Postfix(Building __instance, GameLocation interior)
            {
                if (__instance.buildingType.Value != UltimateBarn)
                    return;
                if (interior == null || interior.warps.Count == 0)
                    return;

                var w = interior.warps[1];
                interior.warps[1] = new(w.X, w.Y, w.TargetName, w.TargetX + 8, w.TargetY, w.flipFarmer.Value, w.npcOnly.Value);
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.updateInteriorWarps))]
        public static class UltimateCoopWarpPatch
        {
            public static void Postfix(Building __instance, GameLocation interior)
            {
                if (__instance.buildingType.Value != UltimateCoop)
                    return;
                if (interior == null || interior.warps.Count == 0)
                    return;

                var w = interior.warps[1];
                interior.warps[1] = new(w.X, w.Y, w.TargetName, w.TargetX - 1, w.TargetY - 3, w.flipFarmer.Value, w.npcOnly.Value);
            }
        }

        [HarmonyPatch(typeof(Utility), "_HasBuildingOrUpgrade")]
        public static class UtilityHasCoopBarnPatch
        {
            public static void Postfix(GameLocation location, string buildingId, ref bool __result)
            {
                string toCheck = null;
                if (buildingId == "Coop" || buildingId == "Big Coop" || buildingId == "Deluxe Coop" || buildingId == UltimatePremiumCoop)
                {
                    toCheck = UltimateCoop;
                }
                else if (buildingId == "Barn" || buildingId == "Big Barn" || buildingId == "Deluxe Barn" || buildingId == UltimatePremiumBarn)
                {
                    toCheck = UltimateBarn;
                }

                if (!__result && toCheck != null)
                {
                    if (location.getNumberBuildingsConstructed(toCheck) > 0)
                    {
                        __result = true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.InitializeIndoor))]
        public static class BuildingAutoGrabberFix
        {
            public static void Postfix(Building __instance, bool forUpgrade)
            {
                if (!forUpgrade)
                    return;
                if (__instance.buildingType.Value != UltimateCoop &&
                    __instance.buildingType.Value != UltimateBarn)
                    return;

                foreach (var obj in __instance.indoors.Value.objects.Values)
                {
                    if (obj.QualifiedItemId == "(BC)165" && obj.heldObject.Value == null)
                    {
                        obj.heldObject.Value = new Chest();
                    }
                }
            }
        }
    }
}
