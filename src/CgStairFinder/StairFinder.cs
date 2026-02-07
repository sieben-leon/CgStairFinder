using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace CgStairFinder
{
    public enum StairType
    {
        [Description(Defined.STAIR_TYPE_UP_DISPLAY_TEXT)]
        Up, // 上り
        [Description(Defined.STAIR_TYPE_DOWN_DISPLAY_TEXT)]
        Down, // 下り
        [Description(Defined.STAIR_TYPE_MOVEABLE_DISPLAY_TEXT)]
        Jump, // 移動可能
        [Description(Defined.STAIR_TYPE_UNKNOW_DISPLAY_TEXT)]
        Unknow
    }

    public struct CgStair
    {
        public int East { get; set; } // 座標 東
        public int South { get; set; } // 座標 南
        public StairType Type { get; set; }
        public static string Translate(StairType stairType)
        {
            var prop = typeof(StairType).GetField(Enum.GetName(typeof(StairType), stairType));
            var attr = (DescriptionAttribute)prop.GetCustomAttributes(typeof(DescriptionAttribute), false)[0];
            return attr.Description;
        }
    }

    public class CgMapStairFinder
    {
        private const ushort StairFlagValue = 49155;
        private readonly MemoryStream ms;

        public CgMapStairFinder(FileInfo file)
        {
            using (var stream = file.OpenRead())
            {
                ms = new MemoryStream();
                stream.CopyTo(ms);
            }
        }

        static StairType GetStType(ushort gnum)
        {
            // オブジェクト番号(gnum)から階段種別を判定
            switch (gnum)
            {
                case 12000:
                case 12001:
                case 13268:
                case 13270:
                case 13272:
                case 13274:
                case 13996:
                case 13998:
                case 15561:
                case 15887:
                case 15889:
                case 15891:
                case 17952:
                case 17954:
                case 17956:
                case 17958:
                case 17960:
                case 17962:
                case 17964:
                case 17966:
                case 17968:
                case 17970:
                case 17972:
                case 17974:
                case 17976:
                case 17978:
                case 17980:
                case 17982:
                case 17984:
                case 17986:
                case 17988:
                case 17990:
                case 17992:
                case 17994:
                case 17996:
                case 17998:
                case 16610:
                case 16611:
                case 16626:
                case 16627:
                case 16628:
                case 16629:
                    return StairType.Up;

                case 12002:
                case 12003:
                case 13269:
                case 13271:
                case 13273:
                case 13275:
                case 13997:
                case 13999:
                case 15562:
                case 15888:
                case 15890:
                case 15892:
                case 17953:
                case 17955:
                case 17957:
                case 17959:
                case 17961:
                case 17963:
                case 17965:
                case 17967:
                case 17969:
                case 17971:
                case 17973:
                case 17975:
                case 17977:
                case 17979:
                case 17981:
                case 17983:
                case 17985:
                case 17987:
                case 17989:
                case 17991:
                case 17993:
                case 17995:
                case 17997:
                case 17999:
                case 16612:
                case 16613:
                case 16614:
                case 16615:
                    return StairType.Down;

                case 14676:
                case 0:
                    return StairType.Jump;

                default:
                    return StairType.Unknow;
            }
        }

        public sealed class CgMapData
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public IList<CgStair> Stairs { get; set; }
            public ushort[] GroundIds { get; set; }
            public ushort[] ObjectIds { get; set; }
            public ushort[] Flags { get; set; }
        }

        private static ushort[] ReadSection(BinaryReader br, int cellCount)
        {
            var values = new ushort[cellCount];
            for (var i = 0; i < cellCount; i++)
            {
                values[i] = br.ReadUInt16();
            }

            return values;
        }

        public CgMapData GetMapData()
        {
            var result = new List<CgStair>();
            ushort[] groundIds;
            ushort[] objectIds;
            ushort[] flags;

            // http://cgsword.com/filesystem_graphicmap.htm#mapdat の形式で地図ファイルを解析
            int width, height;
            using (ms)
            using (var br = new BinaryReader(ms))
            {
                // 先頭3バイトは固定文字列 MAP、続く9バイトは0/空白
                // start: 12
                ms.Seek(12, SeekOrigin.Begin);
                // 2つのDWORD(4バイト): 1つ目が幅(東)、2つ目が高さ(南)
                width = br.ReadInt32();
                height = br.ReadInt32();
                var cellCount = width * height;
                groundIds = ReadSection(br, cellCount);
                objectIds = ReadSection(br, cellCount);
                flags = ReadSection(br, cellCount);

                for (var i = 0; i < height; i++)
                {
                    for (var j = 0; j < width; j++)
                    {
                        var idx = j + i * width;
                        if (flags[idx] == StairFlagValue)
                        {
                            var gnum = objectIds[idx];
                            result.Add(new CgStair { East = j, South = i, Type = GetStType(gnum) });
                        }
                    }
                }
            }

            return new CgMapData
            {
                Width = width,
                Height = height,
                Stairs = result,
                GroundIds = groundIds,
                ObjectIds = objectIds,
                Flags = flags
            };
        }

        public IList<CgStair> GetStairs()
        {
            return GetMapData().Stairs;
        }
    }
}
