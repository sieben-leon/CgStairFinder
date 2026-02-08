using System;

namespace CgStairFinder
{
    public sealed class MapPin
    {
        public int East { get; set; }
        public int South { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }

        public static MapPin Normalize(MapPin pin)
        {
            if (pin == null)
            {
                return null;
            }

            var title = (pin.Title ?? string.Empty).Trim();
            if (title.Length > 5)
            {
                title = title.Substring(0, 5);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return new MapPin
            {
                East = Math.Max(0, pin.East),
                South = Math.Max(0, pin.South),
                Title = title,
                Detail = (pin.Detail ?? string.Empty).Trim()
            };
        }
    }
}
