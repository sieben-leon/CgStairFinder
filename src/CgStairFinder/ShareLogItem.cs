using System;

namespace CgStairFinder
{
    internal sealed class ShareLogItem
    {
        public DetectLog Log { get; set; }
        public bool IsSharedSource { get; set; }

        public override string ToString()
        {
            var source = IsSharedSource ? "共有" : "ローカル";
            var mapName = string.IsNullOrWhiteSpace(Log.MapName) ? string.Empty : string.Format(" ({0})", Log.MapName);
            return string.Format("{0}{1} [{2}] {3:yyyy-MM-dd HH:mm:ss}", Log.MapCode, mapName, source, Log.DetectTime);
        }
    }
}
