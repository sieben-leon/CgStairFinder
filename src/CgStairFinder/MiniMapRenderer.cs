using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CgStairFinder
{
    internal static class MiniMapRenderer
    {
        private enum MiniMapCellKind
        {
            NoMap = 0,
            Unknown = 1,
            Passable = 2,
            Transition = 3,
            Blocked = 4
        }

        private static string cachedTerrainKey;
        private static Bitmap cachedTerrainBitmap;

        public static Bitmap Render(
            Size canvasSize,
            CgMapStairFinder.CgMapData mapData,
            int? east,
            int? south,
            bool showTerrain,
            bool freezeTerrainLayer,
            float zoom,
            string mapCacheKey,
            float panOffsetX,
            float panOffsetY)
        {
            if (mapData == null || mapData.Width <= 0 || mapData.Height <= 0 ||
                canvasSize.Width <= 0 || canvasSize.Height <= 0)
            {
                return null;
            }

            var bitmap = new Bitmap(canvasSize.Width, canvasSize.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(248, 250, 252));

                const float padding = 8f;
                var usableWidth = Math.Max(1f, bitmap.Width - padding * 2);
                var usableHeight = Math.Max(1f, bitmap.Height - padding * 2);
                var squareSide = Math.Max(1f, Math.Min(usableWidth, usableHeight));
                var bounds = new RectangleF(
                    (bitmap.Width - squareSide) / 2f,
                    (bitmap.Height - squareSide) / 2f,
                    squareSide,
                    squareSide);
                var fitRect = FitRectangle(bounds, mapData.Width, mapData.Height);
                var mapRect = ApplyZoom(fitRect, bounds, zoom, mapData.Width, mapData.Height, east, south, panOffsetX, panOffsetY);

                var clipState = g.Save();
                g.SetClip(bounds);

                if (showTerrain)
                {
                    var renderWidth = Math.Max(1, (int)Math.Round(mapRect.Width));
                    var renderHeight = Math.Max(1, (int)Math.Round(mapRect.Height));
                    Bitmap terrain;
                    if (freezeTerrainLayer)
                    {
                        terrain = GetTerrainLayer(mapData, renderWidth, renderHeight, mapCacheKey);
                    }
                    else
                    {
                        InvalidateTerrainCache();
                        terrain = BuildTerrainLayer(mapData, renderWidth, renderHeight);
                    }
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(terrain, mapRect.X, mapRect.Y, mapRect.Width, mapRect.Height);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(241, 245, 249)))
                    {
                        g.FillRectangle(brush, mapRect);
                    }
                }

                using (var borderPen = new Pen(Color.FromArgb(148, 163, 184), 1f))
                {
                    g.DrawRectangle(borderPen, mapRect.X, mapRect.Y, mapRect.Width, mapRect.Height);
                }

                foreach (var stair in mapData.Stairs)
                {
                    var p = ScaleToMapRect(mapRect, mapData.Width, mapData.Height, stair.East, stair.South);
                    using (var brush = new SolidBrush(GetStairColor(stair.Type)))
                    {
                        g.FillEllipse(brush, p.X - 2f, p.Y - 2f, 4f, 4f);
                    }
                }

                if (east.HasValue && south.HasValue &&
                    east.Value >= 0 && east.Value < mapData.Width &&
                    south.Value >= 0 && south.Value < mapData.Height)
                {
                    var player = ScaleToMapRect(mapRect, mapData.Width, mapData.Height, east.Value, south.Value);
                    using (var markerBrush = new SolidBrush(Color.FromArgb(59, 130, 246)))
                    using (var markerPen = new Pen(Color.White, 1.5f))
                    {
                        g.FillEllipse(markerBrush, player.X - 4f, player.Y - 4f, 8f, 8f);
                        g.DrawEllipse(markerPen, player.X - 4f, player.Y - 4f, 8f, 8f);
                    }
                }

                g.Restore(clipState);
            }

            return bitmap;
        }

        private static Bitmap GetTerrainLayer(CgMapStairFinder.CgMapData mapData, int renderWidth, int renderHeight, string mapCacheKey)
        {
            var key = string.Format(
                "{0}|{1}x{2}|{3}x{4}",
                mapCacheKey ?? string.Empty,
                mapData.Width,
                mapData.Height,
                renderWidth,
                renderHeight);

            if (cachedTerrainBitmap != null && string.Equals(cachedTerrainKey, key, StringComparison.Ordinal))
            {
                return cachedTerrainBitmap;
            }

            cachedTerrainBitmap?.Dispose();
            cachedTerrainBitmap = BuildTerrainLayer(mapData, renderWidth, renderHeight);
            cachedTerrainKey = key;
            return cachedTerrainBitmap;
        }

        private static void InvalidateTerrainCache()
        {
            if (cachedTerrainBitmap != null)
            {
                cachedTerrainBitmap.Dispose();
                cachedTerrainBitmap = null;
            }

            cachedTerrainKey = null;
        }

        private static Bitmap BuildTerrainLayer(CgMapStairFinder.CgMapData mapData, int renderWidth, int renderHeight)
        {
            var cellCount = mapData.Width * mapData.Height;
            var layer = new Bitmap(renderWidth, renderHeight);

            if (mapData.Flags == null || mapData.Flags.Length != cellCount)
            {
                using (var g = Graphics.FromImage(layer))
                using (var brush = new SolidBrush(Color.FromArgb(226, 232, 240)))
                {
                    g.FillRectangle(brush, 0, 0, renderWidth, renderHeight);
                }
                return layer;
            }

            var hasObjectIds = mapData.ObjectIds != null && mapData.ObjectIds.Length == cellCount;
            for (var y = 0; y < renderHeight; y++)
            {
                var southStart = y * mapData.Height / renderHeight;
                var southEndExclusive = (y + 1) * mapData.Height / renderHeight;
                if (southEndExclusive <= southStart)
                {
                    southEndExclusive = southStart + 1;
                }

                for (var x = 0; x < renderWidth; x++)
                {
                    var eastStart = x * mapData.Width / renderWidth;
                    var eastEndExclusive = (x + 1) * mapData.Width / renderWidth;
                    if (eastEndExclusive <= eastStart)
                    {
                        eastEndExclusive = eastStart + 1;
                    }

                    var kind = MiniMapCellKind.NoMap;
                    for (var south = southStart; south < southEndExclusive; south++)
                    {
                        var row = south * mapData.Width;
                        for (var east = eastStart; east < eastEndExclusive; east++)
                        {
                            var idx = row + east;
                            var objectId = hasObjectIds ? mapData.ObjectIds[idx] : (ushort)0;
                            var candidate = GetCellKind(mapData.Flags[idx], objectId);
                            if (candidate > kind)
                            {
                                kind = candidate;
                                if (kind == MiniMapCellKind.Blocked)
                                {
                                    break;
                                }
                            }
                        }

                        if (kind == MiniMapCellKind.Blocked)
                        {
                            break;
                        }
                    }

                    layer.SetPixel(x, y, GetCellColor(kind));
                }
            }

            return layer;
        }

        private static RectangleF ApplyZoom(
            RectangleF fitRect,
            RectangleF bounds,
            float zoom,
            int mapWidth,
            int mapHeight,
            int? east,
            int? south,
            float panOffsetX,
            float panOffsetY)
        {
            var clampedZoom = Math.Max(1f, Math.Min(8f, zoom));
            if (Math.Abs(clampedZoom - 1f) < 0.001f)
            {
                return fitRect;
            }

            var width = fitRect.Width * clampedZoom;
            var height = fitRect.Height * clampedZoom;
            var centerX = fitRect.Left + fitRect.Width / 2f;
            var centerY = fitRect.Top + fitRect.Height / 2f;

            if (east.HasValue && south.HasValue &&
                east.Value >= 0 && east.Value < mapWidth &&
                south.Value >= 0 && south.Value < mapHeight)
            {
                var xDiv = Math.Max(1, mapWidth - 1);
                var yDiv = Math.Max(1, mapHeight - 1);
                var focusRatioX = east.Value / (float)xDiv;
                var focusRatioY = south.Value / (float)yDiv;
                centerX = bounds.Left + bounds.Width / 2f - (focusRatioX - 0.5f) * width;
                centerY = bounds.Top + bounds.Height / 2f - (focusRatioY - 0.5f) * height;
            }

            centerX += panOffsetX;
            centerY += panOffsetY;
            return new RectangleF(centerX - width / 2f, centerY - height / 2f, width, height);
        }

        private static RectangleF FitRectangle(RectangleF bounds, int mapWidth, int mapHeight)
        {
            if (mapWidth <= 0 || mapHeight <= 0)
            {
                return bounds;
            }

            var mapRatio = (float)mapWidth / mapHeight;
            var boundsRatio = bounds.Width / bounds.Height;

            if (mapRatio > boundsRatio)
            {
                var fitHeight = bounds.Width / mapRatio;
                var top = bounds.Top + (bounds.Height - fitHeight) / 2f;
                return new RectangleF(bounds.Left, top, bounds.Width, fitHeight);
            }

            var fitWidth = bounds.Height * mapRatio;
            var left = bounds.Left + (bounds.Width - fitWidth) / 2f;
            return new RectangleF(left, bounds.Top, fitWidth, bounds.Height);
        }

        private static PointF ScaleToMapRect(RectangleF mapRect, int mapWidth, int mapHeight, int east, int south)
        {
            var xDiv = Math.Max(1, mapWidth - 1);
            var yDiv = Math.Max(1, mapHeight - 1);
            var x = mapRect.Left + mapRect.Width * east / xDiv;
            var y = mapRect.Top + mapRect.Height * south / yDiv;
            return new PointF(x, y);
        }

        private static Color GetStairColor(StairType type)
        {
            switch (type)
            {
                case StairType.Up:
                    return Color.FromArgb(34, 197, 94);
                case StairType.Down:
                    return Color.FromArgb(239, 68, 68);
                case StairType.Jump:
                    return Color.FromArgb(100, 116, 139);
                default:
                    return Color.FromArgb(245, 158, 11);
            }
        }

        private static MiniMapCellKind GetCellKind(ushort flag, ushort objectId)
        {
            if (flag == 0)
            {
                return MiniMapCellKind.NoMap;
            }

            var transitionCode = (byte)(flag & 0x00FF);
            var collisionCode = (byte)((flag >> 8) & 0x00FF);
            var hasTransition = transitionCode != 0;

            if (collisionCode == 193)
            {
                return MiniMapCellKind.Blocked;
            }

            if (hasTransition)
            {
                return MiniMapCellKind.Transition;
            }

            if (objectId != 0)
            {
                return MiniMapCellKind.Blocked;
            }

            if (collisionCode == 192)
            {
                return MiniMapCellKind.Passable;
            }

            return MiniMapCellKind.Unknown;
        }

        private static Color GetCellColor(MiniMapCellKind kind)
        {
            switch (kind)
            {
                case MiniMapCellKind.Passable:
                    return Color.White;
                case MiniMapCellKind.Blocked:
                    return Color.FromArgb(75, 85, 99);
                case MiniMapCellKind.Transition:
                    return Color.FromArgb(251, 191, 36);
                case MiniMapCellKind.Unknown:
                    return Color.FromArgb(203, 213, 225);
                default:
                    return Color.FromArgb(51, 65, 85);
            }
        }
    }
}
