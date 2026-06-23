using System;
using R3;
using UnityEngine;

namespace CubeRacing
{
    public class PlayerSession
    {
        private const string KeyToken    = "player_token";
        private const string KeyNickname = "player_nickname";
        private const string KeyChips    = "player_chips";

        public Guid   PlayerId { get; private set; }
        public Guid   Token    { get; private set; }
        public string Nickname { get; private set; }
        public ReactiveProperty<int> Chips { get; } = new(0);

        public bool   HasSavedSession => PlayerPrefs.HasKey(KeyToken);
        public string SavedNickname   => PlayerPrefs.GetString(KeyNickname, string.Empty);
        public int    SavedChips      => PlayerPrefs.GetInt(KeyChips, 0);

        public void LoadFromPrefs()
        {
            Token    = Guid.Parse(PlayerPrefs.GetString(KeyToken));
            Nickname = PlayerPrefs.GetString(KeyNickname);
            Chips.Value = PlayerPrefs.GetInt(KeyChips, 0);
        }

        public void Initialize(Guid playerId, Guid token, string nickname, int chips)
        {
            PlayerId = playerId;
            Token    = token;
            Nickname = nickname;
            Chips.Value = chips;
            Save();
        }

        public void UpdateChips(int chips)
        {
            Chips.Value = chips;
            PlayerPrefs.SetInt(KeyChips, chips);
            PlayerPrefs.Save();
        }

        private void Save()
        {
            PlayerPrefs.SetString(KeyToken,    Token.ToString());
            PlayerPrefs.SetString(KeyNickname, Nickname);
            PlayerPrefs.SetInt(KeyChips,       Chips.Value);
            PlayerPrefs.Save();
        }
    }
}
