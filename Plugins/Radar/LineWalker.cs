namespace Radar
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using ExileCenter.RemoteObjects.Components;
    using ExileCenter.RemoteObjects.States.InGameStateObjects;

    /// <summary>
    /// Grid-space line walkability checker.
    /// Uses Bresenham's line algorithm to sample grid cells along a straight line
    /// and checks each against the nibble-encoded walkability data.
    /// </summary>
    public static class LineWalker
    {
        /// <summary>
        /// Checks whether a single grid cell is walkable.
        /// Decodes the 4-bit nibble from the packed walkability byte array.
        /// 0 = blocked, 1-5 = walkable.
        /// Optionally consults a door-override set (positions forced walkable).
        /// </summary>
        public static bool IsWalkable(
            byte[] walkableData,
            int bytesPerRow,
            int x,
            int y,
            HashSet<(int, int)>? doorOverrides = null)
        {
            if (doorOverrides != null && doorOverrides.Contains((x, y)))
            {
                return true;
            }

            var byteIndex = (y * bytesPerRow) + (x / 2);

            if ((uint)byteIndex >= (uint)walkableData.Length)
            {
                return false;
            }

            var shift = ((x & 1) == 0) ? 0 : 4;
            var value = (walkableData[byteIndex] >> shift) & 0xF;
            return value != 0;
        }

        /// <summary>
        /// Result of a line walkability check.
        /// </summary>
        public struct LineResult
        {
            /// <summary>
            /// True if every cell along the line is walkable.
            /// </summary>
            public bool IsClear;

            /// <summary>
            /// Number of blocked (non-walkable) cells encountered.
            /// </summary>
            public int BlockedCells;

            /// <summary>
            /// Total number of cells sampled along the line.
            /// </summary>
            public int TotalCells;
        }

        /// <summary>
        /// Walks a Bresenham line from start to end (Vector2 variant),
        /// checking each grid cell for walkability.
        /// </summary>
        public static LineResult CheckLine(
            byte[] walkableData,
            int bytesPerRow,
            Vector2 start,
            Vector2 end,
            HashSet<(int, int)>? doorOverrides = null)
        {
            return CheckLine(
                walkableData,
                bytesPerRow,
                (int)Math.Round(start.X),
                (int)Math.Round(start.Y),
                (int)Math.Round(end.X),
                (int)Math.Round(end.Y),
                doorOverrides);
        }

        /// <summary>
        /// Walks a Bresenham line from (x0,y0) to (x1,y1),
        /// checking each grid cell for walkability.
        /// </summary>
        public static LineResult CheckLine(
            byte[] walkableData,
            int bytesPerRow,
            int x0,
            int y0,
            int x1,
            int y1,
            HashSet<(int, int)>? doorOverrides = null)
        {
            var result = new LineResult { IsClear = true };

            var dx = Math.Abs(x1 - x0);
            var dy = Math.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx - dy;

            var x = x0;
            var y = y0;

            while (true)
            {
                result.TotalCells++;

                if (!IsWalkable(walkableData, bytesPerRow, x, y, doorOverrides))
                {
                    result.IsClear = false;
                    result.BlockedCells++;
                }

                if (x == x1 && y == y1)
                {
                    break;
                }

                var e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
            }

            return result;
        }

        /// <summary>
        /// Builds a door-override map from all door-like entities in AwakeEntities.
        /// Doors are detected by the TriggerableBlockage component or by entity path
        /// containing "Door". A 5x5 area around each door is marked as forced-walkable
        /// to punch through wall-type doors on the terrain grid.
        /// </summary>
        /// <param name="areaInstance">The current area instance.</param>
        /// <returns>
        /// A HashSet of (x, y) grid positions to treat as walkable,
        /// or null if no door entities exist in the area.
        /// </returns>
        public static HashSet<(int, int)>? BuildDoorOverrideMap(
            AreaInstance areaInstance)
        {
            HashSet<(int, int)>? overrides = null;
            const int doorRadius = 2; // 5x5 area

            void MarkArea(int gx, int gy)
            {
                overrides ??= new HashSet<(int, int)>();
                for (var dx = -doorRadius; dx <= doorRadius; dx++)
                {
                    for (var dy = -doorRadius; dy <= doorRadius; dy++)
                    {
                        overrides.Add((gx + dx, gy + dy));
                    }
                }
            }

            foreach (var kv in areaInstance.AwakeEntities)
            {
                var entity = kv.Value;

                // Method 1: TriggerableBlockage component (the canonical door marker)
                if (entity.TryGetComponent<TriggerableBlockage>(out var _))
                {
                    if (entity.TryGetComponent<Render>(out var render))
                    {
                        MarkArea(
                            (int)Math.Round(render.GridPosition.X),
                            (int)Math.Round(render.GridPosition.Y));
                    }

                    continue;
                }

                // Method 2: Entity path contains "Door" (catch variants that
                // may lack TriggerableBlockage, e.g. certain door subtypes)
                var path = entity.Path;
                if (!string.IsNullOrEmpty(path) &&
                    path.Contains("Door", StringComparison.OrdinalIgnoreCase))
                {
                    if (entity.TryGetComponent<Render>(out var render))
                    {
                        MarkArea(
                            (int)Math.Round(render.GridPosition.X),
                            (int)Math.Round(render.GridPosition.Y));
                    }
                }
            }

            return overrides;
        }
    }
}
