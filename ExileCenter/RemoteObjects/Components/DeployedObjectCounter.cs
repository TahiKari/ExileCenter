// <copyright file="DeployedObjectCounter.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    ///     Counts deployed objects per object-type id. PoE2 changed the deployed-object "type"
    ///     field from a small (0-255) enum into a full dat-row id (e.g. 22938), so a fixed
    ///     <c>int[256]</c> can no longer hold it. This is backed by a dictionary instead, and the
    ///     indexer returns 0 for ids that are not currently deployed — preserving the old
    ///     "absent == 0, never throws" semantics that AutoHotKeyTrigger's
    ///     <c>DeployedObjectsCount[id]</c> dynamic conditions rely on.
    /// </summary>
    public sealed class DeployedObjectCounter : IEnumerable<KeyValuePair<int, int>>
    {
        // Maps the (static-per-patch) DeployedObjectType id to its in-game minion-source category.
        // These ids are observed from live memory (DV -> Actor -> Deployed Objects); extend this map
        // as new ids show up. A missing/stale entry only affects display, never the counts.
        private static readonly Dictionary<int, string> CategoryNames = new()
        {
            { 22938, "Djinns" },
            { 10966, "Skeletons" },
            { 58392, "Spectres" },
            { 48349, "Totems" },
            { 62792, "Offerings" },
        };

        private readonly Dictionary<int, int> counts = new();

        /// <summary>
        ///     Gets the human-readable category name for a DeployedObjectType id, or
        ///     "NO NAME MAPPING" when the id is not in <see cref="CategoryNames" /> yet.
        /// </summary>
        /// <param name="type">the deployed-object type id.</param>
        /// <returns>the category name, or "NO NAME MAPPING".</returns>
        public static string CategoryName(int type)
        {
            return CategoryNames.TryGetValue(type, out var name) ? name : "NO NAME MAPPING";
        }

        /// <summary>
        ///     Gets the number of deployed objects of the given type id (0 if none are deployed).
        /// </summary>
        /// <param name="type">the deployed-object type id (see DV -> Actor -> Deployed Objects).</param>
        public int this[int type] => this.counts.TryGetValue(type, out var count) ? count : 0;

        /// <summary>
        ///     Removes all counts. Called at the start of each actor update.
        /// </summary>
        internal void Clear()
        {
            this.counts.Clear();
        }

        /// <summary>
        ///     Increments the count for the given type id.
        /// </summary>
        /// <param name="type">the deployed-object type id.</param>
        internal void Increment(int type)
        {
            this.counts[type] = this.counts.TryGetValue(type, out var count) ? count + 1 : 1;
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<int, int>> GetEnumerator()
        {
            return this.counts.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
