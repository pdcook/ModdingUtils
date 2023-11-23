using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Linq;
using UnboundLib;
using Photon.Pun;
using UnboundLib.Cards;
using System.Reflection;
using HarmonyLib;
using UnboundLib.Networking;
using UnityEngine;
using TMPro;
using ModdingUtils.Extensions;
using UnboundLib.Utils;
using ModdingUtils.Patches;
using ModdingUtils.MonoBehaviours;

namespace ModdingUtils.Utils
{
    public sealed class Cards
    {
        public static List<CardInfo> active
        {
            get
            {
                return ((ObservableCollection<CardInfo>)typeof(CardManager).GetField("activeCards", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).ToList();

            }
            set { }
        }
        public static List<CardInfo> inactive
        {
            get
            {
                return (List<CardInfo>)typeof(CardManager).GetField("inactiveCards", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            }
            set { }
        }
        public static List<CardInfo> all
        {
            get
            {
                return active.Concat(inactive).ToList();
            }
            set { }
        }

        // singleton design, so that the RNG isn't reset each call
        public static readonly Cards instance = new Cards();
        private static readonly System.Random rng = new System.Random();
        private List<CardInfo> hiddenCards = new List<CardInfo>();
        public ReadOnlyCollection<CardInfo> HiddenCards => new ReadOnlyCollection<CardInfo>(hiddenCards);
        private List<Action<Player, CardInfo, int>> removalCallbacks = new List<Action<Player, CardInfo, int>>();
        private List<CardInfo> allCards => ((ObservableCollection<CardInfo>)typeof(CardManager).GetField("activeCards", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).ToList().Concat((List<CardInfo>)typeof(CardManager).GetField("inactiveCards", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Concat(hiddenCards).ToList();

        private List<CardInfo> ACTIVEANDHIDDENCARDS
        {
            get => activeCards.ToList().Concat(hiddenCards).ToList();
            set { }
        }
        private CardInfo[] activeCards => CardChoice.instance.cards;

        private Cards()
        {
            Cards instance = this;
        }

        public void AddOnRemoveCallback(Action<Player, CardInfo, int> callback)
        {
            removalCallbacks.Add(callback);
        }

        public void AddCardToPlayer(Player player, CardInfo card, bool reassign = false, string twoLetterCode = "", float forceDisplay = 0f, float forceDisplayDelay = 0f)
        {
            AddCardToPlayer(player, card, reassign, twoLetterCode, forceDisplay, forceDisplayDelay, true);
        }
        public void AddCardToPlayer(Player player, CardInfo card, bool reassign = false, string twoLetterCode = "", float forceDisplay = 0f, float forceDisplayDelay = 0f, bool addToCardBar = true)
        {
            // adds the card "card" to the player "player"
            if (card == null) { return; }
            else if (PhotonNetwork.OfflineMode)
            {
                // assign card locally
                ApplyCardStats cardStats = card.gameObject.GetComponentInChildren<ApplyCardStats>();

                // call Start to initialize card stat components for base-game cards
                typeof(ApplyCardStats).InvokeMember("Start",
                                    BindingFlags.Instance | BindingFlags.InvokeMethod |
                                    BindingFlags.NonPublic, null, cardStats, new object[] { });
                cardStats.GetComponent<CardInfo>().sourceCard = card;

                Traverse.Create(cardStats).Field("playerToUpgrade").SetValue(player);

                if (reassign && card.GetComponent<CustomCard>() != null)
                {
                    try
                    {

                        Gun gun = player.GetComponent<Holding>().holdable.GetComponent<Gun>();
                        CharacterData characterData = player.GetComponent<CharacterData>();
                        HealthHandler healthHandler = player.GetComponent<HealthHandler>();
                        Gravity gravity = player.GetComponent<Gravity>();
                        Block block = player.GetComponent<Block>();
                        GunAmmo gunAmmo = gun.GetComponentInChildren<GunAmmo>();
                        CharacterStatModifiers characterStatModifiers = player.GetComponent<CharacterStatModifiers>();
                        card.GetComponent<CustomCard>().OnReassignCard(player, gun, gunAmmo, characterData, healthHandler, gravity, block, characterStatModifiers);
                    }
                    catch (NotImplementedException)
                    { }
                    catch (Exception exception)
                    {
                        UnityEngine.Debug.LogError("[ModdingUtils] EXCEPTION: " + exception.GetType().ToString() + "\nThrown by: " + card.GetComponent<CustomCard>().GetModName() + " - " + card.cardName + " - " + "OnReassignCard(Player, Gun, GunAmmo, HealthHandler, Gravity, Block, CharacterStatModifiers)");
                    }
                }

                if (!reassign || card.GetAdditionalData().canBeReassigned)
                {
                    typeof(ApplyCardStats).InvokeMember("ApplyStats",
                                        BindingFlags.Instance | BindingFlags.InvokeMethod |
                                        BindingFlags.NonPublic, null, cardStats, new object[] { });
                }
                else
                {
                    player.data.currentCards.Add(card);
                }
                if (addToCardBar)
                {
                    SilentAddToCardBar(player.playerID, cardStats.GetComponent<CardInfo>().sourceCard, twoLetterCode, forceDisplay, forceDisplayDelay);
                }
            }
            else if (PhotonNetwork.IsMasterClient)
            {
                // assign card with RPC

                if (addToCardBar)
                {
                    NetworkingManager.RPC(typeof(Cards), nameof(RPCA_AssignCard), new object[] { card.name, player.playerID, reassign, twoLetterCode, forceDisplay, forceDisplayDelay });
                }
                else
                {
                    NetworkingManager.RPC(typeof(Cards), nameof(RPCA_AssignCardWithoutCardBar), new object[] { card.name, player.playerID, reassign, twoLetterCode, forceDisplay, forceDisplayDelay });
                }

            }
        }
        public void AddCardsToPlayer(Player player, CardInfo[] cards, bool reassign = false, string[] twoLetterCodes = null, float[] forceDisplays = null, float[] forceDisplayDelays = null)
        {
            AddCardsToPlayer(player, cards, reassign, twoLetterCodes, forceDisplays, forceDisplayDelays, true);
        }
        public void AddCardsToPlayer(Player player, CardInfo[] cards, bool reassign = false, string[] twoLetterCodes = null, float[] forceDisplays = null, float[] forceDisplayDelays = null, bool addToCardBar = true)
        {
            bool[] reassigns = new bool[cards.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                reassigns[i] = reassign;
            }

            AddCardsToPlayer(player, cards, reassigns, twoLetterCodes, forceDisplays, forceDisplayDelays, addToCardBar);
        }
        public void AddCardsToPlayer(Player player, CardInfo[] cards, bool[] reassigns = null, string[] twoLetterCodes = null, float[] forceDisplays = null, float[] forceDisplayDelays = null)
        {
            AddCardsToPlayer(player, cards, reassigns, twoLetterCodes, forceDisplays, forceDisplayDelays, true);
        }
        public void AddCardsToPlayer(Player player, CardInfo[] cards, bool[] reassigns = null, string[] twoLetterCodes = null, float[] forceDisplays = null, float[] forceDisplayDelays = null, bool addToCardBar = true)
        {
            if (reassigns == null)
            {
                reassigns = new bool[cards.Length];
                for (int i = 0; i < reassigns.Length; i++)
                {
                    reassigns[i] = false;
                }
            }
            if (twoLetterCodes == null)
            {
                twoLetterCodes = new string[cards.Length];
                for (int i = 0; i < twoLetterCodes.Length; i++)
                {
                    twoLetterCodes[i] = "";
                }
            }
            if (forceDisplays == null)
            {
                forceDisplays = new float[cards.Length];
                for (int i = 0; i < forceDisplays.Length; i++)
                {
                    forceDisplays[i] = 0f;
                }
            }
            if (forceDisplayDelays == null)
            {
                forceDisplayDelays = new float[cards.Length];
                for (int i = 0; i < forceDisplayDelays.Length; i++)
                {
                    forceDisplayDelays[i] = 0f;
                }
            }

            for (int i = 0; i < cards.Length; i++)
            {
                AddCardToPlayer(player, cards[i], reassigns[i], twoLetterCodes[i], forceDisplays[i], forceDisplayDelays[i], addToCardBar);
            }
        }
        public CardInfo[] RemoveCardsFromPlayer(Player player, int[] indeces)
        {
            return RemoveCardsFromPlayer(player, indeces, true);
        }
        public CardInfo[] RemoveCardsFromPlayer(Player player, int[] indeces, bool editCardBar = true)
        {
            // copy player's currentCards list
            List<CardInfo> originalCards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                originalCards.Add(origCard);
            }

            List<CardInfo> newCards = new List<CardInfo>();

            for (int i = 0; i < originalCards.Count; i++)
            {
                if (!indeces.Contains(i)) { newCards.Add(originalCards[i]); }
            }

            // now we remove all of the cards from the player
            RemoveAllCardsFromPlayer(player);
            Unbound.Instance.ExecuteAfterSeconds(0.1f, () =>
            {
                if (editCardBar)
                {
                    CardBarUtils.instance.ClearCardBar(player);
                }
                // then add back only the ones we didn't remove, marking them as reassignments
                AddCardsToPlayer(player, newCards.ToArray(), true, addToCardBar: editCardBar);
            });

            // run all callbacks
            foreach (Action<Player, CardInfo, int> callback in removalCallbacks)
            {
                foreach (int idx in indeces)
                {
                    try
                    {
                        callback(player, originalCards[idx], idx);
                    }
                    catch
                    { }
                }
            }

            // return the cards that were removed
            return originalCards.Except(newCards).ToArray();
        }
        public int RemoveCardsFromPlayer(Player player, CardInfo[] cards, SelectionType selectType = SelectionType.All)
        {
            return RemoveCardsFromPlayer(player, cards, selectType, true);
        }
        public int RemoveCardsFromPlayer(Player player, CardInfo[] cards, SelectionType selectType = SelectionType.All, bool editCardBar = true)
        {
            // copy player's currentCards list
            List<CardInfo> originalCards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                originalCards.Add(origCard);
            }

            List<int> indecesToRemove = new List<int>();

            foreach (CardInfo card in cards)
            {
                // get list of all indeces that the card appears
                List<int> indeces = Enumerable.Range(0, player.data.currentCards.Count).Where(idx => player.data.currentCards[idx].name == card.name).ToList();

                int start = 0;
                int end = indeces.Count;

                switch (selectType)
                {
                    case SelectionType.All:
                        start = 0;
                        end = indeces.Count;
                        break;
                    case SelectionType.Newest:
                        start = indeces.Count - 1;
                        end = start + 1;
                        break;
                    case SelectionType.Oldest:
                        start = 0;
                        end = start + 1;
                        break;
                    case SelectionType.Random:
                        start = rng.Next(0, indeces.Count);
                        end = start + 1;
                        break;
                }

                for (int i = start; i < end; i++)
                {
                    indecesToRemove.Add(indeces[i]);
                }
            }

            List<CardInfo> newCards = new List<CardInfo>();

            for (int i = 0; i < originalCards.Count; i++)
            {
                if (!indecesToRemove.Contains(i))
                {
                    newCards.Add(originalCards[i]);
                }
            }

            // now we remove all of the cards from the player
            RemoveAllCardsFromPlayer(player, false);
            Unbound.Instance.ExecuteAfterSeconds(0.1f, () =>
            {
                if (editCardBar)
                {
                    CardBarUtils.instance.ClearCardBar(player);
                }
                // then add back only the ones we didn't remove
                AddCardsToPlayer(player, newCards.ToArray(), true, addToCardBar: editCardBar);
            });

            // run all callbacks
            foreach (Action<Player, CardInfo, int> callback in removalCallbacks)
            {
                foreach (int indx in indecesToRemove)
                {
                    try
                    {
                        callback(player, originalCards[indx], indx);
                    }
                    catch
                    { }
                }
            }

            // return the number of cards removed
            return indecesToRemove.Count;
        }
        public CardInfo RemoveCardFromPlayer(Player player, int idx)
        {
            return RemoveCardFromPlayer(player, idx, true);
        }
        public CardInfo RemoveCardFromPlayer(Player player, int idx, bool editCardBar)
        {
            // copy player's currentCards list
            List<CardInfo> originalCards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                originalCards.Add(origCard);
            }

            List<CardInfo> newCards = new List<CardInfo>();

            for (int i = 0; i < originalCards.Count; i++)
            {
                if (i != idx) { newCards.Add(originalCards[i]); }
            }

            // now we remove all of the cards from the player
            RemoveAllCardsFromPlayer(player);
            Unbound.Instance.ExecuteAfterSeconds(0.1f, () =>
            {
                if (editCardBar)
                {
                    CardBarUtils.instance.ClearCardBar(player);
                }
                // then add back only the ones we didn't remove, marking them as reassignments
                AddCardsToPlayer(player, newCards.ToArray(), true, addToCardBar: editCardBar);
            });

            // run all callbacks
            foreach (Action<Player, CardInfo, int> callback in removalCallbacks)
            {
                try
                {
                    callback(player, originalCards[idx], idx);
                }
                catch
                { }
            }

            // return the card that was removed
            return originalCards[idx];
        }
        public int RemoveCardFromPlayer(Player player, CardInfo card, SelectionType selectType = SelectionType.All)
        {
            return RemoveCardFromPlayer(player, card, selectType, true);
        }
        public int RemoveCardFromPlayer(Player player, CardInfo card, SelectionType selectType = SelectionType.All, bool editCardBar = true)
        {
            // copy player's currentCards list
            List<CardInfo> originalCards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                originalCards.Add(origCard);
            }

            // get list of all indeces that the card appears
            List<int> indeces = Enumerable.Range(0, player.data.currentCards.Count).Where(idx => player.data.currentCards[idx].name == card.name).ToList();

            int start = 0;
            int end = indeces.Count;

            switch (selectType)
            {
                case SelectionType.All:
                    start = 0;
                    end = indeces.Count;
                    break;
                case SelectionType.Newest:
                    start = indeces.Count - 1;
                    end = start + 1;
                    break;
                case SelectionType.Oldest:
                    start = 0;
                    end = 1;
                    break;
                case SelectionType.Random:
                    start = rng.Next(0, indeces.Count);
                    end = start + 1;
                    break;
            }

            List<int> indecesToRemove = new List<int>();
            for (int i = start; i < end; i++)
            {
                indecesToRemove.Add(indeces[i]);
            }

            List<CardInfo> newCards = new List<CardInfo>();

            for (int i = 0; i < originalCards.Count; i++)
            {
                if (!indecesToRemove.Contains(i))
                {
                    newCards.Add(originalCards[i]);
                }
            }

            // now we remove all of the cards from the player
            RemoveAllCardsFromPlayer(player, false);
            Unbound.Instance.ExecuteAfterSeconds(0.1f, () =>
            {
                if (editCardBar)
                { CardBarUtils.instance.ClearCardBar(player); }
                // then add back only the ones we didn't remove
                AddCardsToPlayer(player, newCards.ToArray(), true, addToCardBar: editCardBar);
            });

            // run all callbacks
            foreach (Action<Player, CardInfo, int> callback in removalCallbacks)
            {
                foreach(int indx in indecesToRemove)
                {
                    try
                    {
                        callback(player, originalCards[indx], indx);
                    }
                    catch
                    { }
                }
            }

            // return the number of cards removed
            return indecesToRemove.Count;
        }
        public CardInfo[] RemoveAllCardsFromPlayer(Player player, bool clearBar = true)
        {
            List<CardInfo> cards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                cards.Add(origCard);
            }

            if (PhotonNetwork.OfflineMode)
            {
                // remove all the cards from the player by calling the PATCHED FullReset
                typeof(Player).InvokeMember("FullReset",
                        BindingFlags.Instance | BindingFlags.InvokeMethod |
                        BindingFlags.NonPublic, null, player, new object[] { });
                if (clearBar)
                {
                    CardBarUtils.instance.ClearCardBar(player);
                }
            }
            else if (PhotonNetwork.IsMasterClient)
            {
                NetworkingManager.RPC(typeof(Cards), "RPCA_FullReset", new object[] { player.playerID });
                if (clearBar)
                {
                    CardBarUtils.instance.ClearCardBar(player);
                }
            }

            return cards.ToArray(); // return the removed cards
        }
        public System.Collections.IEnumerator ReplaceCard(Player player, int idx, CardInfo newCard, string twoLetterCode = "", float forceDisplay = 0f, float forceDisplayDelay = 0f)
        {
            yield return ReplaceCard(player, idx, newCard, twoLetterCode, forceDisplay, forceDisplayDelay, true);
        }
        public System.Collections.IEnumerator ReplaceCard(Player player, int idx, CardInfo newCard, string twoLetterCode = "", float forceDisplay = 0f, float forceDisplayDelay = 0f, bool editCardBar = true)
        {
            if (newCard == null)
            {
                yield break;
            }
            List<string> twoLetterCodes = new List<string>();
            List<float> forceDisplays = new List<float>();
            List<float> forceDisplayDelays = new List<float>();
            List<bool> reassigns = new List<bool>();

            // copy player's currentCards list
            List<CardInfo> originalCards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                originalCards.Add(origCard);
            }

