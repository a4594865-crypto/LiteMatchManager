private void HandleHudTick()
    {
        // 如果沒有要顯示，直接 return，不佔用任何運算資源
        if (!_isShowingHud) return;

        float currentTime = Server.CurrentTime;
        
        // 【階段一：時間到，完全停止發送 (完美照抄 ServerGraphic 邏輯)】
        if (currentTime >= _hudEndTime)
        {
            _isShowingHud = false; // 停止 HUD 邏輯
            _hudTargetPlayers.Clear(); // 顯示結束後，清空名單釋放伺服器資源
            
            // 💡 關鍵修正：這裡什麼都不做！不再發送 "" 也不發送隱形區塊。
            // 只要我們停止發送，CS2 就不會再生出那個惱人的遮擋框。
            return;
        }
        
        // 【階段二：倒數中，只有「秒數改變」才對名單發送】
        int remaining = (int)Math.Ceiling(_hudEndTime - currentTime);
        
        // 只有當數字跳動時 (例如 15 變 14)，才組裝字串並發送
        if (remaining != _lastRemainingSeconds)
        {
            _lastRemainingSeconds = remaining;
            string countdownLine = string.Format(Config.HudHtml_Countdown, remaining);
            string currentRenderedHud = _cachedHudBaseHtml + countdownLine;
            
            foreach (var p in _hudTargetPlayers)
            {
                // 僅保留最後一道安全防線：防範這幾秒內剛好有人斷線
                if (p != null && p.IsValid) 
                {
                    p.PrintToCenterHtml(currentRenderedHud);
                }
            }
        }
    }
