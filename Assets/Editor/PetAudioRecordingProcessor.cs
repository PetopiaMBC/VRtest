using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the long CC0 dog recordings in SourceRecordings into short mono WAV
/// one-shots used by PetAudioController. Source files stay untouched.
/// </summary>
public static class PetAudioRecordingProcessor
{
    private const string SourceRoot = "Assets/Audio/PetAudio/SourceRecordings";
    private const string OutputRoot = "Assets/Audio/PetAudio/Resources/PetAudio";
    private const string AuditionSourceRoot = "Assets/Audio/PetAudio/AuditionSources";
    private const string AuditionRoot = "Assets/Audio/PetAudio/Audition";

    [MenuItem("Tools/My Little Pet/실제 강아지 음원 다시 만들기")]
    public static void RebuildFromRealRecordings()
    {
        string chosenBark = AuditionSourceRoot + "/bark_bigsoundbank_sleddog_2954.mp3";
        string chosenPanting = AuditionSourceRoot + "/pant_freesound_blukotek_424948.mp3";
        string whimper = SourceRoot + "/dog_whimper.mp3";

        ExportSlice(chosenBark, 0.03f, 0.34f, "Bark_01", 0.88f, 0.006f, 0.050f);
        ExportSlice(chosenBark, 0.39f, 0.39f, "Bark_02", 0.87f, 0.006f, 0.055f);
        ExportSlice(chosenBark, 5.00f, 0.72f, "Bark_03", 0.89f, 0.006f, 0.070f);
        ExportSlice(chosenBark, 8.00f, 0.76f, "Bark_04", 0.87f, 0.006f, 0.075f);

        // Automatic panting uses longer natural phrases instead of stitching
        // tiny inhale/exhale fragments into a robotic rhythm.
        ExportSlice(chosenPanting, 1.24f, 1.56f, "PantCycle_01", 0.66f, 0.030f, 0.080f);
        ExportSlice(chosenPanting, 2.92f, 1.62f, "PantCycle_02", 0.65f, 0.030f, 0.080f);
        ExportSlice(chosenPanting, 4.70f, 1.60f, "PantCycle_03", 0.66f, 0.030f, 0.080f);
        ExportSlice(chosenPanting, 6.60f, 1.42f, "PantCycle_04", 0.64f, 0.030f, 0.080f);

        // Short variants remain available for frame-authored Animation Events.
        ExportSlice(chosenPanting, 1.30f, 0.55f, "Pant_In_01", 0.62f, 0.020f, 0.060f);
        ExportSlice(chosenPanting, 2.00f, 0.58f, "Pant_In_02", 0.61f, 0.020f, 0.060f);
        ExportSlice(chosenPanting, 3.15f, 0.55f, "Pant_In_03", 0.62f, 0.020f, 0.060f);
        ExportSlice(chosenPanting, 1.82f, 0.55f, "Pant_Out_01", 0.64f, 0.020f, 0.065f);
        ExportSlice(chosenPanting, 2.55f, 0.58f, "Pant_Out_02", 0.63f, 0.020f, 0.065f);
        ExportSlice(chosenPanting, 3.70f, 0.58f, "Pant_Out_03", 0.64f, 0.020f, 0.065f);
        ExportSlice(chosenPanting, 4.48f, 0.58f, "Pant_Out_04", 0.62f, 0.020f, 0.065f);

        ExportSlice(whimper, 0.82f, 0.72f, "HappyWhine_01", 0.55f, 0.025f, 0.090f);
        ExportSlice(whimper, 1.55f, 0.72f, "HappyWhine_02", 0.53f, 0.025f, 0.090f);
        ExportSlice(whimper, 2.34f, 0.72f, "HappyWhine_03", 0.54f, 0.025f, 0.090f);

        ExportSlice(chosenPanting, 5.04f, 0.24f, "Sniff_01", 0.50f, 0.012f, 0.045f);
        ExportSlice(chosenPanting, 5.28f, 0.25f, "Sniff_02", 0.49f, 0.012f, 0.045f);
        ExportSlice(chosenPanting, 5.52f, 0.25f, "Sniff_03", 0.50f, 0.012f, 0.045f);

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        ConfigureGeneratedImporters();
        Debug.Log("[PetAudio] 실제 강아지 녹음으로 목소리 음원 17개를 다시 만들었습니다.");
    }

