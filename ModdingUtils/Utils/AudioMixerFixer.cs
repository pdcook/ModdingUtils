using System;
using System.Collections.Generic;
using Sonigon;
using Sonigon.Internal;
using SoundImplementation;
using UnityEngine;
using UnityEngine.Audio;

namespace ModdingUtils.Utils
{
    [RequireComponent(typeof(SoundUnityEventPlayer))]
    public class AudioMixerFixer : MonoBehaviour
    {
        public enum AudioType
        {
            SFX, MASTER, MUSIC
        }
        public AudioType type;

        private Dictionary<AudioType, string> groupNames = new Dictionary<AudioType, string>
        {
            { AudioType.MASTER, "MasterPrivate" },
            { AudioType.MUSIC, "MUS" },
            { AudioType.SFX, "SFX" },
        };

        private AudioMixerGroup audioGroup;

        public void RunFix()
        {
            this.Awake();
        }

        void Awake()
        {
            audioGroup = SoundVolumeManager.Instance.audioMixer.FindMatchingGroups(groupNames[type])[0];

            var player = GetComponent<SoundUnityEventPlayer>();
            SetAudioMixerGroup(player.soundStart);
            SetAudioMixerGroup(player.soundStartLoop);
            SetAudioMixerGroup(player.soundEnd);
        }

        private void SetAudioMixerGroup(Sonigon.SoundEvent soundEvent)
        {
            if (soundEvent == null) return;
            soundEvent.variables.audioMixerGroup = audioGroup;
        }
    }
}
