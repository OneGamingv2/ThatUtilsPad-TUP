using System;
using UnityEngine;

namespace ThatUtilsPad;

public static class WavUtility
{
    public static AudioClip ToAudioClip(byte[] fileBytes, string name = "wav")
    {
        int channels = BitConverter.ToInt16(fileBytes, 22);
        int sampleRate = BitConverter.ToInt32(fileBytes, 24);
        int bitDepth = BitConverter.ToInt16(fileBytes, 34);

        int dataIndex = 12;
        while (dataIndex < fileBytes.Length - 8)
        {
            string chunkId = System.Text.Encoding.ASCII.GetString(fileBytes, dataIndex, 4);
            int chunkSize = BitConverter.ToInt32(fileBytes, dataIndex + 4);
            if (chunkId == "data")
                break;
            dataIndex += 8 + chunkSize;
        }
        dataIndex += 8;

        int sampleCount = (fileBytes.Length - dataIndex) / (bitDepth / 8);
        float[] data = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            int offset = dataIndex + i * (bitDepth / 8);
            data[i] = bitDepth == 16
                ? BitConverter.ToInt16(fileBytes, offset) / 32768f
                : (fileBytes[offset] - 128) / 128f;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