            List<CardInfo> newCards = new List<CardInfo>();

            for (int i = 0; i < originalCards.Count; i++)
            {
                if (i != idx)
                {
                    newCards.Add(originalCards[i]);
                    twoLetterCodes.Add("");
                    forceDisplays.Add(0f);
                    forceDisplayDelays.Add(0f);
                    reassigns.Add(true);
                }
                else
                {
                    newCards.Add(newCard);
                    twoLetterCodes.Add(twoLetterCode);
                    forceDisplays.Add(forceDisplay);
                    forceDisplayDelays.Add(forceDisplayDelay);
                    reassigns.Add(false);
                }
            }

            // now we remove all of the cards from the player
            RemoveAllCardsFromPlayer(player, editCardBar);

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            if (editCardBar)
            {
                CardBarUtils.instance.ClearCardBar(player);
            }

            yield return null;

            // then add back the new card
            AddCardsToPlayer(player, newCards.ToArray(), reassigns.ToArray(), twoLetterCodes.ToArray(), forceDisplays.ToArray(), forceDisplayDelays.ToArray(), editCardBar);

            yield break;
            // return the card that was removed
            //return originalCards[idx];
        }
        public System.Collections.IEnumerator ReplaceCards(Player player, int[] indeces, CardInfo[] newCards, string[] twoLetterCodes = null)
        {
            yield return ReplaceCards(player, indeces, newCards, twoLetterCodes, true);
        }
        public System.Collections.IEnumerator ReplaceCards(Player player, int[] indeces, CardInfo[] newCards, string[] twoLetterCodes = null, bool editCardBar = true)
        {
            if (twoLetterCodes == null)
            {
                twoLetterCodes = new string[indeces.Length];
                for (int i = 0; i < twoLetterCodes.Length; i++)
                {
                    twoLetterCodes[i] = "";
                }
            }
            // copy player's currentCards list
            List<CardInfo> originalCards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                originalCards.Add(origCard);
            }

