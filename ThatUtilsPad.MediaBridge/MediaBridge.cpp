#include "MediaBridge.h"

#include <windows.h>
#include <combaseapi.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Media.Control.h>
#include <winrt/Windows.Storage.Streams.h>

#include <algorithm>
#include <condition_variable>
#include <cwctype>
#include <deque>
#include <functional>
#include <future>
#include <limits>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <utility>
#include <vector>

using namespace winrt;
using namespace Windows::Media::Control;
using namespace Windows::Storage::Streams;

namespace
{
    constexpr std::uint32_t AbiVersion = 1;
    constexpr std::uint64_t MaximumThumbnailBytes = 32ull * 1024ull * 1024ull;

    struct Snapshot
    {
        std::wstring title;
        std::wstring artist;
        std::wstring source;
        std::wstring errorMessage;
        std::int32_t errorCode = 0;
        std::int32_t playbackStatus = TupMediaPlaybackStatusUnknown;
        double startSeconds = 0;
        double endSeconds = 0;
        double elapsedSeconds = 0;
        std::vector<std::uint8_t> thumbnail;
    };

    wchar_t* CopyString(std::wstring const& value)
    {
        if (value.empty())
        {
            return nullptr;
        }

        auto const bytes = (value.size() + 1) * sizeof(wchar_t);
        auto* copy = static_cast<wchar_t*>(CoTaskMemAlloc(bytes));
        if (!copy)
        {
            throw std::bad_alloc();
        }

        memcpy(copy, value.c_str(), bytes);
        return copy;
    }

    void FreeResult(TupMediaResult* result) noexcept
    {
        if (!result)
        {
            return;
        }

        CoTaskMemFree(result->title);
        CoTaskMemFree(result->artist);
        CoTaskMemFree(result->source);
        CoTaskMemFree(result->errorMessage);
        CoTaskMemFree(result->thumbnailBytes);
        CoTaskMemFree(result);
    }

    TupMediaResult* MakeResult(Snapshot const& snapshot)
    {
        auto* result = static_cast<TupMediaResult*>(CoTaskMemAlloc(sizeof(TupMediaResult)));
        if (!result)
        {
            throw std::bad_alloc();
        }
        ZeroMemory(result, sizeof(TupMediaResult));

        try
        {
            result->abiVersion = AbiVersion;
            result->structSize = sizeof(TupMediaResult);
            result->errorCode = snapshot.errorCode;
            result->playbackStatus = snapshot.playbackStatus;
            result->title = CopyString(snapshot.title);
            result->artist = CopyString(snapshot.artist);
            result->source = CopyString(snapshot.source);
            result->errorMessage = CopyString(snapshot.errorMessage);
            result->startSeconds = snapshot.startSeconds;
            result->endSeconds = snapshot.endSeconds;
            result->elapsedSeconds = snapshot.elapsedSeconds;

            if (!snapshot.thumbnail.empty())
            {
                result->thumbnailLength = static_cast<std::uint32_t>(snapshot.thumbnail.size());
                result->thumbnailBytes = static_cast<std::uint8_t*>(CoTaskMemAlloc(result->thumbnailLength));
                if (!result->thumbnailBytes)
                {
                    throw std::bad_alloc();
                }
                memcpy(result->thumbnailBytes, snapshot.thumbnail.data(), result->thumbnailLength);
            }
            return result;
        }
        catch (...)
        {
            FreeResult(result);
            throw;
        }
    }

    std::wstring Lower(std::wstring value)
    {
        std::transform(value.begin(), value.end(), value.begin(),
            [](wchar_t character) { return static_cast<wchar_t>(std::towlower(character)); });
        return value;
    }

    bool IsUsefulTitle(std::wstring const& title)
    {
        auto const first = title.find_first_not_of(L" \t\r\n");
        if (first == std::wstring::npos)
        {
            return false;
        }

        auto const last = title.find_last_not_of(L" \t\r\n");
        auto const lower = Lower(title.substr(first, last - first + 1));
        return lower != L"unknown" &&
            lower != L"msctfime ui" &&
            lower != L"spotify" &&
            lower != L"spotify premium" &&
            lower != L"spotify free" &&
            lower.find(L"window (spotify.exe)") == std::wstring::npos &&
            lower.find(L"spotify.exe") == std::wstring::npos &&
            lower.rfind(L"gdi+", 0) != 0;
    }

