using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using GorillaLocomotion;
using GorillaNetworking;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PlayFab;
using ThatUtilsPad.MenuComponents;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace ThatUtilsPad;


public partial class Main
{
    // spotify pile goes here now; top file was soup lol
    private readonly List<GameObject> spotifyClickBits = new List<GameObject>();
    private GameObject spotifyPage;
    private TMP_Text songLabel;
    private TMP_Text artistLabel;
    private TMP_Text timeLabel;
    private TMP_Text statusLabel;
    private TMP_Text hintLabel;
    private Transform playGlyph;
    private Transform pauseGlyph;
    private bool spotifyWasOpen;
    private Coroutine spotifyWave;
    private readonly Dictionary<Transform, Vector3> spotifyPopSizes = new Dictionary<Transform, Vector3>();
    private bool spotifyIconGuessOn;
    private bool spotifyIconGuessPlaying;
    private float spotifyIconGuessUntil;
    private RawImage coverRaw;
    private Image coverImage;
    private Sprite coverSprite;
    private Renderer coverRenderer;
    private Material coverMaterial;
    private int coverHash;
    private int coverByteLength;
    private Texture2D nativeCoverTexture;
    private readonly object coverWebLock = new object();
    private bool coverWebBusy;
    private string coverWebAsked = "";
    private string coverBytesKey = "";
    private string coverTextureKey = "";
    private byte[] coverBytes;
    private Texture2D coverTexture;
    private string coverShowingKey = "";
    private Texture2D coverShowingTexture;
    private Coroutine coverSwap;
    private string coverSwapTarget = "";
    private const float CoverFadeTime = 0.5f;
    private const float CoverBlackTime = 0.5f;
    private Image progressFill;
    private float nextSpotifyPaint;
    private Vector3 songHomePos;
    private Color songHomeColor = Color.white;
    private Mods.SpotifyTrackInfo lastGoodSpotify;
    private bool hasLastGoodSpotify;
    private string shownSong = "";
    private string nextSongText = "";
    private string shownArtist = "";
    private string nextArtistText = "";
    private Color artistHomeColor = Color.white;
    private string longSongLine = "";
    private float songCrawlX;
    private float songCrawlReset = 0.05f;
    private float songBlinkClock;
    private const float SongCrawlSpeed = 4.8f;
    private const string SongCrawlGap = "   ";
    private const int SongCrawlCopies = 99;
    private const float SongY = -10.3f;
    private const float SongZ = -0.1f;
    private const float SongOnTime = 5f;
    private const float SongFadeTime = 0.5f;
    private const float SongOffTime = 0.5f;


    private void InitSpotifyHudPage(GameObject menuObj)
    {
        spotifyPage = FindChildByName(menuObj.transform, "SpotifyHudPage")?.gameObject
                         ?? FindChildByName(menuObj.transform, "SpotifyPage")?.gameObject
                         ?? FindChildByName(menuObj.transform, "SpotifyHUD")?.gameObject;

        if (spotifyPage == null)
        {
            Debug.LogWarning("[TUP] SpotifyHudPage not found. Add hierarchy from final notes.");
            return;
        }

        // these labels like to hide in the prefab, little nerds
        songLabel = FindTmpTextAtPath(spotifyPage.transform, "SongNameCont/SongName")
                          ?? FindTmpText(spotifyPage.transform, "SongName", "Song", "Title", "TrackTitle", "TrackName");
        artistLabel = FindTmpText(spotifyPage.transform, "ArtistName", "AuthorName", "Artist", "Author", "Subtitle", "TrackArtist");
        timeLabel = FindTmpText(spotifyPage.transform, "Duration", "Time", "TrackTime");
        statusLabel = FindTmpText(spotifyPage.transform, "Status", "PlaybackStatus");
        hintLabel = FindTmpText(spotifyPage.transform, "Hint", "Source", "HelperText");
        PickSpotifyTextLabels();
        Transform albumIconTransform = FindChildAtPath(menuObj.transform, "SpotifyHudPage/AlbumIconCont/AlbumIcon")
                                       ?? FindChildAtPath(spotifyPage.transform, "AlbumIconCont/AlbumIcon")
                                       ?? FindAlbumHome(spotifyPage.transform);
        coverRaw = GetCoverRawImage(albumIconTransform ?? spotifyPage.transform);
        coverImage = albumIconTransform != null ? albumIconTransform.GetComponent<Image>() ?? albumIconTransform.GetComponentInChildren<Image>(true) : spotifyPage.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name.IndexOf("progress", StringComparison.OrdinalIgnoreCase) < 0);
        if (coverRaw != null) HideAlbumSlotImages(albumIconTransform ?? coverRaw.transform);
        if (coverRaw != null) coverRaw.transform.SetAsLastSibling();
        coverRenderer = albumIconTransform != null ? albumIconTransform.GetComponent<Renderer>() ?? albumIconTransform.GetComponentInChildren<Renderer>(true) : spotifyPage.GetComponentInChildren<Renderer>(true);
        if (coverRenderer != null)
        {
            coverMaterial = new Material(coverRenderer.material);
            coverRenderer.material = coverMaterial;
        }
        progressFill = FindChildByName(spotifyPage.transform, "ProgressFill")?.GetComponent<Image>();

