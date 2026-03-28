using UnityEngine;
using Unity.Profiling; // 引入 Profiler 命名空間
using Unity.Profiling.Memory; // 引入 Memory Profiler 命名空間

public class GCAllocDetector : MonoBehaviour
{
    // 設置一個分配量閾值（例如：超過 1 KB 就觸發快照）
    public long AllocThresholdBytes = 1 * 1024; // 1 KB
    
    // 創建一個計量器來讀取 GC 分配的總字節數
    // "GC.Alloc" 是 Unity Profiler 中分配量的標準標籤
    private ProfilerRecorder gcAllocatedInFrame;

    void OnEnable()
    {
        // 啟動計量器
        // ProfilerCategory.Memory 是 GC Alloc 的分類
        gcAllocatedInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 10); 
    }

    void OnDisable()
    {
        // 停用計量器
        gcAllocatedInFrame.Dispose();
    }

    void Update()
    {
        // 讀取本幀的 GC 分配量 (單位：Bytes)
        long currentAlloc = gcAllocatedInFrame.LastValue;

        if (currentAlloc > AllocThresholdBytes)
        {
            //Debug.LogWarning($"--- High GC Alloc Detected! Alloc: {currentAlloc / 1024f:F2} KB ---");
            
            // 呼叫觸發快照的函數（使用策略一中的 CaptureMemorySnapshot() 即可）
            // CaptureMemorySnapshot(); 
        }
    }
    
    // 請將策略一中的 CaptureMemorySnapshot() 函數也放在此腳本中
}