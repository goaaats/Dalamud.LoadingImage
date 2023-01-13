using System;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace Dalamud.LoadingImage
{
    // ReSharper disable once UnusedType.Global
    public unsafe class LoadingImagePlugin : IDalamudPlugin
    {
        private DalamudPluginInterface _pi;
        private Framework _framework;
        private GameGui _gameGui;

        private delegate int PrintIconPathDelegate(IntPtr pathPtr, int iconId, int hq, int lang);

        private Hook<PrintIconPathDelegate> printIconHook;

        private delegate byte HandleTerriChangeDelegate(IntPtr a1, uint a2, byte a3, byte a4, IntPtr a5);

        private Hook<HandleTerriChangeDelegate> handleTerriChangeHook;

        private TerritoryType[] terris;
        private LoadingImage[] loadings;
        private ContentFinderCondition[] cfcs;

        private bool hasLoading = false;

        private int height = 1080;
        private int width = 1920;
        private float scaleX = 0.595f;
        private float scaleY = 0.595f;
        private float X = -60f;
        private float Y = -220f;

        public LoadingImagePlugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] DataManager dataManager,
            [RequiredVersion("1.0")] GameGui gameGui,
            [RequiredVersion("1.0")] SigScanner sigScanner,
            [RequiredVersion("1.0")] Framework framework
            
        )
        {
            _pi = pluginInterface;
            _gameGui = gameGui;
            _framework = framework;

            this.terris = dataManager.GetExcelSheet<TerritoryType>().ToArray();
            this.loadings = dataManager.GetExcelSheet<LoadingImage>().ToArray();
            this.cfcs = dataManager.GetExcelSheet<ContentFinderCondition>().ToArray();

            this.printIconHook = Hook<PrintIconPathDelegate>.FromAddress(
                sigScanner.ScanText("40 53 48 83 EC 40 41 83 F8 01"),
                this.PrintIconPathDetour);

            this.handleTerriChangeHook = Hook<HandleTerriChangeDelegate>.FromAddress(
                sigScanner.ScanText("40 53 55 56 41 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 4C 8B F1 41 0F B6 F1"),
                this.HandleTerriChangeDetour);

            this.printIconHook.Enable();
            this.handleTerriChangeHook.Enable();

            #if DEBUG
            this._pi.UiBuilder.Draw += UiBuilderOnOnBuildUi;
            #endif

            framework.Update += FrameworkOnOnUpdateEvent;
        }

        private void UiBuilderOnOnBuildUi()
        {
            if (ImGui.Begin("Location test"))
            {
                ImGui.InputInt("W", ref this.width);
                ImGui.InputInt("H", ref this.height);
                ImGui.InputFloat("SX", ref this.scaleX);
                ImGui.InputFloat("SY", ref this.scaleY);
                ImGui.InputFloat("X", ref this.X);
                ImGui.InputFloat("Y", ref this.Y);
                ImGui.Checkbox("hasLoading", ref this.hasLoading);

                ImGui.End();
            }
        }

        private void FrameworkOnOnUpdateEvent(Framework framework)
        {
            if (this.hasLoading != true)
                return;

            var unitBase = (AtkUnitBase*) _gameGui.GetAddonByName("_LocationTitle", 1);
            var unitBaseShort = (AtkUnitBase*) _gameGui.GetAddonByName("_LocationTitleShort", 1);
            
            PluginLog.Log($"unitbase: {(long)unitBase:X} visible: {unitBase->IsVisible}");
            PluginLog.Log($"unishort: {(long)unitBaseShort:X} visible: {unitBaseShort->IsVisible}");

            if (unitBase != null && unitBaseShort != null)
            {
                var loadingImage = unitBase->UldManager.NodeList[4];
                var imgNode = (AtkImageNode*) loadingImage;

                if (loadingImage == null)
                    return;

                var asset = imgNode->PartsList->Parts[imgNode->PartId].UldAsset;

                if (loadingImage->Type == NodeType.Image && imgNode != null && asset != null)
                {
                    var resource = asset->AtkTexture.Resource;
                    if (resource == null)
                        return;

                    var name = resource->TexFileResourceHandle->ResourceHandle.FileName;

                    if (name.BufferPtr == null)
                        return;

                    var texName = name.ToString();

                    if (!texName.Contains("loadingimage"))
                    {
                        var t = unitBase->UldManager.NodeList[4];
                        unitBase->UldManager.NodeList[4] = unitBase->UldManager.NodeList[5];
                        unitBase->UldManager.NodeList[5] = t;

                        t->Flags_2 |= 0x1;

                        loadingImage = unitBase->UldManager.NodeList[4];

                        PluginLog.Information("Swapped!");
                    }
                }

                loadingImage->Width = (ushort) this.width;
                loadingImage->Height = (ushort) this.height;
                loadingImage->ScaleX = this.scaleX;
                loadingImage->ScaleY = this.scaleY;
                loadingImage->X = this.X;
                loadingImage->Y = this.Y;
                loadingImage->Priority = 0;

                loadingImage->Flags_2 |= 0x1;

                this.hasLoading = false;
            }
        }

        public string Name => "Fancy Loading Screens";

        private int toLoadingTerri = -1;

        private int PrintIconPathDetour(IntPtr pathPtr, int iconId, int hq, int lang)
        {
            var r = this.printIconHook.Original(pathPtr, iconId, hq, lang);

            var terriRegion = this.terris.FirstOrDefault(x => x.PlaceNameRegionIcon == iconId);

            if (terriRegion != null)
            {
                PluginLog.Information($"LoadIcon: {iconId} detected for r:{terriRegion.RowId} with toLoadingTerri:{this.toLoadingTerri}");

                try
                {
                    if (this.toLoadingTerri == -1)
                    {
                        PluginLog.Information($"toLoadingImage not set!");
                        this.hasLoading = false;
                        return r;
                    }

                    if (this.cfcs.Any(x => x.ContentLinkType == 1 && x.TerritoryType.Row == this.toLoadingTerri))
                    {
                        PluginLog.Information("Is InstanceContent zone!");
                        this.hasLoading = false;
                        return r;
                    }

                    var terriZone = this.terris.FirstOrDefault(x => x.RowId == this.toLoadingTerri);

                    if (terriZone == null)
                    {
                        PluginLog.Information($"terriZone null!");
                        this.hasLoading = false;
                        return r;
                    }

                    if (terriZone.PlaceNameRegionIcon != terriRegion.PlaceNameRegionIcon)
                    {
                        PluginLog.Information($"Mismatch: {terriZone.RowId} {terriRegion.RowId}");
                        this.hasLoading = false;
                        return r;
                    }

                    var loading = this.loadings.FirstOrDefault(x => x.RowId == terriZone.LoadingImage);

                    if (loading == null)
                    {
                        PluginLog.Information($"LoadingImage null!");
                        this.hasLoading = false;
                        return r;
                    }

                    if (!ShouldProcess())
                    {
                        PluginLog.Log("Process check failed!");
                        this.hasLoading = false;
                        return r;
                    }

                    SafeMemory.WriteString(pathPtr, $"ui/loadingimage/{loading.Name}_hr1.tex");
                    PluginLog.Information($"Replacing icon for territory {terriRegion.RowId}");

                    this.hasLoading = true;
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Could not replace loading image.");
                }
            }

            return r;
        }

        private byte HandleTerriChangeDetour(IntPtr a1, uint a2, byte a3, byte a4, IntPtr a5)
        {
            this.toLoadingTerri = (int) a2;
            PluginLog.Information($"toLoadingTerri: {this.toLoadingTerri}");
            return this.handleTerriChangeHook.Original(a1, a2, a3, a4, a5);
        }

        private bool ShouldProcess()
        {
            var t = (AtkUnitBase*) _gameGui.GetAddonByName("_LocationTitle", 1);
            var ts = (AtkUnitBase*) _gameGui.GetAddonByName("_LocationTitleShort", 1);
            
            #if DEBUG
            PluginLog.Log($"unitbase: {(long)t:X} visible: {t->IsVisible}");
            PluginLog.Log($"unishort: {(long)ts:X} visible: {ts->IsVisible}");
            PluginLog.Log($"t != null: {t != null}");
            PluginLog.Log($"ts != null: {ts != null}");
            PluginLog.Log($"t->IsVisible: {t->IsVisible}");
            PluginLog.Log($"!ts->IsVisible: {!ts->IsVisible}");
            #endif

            return t != null && ts != null && t->IsVisible && !ts->IsVisible;
        }

        public void Dispose()
        {
            this.printIconHook.Dispose();
            this.handleTerriChangeHook.Dispose();
            _framework.Update -= FrameworkOnOnUpdateEvent;

            #if DEBUG
            this._pi.UiBuilder.Draw -= UiBuilderOnOnBuildUi;
            #endif
        }
    }
}
