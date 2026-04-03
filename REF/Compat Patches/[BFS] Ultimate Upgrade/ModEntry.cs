using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Buildings;
using Microsoft.Xna.Framework;

namespace UltimateUpgrade
{
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue,
            Func<string> name, Func<string> tooltip = null,
            string[] allowedValues = null, Func<string, string> formatAllowedValue = null,
            string fieldId = null);
    }


    public class ModConfig
    {
        public string UltimateBarnUpgrade { get; set; } = "Deluxe";
        public string UltimateCoopUpgrade { get; set; } = "Deluxe";
    }

    public class ModEntry : Mod
    {
        private ModConfig _config;

        public override void Entry(IModHelper helper)
        {
            _config = helper.ReadConfig<ModConfig>();
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Content.AssetRequested += OnAssetRequested;
        }
    
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null) return;

            gmcm.Register(
                mod: ModManifest,
                reset: () => _config = new ModConfig(),
                save: () =>
                {
                    Helper.WriteConfig(_config);
                    Helper.GameContent.InvalidateCache("Data/Buildings");
                }
            );
            
            gmcm.AddSectionTitle(ModManifest, () => "Upgrade Path");

            gmcm.AddTextOption(
                mod: ModManifest,
                getValue: () => _config.UltimateBarnUpgrade,
                setValue: val => _config.UltimateBarnUpgrade = val,
                name: () => "Ultimate Coop Upgrade",
                tooltip: () => "Choose which coop to upgrade.",
                allowedValues: new[] { "Deluxe", "Mega", "Giga" }
            );

            gmcm.AddTextOption(
                mod: ModManifest,
                getValue: () => _config.UltimateCoopUpgrade,
                setValue: val => _config.UltimateCoopUpgrade = val,
                name: () => "Ultimate Coop Upgrade",
                tooltip: () => "Choose which coop to upgrade.",
                allowedValues: new[] { "Deluxe", "Mega", "Giant", "Giga" }
            );
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/Buildings")) return;

            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, BuildingData>().Data;

                if (data.TryGetValue("bobkalonger.ultimatecoopnbarnCP_UltimateBarn", out var barnData))
                {
                    barnData.BuildingToUpgrade = _config.UltimateBarnUpgrade switch
                    {
                        "Mega" => "jenf1.megacoopbarn_MegaBarn",
                        "Giga" => "bobkalonger.gigacoopnbarn_GigaBarn",
                        _ => "Deluxe Barn"
                    };
                    barnData.UpgradeSignTile = new Vector2(5.5f, 4f);
                    barnData.UpgradeSignHeight = 50f;
                    barnData.CustomFields ??=new();
                    barnData.CustomFields["bobkalonger.BFS_util/ForceMove"] = "true";
                }

                if (data.TryGetValue("bobkalonger.ultimatecoopnbarnCP_UltimateCoop", out var coopData))
                {
                    coopData.BuildingToUpgrade = _config.UltimateCoopUpgrade switch
                    {
                        "Mega" => "jenf1.megacoopbarn_MegaCoop",
                        "Giant" => "UncleArya.ResourceChickens.GiantCoop",
                        "Giga" => "bobkalonger.gigacoopnbarn_GigaCoop",
                        _ => "Deluxe Coop"
                    };
                    coopData.UpgradeSignTile = new Vector2(4.5f, 4f);
                    coopData.UpgradeSignHeight = 52f;
                    coopData.CustomFields ??= new();
                    coopData.CustomFields["bobkalonger.BFS_util/ForceMove"] = "true";
                }
            }, AssetEditPriority.Late);
        }
    }
}