using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;
using System.Collections.Generic;

namespace LiteMatchManager; // 宣告命名空間

public partial class LiteMatchManager
{
    // === 動態 HUD 秒數的控制變數 ===
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private string _currentRenderedHud = ""; 
    private int _lastRemainingSeconds = -1;
    
    // 【極限優化核心】專屬的 HUD 推播名單快取
    private List<CCSPlayerController> _hudTargetPlayers = new List<CCSPlayerController>();

    // HUD 推播觸發器
    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _lastRemainingSeconds = -1; // 強制重置

        // 推播開始時，一次性抓取有效玩家，避免後續狂刷 Utilities.GetPlayers()
        _hudTargetPlayers.Clear();
        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && !p.IsBot)
            {
                _hudTargetPlayers.Add(p);
            }
        }
    }

    // 專門處理 HUD 的 Tick 邏輯 (會被主檔案的 OnTick 呼叫)
    private void HandleHudTick()
    {
        if (_isShowingHud)
        {
            // 【黑魔法一：防閃爍 (Anti-Flash)】
            // 在 HUD 顯示期間，強制佔用 GameRestart 狀態壓制原生 UI
            if (_gameRules != null)
            {
                _gameRules.GameRestart = true;
            }

            float currentTime = Server.CurrentTime;
            
            if (currentTime >= _hudEndTime)
            {
                _isShowingHud = false; 
                
                // HUD 結束，歸還 GameRestart 狀態
                if (_gameRules != null)
                {
                    _gameRules.GameRestart = false; 
                }
                
                // 【黑魔法二：CSS 坍塌大法 (強制秒殺 15 秒黑框殘影)】
                string clearHtml = "<div style='width: 0px; height: 0px;'></div>";

                foreach (var p in _hudTargetPlayers)
                {
                    if (p != null && p.IsValid) 
                    {
                        p.PrintToCenterHtml(clearHtml);
                        p.PrintToCenter(" "); // 雙重保險
                    }
                }
                
                // 清空名單釋放記憶體
                _hudTargetPlayers.Clear(); 
            }
            else
            {
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                // 【黑魔法三：跳過重複渲染】
                // 只有秒數跳動時才發送，拒絕 Panorama 事件堆積
                if (remaining != _lastRemainingSeconds)
                {
                    _lastRemainingSeconds = remaining;
                    string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
                    _currentRenderedHud = _cachedHudBaseHtml + countdownLine;
                    
                    foreach (var p in _hudTargetPlayers)
                    {
                        if (p != null && p.IsValid) 
                            p.PrintToCenterHtml(_currentRenderedHud);
                    }
                }
            }
        }
    }
} // <--- 剛剛報錯通常就是因為最後漏了這一個 Class 結尾的大括號
