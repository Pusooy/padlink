using System;
using System.Collections.Generic;
using System.Text;

namespace PadLink.App.Services;

/// <summary>
/// 轻量二维码编码器（Byte 模式 / 纠错等级 M / 版本 1-6），无第三方依赖。
/// 仅用于把本机连接地址渲染成手机可扫描的二维码，超长文本返回 null。
/// 实现遵循 ISO/IEC 18004：数据编码 → Reed-Solomon 纠错 → 交织 → 矩阵排布 → 掩码择优。
/// </summary>
public static class QrEncoder
{
    /// <summary>版本 1-6 在纠错等级 M 下的数据码字数。</summary>
    private static readonly int[] DataCodewordsM = { 0, 16, 28, 44, 64, 86, 108 };

    /// <summary>版本 1-6 在纠错等级 M 下的分块参数：{块数, 每块总码字数}（各块等长）。</summary>
    private static readonly int[][] BlocksM =
    {
        Array.Empty<int>(),
        new[] { 1, 26 },   // 16 数据 + 10 纠错
        new[] { 1, 44 },   // 28 + 16
        new[] { 1, 70 },   // 44 + 26
        new[] { 2, 50 },   // 2 × (32 + 18)
        new[] { 2, 67 },   // 2 × (43 + 24)
        new[] { 4, 43 },   // 4 × (27 + 16)
    };

