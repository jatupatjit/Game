namespace CoopGame.Network
{
    /// <summary>
    /// Lobby windows that take the mouse (K and C opened on top of each other).
    /// Only one may be open: every panel calls <see cref="CloseOthers"/> right before it opens.
    /// </summary>
    public static class UIPanels
    {
        public enum Panel { None, Outfit, Gear, Wardrobe, Map }

        /// <summary>Outfit (hat) shop or gear shop is open.</summary>
        public static bool ShopOpen => Cosmetics.IsOpen || GearShop.IsOpen;

        public static bool AnyOpen => ShopOpen || LobbyCharacterCustomizationUI.IsVisible || LevelMap.IsOpen;

        private static int _closedFrame = -1;

        /// <summary>Esc that closes a panel must not also open Pause in the same frame.</summary>
        public static bool BlocksPause => AnyOpen || _closedFrame == UnityEngine.Time.frameCount;

        public static void MarkClosed() => _closedFrame = UnityEngine.Time.frameCount;

        public static void CloseOthers(Panel keep)
        {
            if (keep != Panel.Outfit && Cosmetics.IsOpen) Cosmetics.SetOpen(false);
            if (keep != Panel.Gear && GearShop.IsOpen) GearShop.SetOpen(false);
            if (keep != Panel.Wardrobe && LobbyCharacterCustomizationUI.IsVisible) LobbyCharacterCustomizationUI.CancelOpen();
            if (keep != Panel.Map && LevelMap.IsOpen) LevelMap.SetOpen(false);
        }
    }
}
