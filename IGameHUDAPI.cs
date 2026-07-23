using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

//VersionAPI: 1.DZ.3

namespace CS2_GameHUDAPI
{
    public interface IGameHUDAPI
    {
        public static PluginCapability<IGameHUDAPI> Capability { get; } = new("gamehud:api");

        void Native_GameHUD_SetParams(CCSPlayerController Player, byte channel, CounterStrikeSharp.API.Modules.Utils.Vector vec, System.Drawing.Color color, int fontsize = 18, string fontname = "Verdana", float units = 0.25f, PointWorldTextJustifyHorizontal_t justifyhorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT, PointWorldTextJustifyVertical_t justifyvertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP, PointWorldTextReorientMode_t reorientmode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE, float bgborderheight = 0.0f, float bgborderwidth = 0.0f);

        void Native_GameHUD_SetParams(CCSPlayerController Player, byte channel, System.Numerics.Vector3 vec3, System.Drawing.Color color, int fontsize = 18, string fontname = "Verdana", float units = 0.25f, PointWorldTextJustifyHorizontal_t justifyhorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT, PointWorldTextJustifyVertical_t justifyvertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP, PointWorldTextReorientMode_t reorientmode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE, float bgborderheight = 0.0f, float bgborderwidth = 0.0f);

        void Native_GameHUD_SetParams(CCSPlayerController Player, byte channel, float X, float Y, float Z, System.Drawing.Color color, int fontsize = 18, string fontname = "Verdana", float units = 0.25f, PointWorldTextJustifyHorizontal_t justifyhorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT, PointWorldTextJustifyVertical_t justifyvertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP, PointWorldTextReorientMode_t reorientmode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE, float bgborderheight = 0.0f, float bgborderwidth = 0.0f);

        void Native_GameHUD_UpdateParams(CCSPlayerController Player, byte channel, CounterStrikeSharp.API.Modules.Utils.Vector vec, System.Drawing.Color color, int fontsize = 18, string fontname = "Verdana", float units = 0.25f, PointWorldTextJustifyHorizontal_t justifyhorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT, PointWorldTextJustifyVertical_t justifyvertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP, PointWorldTextReorientMode_t reorientmode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE, float bgborderheight = 0.0f, float bgborderwidth = 0.0f);

        void Native_GameHUD_UpdateParams(CCSPlayerController Player, byte channel, System.Numerics.Vector3 vec3, System.Drawing.Color color, int fontsize = 18, string fontname = "Verdana", float units = 0.25f, PointWorldTextJustifyHorizontal_t justifyhorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT, PointWorldTextJustifyVertical_t justifyvertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP, PointWorldTextReorientMode_t reorientmode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE, float bgborderheight = 0.0f, float bgborderwidth = 0.0f);

        void Native_GameHUD_UpdateParams(CCSPlayerController Player, byte channel, float X, float Y, float Z, System.Drawing.Color color, int fontsize = 18, string fontname = "Verdana", float units = 0.25f, PointWorldTextJustifyHorizontal_t justifyhorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT, PointWorldTextJustifyVertical_t justifyvertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP, PointWorldTextReorientMode_t reorientmode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE, float bgborderheight = 0.0f, float bgborderwidth = 0.0f);

        void Native_GameHUD_Show(CCSPlayerController Player, byte channel, string message, float time = 1.0f);

        void Native_GameHUD_ShowPermanent(CCSPlayerController Player, byte channel, string message);

        void Native_GameHUD_Remove(CCSPlayerController Player, byte channel);

        void Native_GameHUD_SetOwner(CCSPlayerController Player, byte channel, CCSPlayerPawn owner);

        void Native_GameHUD_SetKeyValue(CCSPlayerController Player, byte channel, string key, string value);

        void Native_GameHUD_SetTarget(CCSPlayerController Player, byte channel, string target);

        string? Native_GameHUD_GetKeyValue(CCSPlayerController Player, byte channel, string key);

        string? Native_GameHUD_GetTarget(CCSPlayerController Player, byte channel);
    }
}