        HookSpotifyClick("PlayPauseButton", PressSpotifyPlayPause);
        Transform playPauseButton = FindChildByName(spotifyPage.transform, "PlayPauseButton");
        playGlyph = playPauseButton != null ? playPauseButton.Find("Play") ?? FindChildAtPath(playPauseButton, "Play") ?? FindChildByName(playPauseButton, "Play") : null;
        pauseGlyph = playPauseButton != null ? playPauseButton.Find("Pause") ?? FindChildAtPath(playPauseButton, "Pause") ?? FindChildByName(playPauseButton, "Pause") : null;
        HookSpotifyClick("NextButton", Mods.SpotifyNext);
        HookSpotifyClick("PreviousButton", Mods.SpotifyPrevious);

        spotifyPage.SetActive(false);
        PaintSpotifyHud();
    }

    private void PressSpotifyPlayPause()
    {
        bool nextPlaying = !(pauseGlyph != null && pauseGlyph.gameObject.activeSelf);
        spotifyIconGuessOn = true;
        spotifyIconGuessPlaying = nextPlaying;
        spotifyIconGuessUntil = Time.time + 1.25f;
        FlipSpotifyIcons(nextPlaying);
        Mods.SpotifyPlayPause();
        NudgeSpotifyHudSoon(0.45f);
    }
    private void HookSpotifyClick(string buttonName, Action action)
    {
        if (spotifyPage == null) return;

        Transform button = FindChildByName(spotifyPage.transform, buttonName);
        if (button == null)
        {
            Debug.LogWarning("[TUP] Spotify HUD button missing: " + buttonName);
            return;
        }

        Transform colliderTransform = button.Find("Collider") ?? FindChildByName(button, "Collider");
        if (colliderTransform == null)
        {
            Debug.LogWarning("[TUP] Spotify HUD button has no Collider child: " + buttonName);
            return;
        }

        colliderTransform.gameObject.layer = 2;

        Collider col = colliderTransform.GetComponent<Collider>();
        if (col == null)
            col = colliderTransform.gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;

        Rigidbody rb = colliderTransform.GetComponent<Rigidbody>();
        if (rb == null)
            rb = colliderTransform.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        ButtonTrigger trigger = colliderTransform.GetComponent<ButtonTrigger>() ?? colliderTransform.gameObject.AddComponent<ButtonTrigger>();
        trigger.BtnIdentifier = "Spotify " + buttonName.Replace("Button", "");
        trigger.CustomAction = action;
        trigger.ButtonRoot = button;

        ButtonCollider buttonCollider = colliderTransform.GetComponent<ButtonCollider>() ?? colliderTransform.gameObject.AddComponent<ButtonCollider>();
        buttonCollider.trigger = trigger;

        if (!spotifyClickBits.Contains(colliderTransform.gameObject))
            spotifyClickBits.Add(colliderTransform.gameObject);
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return child;
        return null;
    }

    private static TMP_Text FindTmpText(Transform root, params string[] names)
    {
        foreach (string name in names)
        {
            Transform child = FindChildByName(root, name);
            TMP_Text text = child != null ? child.GetComponent<TMP_Text>() ?? child.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
                return text;
        }

        return null;
    }

    private static TMP_Text FindTmpTextAtPath(Transform root, string path)
    {
        Transform child = root != null ? root.Find(path) : null;
        return child != null ? child.GetComponent<TMP_Text>() ?? child.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static Transform FindChildAtPath(Transform root, string path)
    {
        return root != null ? root.Find(path) : null;
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "null";

        List<string> parts = new List<string>();
        for (Transform current = transform; current != null; current = current.parent)
            parts.Add(current.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private void PickSpotifyTextLabels()
    {
        if (spotifyPage == null)
            return;

        TMP_Text[] texts = spotifyPage.GetComponentsInChildren<TMP_Text>(true)
            .Where(text => text != null && !IsSpotifyTextOnAButton(text))
            .OrderByDescending(text => TextNameScore(text, "song", "title", "track"))
            .ThenBy(text => text.transform.GetSiblingIndex())
            .ToArray();

        if (songLabel == null)
            songLabel = texts.FirstOrDefault();

        if (artistLabel == null)
        {
            artistLabel = texts
                .OrderByDescending(text => TextNameScore(text, "artist", "author", "subtitle"))
                .ThenBy(text => text == songLabel ? 1 : 0)
                .FirstOrDefault(text => text != songLabel);
        }

        if (timeLabel == null)
            timeLabel = texts.FirstOrDefault(text => NameContains(text, "duration", "time"));
        if (statusLabel == null)
            statusLabel = texts.FirstOrDefault(text => NameContains(text, "status", "playback"));
    }

    private static bool IsSpotifyTextOnAButton(TMP_Text text)
    {
        string path = GetTransformPath(text.transform).ToLowerInvariant();
        return path.Contains("button") || path.Contains("next") || path.Contains("previous") || path.Contains("playpause") || path.Contains("page");
    }

    private static int TextNameScore(TMP_Text text, params string[] terms)
    {
        string path = GetTransformPath(text.transform).ToLowerInvariant();
        int score = 0;
        foreach (string term in terms)
            if (path.Contains(term)) score += 10;
        return score;
    }

    private static bool NameContains(TMP_Text text, params string[] terms)
    {
        string path = GetTransformPath(text.transform).ToLowerInvariant();
        return terms.Any(term => path.Contains(term));
    }

private static Transform FindAlbumHome(Transform root)
    {
        string[] names = { "AlbumIcon", "AlbumImage", "AlbumArt", "Artwork", "Cover", "CoverArt", "SongIcon", "TrackIcon" };
        foreach (string name in names)
        {
            Transform child = FindChildByName(root, name);
            if (child != null && !IsSpotifyButtonish(child))
                return child;
        }

        return root.GetComponentsInChildren<Transform>(true)
            .Where(t => t != null && !IsSpotifyButtonish(t))
            .OrderByDescending(t => TextNameScore(GetTransformPath(t), "album", "artwork", "cover", "songicon", "trackicon"))
            .FirstOrDefault(t => TextNameScore(GetTransformPath(t), "album", "artwork", "cover", "songicon", "trackicon") > 0);
    }

    private static bool IsSpotifyButtonish(Transform transform)
    {
        string path = GetTransformPath(transform).ToLowerInvariant();
        return path.Contains("button") || path.Contains("next") || path.Contains("previous") || path.Contains("playpause") || path.Contains("progress");
    }

    private static int TextNameScore(string path, params string[] terms)
    {
        path = (path ?? "").ToLowerInvariant();
        int score = 0;
        foreach (string term in terms)
            if (path.Contains(term)) score += 10;
        return score;
    }

    private static RawImage GetCoverRawImage(Transform slot)
    {
        if (slot == null)
            return null;

        RawImage rawImage = slot.GetComponent<RawImage>() ?? slot.GetComponentInChildren<RawImage>(true);
        if (rawImage != null)
        {
            rawImage.raycastTarget = false;
            return rawImage;
        }

        RectTransform slotRect = slot as RectTransform ?? slot.GetComponent<RectTransform>();
        if (slotRect == null)
            return null;

        GameObject imageObject = new GameObject("AlbumCoverThing", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        imageObject.transform.SetParent(slot, false);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = Vector3.zero;

        rawImage = imageObject.GetComponent<RawImage>();
        rawImage.raycastTarget = false;
        rawImage.color = new Color(1f, 1f, 1f, 0.35f);
        return rawImage;
    }

    private static void HideAlbumSlotImages(Transform slot)
    {
        if (slot == null)
            return;

        foreach (Image image in slot.GetComponentsInChildren<Image>(true))
            image.enabled = false;
    }

    private bool IsSpotifyHudPageSelected()
    {
        if (currentCategory != "Spotify") return false;
        if (!Mods.Actions.TryGetValue(currentCategory, out var category)) return true;
        return currentPage == GetSpotifyHudPageIndex(category);
    }

    private int GetSpotifyHudPageIndex(ModCategory category)
    {
        int normalPages = Mathf.CeilToInt((float)category.Actions.Count / PageSize);
        return Mathf.Max(0, normalPages);
    }

    private int GetCategoryTotalPages(string categoryName, ModCategory category)
    {
        int itemCount = category.Actions.Count + (categoryName == "SelectUser" ? Mods.GetSelectableRigs().Count : 0);
        int normalPages = Mathf.CeilToInt((float)itemCount / PageSize);
        if (categoryName == "Spotify")
            return Mathf.Max(1, normalPages + 1);
        return Mathf.Max(1, normalPages);
    }

    private void SetSpotifyPageOnOff()
    {
        if (spotifyPage == null) return;
        bool visible = IsSpotifyHudPageSelected();
        bool wasVisible = spotifyWasOpen && spotifyPage.activeSelf;
        spotifyPage.SetActive(visible);
        spotifyWasOpen = visible;
        if (visible)
        {
            PaintSpotifyHud();
            if (!wasVisible)
                StartSpotifyHudWave();
        }
        else if (spotifyWave != null)
        {
            StopCoroutine(spotifyWave);
            spotifyWave = null;
        }
    }

    private void StartSpotifyHudWave()
    {
        if (spotifyPage == null)
            return;

        if (spotifyWave != null)
            StopCoroutine(spotifyWave);

        spotifyWave = StartCoroutine(SpotifyHudWaveIn());
    }

    private IEnumerator SpotifyHudWaveIn()
    {
        if (spotifyPage == null)
            yield break;

        string[] orderedNames =
        {
            "AlbumIconCont",
            "SongNameCont",
            "ArtistName",
            "PreviousButton",
            "PlayPauseButton",
            "NextButton"
        };

        List<Transform> parts = new List<Transform>();
        foreach (string name in orderedNames)
        {
            Transform part = spotifyPage.transform.Find(name) ?? FindChildByName(spotifyPage.transform, name);
            if (part == null)
                continue;

            part.gameObject.SetActive(true);
            if (!spotifyPopSizes.ContainsKey(part) || spotifyPopSizes[part] == Vector3.zero)
                spotifyPopSizes[part] = part.localScale == Vector3.zero ? Vector3.one : part.localScale;

            part.localScale = Vector3.zero;
            parts.Add(part);
        }

        foreach (Transform part in parts)
        {
            if (part == null)
                continue;

            Vector3 targetScale = spotifyPopSizes.TryGetValue(part, out Vector3 savedPopSize) ? savedPopSize : Vector3.one;
            StartCoroutine(MenuEffects.PopButton(part.gameObject, targetScale, Vector3.zero, true));
            PlayButtonEnterSound();
            yield return new WaitForSeconds(0.12f);
        }

        spotifyWave = null;
    }

    private void PlayButtonEnterSound()
    {
        AudioSource audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.volume = 0.1f;
        audioSource.PlayOneShot(btnEnterSound);
    }

    private void UpdateSpotifyHudLoop()
    {
        if (!isMenuOpened || spotifyPage == null || !spotifyPage.activeSelf) return;
        UpdateSpotifySongMarquee();
        if (Time.time < nextSpotifyPaint) return;
        nextSpotifyPaint = Time.time + 0.5f;
        PaintSpotifyHud();
    }

    public void NudgeSpotifyHudSoon(float delay = 1.1f)
    {
        StartCoroutine(NudgeSpotifyHudAfterWait(delay));
    }

    private IEnumerator NudgeSpotifyHudAfterWait(float delay)
    {
        yield return new WaitForSeconds(delay);
        Mods.ForgetSpotifyInfo();
        PaintSpotifyHud();
    }

    public void PaintSpotifyHud()
    {
        Mods.SpotifyTrackInfo rawInfo = Mods.GetSpotifyTrackInfo();
        Mods.SpotifyTrackInfo info = GetStableSpotifyInfo(rawInfo);
        QueueSpotifySongText(info.Song, info.Artist);
        if (timeLabel != null) timeLabel.text = info.Duration;
        if (statusLabel != null) statusLabel.text = info.Status;
        if (hintLabel != null) hintLabel.text = info.HasTrack ? "Spotify desktop" : info.Artist;
        UpdateSpotifyPlaybackIcons(rawInfo, info);
        Texture2D albumTexture = GetNativeSpotifyCover(info) ?? GetWebCover(info);
        if (albumTexture == null) StartSpotifyAlbumArtworkDownload(info);
        UpdateSpotifyAlbumTransition(info, albumTexture);
        if (progressFill != null) progressFill.fillAmount = info.EndTime > 0f ? Mathf.Clamp01(info.ElapsedTime / info.EndTime) : 0f;
    }

    private void InitSpotifySongMarquee()
    {
        if (songLabel == null)
            return;

        songHomePos = songLabel.transform.localPosition;
        shownSong = "";
        longSongLine = "";
        songCrawlX = 0f;
        ApplySpotifySongMarqueeVisuals(1f);
    }

    private void QueueSpotifySongText(string song, string artist)
    {
        song = string.IsNullOrWhiteSpace(song) ? "No song" : song.Trim().Replace('\r', ' ').Replace('\n', ' ');
        artist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist.Trim().Replace('\r', ' ').Replace('\n', ' ');
        nextSongText = song;
        nextArtistText = artist;
        ApplySpotifyPendingText();
    }

    private void ApplySpotifyPendingText()
    {
        if (!string.IsNullOrEmpty(nextSongText))
            SetSpotifySongText(nextSongText);
        if (artistLabel != null && !string.IsNullOrEmpty(nextArtistText) && nextArtistText != shownArtist)
        {
            shownArtist = nextArtistText;
            artistLabel.text = shownArtist;
        }
    }
    private void SetSpotifySongText(string song)
    {
        if (songLabel == null)
            return;

        song = string.IsNullOrWhiteSpace(song) ? "No song" : song.Trim();
        song = song.Replace('\r', ' ').Replace('\n', ' ');
        if (song == shownSong)
            return;

        shownSong = song;
        longSongLine = string.Join(SongCrawlGap, Enumerable.Repeat(song, SongCrawlCopies));
        songLabel.text = longSongLine;
        songLabel.ForceMeshUpdate();
        songCrawlReset = Mathf.Max(0.01f, songLabel.textBounds.size.x / SongCrawlCopies);
        songCrawlX = 0f;
        ApplySpotifySongMarqueeVisuals(1f);
    }

    private void UpdateSpotifySongMarquee()
    {
        if (songLabel == null || string.IsNullOrEmpty(longSongLine))
            return;

        songBlinkClock += Time.deltaTime;
        float fadeOutStart = SongOnTime;
        float hiddenStart = fadeOutStart + SongFadeTime;
        float fadeInStart = hiddenStart + SongOffTime;
        float cycleEnd = fadeInStart + SongFadeTime;
        float alpha = 1f;

        if (songBlinkClock < fadeOutStart)
        {
            songCrawlX += Time.deltaTime * SongCrawlSpeed;
        }
        else if (songBlinkClock < hiddenStart)
        {
            songCrawlX += Time.deltaTime * SongCrawlSpeed;
            alpha = 1f - ((songBlinkClock - fadeOutStart) / SongFadeTime);
        }
        else if (songBlinkClock < fadeInStart)
        {
            songCrawlX = 0f;
            alpha = 0f;
        }
        else if (songBlinkClock < cycleEnd)
        {
            songCrawlX = 0f;
            alpha = (songBlinkClock - fadeInStart) / SongFadeTime;
        }
        else
        {
            songBlinkClock = 0f;
            songCrawlX = 0f;
            alpha = 1f;
        }

        if (songCrawlX >= songCrawlReset)
            songCrawlX -= songCrawlReset;
        ApplySpotifySongMarqueeVisuals(alpha);
    }

    private void ApplySpotifySongMarqueeVisuals(float alpha)
    {
        if (songLabel == null)
            return;

        Vector3 position = songHomePos;
        position.x += songCrawlX;
        position.y = SongY;
        position.z = SongZ;
        songLabel.transform.localPosition = position;
        Color color = songHomeColor;
        color.a = songHomeColor.a * Mathf.Clamp01(alpha);
        songLabel.color = color;
    }

    private Mods.SpotifyTrackInfo GetStableSpotifyInfo(Mods.SpotifyTrackInfo info)
    {
        if (info.HasTrack)
        {
            lastGoodSpotify = info;
            hasLastGoodSpotify = true;
            return info;
        }

        if (hasLastGoodSpotify)
        {
            string status = info.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase) ? "Paused" : lastGoodSpotify.Status;
            return new Mods.SpotifyTrackInfo(lastGoodSpotify.Song, lastGoodSpotify.Artist, lastGoodSpotify.Duration, status, true, lastGoodSpotify.StartTime, lastGoodSpotify.EndTime, lastGoodSpotify.ElapsedTime, lastGoodSpotify.ThumbnailBytes);
        }

        return info;
    }

    private void UpdateSpotifyAlbumTransition(Mods.SpotifyTrackInfo info, Texture2D albumTexture)
    {
        string key = GetSpotifyAlbumWebKey(info);
        if (string.IsNullOrEmpty(key))
            return;

        if (key == coverShowingKey)
        {
            if (coverShowingTexture == null && albumTexture != null)
            {
                coverShowingTexture = albumTexture;
                ApplySpotifyAlbumTexture(albumTexture);
                ApplySpotifyPendingText();
            SetSpotifyTransitionColor(Color.white);
            }
            return;
        }

        if (albumTexture == null)
            return;

        if (coverSwap != null && coverSwapTarget == key)
            return;

        if (coverSwap != null)
            StopCoroutine(coverSwap);
        coverSwapTarget = key;
        coverSwap = StartCoroutine(SpotifyAlbumTransition(key, albumTexture));
    }

    private IEnumerator SpotifyAlbumTransition(string newKey, Texture2D newTexture)
    {
        if (coverShowingTexture == null)
        {
            coverShowingKey = newKey;
            coverShowingTexture = newTexture;
            ApplySpotifyAlbumTexture(newTexture);
            ApplySpotifyPendingText();
            SetSpotifyTransitionColor(Color.white);
            coverSwapTarget = "";
            coverSwap = null;
            yield break;
        }

        float t = 0f;
        while (t < CoverFadeTime)
        {
            t += Time.deltaTime;
            SetSpotifyTransitionColor(Color.Lerp(Color.white, Color.black, Mathf.Clamp01(t / CoverFadeTime)));
            yield return null;
        }

        SetSpotifyTransitionColor(Color.black);
        yield return new WaitForSeconds(CoverBlackTime);

        coverShowingKey = newKey;
        coverShowingTexture = newTexture;
        ApplySpotifyAlbumTexture(newTexture);

        t = 0f;
        while (t < CoverFadeTime)
        {
            t += Time.deltaTime;
            SetSpotifyTransitionColor(Color.Lerp(Color.black, Color.white, Mathf.Clamp01(t / CoverFadeTime)));
            yield return null;
        }

        SetSpotifyTransitionColor(Color.white);
        coverSwapTarget = "";
        coverSwap = null;
    }

    private void ApplySpotifyAlbumTexture(Texture2D albumTexture)
    {
        if (albumTexture == null)
            return;

        if (coverRaw != null)
        {
            coverRaw.texture = albumTexture;
            coverRaw.enabled = true;
        }

        if (coverImage != null && coverRaw == null)
        {
            coverSprite = Sprite.Create(albumTexture, new Rect(0f, 0f, albumTexture.width, albumTexture.height), new Vector2(0.5f, 0.5f), 100f);
            coverImage.sprite = coverSprite;
            coverImage.overrideSprite = coverSprite;
            coverImage.type = Image.Type.Simple;
            coverImage.preserveAspect = true;
            coverImage.enabled = true;
        }

        if (coverMaterial != null)
        {
            coverMaterial.mainTexture = albumTexture;
            if (coverMaterial.HasProperty("_BaseMap")) coverMaterial.SetTexture("_BaseMap", albumTexture);
            if (coverMaterial.HasProperty("_MainTex")) coverMaterial.SetTexture("_MainTex", albumTexture);
        }
    }

    private void SetSpotifyTransitionColor(Color color)
    {
        if (coverRaw != null) coverRaw.color = color;
        if (coverImage != null && coverRaw == null) coverImage.color = color;
        if (coverMaterial != null) coverMaterial.color = color;

    }
    private Texture2D GetWebCover(Mods.SpotifyTrackInfo info)
    {
        string key = GetSpotifyAlbumWebKey(info);
        if (string.IsNullOrEmpty(key))
            return null;

        lock (coverWebLock)
        {
            if (coverTexture != null && coverTextureKey == key)
                return coverTexture;

            if (coverBytes == null || coverBytesKey != key)
                return null;

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(coverBytes, false))
                return null;

            coverTexture = texture;
            coverTextureKey = key;
            return coverTexture;
        }
    }

    private void StartSpotifyAlbumArtworkDownload(Mods.SpotifyTrackInfo info)
    {
        string key = GetSpotifyAlbumWebKey(info);
        if (string.IsNullOrEmpty(key))
            return;

        lock (coverWebLock)
        {
            if (coverWebBusy || coverWebAsked == key || coverBytesKey == key)
                return;
            coverWebBusy = true;
            coverWebAsked = key;
        }

        string song = info.Song;
        string artist = info.Artist;
        Task.Run(() => DownloadSpotifyAlbumArtwork(song, artist, key));
    }

    private void DownloadSpotifyAlbumArtwork(string song, string artist, string key)
    {
        try
        {
            string query = WebUtility.UrlEncode((song + " " + artist).Trim());
            string searchUrl = "https://itunes.apple.com/search?term=" + query + "&entity=song&limit=1";
            using WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "ThatUtilsPad/1.0";
            string json = client.DownloadString(searchUrl);
            string artworkUrl = (string)JObject.Parse(json)["results"]?.First?["artworkUrl100"];
            if (string.IsNullOrWhiteSpace(artworkUrl))
                return;

            artworkUrl = artworkUrl.Replace("100x100bb", "600x600bb");
            byte[] bytes = client.DownloadData(artworkUrl);
            if (bytes == null || bytes.Length == 0)
                return;


            lock (coverWebLock)
            {
                coverBytes = bytes;
                coverBytesKey = key;
                coverTexture = null;
                coverTextureKey = "";
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP SPOTIFY] Web album download failed: " + e.Message);
        }
        finally
        {
            lock (coverWebLock)
            {
                coverWebBusy = false;
            }
        }
    }

    private static string GetSpotifyAlbumWebKey(Mods.SpotifyTrackInfo info)
    {
        if (!info.HasTrack || string.IsNullOrWhiteSpace(info.Song) || info.Song.Equals("No song", StringComparison.OrdinalIgnoreCase))
            return "";
        return ((info.Song ?? "") + "|" + (info.Artist ?? "")).Trim().ToLowerInvariant();
    }
    private void UpdateSpotifyPlaybackIcons(Mods.SpotifyTrackInfo rawInfo, Mods.SpotifyTrackInfo stableInfo)
    {
        if (spotifyIconGuessOn)
        {
            if (Time.time < spotifyIconGuessUntil)
            {
                FlipSpotifyIcons(spotifyIconGuessPlaying);
                return;
            }

            spotifyIconGuessOn = false;
        }

        bool playing = rawInfo.Status.Equals("Playing", StringComparison.OrdinalIgnoreCase);
        FlipSpotifyIcons(playing);
    }
    private void FlipSpotifyIcons(bool playing)
    {
        if (playGlyph != null) playGlyph.gameObject.SetActive(!playing);
        if (pauseGlyph != null) pauseGlyph.gameObject.SetActive(playing);
    }
    private Texture2D GetNativeSpotifyCover(Mods.SpotifyTrackInfo info)
    {
        byte[] bytes = info.ThumbnailBytes;
        if (bytes == null || bytes.Length == 0)
            return null;

        int hash = HashSpotifyArtwork(bytes);
        if (coverHash == hash && coverByteLength == bytes.Length && nativeCoverTexture != null)
            return nativeCoverTexture;

        try
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!texture.LoadImage(bytes, false))
            {
                Destroy(texture);
                Debug.LogWarning("[TUP SPOTIFY] Album texture LoadImage failed. Bytes=" + bytes.Length);
                return null;
            }

            if (nativeCoverTexture != null && nativeCoverTexture != coverShowingTexture)
                Destroy(nativeCoverTexture);
            nativeCoverTexture = texture;
            coverHash = hash;
            coverByteLength = bytes.Length;
            return texture;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TUP SPOTIFY] Album image load failed: " + e.Message);
            return null;
        }
    }

    private static int HashSpotifyArtwork(byte[] bytes)
    {
        unchecked
        {
            int hash = (int)2166136261;
            for (int index = 0; index < bytes.Length; index++)
                hash = (hash ^ bytes[index]) * 16777619;
            return hash;
        }
    }
}
