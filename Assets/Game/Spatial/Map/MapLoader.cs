using System;
using System.IO;
using UnityEngine;
using Game.Scripts.Fixed;

namespace Game.Map
{
    /// <summary>
    /// MapData 包含地图格子对应的 MapLayer 和每个格子中心点的高度值。
    /// Layers 和 Heights 的索引顺序为 [row, column] = [y, x]，
    /// width/height 分别表示列数和行数。
    /// </summary>
    [Serializable]
    public struct MapData
    {
        public int width;
        public int height;
        public MapLayer[,] Layers;
        public Fixed[,] Heights;

        public MapData(int width, int height)
        {
            this.width = width;
            this.height = height;
            Layers = new MapLayer[height, width];
            Heights = new Fixed[height, width];
        }

        public MapLayer GetLayer(int x, int y) => Layers[y, x];
        public Fixed GetHeight(int x, int y) => Heights[y, x];
    }

    /// <summary>
    /// MapLoader 提供从 CSV 文本 / TextAsset / 磁盘文件加载 MapData 的方法。
    /// - 层文件（layer csv）可以使用 MapLayer 枚举名称或数值（uint）。
    /// - 高度文件（height csv）使用浮点数（例如: 0.0, 1.5, ...）。
    /// 两个文件必须具有相同的行列尺寸。
    /// </summary>
    public static class MapLoader
    {
        /// <summary>
        /// 从两个 TextAsset（CSV 格式）加载 MapData。
        /// </summary>
        public static MapData LoadFromTextAssets(TextAsset layerAsset, TextAsset heightAsset)
        {
            if (layerAsset == null) throw new ArgumentNullException(nameof(layerAsset));
            if (heightAsset == null) throw new ArgumentNullException(nameof(heightAsset));
            return LoadFromCsvStrings(layerAsset.text, heightAsset.text);
        }

        /// <summary>
        /// 从磁盘文件读取（CSV 文本），并解析为 MapData。
        /// </summary>
        public static MapData LoadFromFiles(string layerFilePath, string heightFilePath)
        {
            if (!File.Exists(layerFilePath)) throw new FileNotFoundException("layer file not found", layerFilePath);
            if (!File.Exists(heightFilePath)) throw new FileNotFoundException("height file not found", heightFilePath);
            var layerCsv = File.ReadAllText(layerFilePath);
            var heightCsv = File.ReadAllText(heightFilePath);
            return LoadFromCsvStrings(layerCsv, heightCsv);
        }

        /// <summary>
        /// 从两段 CSV 文本解析 MapData。每行代表一行格子，逗号分隔。会忽略空行。
        /// </summary>
        public static MapData LoadFromCsvStrings(string layerCsv, string heightCsv)
        {
            var layerRows = ParseCsvToRows(layerCsv);
            var heightRows = ParseCsvToRows(heightCsv);

            if (layerRows.Length == 0) throw new ArgumentException("layer CSV is empty");

            if (layerRows.Length != heightRows.Length || layerRows[0].Length != heightRows[0].Length)
                throw new ArgumentException("Layer and height CSV sizes do not match");

            int rows = layerRows.Length;
            int cols = layerRows[0].Length;
            var data = new MapData(cols, rows);

            for (int y = 0; y < rows; y++)
            {
                if (layerRows[y].Length != cols || heightRows[y].Length != cols)
                    throw new ArgumentException($"irregular row length at row {y}");

                for (int x = 0; x < cols; x++)
                {
                    var layerToken = layerRows[y][x].Trim();
                    MapLayer layerVal = MapLayer.None;

                    if (!string.IsNullOrEmpty(layerToken))
                    {
                        // 尝试按名称解析，例如 "Tanks" 或 "FootUnits"
                        if (!Enum.TryParse(layerToken, true, out layerVal))
                        {
                            // 再尝试按数值解析（uint 或 int）
                            if (uint.TryParse(layerToken, out var uv))
                            {
                                layerVal = (MapLayer)uv;
                            }
                            else if (int.TryParse(layerToken, out var iv))
                            {
                                layerVal = (MapLayer)iv;
                            }
                            else
                            {
                                // 无法解析，保留为 None
                                layerVal = MapLayer.None;
                            }
                        }
                    }

                    data.Layers[y, x] = layerVal;

                    var heightToken = heightRows[y][x].Trim();
                    data.Heights[y, x] = ParseFixedOrZero(heightToken);
                }
            }

            return data;
        }

        private static Fixed ParseFixedOrZero(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Fixed.Zero;

            token = token.Trim();

            // 先尝试按 raw 解析（整数）。如果 CSV 存的是 raw，可以直接用。
            if (int.TryParse(token, out var raw))
                return Fixed.FromRaw(raw);

            // 再尝试按小数解析（优先 InvariantCulture）。
            // 注意：这会经过 double/float，但输出仍是 Fixed（确定性由 Fixed 的实现保证）。
            if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv))
                return Fixed.FromDouble(dv);

            if (double.TryParse(token, out dv))
                return Fixed.FromDouble(dv);

            return Fixed.Zero;
        }

        /// <summary>
        /// 简单的 CSV 解析：按行分割（忽略空行），每行按逗号分割成 token。
        /// 注意：不支持带引号的复杂 CSV 格式。
        /// </summary>
        private static string[][] ParseCsvToRows(string csv)
        {
            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var rows = new string[lines.Length][];
            for (int i = 0; i < lines.Length; i++)
            {
                rows[i] = lines[i].Split(',');
            }
            return rows;
        }

        /// <summary>
        /// 生成一个示例地图：可自定义尺寸。
        /// - Tanks layer：随机格子为障碍（参数控制密度和随机种子）。
        /// - 其它 layer：全通（默认 None）。
        /// - 高度：全 0。
        /// </summary>
        public static MapData CreateExampleMap(int width, int height, float tankObstacleProbability = 0.1f, int? seed = 12345)
        {
            tankObstacleProbability = Mathf.Clamp01(tankObstacleProbability);
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            var data = new MapData(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    data.Heights[y, x] = Fixed.Zero;
                    data.Layers[y, x] = MapLayer.None;
                }
            }

            if (seed.HasValue)
            {
                var rng = new System.Random(seed.Value);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (rng.NextDouble() < tankObstacleProbability)
                            data.Layers[y, x] = MapLayer.Tanks;
                    }
                }
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (UnityEngine.Random.value < tankObstacleProbability)
                            data.Layers[y, x] = MapLayer.Tanks;
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// 生成一个示例地图：200x200。
        /// </summary>
        public static MapData CreateExampleMap200X200(float tankObstacleProbability = 0.1f, int? seed = 12345)
        {
            return CreateExampleMap(200, 200, tankObstacleProbability, seed);
        }
    }
}
