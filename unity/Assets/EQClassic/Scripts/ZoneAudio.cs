using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EQClassic.Shared.World;
using Infrastructure.EQ.MeltySynth;
using Lantern.EQ.Audio.Xmi;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The zone's music and sounds, from the client's files as LanternExtractor exports them:
    /// music regions (music_instances: day and night tracks of the zone's XMI, synthesized at run
    /// time with MeltySynth and Lantern's soundfont), ambient loops by region (sound2d_instances,
    /// day and night) and sound emitters in the world (sound3d_instances), from the client's WAV
    /// sounds. Positions in those lists are Lantern units, the world is scaled by <see cref="Scale"/>.
    /// </summary>
    public sealed class ZoneAudio : MonoBehaviour
    {
        public float Scale = 0.5f;
        public float MusicVolume = 0.3f;

        private sealed class MusicRegion
        {
            public Vector3 Position;
            public float Radius;
            public int DayTrack, NightTrack;
        }

        private sealed class AmbientRegion
        {
            public Vector3 Position;
            public float Radius;
            public string DayClip, NightClip;
            public float DayVolume, NightVolume, Cooldown, CooldownRandom;
            public AudioSource Source;
            public float NextPlay;
        }

        private readonly List<MusicRegion> _music = new List<MusicRegion>();
        private readonly List<AmbientRegion> _ambient = new List<AmbientRegion>();
        private readonly List<GameObject> _emitters = new List<GameObject>();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private XmiFile _xmi;
        private Synthesizer _synth;
        private MidiFileSequencer _sequencer;
        private int _playingTrack = -1;
        private readonly object _lock = new object();
        private float[] _left = new float[0], _right = new float[0];
        private float _nextCheck;
        private AudioSource _musicSource;

        /// <summary>Loads the zone's audio (a zone without lists or files is silent).</summary>
        public void Enter(string zone)
        {
            Leave();
            ReadLines(Find(zone, "music_instances.txt", "_music.txt"), p =>
            {
                if (p.Length >= 6)
                    _music.Add(new MusicRegion { Position = V(p), Radius = F(p[3]), DayTrack = (int)F(p[4]), NightTrack = (int)F(p[5]) });
            });
            ReadLines(Find(zone, "sound2d_instances.txt", "_sound2d.txt"), p =>
            {
                if (p.Length >= 11)
                    _ambient.Add(new AmbientRegion
                    {
                        Position = V(p), Radius = F(p[3]), DayClip = p[4], NightClip = p[5], Cooldown = F(p[6]) / 1000f,
                        CooldownRandom = F(p[8]) / 1000f, DayVolume = F(p[9]), NightVolume = F(p[10]),
                    });
            });
            ReadLines(Find(zone, "sound3d_instances.txt", "_sound3d.txt"), p =>
            {
                if (p.Length >= 8 && Clip(p[4]) is AudioClip clip)
                    _emitters.Add(Emitter(V(p) * Scale, F(p[3]) * Scale, clip, F(p[7]), F(p[5]) / 1000f));
            });
            var xmi = FirstExisting(Path.Combine(ClientPaths.Exports, "music", zone + ".xmi"), Path.Combine(ClientBundles.Directory, "music", zone + ".xmi"));
            var soundFont = Path.Combine(Application.streamingAssetsPath, "Soundfont", "synthusr_samplefix.sf2");
            if (xmi != null && File.Exists(soundFont))
            {
                try
                {
                    using (var stream = File.OpenRead(xmi))
                        _xmi = new XmiFileReader(stream).ReadXmiFile();
                    _synth = new Synthesizer(soundFont, AudioSettings.outputSampleRate);
                    _synth.MasterVolume = MusicVolume;
                    _sequencer = new MidiFileSequencer(_synth);
                    _musicSource = gameObject.AddComponent<AudioSource>();
                    _musicSource.spatialBlend = 0f;
                    _musicSource.Play(); // no clip: OnAudioFilterRead fills the buffer
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"EQClassic: no music for {zone}: {e.Message}");
                    _xmi = null;
                }
            }
        }

        public void Leave()
        {
            lock (_lock)
            {
                _sequencer = null;
                _synth = null;
                _playingTrack = -1;
            }
            _xmi = null;
            if (_musicSource != null)
                Destroy(_musicSource);
            foreach (var a in _ambient)
                if (a.Source != null)
                    Destroy(a.Source.gameObject);
            foreach (var e in _emitters)
                Destroy(e);
            _music.Clear();
            _ambient.Clear();
            _emitters.Clear();
        }

        /// <summary>Called every frame with the listener's position and whether it is day in Norrath.</summary>
        public void Listen(Vector3 listener, bool day)
        {
            if (Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + 0.5f;
            var here = listener / Scale;
            int track = -1;
            float best = float.MaxValue;
            foreach (var m in _music)
            {
                float d = Vector3.Distance(here, m.Position);
                if (d <= m.Radius && d < best)
                {
                    best = d;
                    track = day ? m.DayTrack : m.NightTrack;
                }
            }
            PlayTrack(track);
            foreach (var a in _ambient)
                Ambient(a, Vector3.Distance(here, a.Position) <= a.Radius, day);
        }

        private void PlayTrack(int track)
        {
            if (track == _playingTrack || _xmi == null)
                return;
            MidiFile midi = null;
            if (track >= 0 && track < _xmi.XmidiTracks.Length)
            {
                try
                {
                    using (var stream = _xmi.WriteMidiTrack(track))
                    {
                        stream.Position = 0;
                        midi = new MidiFile(stream);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"EQClassic: music track {track}: {e.Message}");
                }
            }
            lock (_lock)
            {
                _playingTrack = track;
                if (_sequencer == null)
                    return;
                if (midi != null)
                {
                    _sequencer.Play(midi, true);
                    Debug.Log($"EQClassic: music track {track}");
                }
                else
                    _sequencer.Stop();
            }
        }

        /// <summary>The synthesizer, on the audio thread.</summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_lock)
            {
                if (_sequencer == null || _playingTrack < 0)
                    return;
                int frames = data.Length / channels;
                if (_left.Length < frames)
                {
                    _left = new float[frames];
                    _right = new float[frames];
                }
                _sequencer.Render(new Span<float>(_left, 0, frames), new Span<float>(_right, 0, frames));
                for (int i = 0; i < frames; i++)
                {
                    data[i * channels] += _left[i];
                    if (channels > 1)
                        data[i * channels + 1] += _right[i];
                }
            }
        }

        private void Ambient(AmbientRegion a, bool inside, bool day)
        {
            var clip = Clip(day ? a.DayClip : a.NightClip);
            float volume = day ? a.DayVolume : a.NightVolume;
            if (!inside || clip == null || volume <= 0f)
            {
                if (a.Source != null)
                    a.Source.Stop();
                return;
            }
            if (a.Source == null)
            {
                var go = new GameObject("Ambient " + clip.name);
                go.transform.SetParent(transform, false);
                a.Source = go.AddComponent<AudioSource>();
                a.Source.spatialBlend = 0f;
            }
            a.Source.volume = volume;
            if (a.Cooldown <= 0f)
            {
                a.Source.loop = true;
                if (a.Source.clip != clip || !a.Source.isPlaying)
                {
                    a.Source.clip = clip;
                    a.Source.Play();
                }
            }
            else if (Time.time >= a.NextPlay)
            {
                a.Source.PlayOneShot(clip, volume);
                a.NextPlay = Time.time + a.Cooldown + UnityEngine.Random.value * a.CooldownRandom;
            }
        }

        private GameObject Emitter(Vector3 position, float radius, AudioClip clip, float volume, float cooldown)
        {
            var go = new GameObject("Sound " + clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = Mathf.Max(radius, 2f);
            source.loop = true; // periodic sounds (with a cooldown) loop too: a simplification
            source.Play();
            return go;
        }

        /// <summary>A client sound (WAV), read once.</summary>
        private AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (_clips.TryGetValue(name, out var clip))
                return clip;
            var path = FirstExisting(Path.Combine(ClientPaths.Exports, "sounds", name + ".wav"), Path.Combine(ClientBundles.Directory, "sounds", name + ".wav"));
            clip = path == null ? null : ReadWav(name, File.ReadAllBytes(path));
            _clips[name] = clip;
            return clip;
        }

        /// <summary>PCM WAV (8 or 16 bits) to an AudioClip.</summary>
        public static AudioClip ReadWav(string name, byte[] bytes)
        {
            if (bytes.Length < 44 || bytes[0] != 'R' || bytes[8] != 'W')
                return null;
            int channels = 1, rate = 22050, bits = 16, pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (id == "data")
                {
                    size = Math.Min(size, bytes.Length - body);
                    int samples = size / (bits / 8);
                    var data = new float[samples];
                    for (int i = 0; i < samples; i++)
                        data[i] = bits == 8 ? (bytes[body + i] - 128) / 128f : BitConverter.ToInt16(bytes, body + 2 * i) / 32768f;
                    if (samples == 0 || channels <= 0)
                        return null;
                    var clip = AudioClip.Create(name, samples / channels, channels, rate, false);
                    clip.SetData(data, 0);
                    return clip;
                }
                pos = body + size + (size & 1);
            }
            return null;
        }

        private static string Find(string zone, string exportName, string buildSuffix) =>
            FirstExisting(Path.Combine(ClientPaths.Exports, zone, "Zone", exportName), Path.Combine(ClientBundles.Directory, zone + buildSuffix));

        private static string FirstExisting(params string[] paths)
        {
            foreach (var p in paths)
                if (File.Exists(p))
                    return p;
            return null;
        }

        private static void ReadLines(string path, Action<string[]> row)
        {
            if (path == null)
                return;
            foreach (var line in File.ReadAllLines(path))
                if (line.Length > 0 && line[0] != '#')
                    row(line.Split(','));
        }

        private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        private static Vector3 V(string[] p) => new Vector3(F(p[0]), F(p[1]), F(p[2]));

        private void OnDestroy() => Leave();
    }
}
