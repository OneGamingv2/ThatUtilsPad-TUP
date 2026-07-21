#pragma once

#include <cstdint>

#ifdef THATUTILSPADMEDIABRIDGE_EXPORTS
#define TUP_MEDIA_API extern "C" __declspec(dllexport)
#else
#define TUP_MEDIA_API extern "C" __declspec(dllimport)
#endif

enum TupMediaPlaybackStatus : std::int32_t
{
    TupMediaPlaybackStatusUnknown = 0,
    TupMediaPlaybackStatusClosed = 1,
    TupMediaPlaybackStatusOpened = 2,
    TupMediaPlaybackStatusChanging = 3,
    TupMediaPlaybackStatusStopped = 4,
    TupMediaPlaybackStatusPlaying = 5,
    TupMediaPlaybackStatusPaused = 6
};

struct TupMediaResult
{
    std::uint32_t abiVersion;
    std::uint32_t structSize;
    std::int32_t errorCode;
    std::int32_t playbackStatus;
    wchar_t* title;
    wchar_t* artist;
    wchar_t* source;
    wchar_t* errorMessage;
    double startSeconds;
    double endSeconds;
    double elapsedSeconds;
    std::uint8_t* thumbnailBytes;
    std::uint32_t thumbnailLength;
};

TUP_MEDIA_API std::int32_t __cdecl TUP_GetMediaSnapshot(TupMediaResult** result);
TUP_MEDIA_API void __cdecl TUP_FreeMediaResult(TupMediaResult* result);
TUP_MEDIA_API void __cdecl TUP_Shutdown();
