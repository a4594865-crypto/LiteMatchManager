using System;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace LiteHud;

public class LiteHud : BasePlugin
{
    public override string ModuleName => "LiteHud";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Custom";

    // 照抄 ServerGraphic 的全域變數架構
    public bool _isShowingHud = false;
    private bool _runThisTick = false;
    private float _hudEndTime = 0f;
    private CCSGameRulesProxy? _gameRulesProxy;

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[LiteHud] 插件已載入 - 採用 ServerGraphic 核心邏輯");
        RegisterListener<Listeners.OnTick>(OnTickHUD);
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
    }

    private void OnMapStartHandler(string mapName)
    {
        // 換地圖時重置狀態
        _isShowingHud = false;
        _gameRulesProxy = null;
    }

    // 寫一個測試指令方便你在遊戲內直接測試 (控制台輸入 css_testhud 10)
    [ConsoleCommand("css_testhud", "測試 HUD 倒數")]
    [CommandHelper(minArgs: 1, usage: "<秒數>")]
    public void OnTestHudCommand(CCSPlayerController? caller, CommandInfo info)
    {
        if (!float.TryParse(info.GetArg(1), out float duration))
        {
            info.ReplyToCommand("請輸入有效的數字做為秒數。");
            return;
        }

        StartHudCountdown(duration);
        
        if (caller != null)
        {
            caller.PrintToChat($"[LiteHud] 開始 {duration} 秒的 HUD 倒數測試！");
        }
    }

    public void StartHudCountdown(float duration)
    {
        _isShowingHud = true;
        _hudEndTime = Server.CurrentTime + duration;

        // 1. 完全照抄 ServerGraphic 的做法：利用 AddTimer 來控制精準關閉
        AddTimer(duration, () =>
        {
            _isShowingHud = false;
        });
    }

    public void OnTickHUD()
    {
        // 2. 照抄 ServerGraphic 顯示的做法：只要開關是 true，每一個 Tick 就持續發送
        if (_isShowingHud)
        {
            // 計算剩下的秒數
            int remainingSeconds = (int)Math.Ceiling(_hudEndTime - Server.CurrentTime);
            
            // 避免微小延遲導致顯示 0 
            if (remainingSeconds < 1) remainingSeconds = 1;

            string hudText = $"倒數: {remainingSeconds} 秒"; 

            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(player))
                    continue;

                // 發送純文字
                player.PrintToCenterHtml(hudText);
            }
        }

        // 3. 照抄 ServerGraphic OnTick 後半段的強制刷新 UI 黑魔法邏輯
        _runThisTick = !_runThisTick;

        if (!_runThisTick) return;

        var proxy = GetGameRulesProxy();

        if (proxy == null || !proxy.IsValid) return;

        var gameRules = proxy.GameRules;
        if (gameRules == null) return;

        if (gameRules.WarmupPeriod) return;

        float currentTime = Server.CurrentTime;
        float restartTime = gameRules.RestartRoundTime;

        bool expectedState = restartTime < currentTime;

        if (gameRules.GameRestart != expectedState)
        {
            gameRules.GameRestart = expectedState;
            Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
        }
    }

    // 照抄 ServerGraphic 的 Helper 函數，確保 GetGameRulesProxy 能正常運作
    private CCSGameRulesProxy? GetGameRulesProxy()
    {
        if (_gameRulesProxy != null && _gameRulesProxy.IsValid)
        {
            return _gameRulesProxy;
        }

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            _gameRulesProxy = entity;
            return _gameRulesProxy;
        }

        _gameRulesProxy = null;
        return null;
    }

    public static bool IsPlayerValid(CCSPlayerController? player)
    {
        return player != null
            && player.IsValid
            && !player.IsBot
            && player.Pawn != null
            && player.Pawn.IsValid
            && player.Connected == PlayerConnectedState.Connected
            && !player.IsHLTV;
    }
}