    [MenuItem("Tools/My Little Pet/후보 음원 다시 만들기")]
    public static void RebuildAuditionCandidates()
    {
        // Bark candidates deliberately come from different dogs and recordists.
        ExportSlice(AuditionSourceRoot + "/bark_opengameart_mono.wav", 0f, 2.03f,
            "Bark_A_FourCleanBarks", 0.84f, 0.006f, 0.040f, AuditionRoot);
        ExportSlice(AuditionSourceRoot + "/bark_bigsoundbank_sleddog_2954.mp3", 0f, 3.45f,
            "Bark_B_SledDog", 0.84f, 0.006f, 0.060f, AuditionRoot);
        ExportSlice(AuditionSourceRoot + "/bark_freesound_blukotek_424958.mp3", 0f, 4.00f,
            "Bark_C_SmallDogRapid", 0.84f, 0.006f, 0.060f, AuditionRoot);
        ExportSlice(AuditionSourceRoot + "/bark_freesound_yelp_452180.mp3", 0.64f, 1.35f,
            "Bark_D_ShortYelp", 0.84f, 0.006f, 0.060f, AuditionRoot);
        ExportSlice(AuditionSourceRoot + "/bark_bigsoundbank_group_0288.mp3", 0.15f, 3.30f,
            "Bark_E_OutdoorGroup", 0.84f, 0.006f, 0.060f, AuditionRoot);
        ExportSlice(SourceRoot + "/dog_bark_large.mp3", 0f, 3.20f,
            "Bark_F_LargeDog", 0.84f, 0.006f, 0.060f, AuditionRoot);

        // Pant candidates are longer excerpts so breathing rhythm and recording
        // noise can be judged before we cut animation-ready one-shots.
        ExportSlice(AuditionSourceRoot + "/pant_bigsoundbank_dogs_1547.mp3", 2.0f, 5.0f,
            "Pant_A_TwoBigDogs", 0.72f, 0.030f, 0.080f, AuditionRoot);
        ExportSlice(AuditionSourceRoot + "/pant_freesound_blukotek_424948.mp3", 1.5f, 5.0f,
            "Pant_B_MediumDog", 0.72f, 0.030f, 0.080f, AuditionRoot);
        ExportSlice(AuditionSourceRoot + "/pant_freesound_bulldog_134820.mp3", 3.5f, 5.0f,
            "Pant_C_FrenchBulldog", 0.72f, 0.030f, 0.080f, AuditionRoot);
        ExportSlice(AuditionSourceRoot + "/pant_freesound_resting_448784.mp3", 42.0f, 5.0f,
            "Pant_D_RestingDog", 0.72f, 0.030f, 0.080f, AuditionRoot);
        ExportSlice(SourceRoot + "/dog_panting.mp3", 4.0f, 5.0f,
            "Pant_E_CurrentSource", 0.72f, 0.030f, 0.080f, AuditionRoot);

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        ConfigureImporters(AuditionRoot, false);
        Debug.Log("[PetAudio] 선택용 짖음 6개와 헥헥거림 5개를 만들었습니다.");
    }

    private static void ExportSlice(
        string sourcePath,
        float startSeconds,
        float durationSeconds,
        string outputName,
        float targetPeak,
        float fadeInSeconds,
        float fadeOutSeconds,
        string outputRoot = OutputRoot)
    {
        AudioClip source = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath);
        if (source == null)
            throw new FileNotFoundException("강아지 원본 음원을 찾을 수 없습니다.", sourcePath);

        source.LoadAudioData();
        float[] interleaved = new float[source.samples * source.channels];
        if (!source.GetData(interleaved, 0))
            throw new InvalidOperationException("음원을 읽을 수 없습니다: " + sourcePath);

        int startFrame = Mathf.Clamp(
            Mathf.RoundToInt(startSeconds * source.frequency),
            0,
            source.samples - 1);
        int frameCount = Mathf.Clamp(
            Mathf.RoundToInt(durationSeconds * source.frequency),
            1,
            source.samples - startFrame);

        float[] mono = new float[frameCount];
        double mean = 0.0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            double sum = 0.0;
            int sourceOffset = (startFrame + frame) * source.channels;
            for (int channel = 0; channel < source.channels; channel++)
                sum += interleaved[sourceOffset + channel];

            mono[frame] = (float)(sum / source.channels);
            mean += mono[frame];
        }

        float dcOffset = (float)(mean / frameCount);
        float maximum = 0f;
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] -= dcOffset;
            maximum = Mathf.Max(maximum, Mathf.Abs(mono[i]));
        }

        float gain = maximum > 0.00001f ? targetPeak / maximum : 1f;
        int fadeInFrames = Mathf.Clamp(Mathf.RoundToInt(fadeInSeconds * source.frequency), 1, frameCount);
        int fadeOutFrames = Mathf.Clamp(Mathf.RoundToInt(fadeOutSeconds * source.frequency), 1, frameCount);
        for (int i = 0; i < mono.Length; i++)
        {
            float fadeIn = Mathf.Clamp01(i / (float)fadeInFrames);
            float fadeOut = Mathf.Clamp01((mono.Length - 1 - i) / (float)fadeOutFrames);
            mono[i] = Mathf.Clamp(mono[i] * gain * fadeIn * fadeOut, -1f, 1f);
        }

        string absoluteOutput = Path.GetFullPath(Path.Combine(outputRoot, outputName + ".wav"));
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput));
        File.WriteAllBytes(absoluteOutput, CreateMonoPcm16Wav(mono, source.frequency));
    }

    private static byte[] CreateMonoPcm16Wav(float[] samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        int dataLength = samples.Length * sizeof(short);

        using (var stream = new MemoryStream(44 + dataLength))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataLength);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataLength);

            for (int i = 0; i < samples.Length; i++)
                writer.Write((short)Mathf.RoundToInt(samples[i] * short.MaxValue));

            return stream.ToArray();
        }
    }

    private static void ConfigureGeneratedImporters()
    {
        ConfigureImporters(OutputRoot, true);
    }

    private static void ConfigureImporters(string root, bool restrictToRuntimeNames)
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { root });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (restrictToRuntimeNames &&
                !fileName.StartsWith("Bark_", StringComparison.Ordinal) &&
                !fileName.StartsWith("Pant_", StringComparison.Ordinal) &&
                !fileName.StartsWith("PantCycle_", StringComparison.Ordinal) &&
                !fileName.StartsWith("HappyWhine_", StringComparison.Ordinal) &&
                !fileName.StartsWith("Sniff_", StringComparison.Ordinal))
            {
                continue;
            }

            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                continue;

            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                compressionFormat = AudioCompressionFormat.PCM,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = true
            };
            importer.SaveAndReimport();
        }
    }
}