    std::int32_t MapStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        switch (status)
        {
        case GlobalSystemMediaTransportControlsSessionPlaybackStatus::Closed:
            return TupMediaPlaybackStatusClosed;
        case GlobalSystemMediaTransportControlsSessionPlaybackStatus::Opened:
            return TupMediaPlaybackStatusOpened;
        case GlobalSystemMediaTransportControlsSessionPlaybackStatus::Changing:
            return TupMediaPlaybackStatusChanging;
        case GlobalSystemMediaTransportControlsSessionPlaybackStatus::Stopped:
            return TupMediaPlaybackStatusStopped;
        case GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing:
            return TupMediaPlaybackStatusPlaying;
        case GlobalSystemMediaTransportControlsSessionPlaybackStatus::Paused:
            return TupMediaPlaybackStatusPaused;
        default:
            return TupMediaPlaybackStatusUnknown;
        }
    }

    double Seconds(Windows::Foundation::TimeSpan const& value)
    {
        return static_cast<double>(value.count()) / 10'000'000.0;
    }

    struct Candidate
    {
        GlobalSystemMediaTransportControlsSession session{ nullptr };
        GlobalSystemMediaTransportControlsSessionMediaProperties media{ nullptr };
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playback{ nullptr };
        int score = -1;
    };

    class MediaWorker
    {
    public:
        Snapshot Get()
        {
            EnsureStarted();
            auto work = std::make_shared<std::packaged_task<Snapshot()>>([this] { return ReadSnapshot(); });
            auto future = work->get_future();
            {
                std::lock_guard lock(_mutex);
                if (_stopping)
                {
                    Snapshot stopped;
                    stopped.errorCode = HRESULT_FROM_WIN32(ERROR_OPERATION_ABORTED);
                    stopped.errorMessage = L"Media bridge is shutting down.";
                    return stopped;
                }
                _queue.emplace_back([work] { (*work)(); });
            }
            _wake.notify_one();
            return future.get();
        }

        void Shutdown() noexcept
        {
            std::thread worker;
            {
                std::lock_guard lock(_mutex);
                if (!_thread.joinable())
                {
                    return;
                }
                _stopping = true;
                worker = std::move(_thread);
            }
            _wake.notify_all();
            worker.join();
            {
                std::lock_guard lock(_mutex);
                _stopping = false;
                _queue.clear();
            }
        }

        ~MediaWorker()
        {
            Shutdown();
        }

    private:
        void EnsureStarted()
        {
            std::lock_guard lock(_mutex);
            if (!_thread.joinable())
            {
                _stopping = false;
                _startupError = 0;
                _startupErrorMessage.clear();
                _thread = std::thread([this] { Run(); });
            }
        }

        void Run() noexcept
        {
            bool apartmentInitialized = false;
            try
            {
                init_apartment(apartment_type::multi_threaded);
                apartmentInitialized = true;
            }
            catch (hresult_error const& error)
            {
                _startupError = error.code().value;
                _startupErrorMessage = error.message().c_str();
            }
            catch (...)
            {
                _startupError = E_UNEXPECTED;
                _startupErrorMessage = L"Failed to initialize the media worker apartment.";
            }

            for (;;)
            {
                std::function<void()> work;
                {
                    std::unique_lock lock(_mutex);
                    _wake.wait(lock, [this] { return _stopping || !_queue.empty(); });
                    if (_stopping && _queue.empty())
                    {
                        break;
                    }
                    work = std::move(_queue.front());
                    _queue.pop_front();
                }
                work();
            }

            _manager = nullptr;
            if (apartmentInitialized)
            {
                uninit_apartment();
            }
        }

        Snapshot ReadSnapshot() noexcept
        {
            if (_startupError < 0)
            {
                Snapshot result;
                result.errorCode = _startupError;
                result.errorMessage = _startupErrorMessage;
                return result;
            }

            try
            {
                if (!_manager)
                {
                    _manager = GlobalSystemMediaTransportControlsSessionManager::RequestAsync().get();
                }

                Candidate best;
                for (auto const& session : _manager.GetSessions())
                {
                    try
                    {
                        auto media = session.TryGetMediaPropertiesAsync().get();
                        auto playback = session.GetPlaybackInfo();
                        auto const title = std::wstring(media.Title());
                        auto const source = std::wstring(session.SourceAppUserModelId());
                        bool const useful = IsUsefulTitle(title);
                        bool const spotify = Lower(source).find(L"spotify") != std::wstring::npos;
                        bool const playing = playback.PlaybackStatus() ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing;
                        int const score = useful && (spotify || playing) ? (spotify ? 100 : 10) : -1;
                        if (score > best.score)
                        {
                            best.session = session;
                            best.media = media;
                            best.playback = playback;
                            best.score = score;
                        }
                    }
                    catch (...)
                    {
                    }
                }

                Snapshot result;
                if (best.score < 0)
                {
                    return result;
                }

                result.title = best.media.Title().c_str();
                result.artist = best.media.Artist().c_str();
                result.source = best.session.SourceAppUserModelId().c_str();
                result.playbackStatus = MapStatus(best.playback.PlaybackStatus());

                auto const timeline = best.session.GetTimelineProperties();
                result.startSeconds = Seconds(timeline.StartTime());
                result.endSeconds = Seconds(timeline.EndTime());
                result.elapsedSeconds = Seconds(timeline.Position());

                auto const thumbnail = best.media.Thumbnail();
                if (thumbnail)
                {
                    auto stream = thumbnail.OpenReadAsync().get();
                    auto const size = stream.Size();
                    if (size > 0 && size <= MaximumThumbnailBytes &&
                        size <= std::numeric_limits<std::uint32_t>::max())
                    {
                        DataReader reader(stream.GetInputStreamAt(0));
                        auto const length = static_cast<std::uint32_t>(size);
                        auto const loaded = reader.LoadAsync(length).get();
                        result.thumbnail.resize(loaded);
                        reader.ReadBytes(result.thumbnail);
                    }
                }
                return result;
            }
            catch (hresult_error const& error)
            {
                Snapshot result;
                result.errorCode = error.code().value;
                result.errorMessage = error.message().c_str();
                _manager = nullptr;
                return result;
            }
            catch (std::exception const& error)
            {
                Snapshot result;
                result.errorCode = E_FAIL;
                std::string const message(error.what());
                result.errorMessage.assign(message.begin(), message.end());
                return result;
            }
            catch (...)
            {
                Snapshot result;
                result.errorCode = E_UNEXPECTED;
                result.errorMessage = L"Unexpected native media bridge error.";
                return result;
            }
        }

        std::mutex _mutex;
        std::condition_variable _wake;
        std::deque<std::function<void()>> _queue;
        std::thread _thread;
        bool _stopping = false;
        std::int32_t _startupError = 0;
        std::wstring _startupErrorMessage;
        GlobalSystemMediaTransportControlsSessionManager _manager{ nullptr };
    };

    MediaWorker g_worker;
    std::mutex g_apiMutex;
}

std::int32_t __cdecl TUP_GetMediaSnapshot(TupMediaResult** result)
{
    if (!result)
    {
        return E_POINTER;
    }
    *result = nullptr;

    try
    {
        std::lock_guard lock(g_apiMutex);
        auto snapshot = g_worker.Get();
        *result = MakeResult(snapshot);
        return S_OK;
    }
    catch (hresult_error const& error)
    {
        return error.code().value;
    }
    catch (std::bad_alloc const&)
    {
        return E_OUTOFMEMORY;
    }
    catch (...)
    {
        return E_UNEXPECTED;
    }
}

void __cdecl TUP_FreeMediaResult(TupMediaResult* result)
{
    try
    {
        FreeResult(result);
    }
    catch (...)
    {
    }
}

void __cdecl TUP_Shutdown()
{
    try
    {
        std::lock_guard lock(g_apiMutex);
        g_worker.Shutdown();
    }
    catch (...)
    {
    }
}