            List<CardInfo> newCardsToAssign = new List<CardInfo>();
            List<string> twoLetterCodesToAssign = new List<string>();
            List<bool> reassigns = new List<bool>();

            int j = 0;
            for (int i = 0; i < originalCards.Count; i++)
            {
                if (!indeces.Contains(i))
                {
                    newCardsToAssign.Add(originalCards[i]);
                    twoLetterCodesToAssign.Add("");
                    reassigns.Add(true);
                }
                else if (newCards[j] == null)
                {
                    newCardsToAssign.Add(originalCards[i]);
                    twoLetterCodesToAssign.Add("");
                    reassigns.Add(true);
                    j++;
                }
                else
                {
                    newCardsToAssign.Add(newCards[j]);
                    twoLetterCodesToAssign.Add(twoLetterCodes[j]);
                    reassigns.Add(false);
                    j++;
                }
            }

            // now we remove all of the cards from the player
            RemoveAllCardsFromPlayer(player, editCardBar);

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            if (editCardBar)
            {
                CardBarUtils.instance.ClearCardBar(player);
            }

            yield return null;

            // then add back the new cards
            AddCardsToPlayer(player, newCardsToAssign.ToArray(), reassigns.ToArray(), twoLetterCodesToAssign.ToArray(), addToCardBar: editCardBar);

