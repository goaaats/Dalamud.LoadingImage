using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dalamud.Game.Command;
using Dalamud.Game.Internal;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.RichPresence.Config;
using EasyHook;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using ImGuiScene;
using Lumina.Excel.GeneratedSheets;

namespace Dalamud.CharacterSync
{
    // ReSharper disable once UnusedType.Global
    public unsafe class CharacterSyncPlugin : IDalamudPlugin
    {
        private DalamudPluginInterface _pi;

        private delegate int PrintIconPathDelegate(IntPtr pathPtr, int iconId, int hq, int lang);

        private Hook<PrintIconPathDelegate> printIconHook;

        private delegate IntPtr SetPreloadTerritoryDelegate(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, int a5, int a6);

        private Hook<SetPreloadTerritoryDelegate> preloadHook;

        private TerritoryType[] terris;
        private LoadingImage[] loadings;

        private bool hasLoading = false;

        private int height = 1080;
        private int width = 1920;
        private float scaleX = 0.620f;
        private float scaleY = 0.620f;
        private float X = -110f;
        private float Y = -220f;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            _pi = pluginInterface;

            this.terris = pluginInterface.Data.GetExcelSheet<TerritoryType>().ToArray();
            this.loadings = pluginInterface.Data.GetExcelSheet<LoadingImage>().ToArray();

            this.printIconHook = new Hook<PrintIconPathDelegate>(
                pluginInterface.TargetModuleScanner.ScanText("40 53 48 83 EC 40 41 83 F8 01"),
                new PrintIconPathDelegate(this.PrintIconPathDetour));

            this.preloadHook = new Hook<SetPreloadTerritoryDelegate>(
                pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 41 80 7E ?? ?? 75 16"),
                new SetPreloadTerritoryDelegate(this.SetPreloadTerritoryDetour));

            this.preloadHook.Enable();
            this.printIconHook.Enable();

            #if DEBUG
            this._pi.UiBuilder.OnBuildUi += UiBuilderOnOnBuildUi;
            #endif

            this._pi.Framework.OnUpdateEvent += FrameworkOnOnUpdateEvent;
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

                ImGui.End();
            }
        }

        private IntPtr SetPreloadTerritoryDetour(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, int a5, int a6)
        {
            this.toLoadingTerri = a6;
            return this.preloadHook.Original(a1, a2, a3, a4, a5, a6);
        }

        private void FrameworkOnOnUpdateEvent(Framework framework)
        {
            if (this.hasLoading != true)
                return;

            var unitBase = (AtkUnitBase*) this._pi.Framework.Gui.GetUiObjectByName("_LocationTitle", 1);

            if (unitBase != null)
            {
                var loadingImage = unitBase->UldManager.NodeList[4];
                var imgNode = (AtkImageNode*) loadingImage;

                if (loadingImage == null)
                    return;

                var asset = imgNode->PartsList->Parts[imgNode->PartId].UldAsset;

                if (loadingImage->Type == NodeType.Image && imgNode != null && asset != null)
                {
                    var texName = Marshal.PtrToStringAnsi(new IntPtr(asset->AtkTexture.Resource->TexFileResourceHandle->ResourceHandle.FileName));

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

        public string Name => "Character Sync";

        private int toLoadingTerri = -1;

        private int PrintIconPathDetour(IntPtr pathPtr, int iconId, int hq, int lang)
        {
            var r = this.printIconHook.Original(pathPtr, iconId, hq, lang);

            var terriRegion = this.terris.FirstOrDefault(x => x.PlaceNameRegionIcon == iconId);

            if (terriRegion != null)
            {
                try
                {
                    var terriZone = this.terris.FirstOrDefault(x => x.RowId == this.toLoadingTerri);

                    if (terriZone == null)
                        return r;

                    var loading = this.loadings.FirstOrDefault(x => x.RowId == terriZone.LoadingImage);

                    if (loading == null)
                        return r;

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

        public void Dispose()
        {
            printIconHook.Dispose();
            this.preloadHook.Dispose();
            this._pi.Framework.OnUpdateEvent -= FrameworkOnOnUpdateEvent;

            #if DEBUG
            this._pi.UiBuilder.OnBuildUi -= UiBuilderOnOnBuildUi;
            #endif

            _pi.Dispose();
        }
    }
}
