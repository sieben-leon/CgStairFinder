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

            return string.Format("{0} | 東{1}、南{2} -- {3}", MapCode, Pin.East, Pin.South, Pin.Title);
        }
    }
}
