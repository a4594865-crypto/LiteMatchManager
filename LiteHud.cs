using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;

namespace LiteMatchManager;

public partial class LiteMatchManager
{
    // ==========================================
    // 完美移植 cs2menus 的渲染狀態追蹤變數[cite: 4]
    // ==========================================
    private bool _isShowingHud = false;
    private float _expireTime = 0.0f;          // 對應 PlayerMenu.expireTime (絕對超時時間)[cite: 4]
    private float _nextHtmlRender = 0.0f;      // 對應 PlayerMenu.nextHtmlRender[cite: 4]
    private string _lastHtml = "";             // 對應 PlayerMenu.lastHtml (最後送出的內容)[cite: 4]
    private float _lastHtmlSend = 0.0f;        // 對應 PlayerMenu.lastHtmlSend[cite: 4]
    
    // ==========================================
    // 移植 config_2.h 的 HTML 衰退與重發常數[cite: 3]
    // ==========================================
    private const float HtmlRefreshInterval = 1.0f; // 刷新間隔[cite: 3]
    private const float HtmlKeepAlive = 2.0f;       // 面板存活極限[cite: 3]

    private string _cachedHudBaseHtml = "";
    private CCSGameRulesProxy? _gameRulesProxy;

    public void StartHudCountdown(string baseHtml, float durationSecs)
    {
        float curTime = Server.CurrentTime;
        _cachedHudBaseHtml = baseHtml;
        
        // 依照 C++ 邏輯：設定絕對過期時間[cite: 4]
        _expireTime = curTime + durationSecs; 
        _isShowingHud = true;
        
        // 強制立刻觸發第一次渲染，並清空歷史狀態
        _nextHtmlRender = curTime; 
        _lastHtml = "";
        _lastHtmlSend = 0.0f;
    }

    public void OnTickHUD()
    {
        if (!_isShowingHud) return;

        float curtime = Server.CurrentTime;
        var proxy = GetGameRulesProxy();
        var gameRules = proxy?.GameRules;

        // 1. 檢查是否超時 (Expire timed-out menus)[cite: 4]
        if (curtime >= _expireTime)
        {
            _isShowingHud = false;
            
            // 時間到，強制發送空白消除引擎殘留黑框
            foreach (var player in Utilities.GetPlayers())
            {
                if (IsPlayerValid(player)) player.PrintToCenter(" "); 
            }

            // 關閉 htmlFixFlashing，恢復遊戲正常狀態[cite: 3]
            if (gameRules != null)
            {
                gameRules.GameRestart = gameRules.RestartRoundTime < curtime;
                Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
            }
            return;
        }

        // 2. 判斷是否需要重新發送 HTML (Center-panel resend cadence)[cite: 3]
        // 條件：達到刷新間隔 (1秒) OR 即將超過存活極限 (2秒)[cite: 3]
        if (curtime >= _nextHtmlRender || (curtime - _lastHtmlSend) >= HtmlKeepAlive)
        {
            // 動態計算剩餘秒數
            int remaining = (int)Math.Ceiling(_expireTime - curtime);
            if (remaining < 1) remaining = 1;

            string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
            string currentHtml = _cachedHudBaseHtml + countdownLine;

            // Identical refreshes can be skipped: 
            // 只有在「文字改變」或「為了維持 KeepAlive 避免閃爍」時才發送給客戶端[cite: 3, 4]
            if (currentHtml != _lastHtml || (curtime - _lastHtmlSend) >= HtmlKeepAlive)
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (IsPlayerValid(player))
                    {
                        player.PrintToCenterHtml(currentHtml);
                    }
                }
                
                _lastHtml = currentHtml;           // 紀錄最後發送內容[cite: 4]
                _lastHtmlSend = curtime;           // 紀錄發送時間[cite: 4]
            }

            // 推進下一次的排程刷新時間[cite: 4]
            _nextHtmlRender = curtime + HtmlRefreshInterval;
        }

        // 3. Workaround for the center-HTML panel flashing (htmlFixFlashing)[cite: 3]
        // 透過偽裝 m_bGameRestart 來凍結 HUD 的 5 秒自動淡出[cite: 3]
        if (gameRules != null && !gameRules.GameRestart)
        {
            gameRules.GameRestart = true;
            Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
        }
    }

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
}