    /// <summary>版本 1-6 的对齐图案中心坐标表。</summary>
    private static readonly int[][] AlignmentPositions =
    {
        Array.Empty<int>(),
        Array.Empty<int>(),
        new[] { 6, 18 },
        new[] { 6, 22 },
        new[] { 6, 26 },
        new[] { 6, 30 },
        new[] { 6, 34 },
    };

    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];

    static QrEncoder()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfLog[x] = (byte)i;
            x <<= 1;
            if (x >= 0x100) x ^= 0x11D;   // GF(256) 本原多项式 x^8+x^4+x^3+x^2+1
        }
        for (int i = 255; i < GfExp.Length; i++) GfExp[i] = GfExp[i - 255];
    }

    /// <summary>
    /// 编码文本为二维码矩阵，true 表示深色模块。返回值不含静区（quiet zone）。
    /// <paramref name="maskPattern"/> 传 -1 时自动选择最优掩码，0-7 则指定掩码。
    /// </summary>
    public static bool[,]? Encode(string text, int maskPattern = -1)
    {
        if (string.IsNullOrEmpty(text)) return null;

        byte[] payload = Encoding.UTF8.GetBytes(text);

        // 选能容纳数据的最小版本（版本 1-9 的 Byte 模式长度字段固定 8 位）
        int version = 0;
        for (int v = 1; v < DataCodewordsM.Length; v++)
        {
            if (4 + 8 + payload.Length * 8 <= DataCodewordsM[v] * 8)
            {
                version = v;
                break;
            }
        }
        if (version == 0) return null;

        byte[] codewords = InterleaveWithEcc(BuildDataCodewords(payload, version), version);

        if (maskPattern >= 0 && maskPattern <= 7)
            return BuildMatrix(version, codewords, maskPattern);

        bool[,]? best = null;
        int bestScore = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            var matrix = BuildMatrix(version, codewords, mask);
            int score = Score(matrix);
            if (score < bestScore)
            {
                bestScore = score;
                best = matrix;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- 数据编码

    private static byte[] BuildDataCodewords(byte[] payload, int version)
    {
        int capacityBits = DataCodewordsM[version] * 8;
        var bits = new List<bool>(capacityBits + 16);

        void Put(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        Put(0b0100, 4);                 // Byte 模式
        Put(payload.Length, 8);         // 长度（版本 1-9 为 8 位）
        foreach (byte b in payload) Put(b, 8);

        Put(0, Math.Min(4, capacityBits - bits.Count));     // 终止符
        while (bits.Count % 8 != 0) bits.Add(false);        // 补齐到字节

        for (int pad = 0; bits.Count < capacityBits; pad++) // 填充码字 0xEC / 0x11
            Put(pad % 2 == 0 ? 0xEC : 0x11, 8);

        var data = new byte[DataCodewordsM[version]];
        for (int i = 0; i < data.Length; i++)
        {
            int value = 0;
            for (int b = 0; b < 8; b++) value = (value << 1) | (bits[i * 8 + b] ? 1 : 0);
            data[i] = (byte)value;
        }
        return data;
    }

    private static byte[] InterleaveWithEcc(byte[] data, int version)
    {
        int blocks = BlocksM[version][0];
        int totalPerBlock = BlocksM[version][1];
        int dataPerBlock = DataCodewordsM[version] / blocks;
        int ecPerBlock = totalPerBlock - dataPerBlock;

        var dataBlocks = new byte[blocks][];
        var ecBlocks = new byte[blocks][];

        for (int b = 0; b < blocks; b++)
        {
            dataBlocks[b] = new byte[dataPerBlock];
            Array.Copy(data, b * dataPerBlock, dataBlocks[b], 0, dataPerBlock);
            ecBlocks[b] = ComputeRemainder(dataBlocks[b], ecPerBlock);
        }

        var result = new byte[blocks * totalPerBlock];
        int k = 0;
        for (int i = 0; i < dataPerBlock; i++)
            for (int b = 0; b < blocks; b++) result[k++] = dataBlocks[b][i];
        for (int i = 0; i < ecPerBlock; i++)
            for (int b = 0; b < blocks; b++) result[k++] = ecBlocks[b][i];
        return result;
    }

    /// <summary>Reed-Solomon 余式：返回 ecCount 个纠错码字。</summary>
    private static byte[] ComputeRemainder(byte[] data, int ecCount)
    {
        byte[] generator = GetGenerator(ecCount);
        var remainder = new byte[ecCount];

        foreach (byte b in data)
        {
            int factor = b ^ remainder[0];
            Array.Copy(remainder, 1, remainder, 0, ecCount - 1);
            remainder[ecCount - 1] = 0;
            if (factor == 0) continue;
            for (int i = 0; i < ecCount; i++)
                remainder[i] ^= GfMul(generator[i + 1], (byte)factor);
        }
        return remainder;
    }

    private static readonly Dictionary<int, byte[]> GeneratorCache = new();

    private static byte[] GetGenerator(int degree)
    {
        lock (GeneratorCache)
        {
            if (GeneratorCache.TryGetValue(degree, out var cached)) return cached;

            var gen = new byte[] { 1 };
            for (int i = 0; i < degree; i++)
            {
                byte root = GfExp[i];
                var next = new byte[gen.Length + 1];
                next[0] = gen[0];
                for (int k = 1; k < gen.Length; k++)
                    next[k] = (byte)(gen[k] ^ GfMul(gen[k - 1], root));
                next[gen.Length] = GfMul(gen[gen.Length - 1], root);
                gen = next;
            }

            GeneratorCache[degree] = gen;
            return gen;
        }
    }

    private static byte GfMul(byte a, byte b)
        => a == 0 || b == 0 ? (byte)0 : GfExp[GfLog[a] + GfLog[b]];

    // ---------------------------------------------------------------- 矩阵排布

    private static bool[,] BuildMatrix(int version, byte[] codewords, int mask)
    {
        int size = version * 4 + 17;
        var modules = new bool[size, size];
        var reserved = new bool[size, size];

        PlaceFinder(modules, reserved, 0, 0);
        PlaceFinder(modules, reserved, size - 7, 0);
        PlaceFinder(modules, reserved, 0, size - 7);

        PlaceAlignment(modules, reserved, version);
        PlaceTiming(modules, reserved);
        ReserveFormatAreas(reserved);

        PlaceData(modules, reserved, codewords, mask);
        WriteFormatInfo(modules, mask);
        return modules;
    }

    private static void PlaceFinder(bool[,] m, bool[,] reserved, int row, int col)
    {
        int size = m.GetLength(0);
        for (int dr = -1; dr <= 7; dr++)
        {
            int r = row + dr;
            if (r < 0 || r >= size) continue;
            for (int dc = -1; dc <= 7; dc++)
            {
                int c = col + dc;
                if (c < 0 || c >= size) continue;

                bool dark =
                    (dr >= 0 && dr <= 6 && (dc == 0 || dc == 6)) ||
                    (dc >= 0 && dc <= 6 && (dr == 0 || dr == 6)) ||
                    (dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4);

                m[r, c] = dark;
                reserved[r, c] = true;
            }
        }
    }

    private static void PlaceAlignment(bool[,] m, bool[,] reserved, int version)
    {
        int[] positions = AlignmentPositions[version];
        if (positions.Length == 0) return;

        foreach (int row in positions)
        {
            foreach (int col in positions)
            {
                if (reserved[row, col]) continue;   // 与探测图形重叠时跳过

                for (int dr = -2; dr <= 2; dr++)
                {
                    for (int dc = -2; dc <= 2; dc++)
                    {
                        bool dark = Math.Abs(dr) == 2 || Math.Abs(dc) == 2 || (dr == 0 && dc == 0);
                        m[row + dr, col + dc] = dark;
                        reserved[row + dr, col + dc] = true;
                    }
                }
            }
        }
    }

    private static void PlaceTiming(bool[,] m, bool[,] reserved)
    {
        int size = m.GetLength(0);
        for (int i = 8; i < size - 8; i++)
        {
            if (!reserved[6, i])
            {
                m[6, i] = i % 2 == 0;
                reserved[6, i] = true;
            }
            if (!reserved[i, 6])
            {
                m[i, 6] = i % 2 == 0;
                reserved[i, 6] = true;
            }
        }
    }

    /// <summary>预留格式信息与暗模块的位置，避免数据占据。</summary>
    private static void ReserveFormatAreas(bool[,] reserved)
    {
        int size = reserved.GetLength(0);
        for (int i = 0; i <= 8; i++)
        {
            reserved[8, i] = true;
            reserved[i, 8] = true;
        }
        for (int i = 0; i < 8; i++)
        {
            reserved[8, size - 1 - i] = true;
        }
        for (int i = 0; i < 7; i++)
        {
            reserved[size - 1 - i, 8] = true;
        }
        reserved[size - 8, 8] = true;   // 固定暗模块
    }

    private static void PlaceData(bool[,] m, bool[,] reserved, byte[] codewords, int mask)
    {
        int size = m.GetLength(0);
        int bitIndex = 0;
        int totalBits = codewords.Length * 8;
        bool upward = true;

        for (int col = size - 1; col > 0; col -= 2)
        {
            if (col == 6) col = 5;   // 跳过竖向定时图案所在的第 6 列

            for (int i = 0; i < size; i++)
            {
                int row = upward ? size - 1 - i : i;
                for (int k = 0; k < 2; k++)
                {
                    int c = col - k;
                    if (reserved[row, c]) continue;

                    bool dark = false;
                    if (bitIndex < totalBits)
                    {
                        int value = codewords[bitIndex >> 3];
                        dark = ((value >> (7 - (bitIndex & 7))) & 1) != 0;
                        bitIndex++;
                    }
                    m[row, c] = dark ^ MaskBit(mask, row, c);
                }
            }
            upward = !upward;
        }
    }

    private static bool MaskBit(int mask, int row, int col) => mask switch
    {
        0 => (row + col) % 2 == 0,
        1 => row % 2 == 0,
        2 => col % 3 == 0,
        3 => (row + col) % 3 == 0,
        4 => (row / 2 + col / 3) % 2 == 0,
        5 => (row * col) % 2 + (row * col) % 3 == 0,
        6 => ((row * col) % 2 + (row * col) % 3) % 2 == 0,
        _ => ((row * col) % 3 + (row + col) % 2) % 2 == 0,
    };

    private static void WriteFormatInfo(bool[,] m, int mask)
    {
        int size = m.GetLength(0);

        // 纠错等级 M 的格式位为 00，低 3 位为掩码号
        int data = mask;
        int remainder = data << 10;
        for (int i = 14; i >= 10; i--)
        {
            if (((remainder >> i) & 1) != 0) remainder ^= 0x537 << (i - 10);
        }
        int bits = ((data << 10) | remainder) ^ 0x5412;

        for (int i = 0; i < 15; i++)
        {
            bool dark = ((bits >> i) & 1) != 0;

            // 左上竖排（跳过第 6 行）+ 左下副本
            if (i < 6) m[i, 8] = dark;
            else if (i < 8) m[i + 1, 8] = dark;
            else m[size - 15 + i, 8] = dark;

            // 左上横排（跳过第 6 列）+ 右上副本
            if (i < 8) m[8, size - 1 - i] = dark;
            else if (i < 9) m[8, 7] = dark;
            else m[8, 14 - i] = dark;
        }

        m[size - 8, 8] = true;   // 固定暗模块
    }

    // ---------------------------------------------------------------- 掩码评分

    /// <summary>按 ISO/IEC 18004 的四条罚分规则评估掩码质量，分数越低越好。</summary>
    private static int Score(bool[,] m)
    {
        int size = m.GetLength(0);
        int penalty = 0;

        // 规则 1：行/列连续 5 个及以上同色模块
        for (int i = 0; i < size; i++)
        {
            for (int dir = 0; dir < 2; dir++)
            {
                int run = 1;
                for (int j = 1; j < size; j++)
                {
                    bool cur = dir == 0 ? m[i, j] : m[j, i];
                    bool prev = dir == 0 ? m[i, j - 1] : m[j - 1, i];
                    if (cur == prev)
                    {
                        run++;
                        if (run == 5) penalty += 3;
                        else if (run > 5) penalty += 1;
                    }
                    else run = 1;
                }
            }
        }

        // 规则 2：2×2 同色块
        for (int r = 0; r < size - 1; r++)
        {
            for (int c = 0; c < size - 1; c++)
            {
                bool v = m[r, c];
                if (v == m[r, c + 1] && v == m[r + 1, c] && v == m[r + 1, c + 1]) penalty += 3;
            }
        }

        // 规则 3：1:1:3:1:1 类探测图案（含一侧 4 个浅色模块）
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j + 10 < size; j++)
            {
                if (IsFinderLike(m, i, j, true)) penalty += 40;
                if (IsFinderLike(m, i, j, false)) penalty += 40;
            }
        }

        // 规则 4：深色模块比例偏离 50%
        int darkCount = 0;
        foreach (bool v in m) if (v) darkCount++;
        int percent = darkCount * 100 / (size * size);
        penalty += Math.Abs(percent - 50) / 5 * 10;

        return penalty;
    }

    private static bool IsFinderLike(bool[,] m, int row, int col, bool horizontal)
    {
        // 0000 1011101  或  1011101 0000
        ReadOnlySpan<bool> patternA = stackalloc bool[] { false, false, false, false, true, false, true, true, true, false, true };
        ReadOnlySpan<bool> patternB = stackalloc bool[] { true, false, true, true, true, false, true, false, false, false, false };

        for (int p = 0; p < 11; p++)
        {
            bool v = horizontal ? m[row, col + p] : m[col + p, row];
            if (v != patternA[p]) goto TryB;
        }
        return true;

    TryB:
        for (int p = 0; p < 11; p++)
        {
            bool v = horizontal ? m[row, col + p] : m[col + p, row];
            if (v != patternB[p]) return false;
        }
        return true;
    }
}
