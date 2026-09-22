#include "stdafx.h"

namespace
{
    // Basic type aliases
    using DeviceHandle = HMIDIIN;
    using DeviceID = uint32_t;

    // Utility functions for Win32/64 compatibility
#ifdef _WIN64
    DeviceID DeviceHandleToID(DeviceHandle handle)
    {
        return static_cast<DeviceID>(reinterpret_cast<uint64_t>(handle));
    }
    DeviceHandle DeviceIDToHandle(DeviceID id)
    {
        return reinterpret_cast<DeviceHandle>(static_cast<uint64_t>(id));
    }
#else
    DeviceID DeviceHandleToID(DeviceHandle handle)
    {
        return reinterpret_cast<DeviceID>(handle);
    }
    DeviceHandle DeviceIDToHandle(DeviceID id)
    {
        return reinterpret_cast<DeviceHandle>(id);
    }
#endif

    // MIDI message storage class
    class MidiMessage
    {
        DeviceID source_;
        uint8_t status_;
        uint8_t data1_;
        uint8_t data2_;
        uint64_t qpc_timestamp_;

    public:

        MidiMessage(DeviceID source, uint32_t rawData, uint64_t qpcTimestamp)
            : source_(source), status_(rawData), data1_(rawData >> 8),
              data2_(rawData >> 16), qpc_timestamp_(qpcTimestamp)
        {
        }

        uint64_t QpcTimestamp() const { return qpc_timestamp_; }

        uint64_t Encode64Bit()
        {
            uint64_t ul = source_;
            ul |= (uint64_t)status_ << 32;
            ul |= (uint64_t)data1_ << 40;
            ul |= (uint64_t)data2_ << 48;
            return ul;
        }

        std::string ToString()
        {
            char temp[256];
            std::snprintf(temp, sizeof(temp), "(%X) %02X %02X %02X", source_, status_, data1_, data2_);
            return temp;
        }
    };

    // Incoming MIDI message queue
    std::queue<MidiMessage> message_queue;
    uint64_t last_dequeued_qpc_timestamp = 0;

    // Device handler lists
    std::list<DeviceHandle> active_handles;
    std::map<DeviceHandle, unsigned int> handle_device_indices;
    std::set<unsigned int> active_device_indices;
    std::stack<DeviceHandle> handles_to_close;

    // Mutex for resources
    std::recursive_mutex resource_lock;

    // MIDI input callback
    static void CALLBACK MidiInProc(HMIDIIN hMidiIn, UINT wMsg, DWORD_PTR dwInstance, DWORD_PTR dwParam1, DWORD_PTR dwParam2)
    {
        if (wMsg == MIM_DATA)
        {
            DeviceID id = DeviceHandleToID(hMidiIn);
            uint32_t raw = static_cast<uint32_t>(dwParam1);
            LARGE_INTEGER qpc;
            QueryPerformanceCounter(&qpc);
            resource_lock.lock();
            message_queue.push(MidiMessage(id, raw, static_cast<uint64_t>(qpc.QuadPart)));
            resource_lock.unlock();
        }
        else if (wMsg == MIM_CLOSE)
        {
            resource_lock.lock();
            handles_to_close.push(hMidiIn);
            resource_lock.unlock();
        }
    }

    // Retrieve a name of a given device.
    std::string GetDeviceName(DeviceHandle handle)
    {
        auto casted_id = reinterpret_cast<UINT_PTR>(handle);
        MIDIINCAPS caps;
        if (midiInGetDevCaps(casted_id, &caps, sizeof(caps)) == MMSYSERR_NOERROR) {
            std::wstring name(caps.szPname);
            return std::string(name.begin(), name.end());
        }
        return "unknown";
    }

    // Open a MIDI device with a given index.
    void OpenDevice(unsigned int index)
    {
        // RefreshDevices is called from the polling path. Without tracking the
        // opened device indices, a driver that permits shared MIDI input would
        // be opened again every frame and report the same physical key press
        // through multiple handles.
        {
            std::lock_guard<std::recursive_mutex> lock(resource_lock);
            if (active_device_indices.count(index) != 0) return;
        }

        static const DWORD_PTR callback = reinterpret_cast<DWORD_PTR>(MidiInProc);
        DeviceHandle handle;
        if (midiInOpen(&handle, index, callback, NULL, CALLBACK_FUNCTION) == MMSYSERR_NOERROR)
        {
            if (midiInStart(handle) == MMSYSERR_NOERROR)
            {
                std::lock_guard<std::recursive_mutex> lock(resource_lock);
                active_handles.push_back(handle);
                handle_device_indices[handle] = index;
                active_device_indices.insert(index);
            }
            else
            {
                midiInClose(handle);
            }
        }
    }

    // Close a given handler.
    void CloseDevice(DeviceHandle handle)
    {
        midiInClose(handle);

        std::lock_guard<std::recursive_mutex> lock(resource_lock);
        active_handles.remove(handle);
        const auto index = handle_device_indices.find(handle);
        if (index != handle_device_indices.end())
        {
            active_device_indices.erase(index->second);
            handle_device_indices.erase(index);
        }
    }

    // Open the all devices.
    void OpenAllDevices()
    {
        int device_count = midiInGetNumDevs();
        for (int i = 0; i < device_count; i++) OpenDevice(i);
    }

    // Refresh device handlers
    void RefreshDevices()
    {
        resource_lock.lock();

        // Close disconnected handlers.
        while (!handles_to_close.empty()) {
            CloseDevice(handles_to_close.top());
            handles_to_close.pop();
        }

        // Try open all devices to detect newly connected ones.
        OpenAllDevices();

        resource_lock.unlock();
    }

    // Close the all devices.
    void CloseAllDevices()
    {
        resource_lock.lock();
        while (!active_handles.empty())
            CloseDevice(active_handles.front());
        resource_lock.unlock();
    }
}

// Exported functions

#define EXPORT_API extern "C" __declspec(dllexport)

// Counts the number of endpoints.
EXPORT_API int MidiJackCountEndpoints()
{
    return static_cast<int>(active_handles.size());
}

// Get the unique ID of an endpoint.
EXPORT_API uint32_t MidiJackGetEndpointIDAtIndex(int index)
{
    auto itr = active_handles.begin();
    std::advance(itr, index);
    return DeviceHandleToID(*itr);
}

// Get the name of an endpoint.
EXPORT_API const char* MidiJackGetEndpointName(uint32_t id)
{
    auto handle = DeviceIDToHandle(id);
    static std::string buffer;
    buffer = GetDeviceName(handle);
    return buffer.c_str();
}

// Retrieve and erase an MIDI message data from the message queue.
EXPORT_API uint64_t MidiJackDequeueIncomingData()
{
    RefreshDevices();

    resource_lock.lock();
    if (message_queue.empty())
    {
        resource_lock.unlock();
        return 0;
    }
    auto msg = message_queue.front();
    message_queue.pop();
    last_dequeued_qpc_timestamp = msg.QpcTimestamp();
    resource_lock.unlock();

    return msg.Encode64Bit();
}

// QPC timestamp captured inside the native MIDI callback for the message most
// recently returned by MidiJackDequeueIncomingData.
EXPORT_API uint64_t MidiJackGetLastDequeuedTimestampQpc()
{
    resource_lock.lock();
    auto timestamp = last_dequeued_qpc_timestamp;
    resource_lock.unlock();
    return timestamp;
}
