#ifndef NOSTALGIA_NOTE_ARC_CURVE_INCLUDED
#define NOSTALGIA_NOTE_ARC_CURVE_INCLUDED

// 音符落下的拋物線，給「沿著 Z 躺在跑道上」的幾何用（踏板音符、長押尾巴、
// 滑奏、顫音）。頂點要夠多，不然只有兩端被抬起來、中間是一條直線（弦，不是弧）。
//
// 曲線本身不在這裡算。本家那條 Y 曲線是**螢幕座標**，要先反解成世界 Y 才能用，
// 而那需要鏡頭——shader 裡沒有。所以 C# (NoteArcScreen) 每幀解好一張
// 「世界 Z → 該抬多高」的表，這裡查表就好：曲線只有一份，和音符本體逐字相同。
//
// 兩個值由**各自的 shader** 在自己的 cbuffer 裡宣告（要放在對的地方才吃得到
// MaterialPropertyBlock）：
//   _ArcJudgeZ   判定線的世界 Z
//   _ArcTravelZ  可視距離（世界單位）＝ 行進秒數 × 速度
// 表本身（_ArcOffsets / _ArcSampleCount）由 NoteArcScreen.ApplyTo 寫入。

// 曾經改成在 clip 座標上直接指定畫面高度，省下這張表——但那要假設 clip.y 的
// 正負向，而那個約定在不同繪製路徑下會翻轉，結果長押整根上下顛倒。
#define NOTE_ARC_MAX_SAMPLES 65
float _ArcOffsets[NOTE_ARC_MAX_SAMPLES];
float _ArcLateral[NOTE_ARC_MAX_SAMPLES];
float _ArcSampleCount;
// 這個物件的 Y 已經被抬高了多少。長押尾巴掛在音符底下，而音符的 Y 是 C# 照同一
// 張表擺好的——不扣掉就等於抬兩次（整根往上飄，離音符愈遠飄愈多）。
float _ArcAnchorY;

// 母體現在的世界 X，以及它沒被補正過的原始 X。
//
// 長條是掛在音符底下的，頂點的世界 X = 母體的 X ＋ 自己相對母體的偏移。母體的 X
// 已經乘過補正，偏移沒有——所以不能拿整個 X 一起乘或一起除：
//   整個乘  → 母體被乘兩次，長條飄離鍵道
//   整個除  → 寬度被連帶除掉，長條隨著母體的位置忽胖忽瘦
// 先用這兩個值把偏移拆出來，再把「原始位置 ＋ 偏移」整體乘一次補正才對。
float _ArcAnchorX;
float _ArcBaseX;

// 淡入：生成的地方全透明，走到頂點全不透明。
float _ArcFadeStart;
float _ArcFadeEnd;

float NoteArcFade(float worldZ, float judgeZ)
{
    if (_ArcFadeEnd <= _ArcFadeStart + 0.001) return 1.0;
    float d = worldZ - judgeZ;
    return saturate(1.0 - (d - _ArcFadeStart) / (_ArcFadeEnd - _ArcFadeStart));
}

// 查表的位置：回傳 (索引, 內插比例)。兩張表共用。
float2 NoteArcLookup(float worldZ, float judgeZ, float travelZ)
{
    float t = saturate((worldZ - judgeZ) / travelZ) * (_ArcSampleCount - 1);
    float i = clamp(floor(t), 0.0, _ArcSampleCount - 2.0);
    return float2(i, saturate(t - i));
}

float NoteArcOffsetFromTable(float worldZ, float judgeZ, float travelZ)
{
    if (_ArcSampleCount < 2.0 || travelZ <= 0.0) return 0.0;
    float2 l = NoteArcLookup(worldZ, judgeZ, travelZ);
    int i = (int)l.x;
    return lerp(_ArcOffsets[i], _ArcOffsets[i + 1], l.y) - _ArcAnchorY;
}

// 世界 X 要乘多少。垂直修正會把點推遠、在畫面上往中間縮，這裡乘回去。
float NoteArcLateralFromTable(float worldZ, float judgeZ, float travelZ)
{
    if (_ArcSampleCount < 2.0 || travelZ <= 0.0) return 1.0;
    float2 l = NoteArcLookup(worldZ, judgeZ, travelZ);
    int i = (int)l.x;
    return lerp(_ArcLateral[i], _ArcLateral[i + 1], l.y);
}

// 把一個頂點的世界 X 映射到補正後的位置。母體沒動過（踏板那種自己就在原點的
// 幾何）時 _ArcAnchorX 和 _ArcBaseX 都是 0，式子退化成單純的乘法。
float NoteArcMapX(float worldX, float worldZ, float judgeZ, float travelZ)
{
    if (_ArcSampleCount < 2.0 || travelZ <= 0.0) return worldX;
    float local = worldX - _ArcAnchorX;              // 相對母體的偏移，沒被補正過
    return (_ArcBaseX + local) *
           NoteArcLateralFromTable(worldZ, judgeZ, travelZ);
}

#endif
