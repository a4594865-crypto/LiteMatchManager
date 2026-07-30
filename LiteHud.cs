using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;
using System.Collections.Generic;

namespace LiteMatchManager;

public partial class LiteMatchManager
{
    private bool _isShowingHud = false;
    private float _hudEndTime = 0f;
    private string _cachedHudBaseHtml = ""; 
    private string _currentRenderedHud = ""; 
    private int _lastRemainingSeconds = -1;
    
    // 專屬的 HUD 推播名單快取
    private List<CCSPlayerController> _hudTargetPlayers = new List<CCSPlayerController>();

    private void ShowHudWithCountdown(string baseHtml, int durationSeconds)
    {
        _cachedHudBaseHtml = baseHtml;
        _hudEndTime = Server.CurrentTime + durationSeconds;
        _isShowingHud = true;
        _lastRemainingSeconds = -1; 

        _hudTargetPlayers.Clear();
        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && !p.IsBot)
            {
                _hudTargetPlayers.Add(p);
            }
        }
    }

    private void HandleHudTick()
    {
        if (_isShowingHud)
        {
            float currentTime = Server.CurrentTime;
            
            if (currentTime >= _hudEndTime)
            {
                _isShowingHud = false; 
                
                // 【前端黑魔法：CSS 完美隱身】
                // 把外框強制縮小為 0，並加上 overflow: hidden 與 opacity: 0
                // 這樣客戶端的引擎即使還在跑那 5 秒的衰減週期，衰減的也是一個「看不見」的 0x0 物件
                string clearHtml = "<div style='width: 0px; height: 0px; overflow: hidden; opacity: 0;'></div>";

                foreach (var p in _hudTargetPlayers)
                {
                    if (p != null && p.IsValid) 
                    {
                        p.PrintToCenterHtml(clearHtml);
                    }
                }
                
                _hudTargetPlayers.Clear(); 
            }
            else
            {
                int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
                
                // 【效能優化與防延遲：跳過重複渲染】
                // 只有秒數跳動時才發送 (一秒發送一次)，拒絕 Panorama 事件堆積
                // 這是確保倒數到 0 時，能立刻執行上方清除動作的關鍵
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
}
