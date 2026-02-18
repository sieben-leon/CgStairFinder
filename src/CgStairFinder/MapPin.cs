using System;

namespace CgStairFinder
{
    public sealed class MapPin
    {
        public int East { get; set; }
        public int South { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsTimed { get; set; }
        public int AutoDeleteHours { get; set; }

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

            var createdAt = pin.CreatedAt == default(DateTime)
                ? DateTime.Now
                : pin.CreatedAt;
            var autoDeleteHours = Math.Max(0, Math.Min(24 * 365, pin.AutoDeleteHours));
            var isTimed = pin.IsTimed && autoDeleteHours > 0;

            return new MapPin
            {
                East = Math.Max(0, pin.East),
                South = Math.Max(0, pin.South),
                Title = title,
                Detail = (pin.Detail ?? string.Empty).Trim(),
                CreatedAt = createdAt,
                IsTimed = isTimed,
                AutoDeleteHours = isTimed ? autoDeleteHours : 0
            };
        }
    }
}