            yield break;
            // return the card that was removed
            //return originalCards[idx];
        }
        public System.Collections.IEnumerator ReplaceCard(Player player, CardInfo cardToReplace, CardInfo newCard, string twoLetterCode = "", float forceDisplay = 0f, float forceDisplayDelay = 0f, SelectionType selectType = SelectionType.All)
        {
            yield return ReplaceCard(player, cardToReplace, newCard, twoLetterCode, forceDisplay, forceDisplayDelay, selectType, true);
        }
        public System.Collections.IEnumerator ReplaceCard(Player player, CardInfo cardToReplace, CardInfo newCard, string twoLetterCode = "", float forceDisplay = 0f, float forceDisplayDelay = 0f, SelectionType selectType = SelectionType.All, bool editCardBar = true)
        {
            if (newCard == null)
            {
                yield break;
            }
            List<string> twoLetterCodes = new List<string>();
            List<float> forceDisplays = new List<float>();
            List<float> forceDisplayDelays = new List<float>();
            List<bool> reassigns = new List<bool>();

            // copy player's currentCards list
            List<CardInfo> originalCards = new List<CardInfo>();
            foreach (CardInfo origCard in player.data.currentCards)
            {
                originalCards.Add(origCard);
            }

            // get list of all indeces that the card appears
            List<int> indeces = Enumerable.Range(0, player.data.currentCards.Count).Where(idx => player.data.currentCards[idx].name == cardToReplace.name).ToList();

            int start = 0;
            int end = indeces.Count;

            switch (selectType)
            {
                case SelectionType.All:
                    start = 0;
                    end = indeces.Count;
                    break;
                case SelectionType.Newest:
                    start = indeces.Count - 1;
                    end = start + 1;
                    break;
                case SelectionType.Oldest:
                    start = 0;
                    end = 1;
                    break;
                case SelectionType.Random:
                    start = rng.Next(0, indeces.Count);
                    end = start + 1;
                    break;
            }

            List<int> indecesToReplace = new List<int>();
            for (int i = start; i < end; i++)
            {
                indecesToReplace.Add(indeces[i]);
            }

            List<CardInfo> newCards = new List<CardInfo>();

            for (int i = 0; i < originalCards.Count; i++)
            {
                if (!indecesToReplace.Contains(i))
                {
                    newCards.Add(originalCards[i]);
                    twoLetterCodes.Add("");
                    forceDisplays.Add(0f);
                    forceDisplayDelays.Add(0f);
                    reassigns.Add(true);
                }
                else
                {
                    newCards.Add(newCard);
                    twoLetterCodes.Add(twoLetterCode);
                    forceDisplays.Add(forceDisplay);
                    forceDisplayDelays.Add(forceDisplayDelay);
                    reassigns.Add(false);
                }
            }

            // now we remove all of the cards from the player
            RemoveAllCardsFromPlayer(player, editCardBar);

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            if (editCardBar)
            {
                CardBarUtils.instance.ClearCardBar(player);
            }

            yield return null;

            // then add back the new card
            AddCardsToPlayer(player, newCards.ToArray(), reassigns.ToArray(), twoLetterCodes.ToArray(), forceDisplays.ToArray(), forceDisplayDelays.ToArray(), editCardBar);
            //});

            yield break;

            // return the number of cards replaced
            //return indecesToReplace.Count;
        }
        [UnboundRPC]
        internal static void RPCA_AssignCard(string cardObjectName, int playerID, bool reassign, string twoLetterCode, float forceDisplay, float forceDisplayDelay)
        {
            RPCA_AssignCard(cardObjectName, playerID, reassign, twoLetterCode, forceDisplay, forceDisplayDelay, true);
        }
        [UnboundRPC]
        internal static void RPCA_AssignCard(string cardObjectName, int playerID, bool reassign, string twoLetterCode, float forceDisplay, float forceDisplayDelay, bool addToCardBar)
        {
            Player playerToUpgrade;

            CardInfo card = Cards.instance.GetCardWithObjectName(cardObjectName);

            ApplyCardStats cardStats = card.gameObject.GetComponentInChildren<ApplyCardStats>();

            // call Start to initialize card stat components for base-game cards
            typeof(ApplyCardStats).InvokeMember("Start",
                                BindingFlags.Instance | BindingFlags.InvokeMethod |
                                BindingFlags.NonPublic, null, cardStats, new object[] { });
            cardStats.GetComponent<CardInfo>().sourceCard = card;

            playerToUpgrade = (Player)PlayerManager.instance.InvokeMethod("GetPlayerWithID", playerID);

            Traverse.Create(cardStats).Field("playerToUpgrade").SetValue(playerToUpgrade);

            if (reassign && card.GetComponent<CustomCard>() != null)
            {
                try
                {

                    Gun gun = playerToUpgrade.GetComponent<Holding>().holdable.GetComponent<Gun>();
                    CharacterData characterData = playerToUpgrade.GetComponent<CharacterData>();
                    HealthHandler healthHandler = playerToUpgrade.GetComponent<HealthHandler>();
                    Gravity gravity = playerToUpgrade.GetComponent<Gravity>();
                    Block block = playerToUpgrade.GetComponent<Block>();
                    GunAmmo gunAmmo = gun.GetComponentInChildren<GunAmmo>();
                    CharacterStatModifiers characterStatModifiers = playerToUpgrade.GetComponent<CharacterStatModifiers>();
                    card.GetComponent<CustomCard>().OnReassignCard(playerToUpgrade, gun, gunAmmo, characterData, healthHandler, gravity, block, characterStatModifiers);
                }
                catch (NotImplementedException)
                { }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogError("[ModdingUtils] EXCEPTION: " + exception.GetType().ToString() + "\nThrown by: " + card.GetComponent<CustomCard>().GetModName() + " - " + card.cardName + " - " + "OnReassignCard(Player, Gun, GunAmmo, HealthHandler, Gravity, Block, CharacterStatModifiers)");
                }
            }

            if (!reassign || card.GetAdditionalData().canBeReassigned)
            {
                typeof(ApplyCardStats).InvokeMember("ApplyStats",
                                    BindingFlags.Instance | BindingFlags.InvokeMethod |
                                    BindingFlags.NonPublic, null, cardStats, new object[] { });
            }
            else
            {
                playerToUpgrade.data.currentCards.Add(card);
            }
            if (addToCardBar)
            {
                SilentAddToCardBar(playerToUpgrade.playerID, cardStats.GetComponent<CardInfo>().sourceCard, twoLetterCode, forceDisplay, forceDisplayDelay);
            }
            
        }
        [UnboundRPC]
        internal static void RPCA_AssignCardWithoutCardBar(string cardObjectName, int playerID, bool reassign, string twoLetterCode, float forceDisplay, float forceDisplayDelay)
        {
            RPCA_AssignCard(cardObjectName, playerID, reassign, twoLetterCode, forceDisplay, forceDisplayDelay, false);
        }
        [UnboundRPC]
        public static void RPCA_FullReset(int playerID)
        {
            Player playerToReset = (Player)PlayerManager.instance.InvokeMethod("GetPlayerWithID", playerID);

            // remove all the cards from the player by calling the PATCHED FullReset
            typeof(Player).InvokeMember("FullReset",
                    BindingFlags.Instance | BindingFlags.InvokeMethod |
                    BindingFlags.NonPublic, null, playerToReset, new object[] { });
        }
        [UnboundRPC]
        public static void RPCA_ClearCardBar(int playerID)
        {
            CardBar[] cardBars = (CardBar[])Traverse.Create(CardBarHandler.instance).Field("cardBars").GetValue();
            Player playerToReset = (Player)PlayerManager.instance.InvokeMethod("GetPlayerWithID", playerID);

            cardBars[playerToReset.playerID].ClearBar();

        }
        public enum SelectionType
        {
            All,
            Oldest,
            Newest,
            Random
        }

        public bool CardIsUniqueFromCards(CardInfo card, CardInfo[] cards)
        {
            if (card == null)
            {
                return false;
            }
            bool unique = true;

            foreach (CardInfo otherCard in cards)
            {
                if (card.name == otherCard.name)
                {
                    unique = false;
                }
            }

            return unique;
        }

        public bool CardIsNotBlacklisted(CardInfo card, CardCategory[] blacklistedCategories)
        {
            if (card == null)
            {
                return false;
            }
            bool blacklisted = card.categories.Intersect(blacklistedCategories).Any();

            return !blacklisted;
        }

        public bool CardDoesNotConflictWithCardsCategories(CardInfo card, CardInfo[] cards)
        {
            if (card == null)
            {
                return false;
            }

            bool conflicts = false;

            if (cards.Length == 0) { return !conflicts; }

            foreach (CardInfo otherCard in cards)
            {
                if (card.categories != null && otherCard.blacklistedCategories != null && card.categories.Intersect(otherCard.blacklistedCategories).Any())
                {
                    conflicts = true;
                }
            }

            return !conflicts;
        }
        public bool CardDoesNotConflictWithCards(CardInfo card, CardInfo[] cards)
        {
            if (card == null)
            {
                return false;
            }
            bool conflicts = false;

            if (cards.Length == 0) { return !conflicts; }

            foreach (CardInfo otherCard in cards)
            {
                if (card.categories != null && otherCard.blacklistedCategories != null && card.categories.Intersect(otherCard.blacklistedCategories).Any())
                {
                    conflicts = true;
                }
            }

            return !conflicts && (card.allowMultiple || cards.All(cardinfo => cardinfo.name != card.name));
        }

        public bool PlayerIsAllowedCard(Player player, CardInfo card)
        {
            if (card == null)
            {
                return false;
            }
            if (player == null)
            {
                return true;
            }
            bool blacklisted = false;

            foreach (CardInfo currentCard in player.data.currentCards)
            {
                if (card.categories.Intersect(currentCard.blacklistedCategories).Any())
                {
                    blacklisted = true;
                }
            }
            if (card.categories.Intersect(player.data.stats.GetAdditionalData().blacklistedCategories).Any())
            {
                blacklisted = true;
            }

            bool customFlags = true;

            //for (int i = 0; i < cardValidationFunctions.Count; i++)
            //{
            //    try
            //    {
            //        customFlags = cardValidationFunctions[i](player, card);
            //    }
            //    catch (Exception e)
            //    {
            //        UnityEngine.Debug.LogException(e);
            //    }
            //    if (!customFlags)
            //    {
            //        break;
            //    }
            //}

            if (cardValidationFunctions.Count > 0)
            {
                customFlags = !(cardValidationFunctions.Any(f => { bool flag = true; try { flag = f(player, card); } catch (Exception e) { flag = true; UnityEngine.Debug.LogError("Card Validation Function threw an error, returning true instead. See Log below for details."); UnityEngine.Debug.LogException(e); } return !flag; }));
            }

            return !blacklisted && (card.allowMultiple || player.data.currentCards.All(cardinfo => cardinfo.gameObject.name != card.gameObject.name)) && customFlags;
        }

        public void AddCardValidationFunction(Func<Player, CardInfo, bool> function)
        {
            cardValidationFunctions.Add(function);
        }

        List<Func<Player, CardInfo, bool>> cardValidationFunctions = new List<Func<Player, CardInfo, bool>>();

        public int CountPlayerCardsWithCondition(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats, Func<CardInfo, Player, Gun, GunAmmo, CharacterData, HealthHandler, Gravity, Block, CharacterStatModifiers, bool> condition)
        {
            return GetPlayerCardsWithCondition(player, gun, gunAmmo, data, health, gravity, block, characterStats, condition).Length;
        }
        public CardInfo[] GetPlayerCardsWithCondition(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats, Func<CardInfo, Player, Gun, GunAmmo, CharacterData, HealthHandler, Gravity, Block, CharacterStatModifiers, bool> condition)
        {
            if (player?.data?.currentCards is null)
            {
                return new CardInfo[0];
            }
            return player.data.currentCards.Where(cardinfo => condition(cardinfo, player, gun, gunAmmo, data, health, gravity, block, characterStats)).ToArray();
        }
        public int NORARITY_GetRandomCardIDWithCondition(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats, Func<CardInfo, Player, Gun, GunAmmo, CharacterData, HealthHandler, Gravity, Block, CharacterStatModifiers, bool> condition, int maxattempts = 1000)
        {
            CardInfo card = NORARITY_GetRandomCardWithCondition(player, gun, gunAmmo, data, health, gravity, block, characterStats, condition, maxattempts);
            if (card != null)
            {
                return GetCardID(card);
            }
            else
            {
                return -1;
            }

        }
        // get random card without respecting rarity, but always respecting PlayerIsAllowedCard
        public CardInfo NORARITY_GetRandomCardWithCondition(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats, Func<CardInfo, Player, Gun, GunAmmo, CharacterData, HealthHandler, Gravity, Block, CharacterStatModifiers, bool> condition, int maxattempts = 1000)
        {
            // get array of all ACTIVE cards
            CardInfo[] cards = activeCards.Where(card => condition(card, player, gun, gunAmmo, data, health, gravity, block, characterStats) && PlayerIsAllowedCard(player, card)).ToArray();
            if (cards.Length == 0) return null;

            // pseudorandom number generator
            int rID = rng.Next(0, cards.Length); // random card index
            return cards[rID];

        }
        public CardInfo NORARITY_DrawRandomCardWithCondition(CardInfo[] cardsToDrawFrom, Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats, Func<CardInfo, Player, Gun, GunAmmo, CharacterData, HealthHandler, Gravity, Block, CharacterStatModifiers, bool> condition, int maxattempts = 1000)
        {
            cardsToDrawFrom = cardsToDrawFrom.Where(card => condition(card, player, gun, gunAmmo, data, health, gravity, block, characterStats) && PlayerIsAllowedCard(player, card)).ToArray();
            if (cardsToDrawFrom.Length == 0) return null;

            // pseudorandom number generator
            int rID = rng.Next(0, cardsToDrawFrom.Length); // random card index
            return cardsToDrawFrom[rID];

        }
        public CardInfo DrawRandomCardWithCondition(CardInfo[] cardsToDrawFrom, Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats, Func<CardInfo, Player, Gun, GunAmmo, CharacterData, HealthHandler, Gravity, Block, CharacterStatModifiers, bool> condition, int maxattempts = 1000)
        {
            cardsToDrawFrom = cardsToDrawFrom.Where(card => condition(card, player, gun, gunAmmo, data, health, gravity, block, characterStats) && PlayerIsAllowedCard(player, card)).ToArray();
            if (cardsToDrawFrom.Length == 0) return null;

            return CardChoicePatchGetRanomCard.OrignialGetRanomCard(cardsToDrawFrom).GetComponent<CardInfo>();

        }
        // get random card using the base-game's spawn method (which respects rarities), also satisfying some conditions - always including PlayerIsAllowedCard
        public CardInfo GetRandomCardWithCondition(Player player, Gun gun, GunAmmo gunAmmo, CharacterData data, HealthHandler health, Gravity gravity, Block block, CharacterStatModifiers characterStats, Func<CardInfo, Player, Gun, GunAmmo, CharacterData, HealthHandler, Gravity, Block, CharacterStatModifiers, bool> condition, int maxattempts = 1000)
        {

            CardInfo[] cards = activeCards.Where(card => condition(card, player, gun, gunAmmo, data, health, gravity, block, characterStats) && PlayerIsAllowedCard(player, card)).ToArray();
            if (cards.Length == 0) return null;

            return CardChoicePatchGetRanomCard.OrignialGetRanomCard(cards).GetComponent<CardInfo>();

        }
        [Obsolete("GetCardID(string) is deprecated, use GetCardIDFromObjectName(string) instead.")]
        public int GetCardID(string cardName)
        {
            try
            {
                return allCards.Where(card => card.cardName == cardName).Select(card => GetCardID(card)).First();
            }
            catch
            {
                return -1;
            }
        }
        public int GetCardIDFromObjectName(string objectName)
        {
            try
            {
                return allCards.Where(card => card.name == objectName).Select(card => GetCardID(card)).First();
            }
            catch
            {
                return -1;
            }
        }
        public int GetCardID(CardInfo card)
        {
            return Array.IndexOf(allCards.ToArray(), card);
        }
        public CardInfo GetCardWithID(int cardID)
        {
            try
            {
                return allCards[cardID];
            }
            catch
            {
                return null;
            }
        }
        [Obsolete("GetCardWithName is deprecated since multiple cards can have the same cardName. Use GetCardWithObjectName instead and provided the desired Card's .name field.")]
        public CardInfo GetCardWithName(string cardName)
        {
            return allCards.Where(card => card.cardName == cardName).First();
        }
        public CardInfo GetCardWithObjectName(string name)
        {
            CardInfo card = allCards.Where(card => card.name == name).FirstOrDefault();
            if (!card)
            {
                card = HiddenCards.Where(card => card.name == name).FirstOrDefault();
            }
            return card;
        }
        public CardInfo[] GetAllCardsWithCondition(CardChoice cardChoice, Player player, Func<CardInfo, Player, bool> condition)
        {
            List<CardInfo> validCards = new List<CardInfo>();

            foreach (CardInfo card in cardChoice.cards)
            {
                if (condition(card, player))
                {
                    validCards.Add(card);
                }
            }

            return validCards.ToArray();
        }

        public CardInfo[] GetAllCardsWithCondition(CardInfo[] cards, Player player, Func<CardInfo, Player, bool> condition)
        {
            List<CardInfo> validCards = new List<CardInfo>();

            foreach (CardInfo card in cards)
            {
                if (condition(card, player))
                {
                    validCards.Add(card);
                }
            }

            return validCards.ToArray();
        }

        public static void SilentAddToCardBar(int playerID, CardInfo card, string twoLetterCode = "", float forceDisplay = 0f, float forceDisplayDelay = 0f)
        {

            CardBar[] cardBars = (CardBar[])Traverse.Create(CardBarHandler.instance).Field("cardBars").GetValue();

            Traverse.Create(cardBars[playerID]).Field("ci").SetValue(card);
            GameObject source = (GameObject)Traverse.Create(cardBars[playerID]).Field("source").GetValue();
            GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(source, source.transform.position, source.transform.rotation, source.transform.parent);
            gameObject.transform.localScale = Vector3.one;
            string text = card.cardName;
            if (twoLetterCode != "") { text = twoLetterCode; }
            text = text.Substring(0, 2);
            string text2 = text[0].ToString().ToUpper();
            if (text.Length > 1)
            {
                string str = text[1].ToString().ToLower();
                text = text2 + str;
            }
            else
            {
                text = text2;
            }
            gameObject.GetComponentInChildren<TextMeshProUGUI>().text = text;
            Traverse.Create(gameObject.GetComponent<CardBarButton>()).Field("card").SetValue(card);
            gameObject.gameObject.SetActive(true);
        }

        public void AddHiddenCard(CardInfo card)
        {
            hiddenCards.Add(card);
        }

    }
}
