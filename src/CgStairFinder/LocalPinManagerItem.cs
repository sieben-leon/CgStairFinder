namespace CgStairFinder
{
    internal sealed class LocalPinManagerItem
    {
        public string MapCode { get; set; }
        public MapPin Pin { get; set; }

        public override string ToString()
        {
            if (Pin == null)
            {
                return MapCode ?? string.Empty;
            }

            var elapsed = MapPinCollectionService.BuildElapsedAgoText(Pin.CreatedAt, System.DateTime.Now);
            var elapsedText = string.IsNullOrWhiteSpace(elapsed) ? string.Empty : string.Format(" ({0})", elapsed);
            return string.Format("{0} | ìå{1}ÅAìÏ{2} -- {3}{4}", MapCode, Pin.East, Pin.South, Pin.Title, elapsedText);
        }
    }
}
