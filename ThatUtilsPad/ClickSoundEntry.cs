using UnityEngine;

namespace ThatUtilsPad;

public class ClickSoundEntry
{
    public string Name;
    public AudioClip Clip;

    public ClickSoundEntry(string name, AudioClip clip)
    {
        Name = name;
        Clip = clip;
    }
}
